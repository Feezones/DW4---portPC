using DW4.Gameplay;

namespace DW4.Script.Be;

/// <summary>O que o jogo (cliente ou servidor) faz com os comandos dos scripts.</summary>
public interface IBeEngine
{
    /// <summary>Comando entregue a um objeto (be:sd na hora ou depois do atraso, ou uma entrada set do diretor de demo).</summary>
    void Deliver(BeMessage message);

    /// <summary>Consulta (ISSEQNAME, GETSELECT, GETGENERATEPOS…). null = o jogo ainda não sabe responder.</summary>
    double? Query(BeMessage message);

    /// <summary>
    /// Cálculo de luta pedido por um hit_*.lua (be:BLT* e afins, ex. BLTisHitCheck, BLTgetDamageValue(crítico), BLTgetDamageRank(hp que
    /// sobrou, crítico)) no contexto do golpe que acertou o modelo atual. null = o motor não sabe: o BeHost devolve 0 (neutro) e avisa.
    /// </summary>
    double? Fight(string method, double[] args) => null;

    /// <summary>Versão texto (BLTgetAttributeStr, BLPgetAttackZokusei). null = sem resposta.</summary>
    string? FightText(string method, double[] args) => null;

    /// <summary>Posição e direção (radianos, atan2(frente.x, frente.z)) do jogador p (PLAYERgetPosStr). null = sem jogador.</summary>
    (double X, double Y, double Z, double Yaw)? PlayerPose(int player) => null;

}

/// <summary>
/// O modelo "dono" do script em execução (hit_*.lua roda no contexto do objeto atingido): MDLaddHp, MDLsetSeq etc.
/// </summary>
public interface IBeModel
{
    /// <summary>Índice da instância (MDLgetRamNo).</summary>
    int Index { get; }
    int Hp { get; set; }
    /// <summary>Código do golpe que acertou (MDLgetBulletKind): 10000 físico, 40000 + linha técnica (HIPÓTESE), …</summary>
    int BulletKind { get; }
    void SetSequence(string name);
    /// <summary>HP máximo (MDLgetMHp).</summary>
    int MaxHp => 0;
    /// <summary>Nome do modelo (MDLsetSD manda a mensagem para ele mesmo: MODELCTRL nome índice comando).</summary>
    string ModelName => "";
    /// <summary>Posição no mundo (MDLgetPosStr).</summary>
    (double X, double Y, double Z) Position => default;
    /// <summary>Direção em radianos em torno de Y, atan2(frente.x, frente.z) (MDLgetRotStr; mesma convenção do PadMovement).</summary>
    double Yaw => 0;
    /// <summary>Sequência atual (MDLisSeqName); null se não se sabe.</summary>
    string? SequenceName => null;
    /// <summary>MDLgetRecNo: FATO 0x802EE01C lê +0x14 do contexto; HIPÓTESE: a linha do modelo na tabela enemy.</summary>
    int RecNo => 0;
}

/// <summary>
/// Evento de gatilho de área (mat*.lua). FATO (0x803037D0..78): o motor roda "&lt;volume&gt;.lua" quando muda quem está dentro
/// (entrada ou saída). HIPÓTESE forte pelos scripts: MATisPlayer() = 1 na entrada, 0 na saída; MATsetRun(0) desliga o volume.
/// </summary>
public sealed record BeMatEvent(bool Entered, bool AllInside, int PlayerNo, Action<bool> SetRun);

/// <summary>Dados do sistema que os SYS* devolvem (escolhas do menu e do grupo).</summary>
public sealed class BeSystem
{
    /// <summary>Pasta da fase (CFLgetFolderName), ex.: "dng101b1".</summary>
    public string FolderName { get; set; } = "";
    /// <summary>FATO (boot.lua): índice em JPN_, USA_, ENG_, FRA_, ITA_, GER_, SPN_; USA = 1.</summary>
    public int Country { get; set; } = 1;
    public int IsPackOnly { get; set; }
    /// <summary>Até 4 jogadores: nome do modelo ("p_girumon") e as 3 armas (SYSgetWeaponName(p, 0..2)); null = ausente.</summary>
    public (string Model, string[] Weapons)?[] Players { get; } = new (string, string[])?[4];
}

