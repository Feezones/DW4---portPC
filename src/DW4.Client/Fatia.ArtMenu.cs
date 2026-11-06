using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Telas de lista das lojas, do banco, do X-data, do Digi-Lab e do avaliador: todas no <see cref="MenuScreen"/> (layout próprio no
/// estilo do menu do TAB, DECISÃO DO PORT de 2026-11-06). Antes eram a janela j_shwin do jogo (variantes SQ_AWINOPEN, SQ_BWINOPEN,
/// SQ_XDATA*, SQ_SINKA*, SQ_BITPUTINOPEN…; fase5-lojas.md) com 11 linhas por página, só de teclado, e o usuário achou ruim de usar.
/// As regras (estoque, preços, banco, X-data, troca de forma) não mudaram.
/// </summary>
public partial class Fatia
{
    MenuScreen? _listScreen;
    CanvasLayer? _listLayer;
    bool _artMenuOpen, _listLockedPad;

    bool SubWindowOpen => _saveLayer != null || _artMenuOpen;

    /// <summary>Mostra a tela (abre na primeira vez; depois só troca o conteúdo).</summary>
    void ShowList(MenuPage page)
    {
        if (_saveLayer != null) { _saveLayer.QueueFree(); _saveLayer = null; }
        if (!ShopOpen) _artMenuOpen = true;
        if (_padEnabled) { _padEnabled = false; _listLockedPad = true; }
        if (_listScreen == null)
        {
            _listLayer ??= new CanvasLayer { Layer = 18 }; // por cima do HUD (atalhos, técnicas, vida)
            if (!_listLayer.IsInsideTree()) AddChild(_listLayer);
            _listScreen = new MenuScreen(GameFont());
            _listScreen.CloseRequested += () => { if (ShopOpen) CloseShop(); else CloseSaveWindow(); };
            _listLayer.AddChild(_listScreen);
        }
        // Atalhos de item e técnicas no pé da tela ficavam por baixo da ajuda (o quadro deles só se atualiza com a fase andando).
        if (_quickHud != null) _quickHud.Visible = false;
        if (_techBar != null) _techBar.Visible = false;
        if (DiagNpc) GD.Print($"[menu] {page.Title} aba {page.Tab}: {page.Rows.Count} linhas, cursor {page.Cursor} {page.Message}");
        _listScreen.Show(page);
    }

    void CloseListScreen()
    {
        if (_listScreen == null) return;
        _listScreen.QueueFree();
        _listScreen = null;
        if (_listLockedPad) { _padEnabled = true; _listLockedPad = false; }
    }

    /// <summary>Janela de escolhas simples: primeira linha do texto vira o título, o resto o quadro da direita; "\t" separa o valor.</summary>
    void Menu(string text, (string Label, Action Pick)[] options, Action? back = null)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        ShowList(new MenuPage
        {
            Title = lines[0],
            Intro = new MenuInfo { Lines = lines.Skip(1).Where(l => l.Length > 0).Select(l => new MenuLine(l)).ToList() },
            Rows = options.Select(o => { var p = o.Label.Split('\t', 2); return new MenuRow { Label = p[0], Value = p.Length > 1 ? p[1] : "", Pick = o.Pick }; }).ToList(),
            Back = back ?? options.LastOrDefault().Pick,
        });
    }

    void CloseArtMenu()
    {
        if (!_artMenuOpen) return;
        if (DiagNpc) GD.Print("[menu] fechou");
        _artMenuOpen = false;
        if (!ShopOpen) CloseListScreen();
    }

    /// <summary>Menu aberto: o jogador fica parado (a tela lê as teclas sozinha).</summary>
    bool TickArtMenu() => _artMenuOpen;

    // ---------- textos e quadro de item compartilhados pelas telas ----------

    string StatusText(int index, string fallback) => StatusScreen.TextOf(k => TextKey(k, "") is { Length: > 0 } t ? t : null, index, fallback);

    string ItemName(ItemRef i) => _db != null && _db.Contains(i.Table) ? _db[i.Table].Text(i.Row, "NAME") : $"{i.Table} {i.Row}";

    /// <summary>Itens iguais juntos (na ordem em que aparecem) com a quantidade.</summary>
    static List<(ItemRef Item, int Count)> Grouped(IEnumerable<ItemRef> items) =>
        items.GroupBy(i => i).Select(g => (g.Key, g.Count())).ToList();

    /// <summary>Quadro do item: nome, texto, números e, para arma e armadura, os atributos se equipar no lugar do atual.</summary>
    /// <summary>
    /// O que o item pede e se o jogador tem (FATO, GameTables.CanEquipWeapon/CanEquipArmor e EquipRules.ChipFits): arma = o Digimon
    /// no SOUBI e a skill da arma (SKILL) com pelo menos SKILLPOINT; armadura = nível ≥ LVPOINT; chip = lugar na arma (WU) ou na
    /// armadura ∋ (BLUEAB).
    /// </summary>
    static readonly string[] SkillNamesEn = ["Blunt", "Slash", "Stab", "Bash", "Shot", "Crush", "Blast", "Heal", "Force"];

    List<(string Text, bool Ok)> Requirements(ItemRef it)
    {
        var list = new List<(string, bool)>();
        if (_db == null || !_db.Contains(it.Table)) return list;
        var t = _db[it.Table];
        string me = _shinkaRow >= 0 ? _db["shinka"].Text(_shinkaRow, "NAME") : "seu Digimon";
        switch (it.Table)
        {
            case "weapon":
            {
                int soubi = t.Int(it.Row, "SOUBI");
                bool mine = _shinkaRow >= 0 && (soubi & (1 << _shinkaRow)) != 0;
                var who = Enumerable.Range(0, Math.Min(16, _db["shinka"].Rows)).Where(d => (soubi & (1 << d)) != 0).Select(d => _db["shinka"].Text(d, "NAME")).ToList();
                list.Add((mine ? $"Serve para {me}" : $"Não serve para {me}" + (who.Count is > 0 and <= 4 ? $" (só {string.Join(", ", who)})" : ""), mine));
                int k = t.Int(it.Row, "SKILL"), need = t.Int(it.Row, "SKILLPOINT"), have = k >= 0 && k < _skill.Length ? _skill[k] : 0;
                string skill = k is >= 0 and < 9 ? StatusText(71 + k, SkillNamesEn[k]) : "skill";
                list.Add(($"Skill {skill} {need} (você tem {have})", have >= need));
                break;
            }
            case "armor1" or "armor2":
            {
                int need = t.Int(it.Row, "LVPOINT");
                list.Add(($"Nível {need} (você é {_level})", _level >= need));
                break;
            }
            case "abChip":
            {
                bool wu = t.Int(it.Row, "WU") != 0, blue = t.Int(it.Row, "BLUEAB") != 0;
                list.Add((wu && blue ? "Vai na arma ou na armadura ∋" : wu ? "Vai só na arma" : blue ? "Vai só na armadura ∋" : "Não encaixa em nada", wu || blue));
                break;
            }
        }
        return list;
    }

    /// <summary>Marca da linha da loja: diferença do atributo principal para o equipado (arma: força; armadura: defesa) ou o que falta.</summary>
    (string Tag, Color? Color) ItemTag(ItemRef it)
    {
        if (_db == null) return ("", null);
        var miss = Requirements(it).Where(r => !r.Ok).Select(r => r.Text).FirstOrDefault();
        if (miss != null)
        {
            var t = _db[it.Table];
            int k = it.Table == "weapon" ? t.Int(it.Row, "SKILL") : -1;
            string tag = miss.StartsWith("Não serve", StringComparison.Ordinal) ? "não serve"
                : it.Table == "weapon" && k is >= 0 and < 9 ? $"{StatusText(71 + k, SkillNamesEn[k])} {t.Int(it.Row, "SKILLPOINT")}"
                : it.Table is "armor1" or "armor2" ? $"Lv {t.Int(it.Row, "LVPOINT")}" : "falta";
            return (tag, new Color(1f, 0.45f, 0.45f));
        }
        if (it.Table is not ("weapon" or "armor1" or "armor2") || _equip == null || _pf == null) return ("", null);
        var after = it.Table == "weapon" ? EquipRules.SetSocket(_db, _equip, 0, it.Row)
            : EquipRules.SetArmor(_db, _equip, it.Table == "armor1" ? EquipSlot.Armor : EquipSlot.Sub, it.Row);
        if (after == null) return ("", null);
        var then = PreviewFighter(after);
        int d = it.Table == "weapon" ? then.Tsuyosa - _pf.Tsuyosa : it.Table == "armor1" ? then.Mamori - _pf.Mamori
            : then.Tsuyosa + then.Mamori + then.Kashikosa + then.Seishin + then.Subayasa - (_pf.Tsuyosa + _pf.Mamori + _pf.Kashikosa + _pf.Seishin + _pf.Subayasa);
        return d == 0 ? ("=", new Color(0.62f, 0.62f, 0.75f)) : ($"{(d > 0 ? "+" : "")}{d}", d > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.45f, 0.45f));
    }

    MenuInfo ItemMenuInfo(ItemRef it, params MenuLine[] extra)
    {
        var info = new MenuInfo { Title = ItemName(it) };
        if (_db == null) return info;
        var (txt, nums) = StatusScreen.ItemText(_db, it, StatusText);
        if (txt.Length > 0) info.Lines.Add(new MenuLine(txt));
        if (nums.Length > 0) info.Lines.Add(new MenuLine(nums, MenuTone.Info));
        info.Lines.AddRange(extra);
        info.Requirements.AddRange(Requirements(it));
        if (it.Table is "weapon" or "armor1" or "armor2" && _equip != null && _pf != null)
        {
            var after = it.Table == "weapon" ? EquipRules.SetSocket(_db, _equip, 0, it.Row)
                : EquipRules.SetArmor(_db, _equip, it.Table == "armor1" ? EquipSlot.Armor : EquipSlot.Sub, it.Row);
            if (after != null)
            {
                var now = _pf;
                var then = PreviewFighter(after);
                (string, int, int)[] stats =
                [
                    (StatusText(14, "Strength"), now.Tsuyosa, then.Tsuyosa), (StatusText(15, "Defense"), now.Mamori, then.Mamori),
                    (StatusText(16, "Wisdom"), now.Kashikosa, then.Kashikosa), (StatusText(17, "Spirit"), now.Seishin, then.Seishin),
                    (StatusText(18, "Speed"), now.Subayasa, then.Subayasa), ("HP", now.MaxHp, then.MaxHp), ("MP", now.MaxMp, then.MaxMp),
                    (StatusText(19, "⊂Resist"), now.Res(0), then.Res(0)), (StatusText(20, "⊃Resist"), now.Res(1), then.Res(1)),
                    (StatusText(21, "∪Resist"), now.Res(2), then.Res(2)), (StatusText(22, "∩Resist"), now.Res(3), then.Res(3)),
                ];
                var changed = stats.Where(s => s.Item2 != s.Item3).ToList();
                var slot = it.Table == "weapon" ? EquipRules.SocketRows(_equip)[0] : it.Table == "armor1" ? _equip.Armor1Row : _equip.Armor2Row;
                string current = slot >= 0 ? ItemName(new ItemRef(it.Table, slot)) : "nada equipado";
                int up = changed.Count(s => s.Item3 > s.Item2), down = changed.Count(s => s.Item3 < s.Item2);
                string verdict = changed.Count == 0 ? "igual" : down == 0 ? "MELHOR" : up == 0 ? "PIOR" : "melhor em uns, pior em outros";
                info.CompareTitle = changed.Count == 0 ? $"Igual ao que você usa ({current})." : $"Comparado com {current}: {verdict}";
                info.Stats.AddRange(changed.Take(8));
            }
        }
        return info;
    }
}
