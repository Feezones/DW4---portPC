using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Guilda do DW4 (DECISÃO DO PORT, pedido do responsável em 2026-11-07; o original não tem): guilda própria do DW4, por conta do
/// launcher. Abre no O (ação "guilda"), pela pausa (Esc / Start / engrenagem do celular) e responde aos convites do menu de interagir
/// (G / Y / JOGADOR). As regras são do servidor (DMO.API, Dw4GuildService); aqui só se mostra e se pede.
///  · sem guilda: criar (nome e tag) ou esperar um convite de Mestre / Sub-mestre;
///  · Membros: quem está online e onde, cargo e contribuição; tocar num membro abre as ações (chamar para o grupo, cargo, passar o
///    Mestre, tirar da guilda); Mestre e Sub-mestre convidam pelo nome e mudam o aviso;
///  · Baú: itens e BIT da guilda; guardar tira da pasta (o servidor muda o save da conta e devolve o TradeSeq, como na troca);
///  · Histórico: entradas, saídas, cargos, aviso, nível e baú.
/// Nível da guilda: 1 a 10 pelo XP que os membros ganham (inimigos e chefes; em dobro com colega de guilda no grupo); cada nível
/// acima do 1 dá +1% de EXP e BIT (<see cref="GuildBonus"/>). Tag na placa: Nameplate.SetGuild. Chat: canal Guilda (/gu).
/// Mouse, toque (botões grandes no celular) ou controle; Esc / B / FECHAR volta uma página ou fecha.
/// </summary>
public partial class Fatia
{
    bool _guildOpen;
    CanvasLayer? _guildLayer;
    string _guildPage = "membros";
    long _guildMember;
    string[] _guildLog = [];
    string? _guildFocus;
    bool _guildBusy;

    void SetupGuild()
    {
        Online.GuildIn += OnGuildView;
        Online.GuildBankIn += OnGuildBank;
        Online.GuildLogIn += OnGuildLog;
        TreeExiting += () => { Online.GuildIn -= OnGuildView; Online.GuildBankIn -= OnGuildBank; Online.GuildLogIn -= OnGuildLog; };
    }

    /// <summary>% a mais de EXP e BIT pela guilda (0 fora dela ou fora do online).</summary>
    int GuildBonus => Online.State == Online.Status.Online && Online.Guild is { Id: > 0 } g ? Math.Clamp(g.Bonus, 0, 50) : 0;

    static int WithGuildBonus(int amount, int bonus) => bonus <= 0 || amount <= 0 ? amount : (int)Math.Min(int.MaxValue, amount + (long)amount * bonus / 100);

    // EXP ganha num inimigo: vira XP da guilda no servidor (chefe vale mais).
    void GuildXpFrom(Enemy e, int exp)
    {
        if (Online.Guild is not { Id: > 0 }) return;
        if (exp > 0) Online.SendGuild(new GuildCmd("xp", Num: exp));
        if (IsBossEnemy(e)) Online.SendGuild(new GuildCmd("boss"));
    }

    bool GuildInput(InputEvent e)
    {
        if (!_guildOpen)
        {
            if (!PortInput.Pressed(e, "guilda")) return false;
            if (_menuOpen || ShopOpen || SubWindowOpen || _choice >= 0 || InCutscene || ChatOpen || TalkWindowOpen) return false;
            OpenGuild();
            return true;
        }
        if (e.IsActionPressed("ui_cancel") || PortInput.Pressed(e, "guilda") && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            if (_guildPage is "membro" or "guardar" or "aviso" or "convidar") { _guildPage = _guildPage == "guardar" ? "bau" : "membros"; RefreshGuild(); }
            else CloseGuild();
        }
        return true; // os botões e campos já receberam o que era deles; o resto não chega ao jogo
    }

    void OpenGuild()
    {
        if (Online.State != Online.Status.Online) { Toast("A guilda precisa do servidor: você não está online."); return; }
        CloseInteract(showNext: false);
        _guildOpen = true;
        _guildPage = Online.Guild is { Id: > 0 } ? "membros" : "criar";
        _move.Stop();
        Online.SendGuild(new GuildCmd("info"));
        RefreshGuild();
    }

    void CloseGuild()
    {
        _guildLayer?.QueueFree();
        _guildLayer = null;
        _guildOpen = false;
        _guildBusy = false;
        if (_prompts.Count > 0) CallDeferred(MethodName.ShowNextPrompt);
    }

    void OnGuildView(GuildView v)
    {
        if (v.Message.Length > 0) { Toast(v.Message, 4); _guildBusy = false; }
        if (!_guildOpen) return;
        if (v.Id == 0 && _guildPage != "criar") _guildPage = "criar";
        else if (v.Id != 0 && _guildPage == "criar") _guildPage = "membros";
        if (_guildPage == "membro" && v.Members.All(m => m.Account != _guildMember)) _guildPage = "membros";
        RefreshGuild();
    }

    void OnGuildLog(GuildLog l)
    {
        _guildLog = l.Lines;
        if (_guildOpen && _guildPage == "historico") RefreshGuild();
    }