/// <summary>
/// O objeto global <c>be</c> dos scripts: os métodos do motor (tabela de 116 descritores em 0x804242AC, despachante
/// 0x802EA1FC, com sobrecarga pelo número de argumentos) ligados ao <see cref="IBeEngine"/>, ao <see cref="GameRam"/>, ao
/// <see cref="BeMessenger"/> e ao <see cref="DemoManager"/>. Métodos ainda não implementados devolvem nil e são avisados
/// uma vez por <see cref="Unimplemented"/>. Tipos: FATO pelos descritores — booleano de retorno vai como número 0/1
/// (FLGisOn faz pushnumber), nunca nil.
/// </summary>
public sealed class BeHost
{
    readonly IBeEngine _engine;
    readonly HashSet<string> _warned = [];
    readonly List<string> _files = [];
    readonly Queue<string> _luaQueue = new();

    public GameRam Ram { get; }
    public BeSystem Sys { get; }
    public BeMessenger Messenger { get; }
    public DemoManager Demo { get; }
    public LuaTable Table { get; } = new();

    /// <summary>Modelo no contexto do script atual (hit_*.lua); null fora desse contexto.</summary>
    public IBeModel? Model { get; set; }

    /// <summary>Gatilho de área no contexto do script atual (mat*.lua); null fora desse contexto.</summary>
    public BeMatEvent? Mat { get; set; }

    /// <summary>
    /// Script de mira em execução (aim_*.lua): o nome da chamada que o motor fez (MDLisCallName compara com ele) e o alvo que
    /// o script devolve (MDLsetWorldPosTarget). FATO (0x802EDEB0, 0x802EDEFC): os dois ficam no contexto em 0x80535914
    /// (+0x34 nome, +0x38 vetor de saída); sem nome, MDLisCallName só é 1 para nome nulo; sem vetor, o alvo é ignorado.
    /// </summary>
    public string? CallName { get; set; }
    public (double X, double Y, double Z)? WorldPosTarget { get; set; }

    /// <summary>Lista montada pelo boot.lua (CFLclear/CFLsetName): os IGB que a fase carrega.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Método be:* ainda não implementado (nome e argumentos), avisado uma vez por nome.</summary>
    public Action<string, object?[]>? Unimplemented { get; set; }

    /// <summary>Valor de be:TXTgetCallNo(): qual tela de texto o motor pediu ao txt_status.lua (0 = ajuda, 3 = atributos, 15 = menu…).</summary>
    public int TextCallNo { get; set; }

    /// <summary>Se definido, as mensagens be:sd vão para cá em vez do motor (para ler os textos do txt_status.lua sem efeito na fase).</summary>
    public Action<BeMessage>? Capture { get; set; }

    public BeHost(IBeEngine engine, GameRam ram, BeSystem system)
    {
        _engine = engine;
        Ram = ram;
        Sys = system;
        Messenger = new BeMessenger(engine.Deliver);
        Demo = new DemoManager(Messenger, engine.Query);
        Table.Missing = (_, key) => key is string name ? Method(name) : null;
    }

    /// <summary>Próximo script agendado por be:DMsetLua, liberado quando o diretor de demo fica livre (HIPÓTESE).</summary>
    public string? NextLua() => Demo.Idle && _luaQueue.Count > 0 ? _luaQueue.Dequeue() : null;

    /// <summary>Um quadro de 60 Hz: entrega as mensagens atrasadas e avança o diretor de demo.</summary>
    public void Tick()
    {
        Messenger.Tick();
        Demo.Tick();
    }

    static string? S(object?[] a, int i) => i < a.Length ? Lua.ToStr(a[i]) : null;
    static int I(object?[] a, int i) => i < a.Length && Lua.TryNumber(a[i], out double d) ? (int)d : 0;
    static long L(object?[] a, int i) => i < a.Length && Lua.TryNumber(a[i], out double d) ? (long)d : 0;
    // HIPÓTESE: parâmetro booleano a partir de número: diferente de zero (os scripts passam 0/1).
    static bool B(object?[] a, int i) => i < a.Length && (a[i] is double d ? d != 0 : a[i] != null);
    static object?[] N(double v) => [v];

    // FATO (MDLgetBulletName, 0x802EDC08): nome = tabela 0x804DD2A0[código / 10000 − 1]. Bate com os códigos lidos antes: 40000 técnica,
    // 60000 sonda de conversa, 70000 golpe de inimigo. HIPÓTESE: fora da tabela (o jogo leria lixo) devolve "".
    static readonly string[] BulletNames = ["PCWA", "PCCA", "PCSA", "PCMA", "PCBA", "PCTK", "ENAT", "PCMK", "ENSP"];
    public static string BulletName(int kind) => kind / 10000 - 1 is var i && i >= 0 && i < BulletNames.Length ? BulletNames[i] : "";
    static object?[] Bool(bool v) => [v ? 1.0 : 0.0];
    static readonly object?[] Nothing = [];

