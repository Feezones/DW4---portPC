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

    static string Root => Editor ? @"D:\dw4-work" : Path.Combine(ExeDir, "dados");

    static string? Env(string name) => OS.GetEnvironment(name) is { Length: > 0 } v ? v : null;

    public static string Fatia => Env("DW4_FATIA") ?? Path.Combine(Root, "fatia");

    public static string Fases => Env("DW4_FASES") ?? Path.Combine(Root, "fases");

    /// <summary>Arquivos do disco lidos direto (area.afs, para a abertura). Sem DW4_ARQUIVOS: no editor, &lt;fases&gt;\..\extracted\files.</summary>
    public static string Arquivos => Env("DW4_ARQUIVOS")
        ?? (Editor || Env("DW4_FASES") != null
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Fases)) ?? "", "extracted", "files")
            : Path.Combine(Root, "arquivos"));

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
