using System.Text;
using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>Idioma dos textos do jogo (DECISÃO DO PORT, docs/decisoes-port-pc.md: o NTSC-U só tem inglês).</summary>
public enum GameLanguage
{
    /// <summary>Os textos do usa_pack e das tabelas, como no disco.</summary>
    English = 0,
    /// <summary>Falas, interface, nomes de itens/técnicas e descrições em português.</summary>
    Portuguese = 1,
    /// <summary>Falas e interface em português; nomes de itens, técnicas, armas etc. ficam no original em inglês.</summary>
    PortugueseOriginalNames = 2,
}

/// <summary>
/// Tradução dos textos do jogo. O texto original continua vindo da cópia de trabalho (textos.json de cada fase, tabelas do db);
/// a tradução fica num arquivo próprio (<c>&lt;fatia&gt;\idioma\pt-br.json</c>, fora do repositório como os outros dados):
/// <code>
/// { "textos": { "mswk10001": "…" },        // falas e interface, pela chave do beUnicodeObj (String@2)
///   "nomes": { "Long Sword": "Espada Longa" }, // coluna NAME das tabelas (itens, armas, armaduras, chips, técnicas)
///   "descricoes": { "…": "…" } }           // coluna TXT das tabelas
/// </code>
/// Nas falas, um nome de item/técnica vai entre chaves ({Long Sword}): no português completo vira o nome traduzido (tabela
/// "nomes"), no misto fica o original. Chave sem tradução mostra o inglês (nunca some texto). Digimon não são traduzidos
/// (o shinka fica de fora), como nas versões oficiais.
/// </summary>
public static class GameText
{
    public static GameLanguage Language { get; set; } = GameLanguage.English;

    static Dictionary<string, string> s_texts = [], s_names = [], s_descriptions = [];

    /// <summary>Tabelas cujo NAME não se traduz (formas de Digimon e cartas, que são Digimon).</summary>
    static readonly HashSet<string> UntranslatedTables = new(StringComparer.OrdinalIgnoreCase) { "shinka", "card", "enemy" };

    public static int TextCount => s_texts.Count;
    public static int NameCount => s_names.Count;

    /// <summary>Lê o JSON da tradução (substitui a anterior). Devolve false se o texto não é um JSON válido.</summary>
    public static bool Load(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            s_texts = Map(doc.RootElement, "textos");
            s_names = Map(doc.RootElement, "nomes");
            s_descriptions = Map(doc.RootElement, "descricoes");
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static void Clear() { s_texts = []; s_names = []; s_descriptions = []; }

    static Dictionary<string, string> Map(JsonElement root, string name)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object)
            foreach (var p in o.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } s) d[p.Name] = s;
        return d;
    }

    /// <summary>Fala/interface pela chave (mswk…); <paramref name="original"/> é o inglês da fase, usado no inglês ou sem tradução.</summary>
    public static string Text(string? key, string original)
    {
        if (Language == GameLanguage.English || key == null || !s_texts.TryGetValue(key, out var pt)) return original;
        return ResolveNames(pt.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
    }

    /// <summary>Célula de tabela mostrada ao jogador: NAME (só no português completo) e TXT (nos dois portugueses, exceto nomes).</summary>
    public static string Cell(string table, string column, string original)
    {
        if (Language == GameLanguage.English || original.Length == 0) return original;
        switch (column)
        {
            case "NAME" when Language == GameLanguage.Portuguese && !UntranslatedTables.Contains(table):
                return TranslateName(original);
            case "TXT":
                return s_descriptions.TryGetValue(Normalize(original), out var t) ? ResolveNames(t) : original;
            default:
                return original;
        }
    }

    /// <summary>Nome de item/técnica solto (fora de tabela): traduzido só no português completo.</summary>
    public static string Name(string original) => Language == GameLanguage.Portuguese ? TranslateName(original) : original;

    /// <summary>
    /// Nome pela tabela "nomes": inteiro ou pelo miolo, mantendo o ícone da frente (FATO: ∈ arma, ∋/∨ armaduras, ∧ chips, ∞ disco
    /// de técnica; ⊂⊃∪∩ elementos) e o grau do fim (" α".." ε", com "+120" etc. nos chips).
    /// </summary>
    static string TranslateName(string original)
    {
        string s = original.Trim();
        if (s_names.TryGetValue(s, out var whole)) return whole;
        int a = 0;
        while (a < s.Length && !char.IsLetterOrDigit(s[a]) && s[a] != '?') a++;
        int b = s.Length;
        int plus = s.LastIndexOf('+');
        if (plus > a && s[(plus + 1)..].All(char.IsDigit)) b = plus;
        if (b - 1 > a && "αβγδε".Contains(s[b - 1])) b--;
        string core = s[a..b].TrimEnd();
        return core.Length > 0 && s_names.TryGetValue(core, out var n) ? s[..a] + n + s[(a + core.Length)..] : original;
    }

    /// <summary>{Nome} → nome traduzido (português completo) ou o original (misto).</summary>
    static string ResolveNames(string s)
    {
        if (s.IndexOf('{') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            int close;
            if (s[i] == '{' && (close = s.IndexOf('}', i + 1)) > i)
            {
                string name = s[(i + 1)..close];
                sb.Append(Language == GameLanguage.Portuguese ? TranslateName(name) : name);
                i = close;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>Chave das descrições: quebras de linha uniformes e sem espaços nas pontas.</summary>
    public static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
}
