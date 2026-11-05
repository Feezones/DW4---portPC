using System.IO;
using Godot;

namespace DW4.Client;

/// <summary>
/// Onde ficam os dados extraídos do jogo (fora do repositório).
/// Rodando pela Godot (editor ou tools/scripts/cliente.ps1): D:\dw4-work\fatia, fases e extracted\files.
/// Exportado (pacote do launcher RE:DIGITAL): a pasta "dados" ao lado do DW4.exe, com fatia, fases e arquivos.
/// DW4_FATIA, DW4_FASES e DW4_ARQUIVOS continuam mandando em qualquer caso.
/// </summary>
public static class DataPaths
{
    static readonly bool Editor = OS.HasFeature("editor");

    /// <summary>Pasta do DW4.exe exportado (ou a do executável da Godot, no editor).</summary>
    public static string ExeDir => Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";

    /// <summary>Celular (Android): os dados ficam na pasta interna do app, baixados pela tela de login (MobileBoot).</summary>
    public static readonly bool Mobile = OS.HasFeature("android");

    public static string MobileRoot => Path.Combine(OS.GetUserDataDir(), "dados");

    /// <summary>Pasta "dados" (fatia, fases, arquivos e, no pacote novo, pacotes\*.zip).</summary>
    public static string DataRoot => Root;

    static string Root => Env("DW4_DADOS") ?? (Editor ? @"D:\dw4-work" : Mobile ? MobileRoot : Path.Combine(ExeDir, "dados"));

    static string? Env(string name) => OS.GetEnvironment(name) is { Length: > 0 } v ? v : null;

    public static string Fatia => Env("DW4_FATIA") ?? Path.Combine(Root, "fatia");

    public static string Fases => Env("DW4_FASES") ?? Path.Combine(Root, "fases");

    /// <summary>Arquivos do disco lidos direto (area.afs, para a abertura). Sem DW4_ARQUIVOS: no editor, &lt;fases&gt;\..\extracted\files.</summary>
    public static string Arquivos => Env("DW4_ARQUIVOS")
        ?? (Editor || Env("DW4_FASES") != null
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Fases)) ?? "", "extracted", "files")
            : Path.Combine(Root, "arquivos"));

    /// <summary>Versão dos dados baixados no celular (gravada pelo MobileBoot); vazio fora do Android.</summary>
    public static string MobileVersion { get; set; } = "";

    /// <summary>
    /// O projeto se chamava "DW4 - fatia vertical" até a 0.5.0, e a Godot guarda user:// numa pasta com o nome do projeto
    /// (%APPDATA%\Godot\app_userdata\&lt;nome&gt;). Na primeira abertura com o nome novo, copia as vagas (accounts) e as
    /// configurações (config.cfg) da pasta antiga, sem apagar nada lá e sem sobrescrever o que já existir na nova.
    /// </summary>
    public static void MigrateOldUserData()
    {
        try
        {
            var now = ProjectSettings.GlobalizePath("user://").TrimEnd('/', '\\');
            var parent = Path.GetDirectoryName(now);
            if (parent == null) return;
            var old = Path.Combine(parent, "DW4 - fatia vertical");
            if (!Directory.Exists(old) || string.Equals(Path.GetFullPath(old), Path.GetFullPath(now), System.StringComparison.OrdinalIgnoreCase)) return;
            var oldCfg = Path.Combine(old, "config.cfg");
            var newCfg = Path.Combine(now, "config.cfg");
            if (File.Exists(oldCfg) && !File.Exists(newCfg)) { Directory.CreateDirectory(now); File.Copy(oldCfg, newCfg); }
            var oldAcc = Path.Combine(old, "accounts");
            if (!Directory.Exists(oldAcc)) return;
            foreach (var f in Directory.EnumerateFiles(oldAcc, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(now, "accounts", Path.GetRelativePath(oldAcc, f));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target);
            }
            GD.Print($"[dados] vagas e configurações copiadas de {old}");
        }
        catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException)
        {
            GD.PushWarning($"[dados] não deu para copiar os dados da versão antiga: {e.Message}");
        }
    }

    /// <summary>Versão publicada pelo launcher (versao.txt ao lado do exe, gravado por tools/scripts/publicar.ps1); vazio fora do pacote.</summary>
    public static string Version
    {
        get
        {
            try
            {
                var f = Path.Combine(ExeDir, "versao.txt");
                return !Editor && File.Exists(f) ? File.ReadAllText(f).Trim() : "";
            }
            catch (IOException) { return ""; }
        }
    }
}