    // Baú: o servidor já mudou o save da conta; aqui o mesmo na pasta e grava com o TradeSeq novo (igual ao fim da troca).
    void OnGuildBank(GuildBankDone d)
    {
        _guildBusy = false;
        if (!d.Ok) { Toast(d.Message.Length > 0 ? d.Message : "Não deu certo.", 4); if (_guildOpen) RefreshGuild(); return; }
        var moved = new TradeOffer { Bit = d.Bit };
        for (int i = 0; i < d.Count; i++) moved.Items.Add(new ItemRef(d.Table, d.Row));
        bool deposit = d.Action == "deposit";
        TradeSession.Move(deposit ? moved : new TradeOffer(), deposit ? new TradeOffer() : moved, _inventory);
        for (int i = 0; i < _inventory.QuickItems.Length; i++) // atalho de disco que acabou fica vazio
            if (_inventory.QuickItems[i] >= 0 && _inventory.Disks(_inventory.QuickItems[i]) == 0) _inventory.QuickItems[i] = -1;
        Session.TradeApplied(d.Seq);
        _ = SaveProgress();
        if (_quickOpen) RefreshQuickWindow();
        string what = d.Bit > 0 ? $"{d.Bit} BIT" : (d.Count > 1 ? $"{d.Count}x " : "") + ItemName(new ItemRef(d.Table, d.Row));
        Toast(deposit ? $"Guardou {what} no baú da guilda." : $"Tirou {what} do baú da guilda.", 3);
        if (_guildOpen) RefreshGuild();
    }

    void GuildAsk(GuildCmd cmd, bool busy = false)
    {
        if (!Online.SendGuild(cmd)) { Toast("Você não está online."); return; }
        if (busy) { _guildBusy = true; RefreshGuild(); }
    }

    /// <summary>Guardar no baú: grava antes (o servidor tira do save guardado, que precisa estar igual à pasta) e pede.</summary>
    void GuildDeposit(ItemRef? item, int count, int bit)
    {
        if (!Session.Active) { Toast("Entre com um personagem para usar o baú."); return; }
        _ = SaveProgress();
        GuildAsk(item is { } i ? new GuildCmd("deposit", Num: count, Table: i.Table, Row: i.Row, Slot: Session.Slot)
            : new GuildCmd("deposit", Num: bit, Table: "bit", Slot: Session.Slot), busy: true);
    }

    void GuildWithdraw(ItemRef? item, int count, int bit)
    {
        if (!Session.Active) { Toast("Entre com um personagem para usar o baú."); return; }
        _ = SaveProgress();
        GuildAsk(item is { } i ? new GuildCmd("withdraw", Num: count, Table: i.Table, Row: i.Row, Slot: Session.Slot)
            : new GuildCmd("withdraw", Num: bit, Table: "bit", Slot: Session.Slot), busy: true);
    }

    /// <summary>Cópias livres que podem ir para o baú (as mesmas regras da troca: sem equipados nem itens de enredo).</summary>
    List<(ItemRef Item, int Room)> GuildCandidates()
    {
        int Room(ItemRef item)
        {
            if (!TradeSession.Tradable(item)) return 0;
            if (_db != null && EquipRules.CannotGive(_db, _inventory, _equip, item, 1) != null) return 0;
            return TradeSession.Free(_inventory, _equip, item);
        }
        var list = new List<(ItemRef, int)>();
        foreach (var d in _inventory.Devices.Distinct()) if (Room(d) is > 0 and var n) list.Add((d, n));
        foreach (var (row, _) in _inventory.DiskCounts.OrderBy(kv => kv.Key))
            if (Room(new ItemRef("conItem", row)) is > 0 and var n) list.Add((new ItemRef("conItem", row), n));
        return list.OrderBy(x => EquipRules.SortKey(x.Item1)).ToList();
    }

    static string RankName(int rank) => rank switch { 1 => "Mestre", 2 => "Sub-mestre", 3 => "Membro", _ => "Novato" };

    static string StageName(string stage) => stage.StartsWith("stagml", StringComparison.OrdinalIgnoreCase) ? "na cidade"
        : stage.Length > 0 ? $"em {stage}" : "";

    // Histórico: "unix TAB texto", com {item:tabela:linha} trocado pelo nome do item.
    string GuildLogLine(string line)
    {
        int tab = line.IndexOf('\t');
        string when = tab > 0 && long.TryParse(line[..tab], out var unix) && unix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM HH:mm") + "  " : "";
        string text = tab >= 0 ? line[(tab + 1)..] : line;
        int at;
        while ((at = text.IndexOf("{item:", StringComparison.Ordinal)) >= 0)
        {
            int end = text.IndexOf('}', at);
            if (end < 0) break;
            var parts = text[(at + 6)..end].Split(':');
            string name = parts.Length == 2 && int.TryParse(parts[1], out var row) ? ItemName(new ItemRef(parts[0], row)) : "?";
            text = text[..at] + name + text[(end + 1)..];
        }
        return when + text;
    }

    // ---------- logo (aprovado no site, /guild-marks/-<id>.png) ----------

    static readonly Dictionary<(long, long), ImageTexture?> s_logos = [];

