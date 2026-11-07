using System.Text;

namespace DW4.Net;

/// <summary>
/// Protocolo do online do DW4 (ADR 0003). Cada pacote começa com um byte de <see cref="MsgType"/>; números em little-endian,
/// textos em UTF-8 com tamanho (ushort). Sem rede aqui: o servidor e o cliente passam os bytes pela LiteNetLib.
/// </summary>
public static class Protocol
{
    /// <summary>Muda quando o formato muda; cliente e servidor de versões diferentes não conversam.</summary>
    public const int Version = 8; // 2 (0.6.1): arma, menu do F; 3 (0.6.2): combate em grupo (CoopGroup, CoopRelay), HP/MP e PartyRoster;
                                  // 4: vagas de personagem no servidor (SaveList, SaveWrite, SaveDelete, SaveReply);
                                  // 5 (0.6.8): chat pelo servidor (ChatSend, ChatFrom, ChatNotice)
                                  // 6: troca de itens entre jogadores (TradeCmd, TradeView)
                                  // 7: líder no quadro da party (PartyEntry.Leader); party-leave/kick/promote no Interact
                                  // 8: guilda do DW4 (GuildCmd, GuildView, GuildBankDone, GuildLog, PlayerTag, Spawn.Guild, chat da guilda)
    /// <summary>Chave de conexão da LiteNetLib (recusa quem não é o DW4 antes de qualquer pacote).</summary>
    public const string ConnectionKey = "DW4-online-1";
    public const int DefaultPort = 7031;
    /// <summary>Estados de posição por segundo que o cliente manda.</summary>
    public const int StateRate = 20;
    public const int MaxText = 64;
    /// <summary>Membros de um grupo de combate (a party do original tem até 4).</summary>
    public const int MaxGroup = 4;
    /// <summary>Tamanho máximo de um pacote do combate em grupo (cabe num datagrama sem fragmentar).</summary>
    public const int MaxPayload = 1100;
    /// <summary>Tamanho máximo de um save comprimido num pacote (o JSON do jogo tem ~10 KB, ~2 KB comprimido).</summary>
    public const int MaxSave = 128 * 1024;
    /// <summary>Tamanho máximo do JSON de um save depois de descomprimir.</summary>
    public const int MaxSaveJson = 512 * 1024;
    /// <summary>Vagas de personagem por conta.</summary>
    public const int Slots = 4;
    /// <summary>Letras de uma mensagem de chat (o mesmo limite da caixa do jogo, ChatLog.MaxLength).</summary>
    public const int MaxChat = 120;
    /// <summary>Itens por lado numa troca (TradeOffer.MaxItems).</summary>
    public const int MaxTradeItems = 8;
    /// <summary>Membros e pilhas do baú que cabem num <see cref="GuildView"/> (a guilda tem até 65; o baú, 60 tipos).</summary>
    public const int MaxGuildMembers = 80;
    public const int MaxGuildBank = 80;
    public const int MaxGuildLog = 60;
    /// <summary>
    /// Motivo da recusa quando a mesma conta entra em outro aparelho: o cliente derrubado volta ao menu sem gravar e não reconecta
    /// sozinho (senão os dois aparelhos ficariam se derrubando e gravando um por cima do outro).
    /// </summary>
    public const string KickedReason = "Sua conta entrou em outro lugar.";
}

public enum MsgType : byte
{
    // cliente → servidor
    Hello = 1,
    State = 2,
    Interact = 3,
    CoopRelay = 4,
    SaveList = 5,
    SaveWrite = 6,
    SaveDelete = 7,
    ChatSend = 8,
    TradeCmd = 9,
    GuildCmd = 24,
    // servidor → cliente
    Welcome = 10,
    Reject = 11,
    Spawn = 12,
    Despawn = 13,
    RemoteState = 14,
    InteractResult = 15,
    SocialPrompt = 16,
    CoopGroup = 17,
    CoopFrom = 18,
    PartyRoster = 19,
    SaveReply = 20,
    ChatFrom = 21,
    ChatNotice = 22,
    TradeView = 23,
    GuildView = 25,
    GuildBankDone = 26,
    GuildLog = 27,
    PlayerTag = 28,
}

