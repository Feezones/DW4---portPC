using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace DW4.Client;

/// <summary>
/// Leitura dos dados do jogo (fatia, fases) soltos no disco OU de dentro dos pacotes por fase (DECISÃO DO RESPONSÁVEL,
/// 2026-11-04: download mais rápido no PC e no celular). Pacotes: &lt;dados&gt;\pacotes\*.pcz (zip; a Godot só monta zip com extensão .zip ou .pcz, e o launcher ignora *.zip), gerados por "dw4 empacotar", mais
/// &lt;dados&gt;\pacotes\indice.txt com todos os arquivos (caminho lógico, e o físico quando o conteúdo é igual ao de outro
/// arquivo: as músicas repetidas em várias fases vão uma vez só). Os zips são montados na Godot em res://dw4/.
///
/// O resto do jogo continua usando os caminhos de sempre (&lt;dados&gt;\fases\stag0101\...): este arquivo traduz. Sem
/// indice.txt (editor, pasta de trabalho, pacotes antigos), tudo vai direto para o System.IO.
/// </summary>
public static class GameFiles
{
    public const string MountRoot = "res://dw4/";
    public const string IndexName = "indice.txt";

    static bool s_init;
    static string s_root = ""; // pasta "dados" com separador no fim
    // caminho lógico (relativo a dados, com "/", sem diferença de maiúsculas) → caminho físico dentro de res://dw4/
    static readonly Dictionary<string, string> s_files = new(StringComparer.OrdinalIgnoreCase);
    // pasta lógica → nomes dos arquivos e das subpastas
    static readonly Dictionary<string, (List<string> Files, List<string> Dirs)> s_dirs = new(StringComparer.OrdinalIgnoreCase);

    public static bool Packed => Init() && s_files.Count > 0;

    /// <summary>Monta os pacotes (uma vez). Chamado no começo do menu e da fase.</summary>
    public static bool Init()
    {
        if (s_init) return true;
        s_init = true;
        var root = DataPaths.DataRoot;
        var packs = Path.Combine(root, "pacotes");
        var index = Path.Combine(packs, IndexName);
        if (!File.Exists(index)) return true;
        int mounted = 0;
        foreach (var zip in Directory.EnumerateFiles(packs, "*.pcz").OrderBy(p => p, StringComparer.Ordinal))
        {
            if (ProjectSettings.LoadResourcePack(zip, replaceFiles: false)) mounted++;
            else GD.PushError($"[dados] pacote não abriu: {zip}");
        }
        s_root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        foreach (var line in File.ReadLines(index))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            string logical = tab < 0 ? line : line[..tab], physical = tab < 0 ? line : line[(tab + 1)..];
            s_files[logical] = physical;
            AddToDirs(logical);
        }
        GD.Print($"[dados] {mounted} pacotes, {s_files.Count} arquivos ({packs})");
        return true;
    }

    static void AddToDirs(string logical)
    {
        int slash = logical.LastIndexOf('/');
        string dir = slash < 0 ? "" : logical[..slash], name = logical[(slash + 1)..];
        Dir(dir).Files.Add(name);
        while (dir.Length > 0)
        {
            slash = dir.LastIndexOf('/');
            string parent = slash < 0 ? "" : dir[..slash], child = dir[(slash + 1)..];
            var p = Dir(parent);
            if (p.Dirs.Contains(child, StringComparer.OrdinalIgnoreCase)) break;
            p.Dirs.Add(child);
            dir = parent;
        }
    }

    static (List<string> Files, List<string> Dirs) Dir(string dir)
    {
        if (!s_dirs.TryGetValue(dir, out var d)) s_dirs[dir] = d = ([], []);
        return d;
    }

    /// <summary>Caminho do disco → caminho lógico dentro dos pacotes; null se não é da pasta de dados (ou não há pacotes).</summary>
    static string? Logical(string path)
    {
        if (!Packed) return null;
        var full = Path.GetFullPath(path);
        if (full.Length + 1 == s_root.Length && s_root.StartsWith(full, StringComparison.OrdinalIgnoreCase)) return "";
        if (!full.StartsWith(s_root, StringComparison.OrdinalIgnoreCase)) return null;
        return full[s_root.Length..].Replace('\\', '/').TrimEnd('/');
    }


    static string Disk(string logical) => s_root + logical.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Caminho para os carregadores da própria Godot (GltfDocument.AppendFromFile, Image.LoadFromFile).</summary>
    public static string GodotPath(string path) => InPack(path, out var p) ? p : path;

    // Fora do índice (ex.: dados\arquivos\area.afs, que fica solto) vale o disco.
    static bool InPack(string path, out string physical)
    {
        physical = "";
        if (Logical(path) is not { } l || !s_files.TryGetValue(l, out var p)) return false;
        physical = MountRoot + p;
        return true;
    }

    static bool DirInPack(string path, out string logical)
    {
        logical = Logical(path) ?? "";
        return Packed && Logical(path) != null && s_dirs.ContainsKey(logical);
    }

    public static bool Exists(string? path) => path != null && (InPack(path, out _) || File.Exists(path));

    public static bool DirExists(string? path) => path != null && (DirInPack(path, out _) || Directory.Exists(path));

    public static byte[] ReadAllBytes(string path)
    {
        if (!InPack(path, out var p)) return File.ReadAllBytes(path);
        var bytes = Godot.FileAccess.GetFileAsBytes(p);
        if (bytes.Length == 0 && Godot.FileAccess.GetOpenError() != Error.Ok) throw new IOException($"{path}: {Godot.FileAccess.GetOpenError()}");
        return bytes;
    }

    public static string ReadAllText(string path)
    {
        if (!InPack(path, out _)) return File.ReadAllText(path);
        var b = ReadAllBytes(path);
        int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(b, start, b.Length - start);
    }

    public static string[] ReadAllLines(string path)
    {
        if (!InPack(path, out _)) return File.ReadAllLines(path);
        var lines = ReadAllText(path).Split('\n').Select(s => s.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.ToArray();
    }

    public static IEnumerable<string> EnumerateFiles(string dir, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
    {
        if (!DirInPack(dir, out var l)) return Directory.EnumerateFiles(dir, pattern, option);
        var match = Glob(pattern);
        var result = new List<string>();
        void Walk(string d)
        {
            if (!s_dirs.TryGetValue(d, out var e)) return;
            foreach (var f in e.Files) if (match.IsMatch(f)) result.Add(Disk(d.Length == 0 ? f : d + "/" + f));
            if (option == SearchOption.AllDirectories) foreach (var sub in e.Dirs) Walk(d.Length == 0 ? sub : d + "/" + sub);
        }
        Walk(l);
        return result;
    }

    public static string[] GetFiles(string dir) => EnumerateFiles(dir).ToArray();

    public static IEnumerable<string> EnumerateDirectories(string dir)
    {
        if (!DirInPack(dir, out var l)) return Directory.EnumerateDirectories(dir);
        return s_dirs[l].Dirs.Select(d => Disk(l.Length == 0 ? d : l + "/" + d)).ToList();
    }

    static readonly Dictionary<string, Regex> s_globs = [];
    static Regex Glob(string pattern)
    {
        lock (s_globs)
        {
            if (!s_globs.TryGetValue(pattern, out var r))
                s_globs[pattern] = r = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return r;
        }
    }
}
