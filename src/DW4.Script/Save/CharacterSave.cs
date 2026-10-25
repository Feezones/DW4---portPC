using System.Text.Json;
using System.Text.Json.Serialization;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Script.Save;

/// <summary>
/// Personagem gravado numa vaga da conta. DECISÃO DO PORT (docs/decisoes-port-pc.md): formato JSON próprio, não o do cartão de
/// memória. Guarda o registro do jogador na GAMERAM (formas com nível/EXP/skills/UPCNT, inventário, cartas), o estado comum
/// que no grupo é compartilhado (FLAG, COUNTER, DIFFICULTY) e onde o personagem está.
/// </summary>
public sealed class CharacterSave
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>Nome dado na criação (mswkMem0232 "Enter your Digimon's name.").</summary>
    public string Name { get; set; } = "";
    /// <summary>Forma inicial escolhida (DIGIMONNO 0..3; FATO: as 4 escolhíveis do GAMEDATAINIT).</summary>
    public int Starter { get; set; }
    /// <summary>Forma atual (DIGIMONNO) e o modelo dela (MODEL_NAME da shinka, ex. p_girumon).</summary>
    public int CurrentForm { get; set; } = -1;
    public string Model { get; set; } = "";
    public int Difficulty { get; set; }
    public int CameraMode { get; set; }
    public int LightMode { get; set; }
    public string Stage { get; set; } = "";
    public int Entry { get; set; }
    public DateTime SavedAt { get; set; }
    public List<FormSave> Forms { get; set; } = [];
    public int Bit { get; set; }
    public List<ItemSave> Devices { get; set; } = [];
    public int BankBit { get; set; }
    public List<ItemSave> BankDevices { get; set; } = [];
    public int XData { get; set; }
    public Dictionary<int, int> Disks { get; set; } = [];
    public List<int> ImportantItems { get; set; } = [];
    /// <summary>FLAG (1024 bytes) e CARDLIST (50 bytes) em base64; COUNTER (16 inteiros).</summary>
    public string Flags { get; set; } = "";
    public string Cards { get; set; } = "";
    public int[] Counters { get; set; } = [];

    /// <summary>Nível da forma atual (para a tela de vagas).</summary>
    [JsonIgnore]
    public int Level => Forms.FirstOrDefault(f => f.DigimonNo == CurrentForm)?.Level ?? 1;

    public sealed class FormSave
    {
        public int DigimonNo { get; set; }
        public bool Unlocked { get; set; }
        public int Level { get; set; } = 1;
        public long Exp { get; set; }
        public int[] Skill { get; set; } = [];
        public int[] UpCount { get; set; } = [];
    }

    public sealed record ItemSave(string Table, int Row);

    /// <summary>Personagem novo: forma inicial liberada no nível 1 (GAMEDATAINIT), na <paramref name="stage"/>.</summary>
    public static CharacterSave New(string name, int starter, string model, int difficulty, string stage, int entry)
    {
        var ram = new GameRam { Difficulty = difficulty };
        ram.Players[0].Forms.Start(starter);
        var s = Capture(ram, 0, name, stage, entry);
        s.Starter = starter;
        s.Model = model;
        return s;
    }

    /// <summary>Copia o jogador <paramref name="player"/> e o estado comum da GAMERAM.</summary>
    public static CharacterSave Capture(GameRam ram, int player, string name, string stage, int entry)
    {
        var p = ram.Players[player];
        var s = new CharacterSave
        {
            Name = name, CurrentForm = p.Forms.Current, Difficulty = ram.Difficulty, CameraMode = ram.CameraMode, LightMode = ram.LightMode,
            Stage = stage, Entry = entry, SavedAt = DateTime.UtcNow, Bit = p.Items.Bit,
            XData = p.Items.XData, BankBit = p.Items.BankBit, BankDevices = p.Items.BankDevices.Select(d => new ItemSave(d.Table, d.Row)).ToList(),
            Devices = p.Items.Devices.Select(d => new ItemSave(d.Table, d.Row)).ToList(),
            Disks = p.Items.DiskCounts.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value),
            ImportantItems = p.Items.ImportantFlags.OrderBy(x => x).ToList(),
            Flags = Convert.ToBase64String(ram.Flags), Cards = Convert.ToBase64String(p.Cards), Counters = (int[])ram.Counters.Clone(),
        };
        for (int d = 0; d < Evolution.Forms; d++)
        {
            var f = p.Forms[d];
            s.Forms.Add(new FormSave { DigimonNo = d, Unlocked = f.Unlocked, Level = f.Level, Exp = f.Exp, Skill = (int[])f.Skill.Clone(), UpCount = (int[])f.UpCount.Clone() });
        }
        return s;
    }

    /// <summary>Põe o personagem no jogador <paramref name="player"/> da GAMERAM (formas e inventário novos; estado comum por cima).</summary>
    public void Apply(GameRam ram, int player)
    {
        var p = ram.Players[player];
        var forms = new Evolution();
        foreach (var f in Forms.Where(f => f.DigimonNo is >= 0 and < Evolution.Forms))
        {
            var st = forms[f.DigimonNo];
            st.Unlocked = f.Unlocked;
            st.Level = f.Level;
            st.Exp = f.Exp;
            Array.Copy(f.Skill, st.Skill, Math.Min(f.Skill.Length, st.Skill.Length));
            Array.Copy(f.UpCount, st.UpCount, Math.Min(f.UpCount.Length, st.UpCount.Length));
        }
        forms.Restore(CurrentForm);
        p.Forms = forms;
        var inv = new Inventory { Bit = Bit, XData = XData };
        foreach (var d in Devices) inv.Add(new ItemRef(d.Table, d.Row));
        foreach (var (row, n) in Disks) for (int i = 0; i < n; i++) inv.Add(new ItemRef("conItem", row));
        foreach (var row in ImportantItems) inv.Add(new ItemRef("impItem", row));
        inv.RestoreBank(BankBit, BankDevices.Select(d => new ItemRef(d.Table, d.Row)));
        p.Items = inv;
        CopyBase64(Cards, p.Cards);
        CopyBase64(Flags, ram.Flags);
        Array.Clear(ram.Counters);
        Array.Copy(Counters, ram.Counters, Math.Min(Counters.Length, ram.Counters.Length));
        ram.Difficulty = Difficulty;
        ram.CameraMode = CameraMode;
        ram.LightMode = LightMode;
    }

    static void CopyBase64(string text, byte[] target)
    {
        Array.Clear(target);
        if (text.Length == 0) return;
        var bytes = Convert.FromBase64String(text);
        Array.Copy(bytes, target, Math.Min(bytes.Length, target.Length));
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Lê um save; null se o texto não é um save desta versão (ou anterior).</summary>
    public static CharacterSave? FromJson(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<CharacterSave>(json, Options);
            return s != null && s.Version is >= 1 and <= CurrentVersion ? s : null;
        }
        catch (Exception e) when (e is JsonException or FormatException) { return null; }
    }
}