    TextureRect? GuildLogo(long guildId, long version, float size)
    {
        if (guildId <= 0 || version <= 0) return null;
        var rect = new TextureRect { CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        var key = (guildId, version);
        if (s_logos.TryGetValue(key, out var tex)) { rect.Texture = tex; return rect; }
        s_logos[key] = null; // uma vez só por versão, mesmo se falhar
        var http = new HttpRequest { Timeout = 10 };
        AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code != 200 || body.Length is 0 or > 256 * 1024) return;
            var img = new Image();
            if (img.LoadPngFromBuffer(body) != Error.Ok) return;
            s_logos[key] = ImageTexture.CreateFromImage(img);
            if (_guildOpen) RefreshGuild();
        };
        http.Request($"{Online.SiteBase}/guild-marks/-{guildId}.png?v={version}");
        return rect;
    }

    // ---------- visual ----------

    static readonly Color GuildDim = new(0.72f, 0.73f, 0.86f), GuildBlue = new(0.55f, 0.85f, 1f), GuildOnline = new(0.45f, 0.95f, 0.55f);

    static Color RankColor(int rank) => rank switch
    {
        1 => QuickGold,
        2 => new Color(0.82f, 0.68f, 1f),
        3 => GuildBlue,
        _ => new Color(0.7f, 0.7f, 0.75f),
    };

    static StyleBoxFlat GuildBox(Color bg, Color border, int borderW = 1, int radius = 8, int margin = 8)
    {
        var st = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 6 };
        st.SetBorderWidthAll(borderW);
        st.SetCornerRadiusAll(radius);
        st.ContentMarginLeft = st.ContentMarginRight = margin + 4;
        st.ContentMarginTop = st.ContentMarginBottom = margin;
        return st;
    }

    // Botões da janela: fundo escuro, borda fina; foco/mouse acendem a borda; ativo (aba) fica dourado.
    static void GuildStyle(Button b, bool active = false, Color? accent = null)
    {
        var a = accent ?? QuickBorder;
        var normal = active ? GuildBox(new Color(0.32f, 0.26f, 0.08f, 0.95f), QuickGold, 2) : GuildBox(new Color(0.13f, 0.13f, 0.28f, 0.92f), a with { A = 0.55f });
        var hover = active ? normal : GuildBox(new Color(0.19f, 0.19f, 0.4f, 0.95f), a, 2);
        var focus = GuildBox(new Color(0, 0, 0, 0), QuickGold, 2);
        focus.DrawCenter = false;
        var disabled = GuildBox(new Color(0.1f, 0.1f, 0.18f, 0.7f), new Color(0.3f, 0.3f, 0.4f, 0.5f));
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", active ? normal : GuildBox(new Color(0.25f, 0.22f, 0.5f, 0.95f), QuickGold, 2));
        b.AddThemeStyleboxOverride("focus", focus);
        b.AddThemeStyleboxOverride("disabled", disabled);
        b.AddThemeColorOverride("font_color", active ? QuickGold : Colors.White);
        b.AddThemeColorOverride("font_hover_color", active ? QuickGold : Colors.White);
        b.AddThemeColorOverride("font_focus_color", active ? QuickGold : Colors.White);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.6f));
    }

    // Plaquinha colorida (cargo, bônus, membros).
    static PanelContainer GuildChip(string text, Color color, int size)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var st = GuildBox(color with { A = 0.16f }, color with { A = 0.75f }, 1, 10, 2);
        st.ContentMarginLeft = st.ContentMarginRight = 10;
        chip.AddThemeStyleboxOverride("panel", st);
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        chip.AddChild(l);
        return chip;
    }

    // Emblema sem logo: quadrado com a tag.
    static PanelContainer GuildEmblem(string tag, float size, int font)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", GuildBox(new Color(0.22f, 0.17f, 0.05f, 0.95f), QuickGold, 2, 10, 0));
        var l = new Label { Text = tag, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", font);
        l.AddThemeColorOverride("font_color", QuickGold);
        p.AddChild(l);
        return p;
    }

    static ProgressBar GuildBar(double value, double max, float height)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, max), Value = Math.Clamp(value, 0, Math.Max(1, max)), ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var bg = GuildBox(new Color(0.05f, 0.05f, 0.12f, 0.95f), QuickBorder with { A = 0.6f }, 1, 5, 0);
        var fill = GuildBox(QuickGold, QuickGold, 0, 5, 0);
        bar.AddThemeStyleboxOverride("background", bg);
        bar.AddThemeStyleboxOverride("fill", fill);
        return bar;
    }

    // ---------- janela ----------

    void RefreshGuild()
    {
        if (!_guildOpen) return;
        if (GetViewport().GuiGetFocusOwner() is Control f && _guildLayer != null && _guildLayer.IsAncestorOf(f)) _guildFocus = f.Name;
        _guildLayer?.QueueFree();
        _guildLayer = new CanvasLayer { Layer = 16 };
        AddChild(_guildLayer);
        bool touch = Touch && !_usingPad;
        int font = touch ? 26 : 20, small = touch ? 20 : 15;
        float rowH = touch ? 60 : 38;
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _guildLayer.AddChild(center);
        var panel = new PanelContainer();
        var frame = QuickBox(QuickBorder, 3);
        frame.BgColor = new Color(0.06f, 0.06f, 0.16f, 0.94f);
        frame.SetContentMarginAll(touch ? 22 : 18);
        frame.ShadowColor = new Color(0, 0, 0, 0.5f);
        frame.ShadowSize = 12;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(touch ? 1100 : 860, 0) };
        box.AddThemeConstantOverride("separation", touch ? 12 : 8);
        panel.AddChild(box);
        Control? firstFocus = null;
        var dim = GuildDim;
        void Line(Control parent)
        {
            var sep = new HSeparator();
            var st = new StyleBoxLine { Color = QuickBorder with { A = 0.45f }, Thickness = 1 };
            sep.AddThemeStyleboxOverride("separator", st);
            parent.AddChild(sep);
        }
        // Linha de lista: botão sem texto com nome à esquerda e plaquinhas/infos à direita.
        Button ListRow(string name, Action act, bool enabled = true)
        {
            var b = Btn(name, "", enabled, act);
            var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            h.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            h.OffsetLeft = 14; h.OffsetRight = -14;
            h.AddThemeConstantOverride("separation", 10);
            b.AddChild(h);
            return b;
        }
        HBoxContainer RowBox(Button b) => b.GetChild<HBoxContainer>(0);

        Label Text(string s, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { Text = s, HorizontalAlignment = align, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", size);
            if (color is { } c) l.AddThemeColorOverride("font_color", c);
            return l;
        }
        Button Btn(string name, string label, bool enabled, Action act)
        {
            var b = new Button { Name = name, Text = label, Disabled = !enabled || _guildBusy, CustomMinimumSize = new Vector2(0, rowH) };
            b.AddThemeFontSizeOverride("font_size", font);
            GuildStyle(b);
            b.Pressed += act;
            return b;
        }
        LineEdit Field(string name, string text, string placeholder, int max, float width = 0)
        {
            var e = new LineEdit { Name = name, Text = text, PlaceholderText = placeholder, MaxLength = max, CustomMinimumSize = new Vector2(width, rowH),
                SizeFlagsHorizontal = width > 0 ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill };
            e.AddThemeFontSizeOverride("font_size", font);
            e.AddThemeStyleboxOverride("normal", GuildBox(new Color(0.04f, 0.04f, 0.1f, 0.95f), QuickBorder with { A = 0.7f }));
            e.AddThemeStyleboxOverride("focus", GuildBox(new Color(0, 0, 0, 0), QuickGold, 2));
            return e;
        }
        HBoxContainer Row(Control parent)
        {
            var r = new HBoxContainer();
            r.AddThemeConstantOverride("separation", 8);
            parent.AddChild(r);
            return r;
        }
        ScrollContainer Scroll(VBoxContainer inner, float h)
        {
            var sc = new ScrollContainer { CustomMinimumSize = new Vector2(0, h), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            sc.AddChild(inner);
            return sc;
        }
        float listH = touch ? 470 : 380;

        var g = Online.Guild;
        if (g is not { Id: > 0 })
        {
            // ---- sem guilda: criar ----
            box.AddChild(Text("GUILDA", font + 8, QuickGold, HorizontalAlignment.Center));
            Line(box);
            box.AddChild(Text("Você não está em uma guilda. Crie uma (você vira o Mestre) ou peça um convite a um Mestre ou Sub-mestre: " +
                "perto da pessoa, use Interagir.", small, dim));
            var nameRow = Row(box);
            nameRow.AddChild(Text("Nome", font));
            var name = Field("nome", "", "3 a 16 letras ou números", 16);
            nameRow.AddChild(name);
            var tagRow = Row(box);
            tagRow.AddChild(Text("Tag", font));
            var tag = Field("tag", "", "2 a 4 letras (aparece na placa)", 4, touch ? 300 : 240);
            tagRow.AddChild(tag);
            var create = Btn("criar", _guildBusy ? "Criando..." : "Criar guilda", true, () =>
                GuildAsk(new GuildCmd("create", Text: name.Text.Trim(), Text2: tag.Text.Trim()), busy: true));
            name.TextSubmitted += _ => tag.GrabFocus();
            tag.TextSubmitted += _ => create.EmitSignal(BaseButton.SignalName.Pressed);
            GuildStyle(create, active: true);
            box.AddChild(create);
            box.AddChild(Btn("fechar", "Fechar", true, CloseGuild));
            firstFocus = name;
            FinishGuild(box, firstFocus);
            return;
        }

        int myRank = g.MyRank;
        bool manager = myRank is 1 or 2;

        // ---- cabeçalho ----
        var head = Row(box);
        head.AddThemeConstantOverride("separation", 14);
        float emblem = touch ? 96 : 76;
        head.AddChild(GuildLogo(g.Id, g.LogoVersion, emblem) is { Texture: not null } logo ? logo : GuildEmblem(g.Tag, emblem, font));
        var title = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeConstantOverride("separation", 4);
        head.AddChild(title);
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 10);
        title.AddChild(titleRow);
        titleRow.AddChild(Text(g.Name, font + 8, QuickGold));
        titleRow.AddChild(GuildChip($"<{g.Tag}>", GuildBlue, small));
        var xpRow = new HBoxContainer();
        xpRow.AddThemeConstantOverride("separation", 10);
        title.AddChild(xpRow);
        xpRow.AddChild(Text($"Nível {g.Level}", font, Colors.White));
        bool maxLevel = g.NextXp <= 0;
        var bar = GuildBar(maxLevel ? 1 : g.Xp - g.LevelXp, maxLevel ? 1 : g.NextXp - g.LevelXp, touch ? 18 : 14);
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        xpRow.AddChild(bar);
        xpRow.AddChild(Text(maxLevel ? "nível máximo" : $"{g.Xp - g.LevelXp}/{g.NextXp - g.LevelXp} XP", small, dim));
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 8);
        title.AddChild(chips);
        chips.AddChild(GuildChip($"+{g.Bonus}% EXP e BIT", GuildOnline, small));
        chips.AddChild(GuildChip($"{g.Members.Length}/{g.MaxMembers} membros", GuildBlue, small));
        chips.AddChild(GuildChip($"Você: {RankName(myRank)}", RankColor(myRank), small));
        if (g.Notice.Length > 0)
        {
            var notice = new PanelContainer();
            var st = GuildBox(new Color(0.1f, 0.2f, 0.3f, 0.6f), GuildBlue with { A = 0.0f }, 0, 6, 8);
            st.BorderWidthLeft = 4;
            st.BorderColor = GuildBlue;
            notice.AddThemeStyleboxOverride("panel", st);
            notice.AddChild(Text("Aviso: " + g.Notice, small, GuildBlue));
            box.AddChild(notice);
        }

        // ---- abas ----
        var tabs = Row(box);
        foreach (var (page, label) in new[] { ("membros", "Membros"), ("bau", "Baú"), ("historico", "Histórico") })
        {
            bool on = _guildPage == page || page == "membros" && _guildPage is "membro" or "aviso" or "convidar" || page == "bau" && _guildPage == "guardar";
            var b = Btn("aba_" + page, label, true, () =>
            {
                _guildPage = page;
                if (page == "historico") Online.SendGuild(new GuildCmd("log"));
                RefreshGuild();
            });
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            GuildStyle(b, active: on);
            tabs.AddChild(b);
            if (on) firstFocus ??= b;
        }
        Line(box);

        switch (_guildPage)
        {
            case "membro" when g.Members.FirstOrDefault(m => m.Account == _guildMember) is { } m:
            {
                bool online = m.PlayerId >= 0;
                bool self = m.PlayerId == Online.MyId && online;
                var card = new PanelContainer();
                card.AddThemeStyleboxOverride("panel", GuildBox(new Color(0.12f, 0.12f, 0.27f, 0.8f), RankColor(m.Rank) with { A = 0.6f }, 1, 8, 10));
                box.AddChild(card);
                var cardCol = new VBoxContainer();
                cardCol.AddThemeConstantOverride("separation", 6);
                card.AddChild(cardCol);
                var top = Row(cardCol);
                top.AddThemeConstantOverride("separation", 10);
                top.AddChild(Text(m.Name, font + 6, QuickGold));
                top.AddChild(GuildChip(RankName(m.Rank), RankColor(m.Rank), small));
                cardCol.AddChild(Text((online ? "● Online " + StageName(m.Stage) + (m.Level > 0 ? $" · nível {m.Level}" : "") : "● " + LastSeen(m.LastSeen)),
                    small, online ? GuildOnline : dim));
                cardCol.AddChild(Text($"Contribuição para a guilda: {m.Contribution} XP", small, dim));
                var acts = new VBoxContainer();
                acts.AddThemeConstantOverride("separation", 6);
                box.AddChild(acts);
                if (!self)
                {
                    // Convite de grupo pelo mesmo caminho do menu de interagir (regras da party da DMO.API: só amigos, só o líder).
                    var call = Btn("grupo", "Chamar para o grupo", online, () => { Online.Interact(m.PlayerId, "party-invite"); });
                    acts.AddChild(call);
                    firstFocus = call;
                    acts.AddChild(Btn("privado", "Mensagem privada", online, () =>
                    {
                        CloseGuild();
                        _chatChannel = ChatChannel.Whisper;
                        _chatTo = m.Name;
                        OpenChatBox();
                    }));
                }
                bool canManage = !self && manager && m.Rank > myRank;
                if (canManage)
                {
                    foreach (int r in new[] { 2, 3, 4 })
                        if (r != m.Rank && r > myRank)
                            acts.AddChild(Btn($"cargo{r}", $"Mudar para {RankName(r)}", true, () => GuildAsk(new GuildCmd("rank", Account: m.Account, Num: r))));
                    if (myRank == 1)
                        acts.AddChild(Btn("mestre", "Passar o cargo de Mestre", true, () => GuildConfirm($"Passar o cargo de Mestre para {m.Name}? Você vira Sub-mestre.",
                            "Passar", () => GuildAsk(new GuildCmd("transfer", Account: m.Account)))));
                    acts.AddChild(Btn("tirar", "Tirar da guilda", true, () => GuildConfirm($"Tirar {m.Name} da guilda?", "Tirar",
                        () => GuildAsk(new GuildCmd("kick", Account: m.Account)))));
                }
                if (self)
                    acts.AddChild(Btn("sair", "Sair da guilda", true, () => GuildConfirm(
                        myRank == 1 && g.Members.Length > 1 ? "O Mestre só sai depois de passar o cargo. Passe o Mestre a outro membro primeiro." :
                        myRank == 1 ? $"Sair e acabar com a guilda {g.Name}? O baú some junto." : $"Sair da guilda {g.Name}?",
                        "Sair", () => GuildAsk(new GuildCmd("leave")))));
                var back = Btn("voltar", "Voltar", true, () => { _guildPage = "membros"; RefreshGuild(); });
                box.AddChild(back);
                firstFocus ??= acts.GetChildren().OfType<Button>().FirstOrDefault(b => !b.Disabled) ?? back;
                break;
            }
            case "aviso":
            {
                box.AddChild(Text("Aviso da guilda (aparece para todos ao entrar no jogo):", small, dim));
                var notice = Field("aviso_campo", g.Notice, "Até 120 letras", 120);
                box.AddChild(notice);
                var save = Btn("gravar", "Gravar aviso", true, () => { GuildAsk(new GuildCmd("notice", Text: notice.Text)); _guildPage = "membros"; });
                notice.TextSubmitted += _ => save.EmitSignal(BaseButton.SignalName.Pressed);
                var r = Row(box);
                save.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                r.AddChild(save);
                var back = Btn("voltar", "Voltar", true, () => { _guildPage = "membros"; RefreshGuild(); });
                back.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                r.AddChild(back);
                firstFocus = notice;
                break;
            }
            case "convidar":
            {
                box.AddChild(Text("Convidar pelo nome de um jogador online (o personagem ou o Tamer). Perto da pessoa, também dá pelo Interagir.", small, dim));
                var who = Field("convidar_campo", "", "Nome do jogador", 32);
                box.AddChild(who);
                var send = Btn("enviar", "Enviar convite", true, () => { GuildAsk(new GuildCmd("invite", Text: who.Text.Trim())); _guildPage = "membros"; RefreshGuild(); });
                who.TextSubmitted += _ => send.EmitSignal(BaseButton.SignalName.Pressed);
                var r = Row(box);
                send.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                r.AddChild(send);
                var back = Btn("voltar", "Voltar", true, () => { _guildPage = "membros"; RefreshGuild(); });
                back.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                r.AddChild(back);
                firstFocus = who;
                break;
            }
            case "bau":
            {
                bool items = myRank is >= 1 and <= 3, bits = manager;
                var bitCard = new PanelContainer();
                bitCard.AddThemeStyleboxOverride("panel", GuildBox(new Color(0.2f, 0.16f, 0.04f, 0.6f), QuickGold with { A = 0.6f }, 1, 8, 10));
                box.AddChild(bitCard);
                var bitCol = new VBoxContainer();
                bitCol.AddThemeConstantOverride("separation", 8);
                bitCard.AddChild(bitCol);
                var bitHead = Row(bitCol);
                var bankBit = Text($"{g.BankBit:N0} BIT no baú".Replace(',', '.'), font + 2, QuickGold);
                bankBit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bitHead.AddChild(bankBit);
                bitHead.AddChild(Text($"você tem {_inventory.Bit:N0} BIT".Replace(',', '.'), small, dim));
                var bitRow = Row(bitCol);
                var amount = Field("bit_campo", "", "Quantia", 9, touch ? 200 : 160);
                amount.VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number;
                bitRow.AddChild(amount);
                int Amount() => int.TryParse(amount.Text.Trim(), out var v) ? v : 0;
                var putBit = Btn("guardar_bit", "Guardar BIT", true, () => { if (Amount() is > 0 and var v) GuildDeposit(null, 0, Math.Min(v, _inventory.Bit)); });
                var takeBit = Btn("tirar_bit", "Tirar BIT", bits, () => { if (Amount() is > 0 and var v) GuildWithdraw(null, 0, (int)Math.Min(v, g.BankBit)); });
                putBit.SizeFlagsHorizontal = takeBit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bitRow.AddChild(putBit);
                bitRow.AddChild(takeBit);
                if (!bits) bitCol.AddChild(Text("Só o Mestre e os Sub-mestres tiram BIT.", small, dim));
                box.AddChild(Text($"Itens ({g.Bank.Length}/60)" + (items ? $" · {(touch ? "toque" : "clique")} para tirar 1" : ""), small, dim));
                var list = new VBoxContainer();
                list.AddThemeConstantOverride("separation", 4);
                box.AddChild(Scroll(list, listH - rowH * 2));
                if (g.Bank.Length == 0) list.AddChild(Text("O baú está vazio.", font, dim));
                foreach (var it in g.Bank)
                {
                    var item = new ItemRef(it.Table, it.Row);
                    var b = ListRow($"tirar_{it.Table}_{it.Row}", () => GuildWithdraw(item, 1, 0), items);
                    var h = RowBox(b);
                    var nm = Text(ItemName(item), font, items ? Colors.White : dim);
                    nm.AutowrapMode = TextServer.AutowrapMode.Off;
                    nm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    nm.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    h.AddChild(nm);
                    h.AddChild(GuildChip($"x{it.Count}", QuickGold, small));
                    list.AddChild(b);
                    firstFocus ??= items ? b : null;
                }
                if (!items) list.AddChild(Text("Novatos ainda não tiram itens do baú. Peça ao Mestre para mudar seu cargo.", small, dim));
                var put = Btn("guardar", "+ Guardar itens da pasta", true, () => { _guildPage = "guardar"; RefreshGuild(); });
                GuildStyle(put, accent: QuickGold);
                box.AddChild(put);
                firstFocus = put;
                break;
            }
            case "guardar":
            {
                box.AddChild(Text("Escolha o que guardar (toque / clique guarda 1). Itens equipados e de enredo não vão.", small, dim));
                var list = new VBoxContainer();
                list.AddThemeConstantOverride("separation", 4);
                box.AddChild(Scroll(list, listH));
                var cands = GuildCandidates();
                if (cands.Count == 0) list.AddChild(Text("Nada livre para guardar.", font, dim));
                foreach (var (item, room) in cands)
                {
                    var b = ListRow($"guardar_{item.Table}_{item.Row}", () => GuildDeposit(item, 1, 0));
                    var h = RowBox(b);
                    var nm = Text(ItemName(item), font);
                    nm.AutowrapMode = TextServer.AutowrapMode.Off;
                    nm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    nm.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    h.AddChild(nm);
                    if (room > 1) h.AddChild(GuildChip($"tem {room}", GuildBlue, small));
                    list.AddChild(b);
                    firstFocus ??= b;
                }
                var back = Btn("voltar", "Voltar para o baú", true, () => { _guildPage = "bau"; RefreshGuild(); });
                box.AddChild(back);
                firstFocus ??= back;
                break;
            }
            case "historico":
            {
                var list = new VBoxContainer();
                list.AddThemeConstantOverride("separation", 2);
                box.AddChild(Scroll(list, listH));
                if (_guildLog.Length == 0) list.AddChild(Text("Carregando...", small, dim));
                int n = 0;
                foreach (var line in _guildLog)
                {
                    var rowP = new PanelContainer();
                    rowP.AddThemeStyleboxOverride("panel", GuildBox(n++ % 2 == 0 ? new Color(1, 1, 1, 0.04f) : new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 4, 4));
                    var full = GuildLogLine(line);
                    var hr = new HBoxContainer();
                    hr.AddThemeConstantOverride("separation", 12);
                    rowP.AddChild(hr);
                    // "dd/MM HH:mm  texto": data à parte, em cinza
                    if (full.Length > 13 && full[2] == '/' && full[11] == ' ')
                    {
                        var when = Text(full[..11], small, dim);
                        when.AutowrapMode = TextServer.AutowrapMode.Off;
                        hr.AddChild(when);
                        full = full[13..];
                    }
                    var what = Text(full, small);
                    what.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    hr.AddChild(what);
                    list.AddChild(rowP);
                }
                break;
            }
            default:
            {
                _guildPage = "membros";
                var list = new VBoxContainer();
                list.AddThemeConstantOverride("separation", 4);
                int online = g.Members.Count(m => m.PlayerId >= 0);
                box.AddChild(Text($"{online} de {g.Members.Length} online · {(touch ? "toque" : "clique")} num membro para ver as ações", small, dim));
                box.AddChild(Scroll(list, listH - (manager ? rowH : 0)));
                foreach (var m in g.Members.OrderBy(m => m.PlayerId >= 0 ? 0 : 1).ThenBy(m => m.Rank).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool on = m.PlayerId >= 0;
                    string where = on ? (StageName(m.Stage) is { Length: > 0 } st ? st : "online") + (m.Level > 0 ? $" · Lv {m.Level}" : "") : LastSeen(m.LastSeen);
                    var b = ListRow($"membro_{m.Account}", () =>
                    {
                        _guildMember = m.Account;
                        _guildPage = "membro";
                        RefreshGuild();
                    });
                    var h = RowBox(b);
                    var dot = Text(on ? "●" : "●", font, on ? GuildOnline : new Color(0.4f, 0.4f, 0.5f));
                    dot.VerticalAlignment = VerticalAlignment.Center;
                    h.AddChild(dot);
                    var who = Text(m.Name + (m.PlayerId == Online.MyId && on ? "  (você)" : ""), font, on ? Colors.White : dim);
                    who.VerticalAlignment = VerticalAlignment.Center;
                    who.AutowrapMode = TextServer.AutowrapMode.Off;
                    who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    who.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    h.AddChild(who);
                    var place = Text(where, small, on ? GuildOnline with { A = 0.85f } : dim);
                    place.AutowrapMode = TextServer.AutowrapMode.Off;
                    place.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    h.AddChild(place);
                    var rank = GuildChip(RankName(m.Rank), RankColor(m.Rank), small);
                    rank.CustomMinimumSize = new Vector2(touch ? 170 : 130, 0);
                    ((Label)rank.GetChild(0)).HorizontalAlignment = HorizontalAlignment.Center;
                    h.AddChild(rank);
                    list.AddChild(b);
                }
                var bottom = Row(box);
                if (manager)
                {
                    var inv = Btn("convidar", "Convidar pelo nome", g.Members.Length < g.MaxMembers, () => { _guildPage = "convidar"; RefreshGuild(); });
                    var notice = Btn("aviso", "Mudar o aviso", true, () => { _guildPage = "aviso"; RefreshGuild(); });
                    inv.SizeFlagsHorizontal = notice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    bottom.AddChild(inv);
                    bottom.AddChild(notice);
                }
                var leave = Btn("sair", "Sair da guilda", true, () =>
                {
                    _guildMember = g.Members.FirstOrDefault(x => x.PlayerId == Online.MyId)?.Account ?? 0;
                    _guildPage = "membro";
                    RefreshGuild();
                });
                leave.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bottom.AddChild(leave);
                break;
            }
        }
        if (_guildBusy) box.AddChild(Text("Esperando o servidor...", small, QuickGold, HorizontalAlignment.Center));
        box.AddChild(Btn("fechar", "Fechar (Esc)", true, CloseGuild));
        FinishGuild(box, firstFocus);
    }

    static string LastSeen(long unix)
    {
        if (unix <= 0) return "fora do jogo";
        var when = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
        var ago = DateTimeOffset.Now - when;
        return ago.TotalHours < 1 ? "visto agora há pouco" : ago.TotalDays < 1 ? $"visto há {(int)ago.TotalHours} h" : $"visto em {when:dd/MM}";
    }

    void FinishGuild(VBoxContainer box, Control? firstFocus)
    {
        UseGameFont(box);
        var restore = _guildFocus != null ? box.FindChild(_guildFocus, true, false) as Control : null;
        if (restore is BaseButton { Disabled: true }) restore = null;
        (restore ?? firstFocus)?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // Pergunta de confirmação dentro da própria janela (troca o conteúdo por Sim / Cancelar).
    void GuildConfirm(string question, string yes, Action act)
    {
        if (_guildLayer == null) return;
        _guildLayer.QueueFree();
        _guildLayer = new CanvasLayer { Layer = 16 };
        AddChild(_guildLayer);
        bool touch = Touch && !_usingPad;
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _guildLayer.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", QuickBox(QuickBorder));
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(touch ? 700 : 480, 0) };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        var q = new Label { Text = question, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        q.AddThemeFontSizeOverride("font_size", touch ? 26 : 20);
        q.AddThemeColorOverride("font_color", QuickGold);
        box.AddChild(q);
        bool blocked = question.StartsWith("O Mestre só sai", StringComparison.Ordinal);
        Button? first = null;
        foreach (var (label, run) in blocked ? new[] { ("Voltar", (Action?)null) } : new[] { (yes, (Action?)act), ("Cancelar", null) })
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(0, touch ? 60 : 38) };
            b.AddThemeFontSizeOverride("font_size", touch ? 26 : 20);
            b.Pressed += () => { run?.Invoke(); RefreshGuild(); };
            box.AddChild(b);
            first ??= b;
        }
        UseGameFont(box);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    void ShowGuildPrompt(SocialPrompt p)
    {
        bool busy = Online.Guild is { Id: > 0 };
        OpenInteractWindow($"{p.FromTamer} convidou você para a guilda {p.InviteId}.",
        [
            ("Entrar na guilda", !busy, busy ? "Você já está em uma guilda." : null, () => Online.SendGuild(new GuildCmd("accept", p.FromId))),
            ("Recusar", true, null, () => Online.SendGuild(new GuildCmd("decline", p.FromId))),
        ]);
    }

    // DEPURAÇÃO (DW4_ABRIR=guilda | guilda-bau | guilda-membro | guilda-criar): janela com uma guilda de exemplo, sem servidor, para
    // o DW4_PRINT fotografar no PC e no celular (DW4_TOQUE=1).
    void OpenGuildDemo(string page)
    {
        if (!OS.IsDebugBuild()) return;
        Online.DebugFakeGuild(page == "criar" ? GuildView.None() : new GuildView(5, "Tamers BR", "TBR", 3, 3500, 3000, 6000, 30, 2,
            "Reunião sábado às 20h no lobby!", 12500, 0, 1,
            [new(1, PlayerName, 1, 3500, Online.MyId, _stageName, _level, 0), new(2, "Renamon", 2, 1200, 99, "stag0102", 33, 0),
             new(3, "Patamon", 3, 300, -1, "", 0, DateTimeOffset.Now.AddHours(-5).ToUnixTimeSeconds()),
             new(4, "Agumon", 4, 0, -1, "", 0, DateTimeOffset.Now.AddDays(-3).ToUnixTimeSeconds())],
            [new("conItem", 0, 12), new("weapon", 5, 1), new("armor1", 2, 2)]));
        _guildOpen = true;
        _guildPage = page switch { "bau" => "bau", "membro" => "membro", "criar" => "criar", _ => "membros" };
        _guildMember = 2;
        RefreshGuild();
    }
}
