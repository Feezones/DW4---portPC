using System.IO;
using System.Linq;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Loja de cartas (stagIS11, fase de menu): abrir B. Pack pela regra do jogo (<see cref="CardPack"/>, 0x8039ACC4) com a tabela
/// "card" de &lt;fases&gt;\cartas (dw4 fase-exportar, area.afs #621). DECISÃO DA FATIA: janela da Godot com as 3 cartas e a cor
/// da raridade no lugar do j_shbooster/j_shcardNN; o álbum (menu de status) ainda não está no port.
/// </summary>
public partial class Fatia
{
    GameTables? _cardDb;
    bool _cardTried;

    GameTables.Table? CardTable()
    {
        if (!_cardTried)
        {
            _cardTried = true;
            var dir = Path.Combine(_dir, "..", "cartas");
            var file = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*_db.json").FirstOrDefault() : null;
            _cardDb = file != null ? GameTables.FromJson(File.ReadAllText(file)) : null;
            if (_cardDb == null) _problems.Add("falta a tabela das cartas (dw4 fase-exportar → <fases>/cartas): loja de cartas desligada");
        }
        return _cardDb != null && _cardDb.Contains("card") ? _cardDb["card"] : null;
    }

    void OpenCardShop(string result = "")
    {
        var card = CardTable();
        if (card == null) { SaveWindow("Loja de cartas: falta a tabela das cartas (dw4 fase-exportar).", [("OK", CloseSaveWindow)]); return; }
        var list = Ram.Players[0].Cards;
        int packs = _inventory.Disks(CardPack.PackRow);
        string head = $"B. Pack: {packs}    Cartas: {list.Count(b => b != 0)}/{card.Rows}" + (result.Length > 0 ? $"\n\n{result}" : "");
        SaveWindow(head, packs > 0
            ? [("Abrir um B. Pack", OpenPack), ("Sair", CloseSaveWindow)]
            : [("Sem B. Pack.", CloseSaveWindow)]);
    }

    void OpenPack()
    {
        var card = CardTable()!;
        var list = Ram.Players[0].Cards;
        var before = list.ToArray();
        var rows = CardPack.Open(_inventory, card, list, _mt);
        string[] rare = ["comum", "incomum", "rara"];
        var lines = rows.Select(r => $"{card.Text(r, "NAME")}  ({rare[System.Math.Clamp(card.Int(r, "RARE"), 0, 2)]}{(before[r] != 0 ? ", repetida" : "")})");
        ScriptLog($"B. Pack: {string.Join(", ", rows)}");
        OpenCardShop(string.Join("\n", lines));
    }
}
