using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Itens no meio da fase. FATO (textos do tutorial da stagML02, mswk201984/204308): o botão √ abre a Item Window, o direcional
/// escolhe o disco e o ≪ usa. FATO: o jogador tem a SQ_ITEM, o efeito x_250curehit01 sai na ação ITEMC1 (SQ_ATCK01, disparos.json)
/// e o disco cura pelo HP/MP da conItem (Inventory.UseDisk, 0x80349B0C).
/// DECISÃO DO PORT (PC, pedido do usuário em 2026-11-02): a janela (tecla I / B no controle) também marca 5 atalhos (apertar 1–5
/// com o disco escolhido), as teclas 1–5 usam o disco do atalho, e a HUD mostra os 5 atalhos com o nome curto e a quantidade
/// (os ícones de disco do jogo não foram achados na arte exportada). Os atalhos vão no save da vaga.
/// </summary>
public partial class Fatia
{
    CanvasLayer? _quickLayer;
    Control? _quickHud, _quickWindow;
    readonly List<Label> _quickHudLabels = [];
    readonly List<Control> _quickBoxes = [];
    VBoxContainer? _quickRows;
    int _quickCursor;
    bool _quickOpen;
    string _quickHudKey = "";

    static readonly Color QuickBg = new(0.08f, 0.08f, 0.2f, 0.85f), QuickBorder = new(0.45f, 0.42f, 0.95f), QuickGold = new(1f, 0.85f, 0.35f);

    static StyleBoxFlat QuickBox(Color border, int width = 2)
    {
        var s = new StyleBoxFlat { BgColor = QuickBg, BorderColor = border, CornerDetail = 6 };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(8);
        s.SetContentMarginAll(6);
        return s;
    }