/// <summary>
/// Cliente → servidor: guilda do DW4 (protocolo 8). Ações: info (manda a <see cref="GuildView"/>), create (Text = nome, Text2 = tag),
/// invite (TargetId = jogador perto, ou Text = nome de um jogador online), accept / decline (TargetId = quem convidou), leave,
/// kick / transfer (Account = conta do membro), rank (Account e Num = cargo 2..4), notice (Text), deposit / withdraw (Table e Row
/// do item, Num = quantidade; Table "bit" = BIT, Num = quantia; Slot = a vaga do personagem), log, xp (Num = EXP ganha num inimigo)
/// e boss (chefe derrotado).
/// </summary>
public sealed record GuildCmd(string Action, int TargetId = 0, long Account = 0, string Text = "", string Text2 = "", int Num = 0, string Table = "",
    int Row = 0, int Slot = 0);

/// <summary>Membro na janela da guilda: conta, nome, cargo (1 Mestre .. 4 Novato), contribuição; online = PlayerId ≥ 0 (fase e nível); fora do jogo = −1.</summary>
public sealed record GuildMemberEntry(long Account, string Name, int Rank, long Contribution, int PlayerId, string Stage, int Level, long LastSeen);

public sealed record GuildBankEntry(string Table, int Row, int Count);

/// <summary>
/// Servidor → cliente: a guilda do jogador (Id 0 = sem guilda). Vem ao entrar e a cada mudança (membro entrou, saiu, cargo, aviso,
/// baú, nível, alguém ficou online). <see cref="Message"/> = resultado da última ação de quem recebe ("Guilda criada.", o motivo da
/// recusa...); vazio nas atualizações. <see cref="Bonus"/> = % a mais de EXP e BIT.
/// </summary>
public sealed record GuildView(long Id, string Name, string Tag, int Level, long Xp, long LevelXp, long NextXp, int MaxMembers, int Bonus,
    string Notice, long BankBit, long LogoVersion, int MyRank, GuildMemberEntry[] Members, GuildBankEntry[] Bank, string Message = "")
{
    public static GuildView None(string message = "") => new(0, "", "", 0, 0, 0, 0, 0, 0, "", 0, 0, 0, [], [], message);
}

/// <summary>
/// Servidor → cliente: guardar/tirar do baú terminou. Ok: o save do servidor já mudou (sai ou entra o item / BIT) e
/// <see cref="Seq"/> é o TradeSeq novo; o cliente faz o mesmo e grava. Senão, o motivo em Message e nada mudou.
/// </summary>
public sealed record GuildBankDone(bool Ok, string Action, string Table, int Row, int Count, int Bit, int Seq, string Message);

/// <summary>Servidor → cliente: o histórico da guilda, já em texto, do mais novo para o mais velho.</summary>
public sealed record GuildLog(string[] Lines);

/// <summary>Servidor → quem vê o jogador: a tag da guilda dele mudou ("" = sem guilda).</summary>
public sealed record PlayerTag(int PlayerId, string Tag);

/// <summary>Um item na troca: tabela do jogo (weapon, armor1, armor2, abChip, conItem) e linha.</summary>
public sealed record TradeItem(string Table, int Row);

/// <summary>
/// Cliente → servidor: troca de itens (protocolo 6). Ações: ask (pede troca a <see cref="TargetId"/>, com a vaga <see cref="Slot"/>
/// de quem pede), accept / decline (resposta ao pedido de <see cref="TargetId"/>, com a vaga de quem aceita), add / remove
/// (<see cref="Table"/> e <see cref="Row"/>), bit (<see cref="Bit"/> oferecido), lock, confirm e cancel.
/// </summary>
public sealed record TradeCmd(string Action, int TargetId = 0, int Slot = 0, string Table = "", int Row = 0, int Bit = 0);

