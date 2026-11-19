using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// DEPURAÇÃO (só no build Debug): DW4_TELA=status[:aba] mostra o menu de status (StatusScreen) com um jogador de teste — Guilmon
/// nível 30 com armas, armaduras, chips, discos e itens de evento — sem abrir fase, para conferir a tela por captura.
/// Abas: equip (padrão), equipchip (escolhendo chip), pasta, quebrar, tecnicas, skill.
/// </summary>
public partial class MainMenu
{
    void ShowStatusPreview(string tab)
    {
        var dbFile = GameFiles.DirExists(Path.Combine(Common, "db")) ? GameFiles.EnumerateFiles(Path.Combine(Common, "db"), "*_db.json").FirstOrDefault() : null;
        if (dbFile == null) { GD.PrintErr("[status] falta o *_db.json"); return; }
        var db = GameTables.FromJson(GameFiles.ReadAllText(dbFile));
        var texts = new System.Collections.Generic.Dictionary<string, string>();
        var tf = Path.Combine(Common, "textos", "textos.json");
        if (GameFiles.Exists(tf))
        {
            using var doc = JsonDocument.Parse(GameFiles.ReadAllText(tf));
            foreach (var t in doc.RootElement.GetProperty("texts").EnumerateArray()) texts[t.GetProperty("key").GetString() ?? ""] = t.GetProperty("text").GetString() ?? "";
        }
        int shinka = 2, level = 30;
        var skill = new[] { 120, 340, 90, 60, 40, 30, 260, 150, 80 };
        var forms = new Evolution();
        forms.Start(shinka);
        forms[shinka].Level = level;
        forms.Unlock(6);
        var inv = new Inventory { Bit = 12840, XData = 355 };
        foreach (var w in new[] { 266, 275, 274, 26, 51, 0 }) inv.Add(new ItemRef("weapon", w));
        foreach (var a in new[] { 0, 1 }) inv.Add(new ItemRef("armor1", a));
        foreach (var a in new[] { 0, 24 }) inv.Add(new ItemRef("armor2", a));
        foreach (var c in new[] { 0, 48, 120, 108 }) inv.Add(new ItemRef("abChip", c));
        foreach (var d in new[] { 0, 0, 0, 3, 7 }) inv.Add(new ItemRef("conItem", d));
        inv.Add(new ItemRef("impItem", 0));
        Equipment eq = new(274, 0, 0) { ReserveWeapons = [275, -1], WeaponChips = [48], ArmorChips = [120] };
        var font = BitmapFont.Load(Path.Combine(Common, "fonte"));
        var facesFile = Path.Combine(Common, "menu");
        var faceImg = GameFiles.DirExists(facesFile) ? GameFiles.EnumerateFiles(facesFile, "*charaicon.png").FirstOrDefault() : null;
        var screen = new StatusScreen(new StatusContext
        {
            Db = db, Inventory = () => inv, Equipment = () => eq, SetEquipment = e => eq = e,
            Preview = e => db.Player(shinka, level, e, skill), Player = () => db.Player(shinka, level, eq, skill),
            Numbers = () => (level, 1290, 210, PlayerGrowth.NextExp(level - 1) + 9000), Name = "Feelipe", ShinkaRow = shinka, Skill = () => skill,
            Forms = forms, TextByKey = k => texts.TryGetValue(k, out var t) ? t : null, Font = font,
            Faces = faceImg != null && Image.LoadFromFile(GameFiles.GodotPath(faceImg)) is { } img ? ImageTexture.CreateFromImage(img) : null,
        });
        _center.Visible = false;
        AddChild(screen);
        screen.CallDeferred(StatusScreen.MethodName.Preview, tab);
    }
}
