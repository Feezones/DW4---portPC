using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Janela de troca de itens entre jogadores (DECISÃO DO PORT, pedido do responsável em 2026-11-07; o original só tem o "Give" da
/// pasta para o grupo local). Abre pelo "Trocar itens" do menu de interagir (G no teclado, Y no controle, JOGADOR no celular);
/// o outro aceita ou recusa. Cada lado põe até 8 itens (armas, armaduras, chips e discos livres, que o jogo deixa dar: ISJYOUDO,
/// sem equipados nem itens de enredo) e BIT, trava a oferta e, com as duas travadas, confirma. Qualquer mudança destrava os dois.
/// Quem troca é o servidor, nos saves da conta (GameServer.Trade.cs); aqui só se mostra e, no fim, se aplica o mesmo resultado na
/// GAMERAM e grava. Mouse, toque (botões grandes no celular) ou controle (cima/baixo e confirmar); Esc / B cancela.
/// </summary>
public partial class Fatia
{
    bool _tradeOpen;
    CanvasLayer? _tradeLayer;
    TradeView? _trade;
    bool _tradePicking;
    string _tradeNotice = "";
    string? _tradeFocus;

    bool TradeInput(InputEvent e)
    {
        if (!_tradeOpen) return false;
        if (e.IsActionPressed("ui_cancel"))
        {
            if (_tradePicking) { _tradePicking = false; RefreshTrade(); }
            else TradeCancel();
        }
        return true; // os botões já receberam o que era deles; o resto não chega ao jogo
    }

    void AskTrade(int target)
    {
        if (!Session.Active) { Toast("Entre com um personagem para trocar itens."); return; }
        if (!Online.Trade(new TradeCmd("ask", target, Session.Slot))) Toast("Você não está online.");
    }

    void ShowTradePrompt(SocialPrompt p)
    {
        OpenInteractWindow($"{p.FromTamer} quer trocar itens com você.",
        [
            ("Aceitar", Session.Active, Session.Active ? null : "Entre com um personagem para trocar itens.",
                () => Online.Trade(new TradeCmd("accept", p.FromId, Session.Slot))),
            ("Recusar", true, null, () => Online.Trade(new TradeCmd("decline", p.FromId, Session.Slot))),
        ]);
    }

    void OnTradeView(TradeView v)
    {
        switch (v.State)
        {
            case "ask":
                // Pergunta na fila das outras (amizade, grupo); com a pessoa no meio de outra janela, espera ela fechar.
                if (_tradeOpen) { Online.Trade(new TradeCmd("decline", v.OtherId, Session.Slot)); return; }
                OnSocialPrompt(new SocialPrompt("trade", v.OtherId, v.OtherName));
                return;
            case "asked":
                Toast($"Pedido de troca enviado para {v.OtherName}. Esperando a resposta...", 4);
                return;
            case "open" or "committing":
                if (_trade != null && _trade.OtherId != v.OtherId) return;
                _trade = v;
                _tradeNotice = v.Message;
                if (!_tradeOpen) OpenTrade(); else RefreshTrade();
                return;
            case "done":
                TradeDone(v);
                return;
            case "cancelled":
                if (_trade == null || _trade.OtherId == v.OtherId) CloseTrade();
                if (v.Message.Length > 0) { Toast(v.Message, 4); Session.SystemMessage(v.Message); }
                return;
        }
    }

    // A troca já foi feita no save do servidor: o mesmo aqui (sai o que dei, entra o que recebi) e grava com o novo TradeSeq.
    void TradeDone(TradeView v)
    {
        var give = Offer(v.MyItems, v.MyBit);
        var get = Offer(v.TheirItems, v.TheirBit);
        TradeSession.Move(give, get, _inventory);
        for (int i = 0; i < _inventory.QuickItems.Length; i++) // atalho de disco que acabou fica vazio
            if (_inventory.QuickItems[i] >= 0 && _inventory.Disks(_inventory.QuickItems[i]) == 0 && give.Items.Any(x => x.Table == "conItem" && x.Row == _inventory.QuickItems[i]))
                _inventory.QuickItems[i] = -1;
        Session.TradeApplied(v.Seq);
        CloseTrade();
        _ = SaveProgress();
        if (_quickOpen) RefreshQuickWindow();
        string got = get.Items.Count == 0 && get.Bit == 0 ? "nada" : Describe(get);
        string msg = $"Troca com {v.OtherName} feita. Você recebeu: {got}.";
        Toast($"Troca com {v.OtherName} feita!", 4);
        Session.SystemMessage(msg);
    }

    static TradeOffer Offer(TradeItem[] items, int bit)
    {
        var o = new TradeOffer { Bit = bit };
        o.Items.AddRange(items.Select(i => new ItemRef(i.Table, i.Row)));
        return o;
    }

    string Describe(TradeOffer o)
    {
        var parts = o.Items.GroupBy(i => i).Select(g => g.Count() > 1 ? $"{ItemName(g.Key)} x{g.Count()}" : ItemName(g.Key)).ToList();
        if (o.Bit > 0) parts.Add($"{o.Bit} BIT");
        return string.Join(", ", parts);
    }

    void TradeCancel()
    {
        if (_trade is { State: "committing" }) return; // já está gravando: termina sozinha
        if (_trade != null) Online.Trade(new TradeCmd("cancel", _trade.OtherId));
        CloseTrade();
    }

    void OpenTrade()
    {
        CloseInteract(showNext: false);
        _tradeOpen = true;
        _tradePicking = false;
        _move.Stop();
        RefreshTrade();
    }

    void CloseTrade()
    {
        _tradeLayer?.QueueFree();
        _tradeLayer = null;
        _tradeOpen = false;
        _trade = null;
        _tradePicking = false;
        _tradeNotice = "";
        if (_prompts.Count > 0) CallDeferred(MethodName.ShowNextPrompt);
    }

    /// <summary>Cópias que ainda dá para pôr na oferta: livres (sem equipar), que o jogo deixa dar, menos as já oferecidas.</summary>
    int TradeRoom(ItemRef item)
    {
        if (!TradeSession.Tradable(item)) return 0;
        if (_db != null && EquipRules.CannotGive(_db, _inventory, _equip, item, 1) != null) return 0;
        int offered = _trade?.MyItems.Count(i => i.Table == item.Table && i.Row == item.Row) ?? 0;
        return TradeSession.Free(_inventory, _equip, item) - offered;
    }

    List<(ItemRef Item, int Room)> TradeCandidates()
    {
        var list = new List<(ItemRef, int)>();
        foreach (var d in _inventory.Devices.Distinct()) if (TradeRoom(d) is > 0 and var n) list.Add((d, n));
        foreach (var (row, _) in _inventory.DiskCounts.OrderBy(kv => kv.Key))
            if (TradeRoom(new ItemRef("conItem", row)) is > 0 and var n) list.Add((new ItemRef("conItem", row), n));
        return list.OrderBy(x => EquipRules.SortKey(x.Item1)).ToList();
    }

    void RefreshTrade()
    {
        if (!_tradeOpen || _trade is not { } t) return;
        if (GetViewport().GuiGetFocusOwner() is Control f && _tradeLayer != null && _tradeLayer.IsAncestorOf(f)) _tradeFocus = f.Name;
        _tradeLayer?.QueueFree();
        _tradeLayer = new CanvasLayer { Layer = 16 };
        AddChild(_tradeLayer);
        bool touch = Touch && !_usingPad;
        int font = touch ? 26 : 20, small = touch ? 20 : 15;
        float rowH = touch ? 64 : 40;
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tradeLayer.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", QuickBox(QuickBorder));
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(touch ? 1100 : 860, 0) };
        box.AddThemeConstantOverride("separation", touch ? 10 : 6);
        panel.AddChild(box);
        bool committing = t.State == "committing";
        Button? firstFocus = null;

        Label Text(string s, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { Text = s, HorizontalAlignment = align, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", size);
            if (color is { } c) l.AddThemeColorOverride("font_color", c);
            return l;
        }
        Button Btn(string name, string label, bool enabled, Action act)
        {
            var b = new Button { Name = name, Text = label, Disabled = !enabled, CustomMinimumSize = new Vector2(0, rowH) };
            b.AddThemeFontSizeOverride("font_size", font);
            b.Pressed += act;
            return b;
        }

        box.AddChild(Text($"Troca com {t.OtherName}", font + 4, QuickGold, HorizontalAlignment.Center));

        if (_tradePicking)
        {
            box.AddChild(Text("Escolha o que oferecer (toque / clique para pôr na troca):", small));
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, touch ? 520 : 420), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            scroll.AddChild(list);
            box.AddChild(scroll);
            var cands = TradeCandidates();
            if (cands.Count == 0) list.AddChild(Text("Nada livre para trocar (itens equipados e de enredo não vão).", font));
            foreach (var (item, room) in cands)
            {
                string label = item.Table == "conItem" ? $"{ItemName(item)}  (x{room})" : room > 1 ? $"{ItemName(item)}  (x{room})" : ItemName(item);
                var b = Btn($"add_{item.Table}_{item.Row}", label, t.MyItems.Length < TradeOffer.MaxItems && !committing,
                    () => Online.Trade(new TradeCmd("add", t.OtherId, Table: item.Table, Row: item.Row)));
                b.Alignment = HorizontalAlignment.Left;
                list.AddChild(b);
                firstFocus ??= b;
            }
            var back = Btn("voltar", "Voltar para a troca", true, () => { _tradePicking = false; RefreshTrade(); });
            box.AddChild(back);
            firstFocus ??= back;
        }
        else
        {
            var cols = new HBoxContainer();
            cols.AddThemeConstantOverride("separation", 16);
            box.AddChild(cols);
            VBoxContainer Column(string title, TradeItem[] items, int bit, bool locked, bool confirmed, bool mine)
            {
                var c = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                c.AddThemeConstantOverride("separation", 4);
                var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                p.AddThemeStyleboxOverride("panel", QuickBox(confirmed ? new Color(0.4f, 0.9f, 0.4f) : locked ? QuickGold : QuickBorder));
                p.AddChild(c);
                cols.AddChild(p);
                c.AddChild(Text(title, font, QuickGold));
                string state = confirmed ? "Confirmou" : locked ? "Oferta travada" : "Montando a oferta...";
                c.AddChild(Text(state, small, confirmed ? new Color(0.5f, 1f, 0.5f) : locked ? QuickGold : new Color(0.75f, 0.75f, 0.85f)));
                if (items.Length == 0) c.AddChild(Text(mine ? "(nenhum item)" : "(nenhum item ainda)", small, new Color(0.7f, 0.7f, 0.8f)));
                foreach (var g in items.GroupBy(i => (i.Table, i.Row)))
                {
                    var item = new ItemRef(g.Key.Table, g.Key.Row);
                    string label = g.Count() > 1 ? $"{ItemName(item)} x{g.Count()}" : ItemName(item);
                    if (mine)
                    {
                        var b = Btn($"rem_{item.Table}_{item.Row}", label + "   ✕", !committing,
                            () => Online.Trade(new TradeCmd("remove", t.OtherId, Table: item.Table, Row: item.Row)));
                        b.Alignment = HorizontalAlignment.Left;
                        b.TooltipText = "Tirar da troca";
                        c.AddChild(b);
                    }
                    else c.AddChild(Text("• " + label, font));
                }
                c.AddChild(Text($"BIT: {bit}", font));
                return c;
            }
            var my = Column("Você oferece", t.MyItems, t.MyBit, t.MyLocked, t.MyConfirmed, true);
            Column($"{t.OtherName} oferece", t.TheirItems, t.TheirBit, t.TheirLocked, t.TheirConfirmed, false);

            var add = Btn("adicionar", $"+ Adicionar item ({t.MyItems.Length}/{TradeOffer.MaxItems})", t.MyItems.Length < TradeOffer.MaxItems && !committing,
                () => { _tradePicking = true; RefreshTrade(); });
            my.AddChild(add);
            firstFocus = add;

            // BIT: botões de passo (bom no toque e no controle) e campo para digitar.
            var bitRow = new HBoxContainer();
            bitRow.AddThemeConstantOverride("separation", 4);
            my.AddChild(bitRow);
            int have = _inventory.Bit;
            void SetBit(int v) => Online.Trade(new TradeCmd("bit", t.OtherId, Bit: Math.Clamp(v, 0, have)));
            foreach (var (label, step) in new[] { ("-1000", -1000), ("-100", -100), ("+100", 100), ("+1000", 1000) })
            {
                var b = Btn($"bit{step}", label, !committing, () => SetBit(t.MyBit + step));
                b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bitRow.AddChild(b);
            }
            var edit = new LineEdit { Name = "bit_campo", Text = t.MyBit.ToString(), PlaceholderText = "BIT", Editable = !committing,
                CustomMinimumSize = new Vector2(touch ? 160 : 120, rowH), VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number };
            edit.AddThemeFontSizeOverride("font_size", font);
            edit.TextSubmitted += s => { if (int.TryParse(s.Trim(), out var v)) SetBit(v); };
            edit.FocusExited += () => { if (int.TryParse(edit.Text.Trim(), out var v) && v != t.MyBit) SetBit(v); };
            bitRow.AddChild(edit);
            my.AddChild(Text($"Você tem {have} BIT.", small, new Color(0.75f, 0.75f, 0.85f)));

            if (_tradeNotice.Length > 0) box.AddChild(Text(_tradeNotice, small, new Color(1f, 0.55f, 0.45f), HorizontalAlignment.Center));
            string hint = committing ? "Gravando a troca no servidor..."
                : !t.MyLocked ? "Monte a sua oferta e trave. Qualquer mudança destrava os dois lados."
                : !t.TheirLocked ? $"Esperando {t.OtherName} travar a oferta."
                : !t.MyConfirmed ? "Confira as duas ofertas e confirme."
                : $"Esperando {t.OtherName} confirmar.";
            box.AddChild(Text(hint, small, new Color(0.85f, 0.85f, 0.95f), HorizontalAlignment.Center));

            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", 10);
            box.AddChild(actions);
            var lockB = Btn("travar", t.MyLocked ? "Oferta travada" : "Travar oferta", !t.MyLocked && !committing,
                () => Online.Trade(new TradeCmd("lock", t.OtherId)));
            var okB = Btn("confirmar", t.MyConfirmed ? "Confirmado" : "Confirmar troca", t.MyLocked && t.TheirLocked && !t.MyConfirmed && !committing,
                () =>
                {
                    // Grava antes: o servidor troca a partir do save guardado, que precisa estar igual ao que está na tela.
                    _ = SaveProgress();
                    Online.Trade(new TradeCmd("confirm", t.OtherId));
                });
            var cancelB = Btn("cancelar", "Cancelar", !committing, TradeCancel);
            foreach (var b in new[] { lockB, okB, cancelB }) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; actions.AddChild(b); }
            if (!lockB.Disabled) firstFocus = lockB;
            else if (!okB.Disabled) firstFocus = okB;
        }

        UseGameFont(box);
        var restore = _tradeFocus != null ? box.FindChild(_tradeFocus, true, false) as Control : null;
        if (restore is BaseButton { Disabled: true }) restore = null;
        (restore ?? firstFocus)?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // DEPURAÇÃO (DW4_ABRIR=troca | troca-lista): janela de troca com uma oferta de exemplo dos dois lados, sem servidor (para o
    // DW4_PRINT fotografar no PC e no celular, DW4_TOQUE=1).
    void OpenTradeDemo(bool picking)
    {
        if (_db == null) return;
        foreach (var it in new[] { new ItemRef("weapon", 2), new ItemRef("armor1", 1), new ItemRef("armor2", 2), new ItemRef("abChip", 0) }) _inventory.Add(it);
        for (int i = 0; i < 3; i++) _inventory.Add(new ItemRef("conItem", 0));
        _inventory.Add(new ItemRef("conItem", 3));
        if (_inventory.Bit < 5000) _inventory.Bit = 5000;
        _trade = new TradeView(-7, "Agumon", "open", [new("armor1", 1), new("conItem", 0), new("conItem", 0)], 1200, true, false,
            [new("weapon", 5), new("conItem", 3)], 300, true, true);
        OpenTrade();
        _tradePicking = picking;
        RefreshTrade();
    }
}