/// <summary>
/// Servidor → cliente: como está a troca com <see cref="OtherId"/> (<see cref="OtherName"/>, o nome da placa). State: ask (o outro
/// pediu a troca: aceitar ou recusar), asked (seu pedido foi enviado), open, committing (gravando), done (feita: os itens já
/// trocaram no save do servidor; <see cref="Seq"/> é o número de trocas da vaga) ou cancelled (o motivo em <see cref="Message"/>).
/// Message num estado open é um aviso só para você (ex.: "No máximo 8 itens por troca.").
/// </summary>
public sealed record TradeView(int OtherId, string OtherName, string State, TradeItem[] MyItems, int MyBit, bool MyLocked, bool MyConfirmed,
    TradeItem[] TheirItems, int TheirBit, bool TheirLocked, bool TheirConfirmed, string Message = "", int Seq = 0);

/// <summary>Canal do chat no fio (mesma ordem do ChatChannel do jogo): todos no DW4, a party da conta ou privada.</summary>
public enum ChatTo : byte { Global = 0, Party = 1, Whisper = 2, Guild = 4 }

/// <summary>
/// Cliente → servidor: mensagem de chat. <see cref="To"/> é o destinatário da privada (nome do personagem da placa ou do Tamer);
/// vazio nos outros canais.
/// </summary>
public sealed record ChatSend(ChatTo Channel, string To, string Text);

/// <summary>
/// Servidor → cliente: mensagem de chat de <see cref="FromId"/>. <see cref="Sender"/> é o nome que aparece (personagem, ou o Tamer
/// sem personagem); na privada, <see cref="To"/> é o nome do destinatário (quem mandou recebe a cópia com ele).
/// </summary>
public sealed record ChatFrom(ChatTo Channel, int FromId, string Sender, string To, string Text);

/// <summary>Servidor → cliente: aviso sobre o chat só para quem mandou ("Ana não está online.", "Você não está em um grupo.").</summary>
public sealed record ChatNotice(string Text);

/// <summary>Cliente → servidor, ao conectar: ticket do launcher e o personagem da vaga.</summary>
public sealed record Hello(int Version, string Ticket, string Character, string Model, int Level);

/// <summary>Estado do jogador: fase, posição, direção, animação. Também anuncia troca de fase, forma e nível.</summary>
public sealed record PlayerState(string Stage, float X, float Y, float Z, float Facing, string Anim, string Model, int Level, string Weapon = "",
    int Hp = 0, int MaxHp = 0, int Mp = 0, int MaxMp = 0);

/// <summary>Um membro da party online no DW4 (em qualquer fase): para o quadro do grupo no canto esquerdo da tela.</summary>
/// <see cref="Leader"/> (protocolo 7): é o líder da party (quem criou o grupo ou recebeu a liderança).
public sealed record PartyEntry(int PlayerId, string Character, string Model, int Level, int Hp, int MaxHp, int Mp, int MaxMp, string Stage,
    bool Leader = false);

/// <summary>
/// Servidor → membros da party: quem da party está no DW4 agora, com nível, HP e MP (vem quando algo disso muda; vazio = sem party).
/// </summary>
public sealed record PartyRoster(PartyEntry[] Members);

public sealed record Welcome(int PlayerId, string Tamer);
public sealed record Reject(string Reason);
/// <summary>Outro jogador apareceu na mesma instância da fase. <see cref="Guild"/> = tag da guilda dele (protocolo 8; "" = sem guilda).</summary>
public sealed record Spawn(int PlayerId, string Character, string Tamer, PlayerState State, string Guild = "");
public sealed record Despawn(int PlayerId);
public sealed record RemoteState(int PlayerId, PlayerState State);

/// <summary>
/// Menu do F (cliente → servidor): <see cref="Action"/> sobre o jogador <see cref="TargetId"/>. Ações: relation (montar o menu),
/// friend-request, party-invite; e as respostas a um <see cref="SocialPrompt"/>: friend-accept, friend-decline, party-accept,
/// party-decline (o alvo é quem perguntou; <see cref="InviteId"/> no convite de grupo). Gestão do grupo (protocolo 7, quadro da
/// party): party-kick e party-promote (só o líder; alvo = membro da party, em qualquer fase) e party-leave (alvo = você mesmo).
/// </summary>
public sealed record Interact(int TargetId, string Action, string InviteId = "");