    static double[] Numbers(object?[] a) => a.Skip(1).Select(x => Lua.TryNumber(x, out double d) ? d : 0).ToArray();

    // Sem resposta do motor: valor neutro e um aviso por método (Unimplemented).
    T Warn<T>(string name, T neutral)
    {
        if (_warned.Add("fight:" + name)) Unimplemented?.Invoke(name + " (sem resposta do motor, neutro)", []);
        return neutral;
    }

    static BeMessage Msg(object?[] a, int cmdAt = 4, int argsAt = -1) =>
        new(S(a, 1) ?? "", S(a, 2), I(a, 3), S(a, cmdAt) ?? "", BeArg.Parse(argsAt > 0 ? S(a, argsAt) : null), argsAt > 0 ? S(a, argsAt) : null);

    LuaHostFunction Method(string name) => new(name, a => Call(name, a));

    // a[0] é o próprio be (chamada be:Metodo(...)).
    object?[] Call(string name, object?[] a)
    {
        int n = a.Length - 1;
        switch (name)
        {
            // Mensagens na hora ou com atraso: sd (ids 29..32) = (classe, nome, índice, comando[, argumentos][, atraso]).
            case "sd":
            {
                bool hasArgs = n >= 5 && a[5] is string;
                var m = Msg(a, 4, hasArgs ? 5 : -1);
                long delay = n >= 6 ? L(a, 6) : n == 5 && !hasArgs ? L(a, 5) : 0;
                if (Capture != null) Capture(m);
                else Messenger.Send(m, delay);
                return Nothing;
            }
            case "DMsetSD":
            {
                bool hasArgs = n >= 5 && a[5] is string;
                long delay = n >= 6 ? L(a, 6) : n == 5 && !hasArgs ? L(a, 5) : 0;
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Set, Msg(a, 4, hasArgs ? 5 : -1), delay));
                return Nothing;
            }
            case "DMchkSD":
            {
                // (classe, nome, índice, consulta[, argumento], condição)
                string cond = S(a, n) ?? "";
                var m = Msg(a, 4, n >= 6 ? 5 : -1);
                var kind = cond is "!=0" or "==1" ? DemoManager.Kind.CheckNonZero : DemoManager.Kind.CheckZero;
                Demo.Add(new DemoManager.Entry(kind, m, 0));
                return Nothing;
            }
            case "DMswtSD":
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Switch, Msg(a, 4, n >= 5 ? 5 : -1), 0));
                return Nothing;
            case "DMcase":
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Case, null, 0, I(a, 1)));
                return Nothing;
            case "DMswtEnd":
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.SwitchEnd, null, 0));
                return Nothing;
            case "DMrun":
                Demo.Run();
                return Nothing;
            case "DMgetCount":
                return N(Demo.Count);
            case "DMsetLua":
                if (S(a, 1) is { } lua) _luaQueue.Enqueue(lua);
                return Nothing;

            // Estado comum
            case "FLGisOn": return N(Ram.FlagOn(I(a, 1)));
            case "FLGset": Ram.SetFlag(I(a, 1), B(a, 2)); return Nothing;
            case "COUNTERget": return N(Ram.Counter(I(a, 1)));
            case "COUNTERset": Ram.SetCounter(I(a, 1), I(a, 2)); return Nothing;

            // Lista de arquivos da fase
            case "CFLclear": _files.Clear(); return Nothing;
            case "CFLsetName": if (S(a, 1) is { } file) _files.Add(file); return Nothing;
            case "CFLgetFolderName": return [Sys.FolderName];

            // Sistema
            case "SYSisJoinPlayer": return Bool(Player(I(a, 1)) != null);
            case "SYSgetPlayerNinzu": return N(Ram.Ninzu);
            case "SYSgetPlayerName": return [Player(I(a, 1))?.Model];
            case "SYSgetWeaponName":
                return [Player(I(a, 1)) is { } p && I(a, 2) is var w && w >= 0 && w < p.Weapons.Length ? p.Weapons[w] : null];
            // SYSctlPlayer(tipo, comando, jogador, valor), ex.: ("HP", "GETHP", p, 0): pergunta ao motor (classe SYSCTL).
            case "SYSctlPlayer":
                return N(_engine.Query(new BeMessage("SYSCTL", S(a, 1), I(a, 3), S(a, 2) ?? "", [], null)) ?? 0);
            case "SYSgetDifficulty": return N(Ram.Difficulty);
            case "SYSgetCountry": return N(Sys.Country);
            // HIPÓTESE (stag0101 first.lua compara o retorno com número): SYSgetRandom(n) → inteiro em [0, n);
            // SYSgetRandom(a, b) → inteiro em [a, b]. Sem argumento: [0, 32768). Conferir a aridade no descritor be (0x804242AC).
            case "SYSgetRandom":
                {
                    int lo = 0, hi = 32767;
                    if (a.Length > 2) { lo = I(a, 1); hi = I(a, 2); }
                    else if (a.Length > 1) hi = I(a, 1) - 1;
                    return N(hi <= lo ? lo : Random.Shared.Next(lo, hi + 1));
                }
            // FATO (txt_status.lua): "if be:TXTgetCallNo() == n then … TEXTCTL TEXTSET \"FONTE/nó/I<k>\" …"; o motor escolhe o n.
            case "TXTgetCallNo": return N(TextCallNo);
            case "SYSgetCameraMode": return N(Ram.CameraMode);
            case "SYSgetLightMode": return N(Ram.LightMode);
            case "SYSgetIsPackOnly": return N(Sys.IsPackOnly);

            // Modelo do contexto (hit_g_box.lua: MDLsetDMGTurn(0); se não for técnica 40040..40099, MDLaddHp(−100) e SQ_DDMG).
            case "MDLgetBulletKind": return N(Model?.BulletKind ?? 0);
            case "MDLgetHp": return N(Model?.Hp ?? 0);
            case "MDLgetMHp": return N(Model?.MaxHp ?? 0);
            // MDLsetSD(comando[, argumentos]): be:sd para o próprio modelo do contexto (SETLABEL "SQ_SDMG/common", SETSLEEP "I<n>"…).
            case "MDLsetSD" when Model != null:
            {
                var m = new BeMessage("MODELCTRL", Model.ModelName, Model.Index, S(a, 1) ?? "", BeArg.Parse(S(a, 2)), S(a, 2));
                if (Capture != null) Capture(m); else Messenger.Send(m, 0);
                return Nothing;
            }
            // Luta (hit_f_apocaly.lua e outros hit_*.lua de chefe): o motor calcula pelo golpe atual; sem resposta, 0 e aviso.
            case "MDLgetBulletName": return [BulletName(Model?.BulletKind ?? 0)];
            case "BLTgetAttributeStr" or "BLPgetAttackZokusei":
                return [_engine.FightText(name, Numbers(a)) ?? Warn(name, "common")];
            case var f when (f.StartsWith("BLT", StringComparison.Ordinal) || f.StartsWith("BLP", StringComparison.Ordinal)) && f is not ("BLTgetShootRamNo") || f is "MDLdspDamage" or "MDLdspDamageP":
                return N(_engine.Fight(name, Numbers(a)) ?? Warn(name, 0.0));
            case "MDLaddHp": if (Model != null) Model.Hp += I(a, 1); return Nothing;
            case "MDLsetSeq": if (Model != null && S(a, 1) is { } seq) Model.SetSequence(seq); return Nothing;
            // MDLisLive(nome, índice): o objeto existe? (o jogo consulta; aqui pelo IBeEngine com ISLIVE). Sem argumentos: o do contexto.
            case "MDLisLive" when n >= 2:
                return Bool(_engine.Query(new BeMessage("MODELCTRL", S(a, 1), I(a, 2), "ISLIVE", [], null)) is > 0);
            case "MDLisLive": return Bool(Model is { Hp: > 0 });
            case "MDLgetRamNo": return N(Model?.Index ?? 0);
            case "MDLgetRecNo": return N(Model?.RecNo ?? 0);
            // HIPÓTESE: jogador que acertou o golpe; a fatia tem só o jogador 0.
            case "BLTgetShootRamNo": return N(0);
            // HIPÓTESE pelo nome: turno de dano (reação); sem efeito na fatia.
            case "MDLsetDMGTurn": return Nothing;

            // Gatilho de área do contexto
            case "MATisPlayer": return Bool(Mat?.Entered == true);
            case "MATisAllPlayer": return Bool(Mat?.AllInside == true);
            case "MATgetPlayerNo": return N(Mat?.PlayerNo ?? 0);
            case "MATsetRun": Mat?.SetRun(B(a, 1)); return Nothing;

            // FATO (0x802EE1F0): envia PLAYERPOINT GETGENERATEPOS e devolve o inteiro (ponto de entrada na fase).
            case "PLAYERgetPos":
                return N(_engine.Query(new BeMessage("PLAYERPOINT", null, -1, "GETGENERATEPOS", [], null)) ?? 0);
        }
        if (Lobby(name, a) is { } done) return done;
        if (_warned.Add(name)) Unimplemented?.Invoke(name, a[1..]);
        return Nothing;
    }

    // Métodos que o lobby (stagml11) usa, lidos no main.dol em 2026-10-25 (docs/gameplay/fase5-lobby-be.md).
    object?[]? Lobby(string name, object?[] a)
    {
        switch (name)
        {
            // FATO (0x802EE480): corpo vazio (blr). Caixa de depuração do desenvolvimento, sem efeito no jogo.
            case "MsgBox": return Nothing;
            // FATO (0x802EBC88): devolve sempre 1.
            case "SYSgetArea": return N(1);

            // FATO (0x802EBCFC, 0x802EBE94, 0x802EBEF8): GAMERAM GATESTAGENO = n e GATESTAGENAME = nome (nulo → texto vazio).
            case "SYSsetGateStage":
                Ram.GateStageName = S(a, 1) ?? "";
                Ram.GateStageNo = I(a, 2);
                return Nothing;
            case "SYSgetGateStageName": return [Ram.GateStageName];
            case "SYSgetGateStageNo": return N(Ram.GateStageNo);

            // FATO (0x802EC380): LEVEL do registro do jogador p.
            case "SYSgetPlayerLevel": return N(PlayerData(I(a, 1))?.Level() ?? 0);
            // FATO (0x802EC3E8): BASE_ISUSE[i] do jogador p (forma i já obtida).
            case "SYSisGetShinkaDigimon":
                return Bool(PlayerData(I(a, 1)) is { } who && Form(I(a, 2)) is { } fi && who.Forms[fi].Unlocked);
            // FATO (0x802EC590): SYSctlShinka(comando, p, i). GETEXP/GETLEVEL = BASE_EXP/BASE_LEVEL[i] se a forma foi obtida,
            // senão −1; SETSHINKA = libera (0 se já estava, 1 se liberou); outro comando = 0.
            case "SYSctlShinka":
            {
                if (PlayerData(I(a, 2)) is not { } pd || Form(I(a, 3)) is not { } f) return N(0);
                var form = pd.Forms[f];
                return S(a, 1) switch
                {
                    "GETEXP" => N(form.Unlocked ? form.Exp : -1),
                    "GETLEVEL" => N(form.Unlocked ? form.Level : -1),
                    "SETSHINKA" => Bool(pd.Forms.Unlock(f)),
                    _ => N(0),
                };
            }
            // FATO (0x802EC430): CARDLIST do jogador 0. KAZU = quantas valem 1; SET = 0 se já tinha, senão marca e 1;
            // CLR = 0 se não tinha, senão desmarca e 1; outro = 0.
            case "SYSctlCardFolder":
            {
                var cards = Ram.Players[0].Cards;
                int i = I(a, 2);
                bool inRange = i >= 0 && i < cards.Length;
                switch (S(a, 1))
                {
                    case "KAZU": return N(cards.Count(c => c == 1));
                    case "SET":
                        if (inRange && cards[i] != 0) return N(0);
                        if (inRange) cards[i] = 1;
                        return N(1);
                    case "CLR":
                        if (!inRange || cards[i] == 0) return N(0);
                        cards[i] = 0;
                        return N(1);
                    default: return N(0);
                }
            }

            // FATO (0x802EC26C, 0x802EBF5C, 0x802EC164): os três viram a mensagem GAMERAM CTRLITEM com a pasta ("BIT", "DEVICE",
            // "FLAG") e os argumentos, e devolvem a resposta. O tratador do CTRLITEM não foi lido: o efeito de cada comando abaixo
            // é HIPÓTESE pelos nomes e pelo uso nos scripts (ex.: treasure02 "ADD" 0 2000 depois de dar o prêmio).
            // SYSctlBit(comando, p, quantia)
            case "SYSctlBit":
            {
                if (PlayerData(I(a, 2)) is not { } pd) return N(0);
                var items = pd.Items;
                switch (S(a, 1))
                {
                    case "ADD": items.Bit = Math.Max(0, items.Bit + I(a, 3)); return N(1);
                    case "SET": items.Bit = Math.Max(0, I(a, 3)); return N(1);
                    case "GET": return N(items.Bit);
                    default: return Warn("SYSctlBit " + S(a, 1), N(0));
                }
            }
            // SYSctlDeviceFolder(comando, p, tabela, linha, 0, 0): ISMAX = pasta cheia (36, FATO do DEVICEFOLDER); IS = tem o
            // item; ISEQUIP = equipado; SET = guarda (0 se a pasta está cheia); CLR = tira.
            case "SYSctlDeviceFolder":
            {
                if (PlayerData(I(a, 2)) is not { } pd) return N(0);
                var item = new ItemRef(S(a, 3) ?? "", I(a, 4));
                return S(a, 1) switch
                {
                    "ISMAX" => Bool(pd.Items.Devices.Count >= Inventory.DeviceMax),
                    "IS" => Bool(pd.Items.Has(item)),
                    "ISEQUIP" => Bool(pd.IsEquipped(item)),
                    "SET" => Bool(pd.Items.Add(item)),
                    "CLR" => Bool(pd.Items.Remove(item)),
                    var c => Warn("SYSctlDeviceFolder " + c, N(0)),
                };
            }
            // SYSctlFlagFolder(comando, n): bit n da FLAGFOLDER (FATO: 12 bytes; os impItem ligam esses bits) do jogador 0.
            case "SYSctlFlagFolder":
            {
                var items = Ram.Players[0].Items;
                var item = new ItemRef("impItem", I(a, 2));
                return S(a, 1) switch
                {
                    "IS" => Bool(items.Has(item)),
                    "SET" => Bool(items.Add(item)),
                    "CLR" => Bool(items.Remove(item)),
                    var c => Warn("SYSctlFlagFolder " + c, N(0)),
                };
            }

            // FATO (0x802ECFB0 → 0x80302DB8): MATgetPlayerMatStat(p) = estado do jogador p no volume do gatilho atual (bit p de
            // dois mapas): 0, 1, 2 ou 3 (os dois). FATO (func.lua do lobby, funcHealGate): o volume da cura conta 0, 1 e 2 e só cura
            // quem dá 2. HIPÓTESE: 2 = entrou agora, 1 = saiu agora. O gatilho roda quando alguém entra ou sai (0x803037D0).
            case "MATgetPlayerMatStat":
                return N(Mat is { } mat && I(a, 1) == mat.PlayerNo ? (mat.Entered ? 2 : 1) : 0);

            // FATO (0x802ED8E0): leva o deslocamento local (x, y, z) pela matriz do modelo do contexto e devolve "V<%f,%f,%f>".
            // HIPÓTESE: a matriz é só posição + giro em Y (sem escala nem inclinação).
            case "MDLgetPosStr":
            {
                var (px, py, pz) = Model?.Position ?? default;
                double yaw = Model?.Yaw ?? 0, x = D(a, 1), z = D(a, 3);
                return [Vec(px + x * Math.Cos(yaw) + z * Math.Sin(yaw), py + D(a, 2), pz - x * Math.Sin(yaw) + z * Math.Cos(yaw))];
            }
            // FATO (0x802ED9A4): direção do modelo em graus (atan2 da frente × 57,29578) somada ao y pedido: "V<x,giro+y,z>".
            case "MDLgetRotStr":
                return [Vec(D(a, 1), (Model?.Yaw ?? 0) * 180 / Math.PI + D(a, 2), D(a, 3))];

            // FATO (0x802ECF50 → 0x80302D08 / 0x802ECF80 → 0x80302D58): o jogador p acabou de entrar (bit só no mapa atual) /
            // de sair (bit só no anterior) do volume do gatilho atual. Mesma leitura do MATgetPlayerMatStat (2 entrou, 1 saiu).
            case "MATisRidePlayer": return Bool(Mat is { } mr && I(a, 1) == mr.PlayerNo && mr.Entered);
            case "MATisRideOffPlayer": return Bool(Mat is { } mo && I(a, 1) == mo.PlayerNo && !mo.Entered);
            // FATO (0x802ED8D0): nome de informação do modelo do contexto (+0x18 de 0x80535914).
            case "MDLgetInfoName": return [Model?.ModelName];
            // FATO (0x802EE02C): o modelo do contexto está na sequência pedida.
            case "MDLisSeqName": return Bool(Model?.SequenceName is { } cur && cur == S(a, 1));
            // FATO (0x802EDF84): distância ao quadrado do modelo do contexto até o ponto.
            case "MDLgetKyoriSquare":
            {
                var (px, py, pz) = Model?.Position ?? default;
                double dx = px - D(a, 1), dy = py - D(a, 2), dz = pz - D(a, 3);
                return N(dx * dx + dy * dy + dz * dz);
            }
            // FATO (0x802EE144): manda MODELCTRL nome índice CHECKSTATUS estado e devolve a resposta (1 = está nesse estado).
            case "MDLisStatus":
                return Bool(_engine.Query(new BeMessage("MODELCTRL", S(a, 1), I(a, 2), "CHECKSTATUS", BeArg.Parse(S(a, 3)), S(a, 3))) is > 0);
            // FATO (0x802EDE70 → 0x802EDC54 com 1): mostra o número de EXP ganho sobre o modelo (MODELCTRL FIND_SPACE…). O motor desenha.
            case "MDLdspExp":
                _engine.Deliver(new BeMessage("MODELCTRL", Model?.ModelName, Model?.Index ?? 0, "DSPEXP", [], I(a, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return Nothing;
            // FATO (0x802EE36C): posição do jogador p levada pelo deslocamento → "V<%f,%f,%f>". O motor dá a pose do jogador.
            case "PLAYERgetPosStr":
            {
                var (px, py, pz, yaw) = _engine.PlayerPose(I(a, 1)) ?? default;
                double x = D(a, 2), z = D(a, 4);
                return [Vec(px + x * Math.Cos(yaw) + z * Math.Sin(yaw), py + D(a, 3), pz - x * Math.Sin(yaw) + z * Math.Cos(yaw))];
            }
            // SYSctlDiskFolder(comando, p, linha da conItem, quantidade): GAMERAM CTRLITEM "DISK" (FATO: 0x802EC064, mesma forma do
            // DEVICE). HIPÓTESE pelo uso: SET soma, CLR tira, IS = tem, ISMAX = cheio (9 por tipo, FATO do DISKFOLDER).
            case "SYSctlDiskFolder":
            {
                if (PlayerData(I(a, 2)) is not { } dp) return N(0);
                int row = I(a, 3), count = Math.Max(1, I(a, 4));
                var disk = new ItemRef("conItem", row);
                switch (S(a, 1))
                {
                    case "IS": return Bool(dp.Items.Disks(row) > 0);
                    case "ISMAX": return Bool(dp.Items.Disks(row) >= Inventory.DiskMax);
                    case "SET": { bool any = false; for (int k = 0; k < count && dp.Items.Add(disk); k++) any = true; return Bool(any); }
                    case "CLR": { bool any = false; for (int k = 0; k < count && dp.Items.Remove(disk); k++) any = true; return Bool(any); }
                    default: return Warn("SYSctlDiskFolder " + S(a, 1), N(0));
                }
            }

            case "MDLisCallName": return Bool(CallName == S(a, 1));
            case "MDLsetWorldPosTarget":
            {
                if (n(a) >= 3) WorldPosTarget = (D(a, 1), D(a, 2), D(a, 3));
                else if (BeArg.Parse(S(a, 1)).FirstOrDefault() is { Kind: 'V', Numbers.Length: 3 } v)
                    WorldPosTarget = (v.Numbers[0], v.Numbers[1], v.Numbers[2]);
                return Nothing;
            }
        }
        return null;

        static int n(object?[] args) => args.Length - 1;
    }

    static double D(object?[] a, int i) => i < a.Length && Lua.TryNumber(a[i], out double d) ? d : 0;

    // printf "%f" (6 casas, ponto) como o jogo.
    static string Vec(double x, double y, double z) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"V<{x:F6},{y:F6},{z:F6}>");

    BePlayer? PlayerData(int p) => p >= 0 && p < Ram.Players.Length ? Ram.Players[p] : null;
    static int? Form(int i) => i >= 0 && i < Evolution.Forms ? i : null;

    (string Model, string[] Weapons)? Player(int p) => p >= 0 && p < Sys.Players.Length ? Sys.Players[p] : null;
}