    List<int> QuickDisks() => _db == null ? [] : _inventory.DiskCounts.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k).ToList();

    string DiskName(int row) => _db != null && _db.Contains("conItem") ? GameName(_db["conItem"].Text(row, "NAME")) : $"Disco {row}";

    // Nome curto para a HUD: "HP Disk α" → "HP α".
    string DiskShort(int row) => DiskName(row).Replace(" Disk", "").Replace("Disk ", "");

    static string GameName(string name) => name.TrimStart('∈').Trim();

    void SetupQuickItems()
    {
        if (_quickLayer != null) return;
        _quickLayer = new CanvasLayer { Layer = 3 };
        AddChild(_quickLayer);
        // HUD: 5 caixas na escala da altura da janela (base 720). PC: coluna em pé na lateral direita (pedido do usuário,
        // 2026-11-06: "em pé fica melhor, senão fica muito poluído"). Celular: continua deitada no pé da tela.
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _quickLayer.AddChild(holder);
        BoxContainer bar = Touch ? new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }
            : new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 6);
        holder.AddChild(bar);
        _quickHud = bar;
        for (int i = 0; i < 5; i++)
        {
            // Tocar/clicar no atalho usa o disco; soltar ali um disco arrastado da janela de itens põe nele (pedido do usuário,
            // versão do celular, 2026-11-04).
            var box = new PanelContainer { CustomMinimumSize = new Vector2(92, 40), MouseFilter = Control.MouseFilterEnum.Stop };
            int slot = i;
            box.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) return;
                box.AcceptEvent();
                if (!_quickOpen) UseQuickSlot(slot);
            };
            box.SetDragForwarding(new Callable(), Callable.From((Vector2 _, Variant d) => d.VariantType == Variant.Type.Int),
                Callable.From((Vector2 _, Variant d) => PutInSlot(slot, d.AsInt32())));
            _quickBoxes.Add(box);
            box.AddThemeStyleboxOverride("panel", QuickBox(QuickBorder));
            var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 15);
            box.AddChild(label);
            bar.AddChild(box);
            _quickHudLabels.Add(label);
        }
        UseGameFont(bar);
        // Janela de itens (Item Window): lista dos discos à direita.
        var win = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(330, 0) };
        win.AddThemeStyleboxOverride("panel", QuickBox(QuickGold, 3));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        win.AddChild(col);
        var title = new Label { Text = "Itens" };
        title.AddThemeColorOverride("font_color", QuickGold);
        col.AddChild(title);
        _quickRows = new VBoxContainer();
        col.AddChild(_quickRows);
        var help = new Label { Name = "ajuda", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) };
        help.AddThemeFontSizeOverride("font_size", 14);
        help.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.85f));
        col.AddChild(help);
        holder.AddChild(win);
        _quickWindow = win;
        UseGameFont(win);
    }

    void TickQuickItems()
    {
        if (_quickLayer == null || _quickHud == null) return;
        // DEPURAÇÃO (só no build Debug, não é do jogo): DW4_USAR_ITEM=<linha da conItem> ganha 1 desse disco e o usa 5 s depois
        // de abrir a fase (conferir o Gate Disk sem ninguém no controle).
        if (OS.IsDebugBuild() && _tick == 300 && int.TryParse(OS.GetEnvironment("DW4_USAR_ITEM"), out int debugItem))
        {
            _inventory.Add(new ItemRef("conItem", debugItem));
            GD.Print($"[item] usando {DiskName(debugItem)} (tem {_inventory.Disks(debugItem)})");
            UseDisk(debugItem);
            GD.Print($"[item] depois: {_inventory.Disks(debugItem)}");
        }
        var size = GetViewport().GetVisibleRect().Size;
        float s = size.Y / 720f;
        _quickHud.Scale = new Vector2(s, s);
        var hudSize = _quickHud.GetCombinedMinimumSize() * s;
        // Em 4:3 a barra centrada encostava no painel de status (canto de baixo à esquerda): começa depois dele.
        // Com a barra de técnicas no pé da tela (Fatia.TechBar.cs), a hotbar fica logo acima dela (o 14 deixa lugar às plaquinhas das teclas).
        if (Touch)
        {
            float techH = _techBar is { Visible: true } tb ? tb.GetCombinedMinimumSize().Y * s + 14 * s : 0;
            _quickHud.Position = new Vector2(Math.Max((size.X - hudSize.X) / 2, 290 * s), size.Y - hudSize.Y - 10 * s - techH);
        }
        else _quickHud.Position = new Vector2(size.X - hudSize.X - 16 * s, (size.Y - hudSize.Y) / 2);
        _quickHud.Visible = !InCutscene && !_menuOpen && !ShopOpen && !SubWindowOpen;
        var inv = _inventory;
        string key = string.Join(",", inv.QuickItems.Select(r => r >= 0 ? $"{r}:{inv.Disks(r)}" : "-"));
        if (key != _quickHudKey)
        {
            _quickHudKey = key;
            string KeyName(int i) => PortSettings.Current.Bindings.Get(InputBindings.Keyboard, $"item{i + 1}").FirstOrDefault() is { } b ? InputBindings.Describe(b) : $"{i + 1}";
            for (int i = 0; i < 5; i++)
            {
                int row = inv.QuickItems[i];
                _quickHudLabels[i].Text = row >= 0 ? $"{KeyName(i)}  {DiskShort(row)}\n×{inv.Disks(row)}" : $"{KeyName(i)}\n—";
                _quickHudLabels[i].Modulate = row >= 0 && inv.Disks(row) > 0 ? Colors.White : new Color(1, 1, 1, 0.5f);
            }
        }
        if (_quickWindow is { Visible: true } w)
        {
            w.Scale = new Vector2(s, s);
            var ws = w.GetCombinedMinimumSize() * s;
            // PC: a coluna dos atalhos está na direita, então a janela abre do lado esquerdo dela (dá para arrastar o disco até lá).
            float right = Touch ? 24 * s : hudSize.X + 16 * s + 14 * s;
            w.Position = new Vector2(size.X - ws.X - right, (size.Y - ws.Y) / 2);
        }
    }

    void OpenQuickWindow(bool open)
    {
        if (_quickWindow == null) return;
        _quickOpen = open;
        _quickWindow.Visible = open;
        if (open) { _quickCursor = 0; RefreshQuickWindow(); }
    }

    void RefreshQuickWindow()
    {
        if (_quickRows == null || _quickWindow == null) return;
        foreach (var c in _quickRows.GetChildren()) c.QueueFree();
        var disks = QuickDisks();
        _quickCursor = Math.Clamp(_quickCursor, 0, Math.Max(0, disks.Count - 1));
        if (disks.Count == 0) _quickRows.AddChild(new Label { Text = "Nenhum disco." });
        for (int i = 0; i < disks.Count; i++)
        {
            int row = disks[i];
            int slot = Array.IndexOf(_inventory.QuickItems, row);
            var l = new Label { Text = $"{(i == _quickCursor ? "> " : "   ")}{DiskName(row)}  ×{_inventory.Disks(row)}{(slot >= 0 ? $"   [{slot + 1}]" : "")}" };
            l.AddThemeColorOverride("font_color", i == _quickCursor ? QuickGold : Colors.White);
            // Mouse: passar por cima escolhe a linha (1–5 põe no atalho), clique esquerdo usa.
            l.MouseFilter = Control.MouseFilterEnum.Stop;
            int index = i;
            l.MouseEntered += () => { if (_quickCursor != index) { _quickCursor = index; CallDeferred(MethodName.RefreshQuickWindow); } };
            // Arrastar a linha até um atalho da barra de baixo põe o disco nele.
            l.SetDragForwarding(Callable.From((Vector2 _) =>
            {
                var preview = new Label { Text = DiskShort(row), Modulate = QuickGold };
                preview.AddThemeFontSizeOverride("font_size", 26);
                l.SetDragPreview(preview);
                return Variant.From(row);
            }), new Callable(), new Callable());
            l.GuiInput += ev =>
            {
                // Usa ao soltar (não ao apertar): apertar e arrastar é pôr no atalho.
                if (ev is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) return;
                l.AcceptEvent();
                _quickCursor = index;
                UseDisk(row);
                CallDeferred(MethodName.RefreshQuickWindow);
            };
            _quickRows.AddChild(l);
        }
        if (_quickWindow.FindChild("ajuda", true, false) is Label help)
            help.Text = Touch ? "Toque: usar   Arraste até um atalho lá embaixo: pôr nele"
                : $"Clique ou {DeviceIcons.Of("confirmar")}: usar   1–5 com o mouse em cima ou arraste até a coluna à direita: pôr no atalho   {DeviceIcons.Of("cancelar")}: fechar";
        // Sem UseGameFont aqui: ele multiplica por 1,3 os tamanhos já pedidos a cada chamada, e a janela crescia a cada seta
        // (relato de 2026-11-02). O tema com a fonte do jogo vem da janela (posto uma vez no SetupQuickItems).
    }

    // Janela aberta: consome as teclas. Fechada: I/B abre e 1–5 usam os atalhos.
    bool QuickItemInput(InputEvent e)
    {
        if (_quickLayer == null) return false;
        if (!_quickOpen)
        {
            if (PortInput.Pressed(e, "itens") && _padEnabled && _playerCtrl && !InCutscene) { OpenQuickWindow(true); return true; }
            for (int i = 0; i < 5; i++) if (PortInput.Pressed(e, $"item{i + 1}")) { UseQuickSlot(i); return true; }
            return false;
        }
        var disks = QuickDisks();
        if (e.IsActionPressed("ui_cancel") || PortInput.Pressed(e, "itens")) { OpenQuickWindow(false); return true; }
        if (e.IsActionPressed("ui_up")) { _quickCursor = Math.Max(0, _quickCursor - 1); RefreshQuickWindow(); return true; }
        if (e.IsActionPressed("ui_down")) { _quickCursor = Math.Min(Math.Max(0, disks.Count - 1), _quickCursor + 1); RefreshQuickWindow(); return true; }
        for (int i = 0; i < 5; i++)
            if (PortInput.Pressed(e, $"item{i + 1}") && _quickCursor < disks.Count)
            {
                PutInSlot(i, disks[_quickCursor]);
                return true;
            }
        if (e.IsActionPressed("ui_accept") && _quickCursor < disks.Count)
        {
            UseDisk(disks[_quickCursor]);
            RefreshQuickWindow();
            return true;
        }
        return e is InputEventKey or InputEventJoypadButton; // nada mais chega ao jogo com a janela aberta
    }

    // Um disco fica num atalho só: sai do outro e entra neste.
    void PutInSlot(int slot, int row)
    {
        for (int k = 0; k < 5; k++) if (_inventory.QuickItems[k] == row) _inventory.QuickItems[k] = -1;
        _inventory.QuickItems[slot] = row;
        Toast($"{DiskName(row)} no atalho {slot + 1}");
        RefreshQuickWindow();
    }

    // Toque em cima de um atalho (celular): é para a barra, não para o analógico.
    bool QuickSlotAt(Vector2 p) => _quickHud is { Visible: true } && _quickBoxes.Any(b => (b.GetGlobalTransform() * new Rect2(Vector2.Zero, b.Size)).HasPoint(p));

    void UseQuickSlot(int slot)
    {
        int row = _inventory.QuickItems[slot];
        if (row < 0) { Toast($"Atalho {slot + 1} vazio: abra a janela de itens ({DeviceIcons.Of("itens")}) e aperte {slot + 1} num disco."); return; }
        if (_inventory.Disks(row) <= 0) { Toast($"Sem {DiskName(row)}."); return; }
        UseDisk(row);
    }

    // FATO (conItem): linha 6 キュアディスク (tira o veneno), 7 レイズディスク (revive as BackUP chips), 8 ゲートディスク (leva
    // todos à Main Lobby), 10 ブースタパック e 11 キーチェーン (não são de usar: troca no balcão das cartas e colecionável).
    const int CureDiskRow = 6, RaiseDiskRow = 7, GateDiskRow = 8, BoosterPackRow = 10, KeyChainRow = 11;

    // Usa o disco: SQ_ITEM do jogador, o efeito de cura e o HP/MP da conItem (soma com limite no máximo).
    void UseDisk(int row)
    {
        if (_db == null || _pf == null || !_padEnabled || !_playerCtrl || InCutscene || _hp <= 0) return;
        if (_runner?.Running == true && _runner.Sequence != "SQ_WAIT" && _runner.Sequence != "SQ_WALK") { Toast("Agora não dá para usar item."); return; }
        if (row == GateDiskRow) { UseGateDisk(row); return; }
        if (UseRaiseDisk(row)) return; // Raise Disk: só no backup chip (Fatia.BackupChip.cs)
        // Sem efeito no port ainda: não gasta (antes sumia sem fazer nada).
        if (row is BoosterPackRow or KeyChainRow) { Toast($"{DiskName(row)} não é de usar."); return; }
        if (row is CureDiskRow) { Toast($"{DiskName(row)} ainda não funciona no port; o disco não foi gasto."); return; }
        if (_inventory.UseDisk(_db, row) is not { } heal) return;
        int hp = _hp, mp = _mp;
        _hp = Math.Min(_pf.MaxHp, _hp + heal.Hp);
        _mp = Math.Min(_pf.MaxMp, _mp + heal.Mp);
        if (_runner != null && _runner.Start("SQ_ITEM", StyleOf("SQ_ITEM"))) _move.Stop();
        PlayerEffect("x_250curehit01", "SQ_ATCK01");
        if (_hp > hp) Float(_player, $"+{_hp - hp}", Colors.LightGreen);
        if (_mp > mp) Float(_player, $"MP +{_mp - mp}", Colors.LightBlue);
        _lastEvent = $"usou {DiskName(row)}";
        ScriptLog($"usou {DiskName(row)} (HP {hp}→{_hp}, MP {mp}→{_mp})");
    }

    // Gate Disk: FATO, toda fase tem a gatedisc.lua, que é o uso do disco: tira os jogadores do controle, guarda o portão de volta
    // (SYSsetGateStage), toca SQ_EXIT, escurece (i_fadewn01 SQ_STCHANGE01) e manda PLAYERPOINT SETGENERATEPOS I<3> e STAGECTL
    // NEXTSTAGE "stagML11" (Emergency Gate do lobby). HIPÓTESE: o script não faz nada com a fila de demo ocupada (abre com
    // DMgetCount), então o disco só é gasto com a fila livre.
    // FATO: "NDMWPANEL NOGATEDISK I<1>" no first.lua do tutorial (stag0001), das salas de chefe (stagbs*) e das quests (dngq*)
    // trava o Gate Disk, e I<0> destrava (stagML11, matz00/stchg01 da stag0001 ao fim do tutorial). HIPÓTESE: vale até a troca de
    // fase (as dungeons comuns não mandam nada e o disco funciona nelas).
    bool _noGateDisk;

    bool DeliverToQuickItems(BeMessage m)
    {
        if (m.Class != "NDMWPANEL" || m.Command != "NOGATEDISK") return false;
        _noGateDisk = m.Args.Count > 0 && m.Args[0] is { Kind: 'I', Numbers.Length: > 0 } a && a.Numbers[0] != 0;
        return true;
    }

    void UseGateDisk(int row)
    {
        if (_stage == null || !_stage.Has("gatedisc.lua") || _noGateDisk) { Toast($"{DiskName(row)} não funciona aqui."); return; }
        if (_leaving || _stage.Host.Demo.Count != 0) { Toast("Agora não dá para usar item."); return; }
        if (_inventory.UseDisk(_db!, row) == null) return;
        OpenQuickWindow(false);
        _lastEvent = $"usou {DiskName(row)}";
        ScriptLog($"usou {DiskName(row)}: gatedisc.lua");
        if (!_stage.Run("gatedisc.lua")) ScriptLog("gatedisc.lua não rodou");
    }
}