/// <summary>
/// Resposta do servidor a um <see cref="Interact"/>: deu certo ou o aviso (Message, os textos da DMO.API). Em "relation", o que
/// o menu precisa: Relation (none | friend | requestSent | requestReceived | blockedByYou | self) e a party.
/// </summary>
public sealed record InteractResult(int TargetId, string Action, bool Ok, string Message, string Relation = "", bool HasParty = false,
    bool AmLeader = false, bool TargetInMyParty = false, int PartyOccupied = 0, bool TargetInOtherParty = false);

/// <summary>Pergunta para quem recebeu um pedido de amizade (Kind "friend") ou um convite de grupo (Kind "party").</summary>
public sealed record SocialPrompt(string Kind, int FromId, string FromTamer, string InviteId = "");

/// <summary>
/// Grupo de combate (inimigos, dano e drops compartilhados: a party na mesma fase). <see cref="HostId"/> roda os inimigos; os
/// outros mostram cópias. Vem a cada mudança (alguém entrou, saiu, trocou de fase ou de party). Sozinho: Members = [você].
/// </summary>
public sealed record CoopGroup(int HostId, int[] Members);

/// <summary>Cliente → servidor: pacote do combate em grupo (<see cref="CoopCodec"/>) para os outros membros do grupo.</summary>
public sealed record CoopRelay(bool Reliable, byte[] Payload);

/// <summary>Servidor → cliente: pacote do combate em grupo vindo de <see cref="FromId"/>.</summary>
public sealed record CoopFrom(int FromId, byte[] Payload);

/// <summary>Cliente → servidor: as vagas de personagem da conta (a conta é a do ticket; resposta: <see cref="SaveReply"/> com as vagas).</summary>
public sealed record SaveList(int ReqId);

/// <summary>Cliente → servidor: grava a vaga <see cref="Slot"/> (0..3) com o save (JSON comprimido, <see cref="SaveData"/>).</summary>
public sealed record SaveWrite(int ReqId, int Slot, byte[] Data);

/// <summary>Cliente → servidor: apaga a vaga.</summary>
public sealed record SaveDelete(int ReqId, int Slot);

/// <summary>Uma vaga ocupada: número e o save (JSON comprimido).</summary>
public sealed record SaveEntry(int Slot, byte[] Data);

/// <summary>
/// Servidor → cliente: resposta a <see cref="SaveList"/>, <see cref="SaveWrite"/> ou <see cref="SaveDelete"/> (mesmo ReqId).
/// Ok=false traz o motivo em Message. Na lista, Slots = as vagas ocupadas.
/// </summary>
public sealed record SaveReply(int ReqId, bool Ok, string Message, SaveEntry[] Slots);

/// <summary>Save no fio: o JSON do jogo em gzip (os bytes de FLAG e CARDLIST em base64 são quase todos zero e comprimem bem).</summary>
public static class SaveData
{
    public static byte[] Pack(string json)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            var b = Encoding.UTF8.GetBytes(json);
            gz.Write(b, 0, b.Length);
        }
        return ms.ToArray();
    }

    /// <summary>Descomprime; <see cref="FormatException"/> se não é gzip ou passa de <see cref="Protocol.MaxSaveJson"/>.</summary>
    public static string Unpack(byte[] data)
    {
        try
        {
            using var gz = new System.IO.Compression.GZipStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            var buf = new byte[16384];
            int n;
            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
            {
                outMs.Write(buf, 0, n);
                if (outMs.Length > Protocol.MaxSaveJson) throw new FormatException("save grande demais");
            }
            return Encoding.UTF8.GetString(outMs.ToArray());
        }
        catch (InvalidDataException) { throw new FormatException("save comprimido inválido"); }
    }
}

