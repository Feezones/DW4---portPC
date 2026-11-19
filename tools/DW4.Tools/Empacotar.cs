using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// dw4 empacotar &lt;fatia&gt; &lt;fases&gt; &lt;saida&gt; [--ogg]
///
/// Junta os dados do jogo em pacotes por fase (DECISÃO DO RESPONSÁVEL, 2026-11-04: download mais rápido, porque o launcher e
/// o celular fazem um pedido por arquivo e eram 52 mil). Gera em &lt;saida&gt;:
///   fatia.pcz, fases.pcz (arquivos soltos da raiz das fases) e fases-&lt;fase&gt;.pcz (zip com outra extensão: a Godot monta
///   .zip e .pcz, e o launcher não publica *.zip), com as entradas em dw4/&lt;caminho lógico&gt;;
///   indice.txt: um arquivo lógico por linha (fatia/..., fases/...), com "\t&lt;físico&gt;" quando o conteúdo é igual a um
///   arquivo anterior (só .wav: a mesma música repetida em várias fases vai uma vez só).
/// O jogo lê pelo GameFiles.cs. Os zips são deterministas (ordem, datas e compressão fixas): uma fase que não mudou gera o
/// mesmo zip, e o launcher não baixa de novo. Um zip igual ao que já está na saída não é regravado; zips que sobraram de
/// fases que não existem mais são apagados.
/// </summary>
static class Empacotar
{
    static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly HashSet<string> Stored = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".png", ".ogg", ".adx", ".jpg", ".zip" };

    public static int Run(string fatia, string fases, string output)
    {
        Directory.CreateDirectory(output);
        var groups = new List<(string Pack, List<(string Logical, string Disk)> Files)>();
        groups.Add(("fatia", List(fatia, "fatia", rel =>
            !rel.StartsWith("analise/", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("colisao_antiga_", StringComparison.OrdinalIgnoreCase))));
        groups.Add(("fases", Directory.EnumerateFiles(fases).Order(StringComparer.Ordinal)
            .Select(f => ("fases/" + Path.GetFileName(f), f)).ToList()));
        foreach (var dir in Directory.EnumerateDirectories(fases).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            groups.Add(("fases-" + name, List(dir, "fases/" + name, _ => true)));
        }

        var firstWav = new Dictionary<string, string>(StringComparer.Ordinal); // sha → caminho lógico do primeiro
        var index = new StringBuilder("# dw4 empacotar: arquivo lógico [TAB arquivo físico quando repetido]\n");
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0, saved = 0;
        int changed = 0;
        foreach (var (pack, files) in groups)
        {
            var zipPath = Path.Combine(output, pack + ".pcz");
            var tmp = zipPath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (logical, disk) in files)
                {
                    var bytes = File.ReadAllBytes(disk);
                    total += bytes.Length;
                    if (logical.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        var sha = Convert.ToHexString(SHA256.HashData(bytes));
                        if (firstWav.TryGetValue(sha, out var first))
                        {
                            index.Append(logical).Append('\t').Append(first).Append('\n');
                            saved += bytes.Length;
                            continue;
                        }
                        firstWav[sha] = logical;
                    }
                    index.Append(logical).Append('\n');
                    var level = Stored.Contains(Path.GetExtension(logical)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var entry = zip.CreateEntry("dw4/" + logical, level);
                    entry.LastWriteTime = FixedTime;
                    using var s = entry.Open();
                    s.Write(bytes);
                }
            }
            if (File.Exists(zipPath) && SameFile(zipPath, tmp)) File.Delete(tmp);
            else { File.Move(tmp, zipPath, overwrite: true); changed++; }
            written.Add(Path.GetFileName(zipPath));
        }
        foreach (var old in Directory.EnumerateFiles(output, "*.pcz"))
            if (!written.Contains(Path.GetFileName(old))) { File.Delete(old); Console.WriteLine($"apagado {Path.GetFileName(old)}"); }
        var indexBytes = Encoding.UTF8.GetBytes(index.ToString());
        var indexPath = Path.Combine(output, "indice.txt");
        if (!File.Exists(indexPath) || !File.ReadAllBytes(indexPath).AsSpan().SequenceEqual(indexBytes)) File.WriteAllBytes(indexPath, indexBytes);
        long packed = Directory.EnumerateFiles(output, "*.pcz").Sum(f => new FileInfo(f).Length);
        Console.WriteLine($"{groups.Count} pacotes ({changed} mudaram), {groups.Sum(g => g.Files.Count)} arquivos: {total / 1048576.0:0} MB → {packed / 1048576.0:0} MB" +
            $" (músicas repetidas: {saved / 1048576.0:0} MB a menos)");
        return 0;
    }

    static List<(string Logical, string Disk)> List(string root, string prefix, Func<string, bool> keep) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(root, f).Replace('\\', '/'), Disk: f))
            .Where(x => keep(x.Rel) && !x.Rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Rel, StringComparer.Ordinal)
            .Select(x => (prefix + "/" + x.Rel, x.Disk)).ToList();

    static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        if (fa.Length != new FileInfo(b).Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }
}
