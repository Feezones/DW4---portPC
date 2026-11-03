using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Menus do banco, X-data e LABO com a arte das lojas (j_shback, j_shbase, j_shwin) e a fonte do jogo.
/// FATO: o j_shwin tem as variantes SQ_BITPUTINOPEN/SQ_BITDRAWOUTOPEN, SQ_XDATAITEMOPEN/SQ_XDATASTATUSOPEN e
/// SQ_SINKAONOPEN/SQ_SINKAOFFOPEN, e as classes beNDMWShopCtrlEditBit, XdataChip, Evolve as usam (fase5-lojas.md).
/// HIPÓTESE: a lista fica no mesmo lugar da loja (12 linhas de 16 px). DESCONHECIDO: o texto de cada variante no original.
/// Sem a pasta loja, <see cref="Menu"/> cai na caixa da Godot (SaveWindow).
/// Controles: cima/baixo escolhe, ataque confirma, esquerda/direita mexe no valor da linha (quando tem) ou troca a página,
/// K/Backspace/B volta.
/// </summary>
public partial class Fatia
{
    /// <summary>Linha do menu: rótulo, valor à direita, escolha e ajuste com esquerda/direita (−1/+1).</summary>
    sealed record ArtRow(string Label, string Value, Action? Pick, Action<int>? Adjust = null);

    sealed class ArtMenuState
    {
        public string Variant = "", Title = "", Help = "";
        public List<ArtRow> Rows = [];
        public int Cursor, Page;
        public Action? Back;
    }

    ArtMenuState? _artMenu;
    readonly HashSet<string> _artHeld = [];

    bool SubWindowOpen => _saveLayer != null || _artMenu != null;

    /// <summary>Janela de escolhas: com a arte, primeira linha do texto vira o título e o resto a ajuda; sem ela, SaveWindow.</summary>
    void Menu(string variant, string text, (string Label, Action Pick)[] options, Action? back = null)
    {
        if (!ShopArtReady()) { SaveWindow(text, options); return; }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        // Reaberto pela própria escolha (mesma variante e mesmas linhas): o cursor fica onde estava.
        int keep = _artMenu is { } old && old.Variant == variant && old.Rows.Count == options.Length ? old.Page * ShopPage + old.Cursor : 0;
        OpenArtMenu(variant, lines[0], string.Join("  ", lines.Skip(1).Where(l => l.Length > 0)),
            options.Select(o => { var p = o.Label.Split('\t', 2); return new ArtRow(p[0], p.Length > 1 ? p[1] : "", o.Pick); }).ToList(),
            back ?? options.LastOrDefault().Pick, keep);
    }

    void OpenArtMenu(string variant, string title, string help, List<ArtRow> rows, Action? back, int cursor = 0)
    {
        if (_saveLayer != null) { _saveLayer.QueueFree(); _saveLayer = null; }
        bool reopen = _artMenu != null; // trocar de menu não fecha a arte (a variante igual não recomeça a abertura)
        _padEnabled = false;
        _artMenu = new ArtMenuState { Variant = variant, Title = title, Help = help, Rows = rows, Back = back ?? CloseSaveWindow };
        _artMenu.Page = Math.Clamp(cursor, 0, Math.Max(0, rows.Count - 1)) / ShopPage;
        _artMenu.Cursor = Math.Clamp(cursor, 0, Math.Max(0, rows.Count - 1)) % ShopPage;
        if (!reopen)
        {
            _artHeld.Clear();
            foreach (var k in ShopKeys()) _artHeld.Add(k); // o botão que abriu ainda pode estar apertado
        }
        DrawArtMenu();
    }

    void CloseArtMenu()
    {
        if (_artMenu == null) return;
        _artMenu = null;
        _padEnabled = true;
        if (!ShopOpen) UpdateShopArt("", [], "", "", 0);
    }

    void DrawArtMenu()
    {
        if (_artMenu is not { } m) return;
        var lines = Enumerable.Range(m.Page * ShopPage, ShopPage)
            .Select(i => i < m.Rows.Count ? (m.Rows[i].Value.Length > 0 ? $"{m.Rows[i].Label}\t{m.Rows[i].Value}" : m.Rows[i].Label) : "").ToList();
        int pages = Math.Max(1, (m.Rows.Count + ShopPage - 1) / ShopPage);
        UpdateShopArt(m.Variant, lines, pages > 1 ? $"{m.Title}   {m.Page + 1}/{pages}" : m.Title, m.Help, m.Cursor);
    }

    /// <summary>Menu aberto: consome a entrada e segura o resto do quadro (como a loja).</summary>
    bool TickArtMenu()
    {
        if (_artMenu is not { } m) return false;
        var now = ShopKeys().ToHashSet();
        bool Pressed(string k) => now.Contains(k) && !_artHeld.Contains(k);
        _artHeld.Clear();
        foreach (var k in now) _artHeld.Add(k);
        int count = m.Rows.Count, pages = Math.Max(1, (count + ShopPage - 1) / ShopPage);
        int inPage = Math.Max(1, Math.Min(ShopPage, count - m.Page * ShopPage));
        int index = m.Page * ShopPage + m.Cursor;
        var row = index < count ? m.Rows[index] : null;
        if (Pressed("close")) { m.Back?.Invoke(); return true; }
        if (Pressed("up")) m.Cursor = (m.Cursor + inPage - 1) % inPage;
        if (Pressed("down")) m.Cursor = (m.Cursor + 1) % inPage;
        foreach (var (key, dir) in new[] { ("left", -1), ("right", 1) })
        {
            if (!Pressed(key)) continue;
            if (row?.Adjust != null) row.Adjust(dir);
            else if (pages > 1) { m.Page = (m.Page + pages + dir) % pages; m.Cursor = Math.Min(m.Cursor, Math.Min(ShopPage, count - m.Page * ShopPage) - 1); }
        }
        if (Pressed("ok") && row?.Pick != null) { row.Pick(); return true; } // a escolha pode abrir outro menu
        if (_artMenu == m) DrawArtMenu();
        return true;
    }
}