/// <summary>Grava e lê os pacotes. Erro de formato vira <see cref="FormatException"/> (o servidor derruba quem mandou).</summary>
public static class Codec
{
    public static byte[] Write(object msg)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        switch (msg)
        {
            case Hello h: w.Write((byte)MsgType.Hello); w.Write(h.Version); Str(w, h.Ticket, 128); Str(w, h.Character); Str(w, h.Model); w.Write(h.Level); break;
            case PlayerState s: w.Write((byte)MsgType.State); State(w, s); break;
            case Welcome x: w.Write((byte)MsgType.Welcome); w.Write(x.PlayerId); Str(w, x.Tamer); break;
            case Reject x: w.Write((byte)MsgType.Reject); Str(w, x.Reason, 200); break;
            case Spawn x: w.Write((byte)MsgType.Spawn); w.Write(x.PlayerId); Str(w, x.Character); Str(w, x.Tamer); State(w, x.State); Str(w, x.Guild, 8); break;
            case GuildCmd x:
                w.Write((byte)MsgType.GuildCmd); Str(w, x.Action, 16); w.Write(x.TargetId); w.Write(x.Account); Str(w, x.Text, 200); Str(w, x.Text2, 16);
                w.Write(x.Num); Str(w, x.Table, 16); w.Write(x.Row); w.Write((byte)Math.Clamp(x.Slot, 0, Protocol.Slots - 1));
                break;
            case GuildView x:
                w.Write((byte)MsgType.GuildView); w.Write(x.Id); Str(w, x.Name, 32); Str(w, x.Tag, 8); w.Write(x.Level); w.Write(x.Xp); w.Write(x.LevelXp);
                w.Write(x.NextXp); w.Write(x.MaxMembers); w.Write(x.Bonus); Str(w, x.Notice, 200); w.Write(x.BankBit); w.Write(x.LogoVersion); w.Write(x.MyRank);
                if (x.Members.Length > Protocol.MaxGuildMembers || x.Bank.Length > Protocol.MaxGuildBank) throw new ArgumentException("guilda grande demais");
                w.Write((byte)x.Members.Length);
                foreach (var m in x.Members)
                {
                    w.Write(m.Account); Str(w, m.Name); w.Write((byte)m.Rank); w.Write(m.Contribution); w.Write(m.PlayerId); Str(w, m.Stage); w.Write(m.Level);
                    w.Write(m.LastSeen);
                }
                w.Write((byte)x.Bank.Length);
                foreach (var b in x.Bank) { Str(w, b.Table, 16); w.Write(b.Row); w.Write(b.Count); }
                Str(w, x.Message, 200);
                break;
            case GuildBankDone x:
                w.Write((byte)MsgType.GuildBankDone); w.Write(x.Ok); Str(w, x.Action, 16); Str(w, x.Table, 16); w.Write(x.Row); w.Write(x.Count); w.Write(x.Bit);
                w.Write(x.Seq); Str(w, x.Message, 200);
                break;
            case GuildLog x:
                w.Write((byte)MsgType.GuildLog); w.Write((byte)Math.Min(x.Lines.Length, Protocol.MaxGuildLog));
                foreach (var l in x.Lines.Take(Protocol.MaxGuildLog)) Str(w, l, 200);
                break;
            case PlayerTag x: w.Write((byte)MsgType.PlayerTag); w.Write(x.PlayerId); Str(w, x.Tag, 8); break;
            case Despawn x: w.Write((byte)MsgType.Despawn); w.Write(x.PlayerId); break;
            case Interact x: w.Write((byte)MsgType.Interact); w.Write(x.TargetId); Str(w, x.Action, 32); Str(w, x.InviteId, 64); break;
            case InteractResult x:
                w.Write((byte)MsgType.InteractResult); w.Write(x.TargetId); Str(w, x.Action, 32); w.Write(x.Ok); Str(w, x.Message, 200);
                Str(w, x.Relation, 32); w.Write(x.HasParty); w.Write(x.AmLeader); w.Write(x.TargetInMyParty); w.Write(x.PartyOccupied);
                w.Write(x.TargetInOtherParty);
                break;
            case CoopGroup x:
                w.Write((byte)MsgType.CoopGroup); w.Write(x.HostId); w.Write((byte)Math.Min(x.Members.Length, Protocol.MaxGroup));
                foreach (var m in x.Members.Take(Protocol.MaxGroup)) w.Write(m);
                break;
            case CoopRelay x: w.Write((byte)MsgType.CoopRelay); w.Write(x.Reliable); Bytes(w, x.Payload); break;
            case CoopFrom x: w.Write((byte)MsgType.CoopFrom); w.Write(x.FromId); Bytes(w, x.Payload); break;
            case PartyRoster x:
                w.Write((byte)MsgType.PartyRoster); w.Write((byte)Math.Min(x.Members.Length, Protocol.MaxGroup));
                foreach (var m in x.Members.Take(Protocol.MaxGroup))
                {
                    w.Write(m.PlayerId); Str(w, m.Character); Str(w, m.Model); w.Write(m.Level);
                    w.Write(m.Hp); w.Write(m.MaxHp); w.Write(m.Mp); w.Write(m.MaxMp); Str(w, m.Stage); w.Write(m.Leader);
                }
                break;
            case SocialPrompt x: w.Write((byte)MsgType.SocialPrompt); Str(w, x.Kind, 16); w.Write(x.FromId); Str(w, x.FromTamer); Str(w, x.InviteId, 64); break;
            case RemoteState x: w.Write((byte)MsgType.RemoteState); w.Write(x.PlayerId); State(w, x.State); break;
            case SaveList x: w.Write((byte)MsgType.SaveList); w.Write(x.ReqId); break;
            case SaveWrite x: w.Write((byte)MsgType.SaveWrite); w.Write(x.ReqId); w.Write((byte)x.Slot); Save(w, x.Data); break;
            case SaveDelete x: w.Write((byte)MsgType.SaveDelete); w.Write(x.ReqId); w.Write((byte)x.Slot); break;
            case ChatSend x: w.Write((byte)MsgType.ChatSend); w.Write((byte)x.Channel); Str(w, x.To); Str(w, x.Text, Protocol.MaxChat); break;
            case ChatFrom x:
                w.Write((byte)MsgType.ChatFrom); w.Write((byte)x.Channel); w.Write(x.FromId); Str(w, x.Sender); Str(w, x.To);
                Str(w, x.Text, Protocol.MaxChat);
                break;
            case ChatNotice x: w.Write((byte)MsgType.ChatNotice); Str(w, x.Text, 200); break;
            case TradeCmd x:
                w.Write((byte)MsgType.TradeCmd); Str(w, x.Action, 16); w.Write(x.TargetId); w.Write((byte)Math.Clamp(x.Slot, 0, Protocol.Slots - 1));
                Str(w, x.Table, 16); w.Write(x.Row); w.Write(x.Bit);
                break;
            case TradeView x:
                w.Write((byte)MsgType.TradeView); w.Write(x.OtherId); Str(w, x.OtherName); Str(w, x.State, 16);
                Items(w, x.MyItems); w.Write(x.MyBit); w.Write(x.MyLocked); w.Write(x.MyConfirmed);
                Items(w, x.TheirItems); w.Write(x.TheirBit); w.Write(x.TheirLocked); w.Write(x.TheirConfirmed);
                Str(w, x.Message, 200); w.Write(x.Seq);
                break;
            case SaveReply x:
                w.Write((byte)MsgType.SaveReply); w.Write(x.ReqId); w.Write(x.Ok); Str(w, x.Message, 200);
                w.Write((byte)Math.Min(x.Slots.Length, Protocol.Slots));
                foreach (var e in x.Slots.Take(Protocol.Slots)) { w.Write((byte)e.Slot); Save(w, e.Data); }
                break;
            default: throw new ArgumentException("mensagem desconhecida: " + msg.GetType().Name);
        }
        return ms.ToArray();
    }

    public static object Read(ReadOnlySpan<byte> data) => Read(data.ToArray());

    public static object Read(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            var type = (MsgType)r.ReadByte();
            object msg = type switch
            {
                MsgType.Hello => new Hello(r.ReadInt32(), Str(r, 128), Str(r), Str(r), r.ReadInt32()),
                MsgType.State => State(r),
                MsgType.Welcome => new Welcome(r.ReadInt32(), Str(r)),
                MsgType.Reject => new Reject(Str(r, 200)),
                MsgType.Spawn => new Spawn(r.ReadInt32(), Str(r), Str(r), State(r), Str(r, 8)),
                MsgType.GuildCmd => new GuildCmd(Str(r, 16), r.ReadInt32(), r.ReadInt64(), Str(r, 200), Str(r, 16), r.ReadInt32(), Str(r, 16), r.ReadInt32(), Slot(r)),
                MsgType.GuildView => ReadGuild(r),
                MsgType.GuildBankDone => new GuildBankDone(r.ReadBoolean(), Str(r, 16), Str(r, 16), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Str(r, 200)),
                MsgType.GuildLog => ReadGuildLog(r),
                MsgType.PlayerTag => new PlayerTag(r.ReadInt32(), Str(r, 8)),
                MsgType.Despawn => new Despawn(r.ReadInt32()),
                MsgType.Interact => new Interact(r.ReadInt32(), Str(r, 32), Str(r, 64)),
                MsgType.InteractResult => new InteractResult(r.ReadInt32(), Str(r, 32), r.ReadBoolean(), Str(r, 200), Str(r, 32), r.ReadBoolean(),
                    r.ReadBoolean(), r.ReadBoolean(), r.ReadInt32(), r.ReadBoolean()),
                MsgType.CoopGroup => ReadGroup(r),
                MsgType.CoopRelay => new CoopRelay(r.ReadBoolean(), Bytes(r)),
                MsgType.CoopFrom => new CoopFrom(r.ReadInt32(), Bytes(r)),
                MsgType.PartyRoster => ReadRoster(r),
                MsgType.SocialPrompt => new SocialPrompt(Str(r, 16), r.ReadInt32(), Str(r), Str(r, 64)),
                MsgType.RemoteState => new RemoteState(r.ReadInt32(), State(r)),
                MsgType.SaveList => new SaveList(r.ReadInt32()),
                MsgType.SaveWrite => new SaveWrite(r.ReadInt32(), Slot(r), Save(r)),
                MsgType.SaveDelete => new SaveDelete(r.ReadInt32(), Slot(r)),
                MsgType.SaveReply => ReadSaveReply(r),
                MsgType.ChatSend => new ChatSend(Channel(r), Str(r), Str(r, Protocol.MaxChat)),
                MsgType.ChatFrom => new ChatFrom(Channel(r), r.ReadInt32(), Str(r), Str(r), Str(r, Protocol.MaxChat)),
                MsgType.ChatNotice => new ChatNotice(Str(r, 200)),
                MsgType.TradeCmd => new TradeCmd(Str(r, 16), r.ReadInt32(), Slot(r), Str(r, 16), r.ReadInt32(), r.ReadInt32()),
                MsgType.TradeView => new TradeView(r.ReadInt32(), Str(r), Str(r, 16), Items(r), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean(),
                    Items(r), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean(), Str(r, 200), r.ReadInt32()),
                _ => throw new FormatException($"tipo {(byte)type}"),
            };
            if (r.BaseStream.Position != data.Length) throw new FormatException("sobra no pacote");
            return msg;
        }
        catch (EndOfStreamException) { throw new FormatException("pacote curto"); }
    }

    static CoopGroup ReadGroup(BinaryReader r)
    {
        int host = r.ReadInt32(), n = r.ReadByte();
        if (n > Protocol.MaxGroup) throw new FormatException("grupo grande demais");
        var m = new int[n];
        for (int i = 0; i < n; i++) m[i] = r.ReadInt32();
        return new CoopGroup(host, m);
    }

    static GuildView ReadGuild(BinaryReader r)
    {
        long id = r.ReadInt64();
        string name = Str(r, 32), tag = Str(r, 8);
        int level = r.ReadInt32();
        long xp = r.ReadInt64(), levelXp = r.ReadInt64(), nextXp = r.ReadInt64();
        int max = r.ReadInt32(), bonus = r.ReadInt32();
        string notice = Str(r, 200);
        long bankBit = r.ReadInt64(), logo = r.ReadInt64();
        int rank = r.ReadInt32();
        int n = r.ReadByte();
        if (n > Protocol.MaxGuildMembers) throw new FormatException("membros demais");
        var members = new GuildMemberEntry[n];
        for (int i = 0; i < n; i++)
            members[i] = new GuildMemberEntry(r.ReadInt64(), Str(r), r.ReadByte(), r.ReadInt64(), r.ReadInt32(), Str(r), r.ReadInt32(), r.ReadInt64());
        int b = r.ReadByte();
        if (b > Protocol.MaxGuildBank) throw new FormatException("baú grande demais");
        var bank = new GuildBankEntry[b];
        for (int i = 0; i < b; i++) bank[i] = new GuildBankEntry(Str(r, 16), r.ReadInt32(), r.ReadInt32());
        return new GuildView(id, name, tag, level, xp, levelXp, nextXp, max, bonus, notice, bankBit, logo, rank, members, bank, Str(r, 200));
    }

    static GuildLog ReadGuildLog(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > Protocol.MaxGuildLog) throw new FormatException("histórico grande demais");
        var lines = new string[n];
        for (int i = 0; i < n; i++) lines[i] = Str(r, 200);
        return new GuildLog(lines);
    }

    static PartyRoster ReadRoster(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > Protocol.MaxGroup) throw new FormatException("party grande demais");
        var m = new PartyEntry[n];
        for (int i = 0; i < n; i++)
            m[i] = new PartyEntry(r.ReadInt32(), Str(r), Str(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Str(r),
                r.ReadBoolean());
        return new PartyRoster(m);
    }

    static SaveReply ReadSaveReply(BinaryReader r)
    {
        int id = r.ReadInt32();
        bool ok = r.ReadBoolean();
        string msg = Str(r, 200);
        int n = r.ReadByte();
        if (n > Protocol.Slots) throw new FormatException("vagas demais");
        var slots = new SaveEntry[n];
        for (int i = 0; i < n; i++) slots[i] = new SaveEntry(Slot(r), Save(r));
        return new SaveReply(id, ok, msg, slots);
    }

    static void Items(BinaryWriter w, TradeItem[] items)
    {
        if (items.Length > Protocol.MaxTradeItems) throw new ArgumentException("itens demais na troca");
        w.Write((byte)items.Length);
        foreach (var i in items) { Str(w, i.Table, 16); w.Write(i.Row); }
    }

    static TradeItem[] Items(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > Protocol.MaxTradeItems) throw new FormatException("itens demais na troca");
        var items = new TradeItem[n];
        for (int i = 0; i < n; i++) items[i] = new TradeItem(Str(r, 16), r.ReadInt32());
        return items;
    }

    static ChatTo Channel(BinaryReader r)
    {
        var c = (ChatTo)r.ReadByte();
        if (c is not (ChatTo.Global or ChatTo.Party or ChatTo.Whisper or ChatTo.Guild)) throw new FormatException("canal de chat inválido");
        return c;
    }

    static int Slot(BinaryReader r)
    {
        int s = r.ReadByte();
        if (s >= Protocol.Slots) throw new FormatException("vaga inválida");
        return s;
    }

    static void Save(BinaryWriter w, byte[] b)
    {
        if (b.Length > Protocol.MaxSave) throw new ArgumentException("save grande demais");
        w.Write(b.Length);
        w.Write(b);
    }

    static byte[] Save(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n is < 0 or > Protocol.MaxSave) throw new FormatException("save grande demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return b;
    }

    static void Bytes(BinaryWriter w, byte[] b)
    {
        if (b.Length > Protocol.MaxPayload) throw new ArgumentException("pacote do grupo grande demais");
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    static byte[] Bytes(BinaryReader r)
    {
        int n = r.ReadUInt16();
        if (n > Protocol.MaxPayload) throw new FormatException("pacote do grupo grande demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return b;
    }

    static void State(BinaryWriter w, PlayerState s)
    {
        Str(w, s.Stage); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write(s.Facing); Str(w, s.Anim); Str(w, s.Model); w.Write(s.Level); Str(w, s.Weapon);
        w.Write(s.Hp); w.Write(s.MaxHp); w.Write(s.Mp); w.Write(s.MaxMp);
    }

    static PlayerState State(BinaryReader r)
    {
        var s = new PlayerState(Str(r), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), Str(r), Str(r), r.ReadInt32(), Str(r),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        if (!float.IsFinite(s.X) || !float.IsFinite(s.Y) || !float.IsFinite(s.Z) || !float.IsFinite(s.Facing)) throw new FormatException("número inválido");
        return s;
    }

    static void Str(BinaryWriter w, string s, int max = Protocol.MaxText)
    {
        var b = Encoding.UTF8.GetBytes(s.Length > max ? s[..max] : s);
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    static string Str(BinaryReader r, int max = Protocol.MaxText)
    {
        int n = r.ReadUInt16();
        if (n > max * 4) throw new FormatException("texto grande demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(b);
    }
}
