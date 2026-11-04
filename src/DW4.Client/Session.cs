using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;
using DW4.Script.Save;

namespace DW4.Client;

/// <summary>
/// Entrada no jogo pela vaga escolhida em "Jogar online". As vagas da conta vêm do servidor (<see cref="OnlineCharacterStore"/>,
/// desde 2026-11-04); o <see cref="IOnlineGate"/> ficou como ponto de extensão (hoje <see cref="LocalGate"/>, sempre deixa entrar).
/// </summary>
public interface IOnlineGate
{
    /// <summary>Conecta o personagem da vaga; false com o motivo quando não dá para entrar.</summary>
    bool Enter(string account, int slot, CharacterSave character, out string? error);
    /// <summary>Saiu do jogo (volta ao menu ou fechou).</summary>
    void Leave();
}

/// <summary>Sem servidor: entra sempre no jogo local.</summary>
public sealed class LocalGate : IOnlineGate
{
    public bool Enter(string account, int slot, CharacterSave character, out string? error) { error = null; return true; }
    public void Leave() { }
}

public static class Session
{
    public const string MenuScene = "res://MainMenu.tscn", GameScene = "res://Fatia.tscn";
    /// <summary>
    /// Começo do jogo vindo do menu: lobby, entrada 0. FATO: a gatedisc.lua (em toda fase, inclusive nas stagIS*) é o uso do
    /// Gate Disk, que sai com SETGENERATEPOS I&lt;3&gt; para o Emergency Gate do lobby (entrada 3, em (210, 40, 120)); o
    /// hostLoad.lua e o gameStart.lua da stagIS10 não escolhem entrada. FATO (relato do usuário com o original, 2026-11-02): ao
    /// entrar, o Digimon aparece em (60,9, 10, −43), dentro da caixa da entrada 0 da stagml11 (41..79 × −55..−20; vagas em
    /// (60, −30), (60, −50), (70, −40), (50, −40)). HIPÓTESE: sem SETGENERATEPOS o jogo usa a entrada 0.
    /// </summary>
    public const string NewGameStage = "stagml11";
    public const int NewGameEntry = 0;

    static ICharacterStore? s_store;
    /// <summary>
    /// Onde ficam as vagas. DECISÃO DO RESPONSÁVEL (2026-11-04, "tudo tem que ser online, nada local"): com ticket (launcher no PC,
    /// login no celular) as vagas são as da conta no servidor. Sem ticket só no editor da Godot (conta da configuração), em
    /// arquivos, para desenvolver sem servidor; o jogo exportado sem ticket não mostra vagas.
    /// </summary>
    public static ICharacterStore Store
    {
        get => s_store ??= Online.Available ? new OnlineCharacterStore() : new LocalCharacterStore(ProjectSettings.GlobalizePath("user://"));
        set => s_store = value;
    }
    /// <summary>As vagas estão no servidor (e as antigas do aparelho sobem para lá na migração).</summary>
    public static bool UsesServer => Store is OnlineCharacterStore;
    /// <summary>Aviso para o menu mostrar ao voltar (ex.: a conta entrou em outro aparelho).</summary>
    public static string? MenuNotice { get; set; }
    public static IOnlineGate Gate { get; set; } = new LocalGate();

    // Chat (DECISÃO DO PORT): canal local hoje; PONTO DA FASE 6: um IChatChannel de rede. O histórico fica aqui para
    // sobreviver à troca de fase (a cena é recarregada).
    static IChatChannel s_chat = Hook(new LocalChatChannel());
    static readonly List<ChatMessage> s_history = [];
    public static IReadOnlyList<ChatMessage> ChatHistory => s_history;
    public static IChatChannel Chat { get => s_chat; set => s_chat = Hook(value); }
    static IChatChannel Hook(IChatChannel c)
    {
        c.Received += m => { s_history.Add(m); if (s_history.Count > 50) s_history.RemoveAt(0); };
        return c;
    }
    /// <summary>
    /// Conta dona das vagas. Pelo launcher RE:DIGITAL (integração "dw4"): DW4.exe -- --conta &lt;usuário&gt; --ticket &lt;ticket&gt;,
    /// e as vagas ficam em user://accounts/&lt;usuário&gt;. Pela Godot (editor) sem --conta: a conta da configuração ("local").
    /// Exportado e aberto sem o launcher: null, e o menu não mostra vagas (pede para abrir pelo launcher).
    /// PONTO DA FASE 6: o servidor confere o ticket e passa a guardar as vagas da conta.
    /// </summary>
    public static string? Account { get { ReadLauncherArgs(); return s_account; } }
    /// <summary>Ticket de uso único (2 min) que o launcher pediu à DMO.API para esta abertura; null fora do launcher.</summary>
    public static string? LauncherTicket { get { ReadLauncherArgs(); return s_ticket; } }
    static string? s_account, s_ticket;
    static bool s_argsRead;

    /// <summary>Celular: a conta e o ticket vêm da tela de login do próprio jogo (MobileBoot), no lugar dos argumentos do launcher.</summary>
    public static void UseLogin(string account, string ticket)
    {
        s_argsRead = true;
        s_account = account.Trim().ToLowerInvariant();
        s_ticket = ticket;
        GD.Print("[conta] conta da tela de login do celular");
    }

    static void ReadLauncherArgs()
    {
        if (s_argsRead) return;
        s_argsRead = true;
        string? conta = null;
        // Os argumentos depois de "--" vão para o jogo (GetCmdlineUserArgs); os outros são da Godot.
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--conta") conta = args[i + 1];
            else if (args[i] == "--ticket") s_ticket = args[i + 1];
        }
        if (conta != null)
        {
            conta = conta.Trim().ToLowerInvariant();
            bool ok = conta.Length is > 0 and <= 64 && conta is not ("." or "..") && conta.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
            s_account = ok ? conta : null;
            if (!ok) GD.PushWarning("[conta] nome de conta inválido vindo do launcher");
        }
        else s_account = OS.HasFeature("editor") ? PortSettings.Current.Account : null;
        GD.Print($"[conta] {(s_account == null ? "sem conta (abra pelo launcher)" : conta != null ? "conta do launcher" : "conta local da configuração")}");
    }

    public static int Slot { get; private set; } = -1;
    public static CharacterSave? Character { get; private set; }
    public static bool Active => Character != null;
    /// <summary>
    /// FATO: a gameStart.lua da stagIS10 roda a cada começo de jogo (zera as flags de trabalho e copia as permanentes da
    /// dificuldade, 700+/741+/771+, para elas). O port roda ela uma vez na primeira fase depois de escolher a vaga.
    /// </summary>
    public static bool PendingGameStart { get; set; }

    /// <summary>Entra com o personagem da vaga: GAMERAM nova com o save e a fase gravada.</summary>
    public static bool Begin(SceneTree tree, int slot, CharacterSave character, out string? error)
    {
        if (Account is not { } account) { error = "Abra o Digimon World 4 pelo launcher RE:DIGITAL, com a sua conta conectada."; return false; }
        if (!Gate.Enter(account, slot, character, out error)) return false;
        Online.Disconnect(); // outra vaga: a conexão nova leva o personagem novo
        s_unsaved = null;
        Slot = slot;
        Character = character;
        var ram = new GameRam();
        character.Apply(ram, 0);
        // As opções de jogo (câmera e luz da dungeon) são da configuração do port, não da vaga.
        ram.CameraMode = PortSettings.Current.CameraMode;
        ram.LightMode = PortSettings.Current.LightMode;
        Fatia.UseRam(ram);
        PendingGameStart = true;
        Fatia.PlayerModel = character.Model.Length > 0 ? character.Model : "p_girumon";
        // Vaga gravada numa fase de menu (stagIS*, sem cenário no port), sem fase ou no próprio lobby: começa no lobby, entrada 0
        // (a 3 das vagas antigas era a do Gate Disk).
        bool lobby = character.Stage.Length == 0 || Fatia.IsMenuStage(character.Stage) || character.Stage.Equals(NewGameStage, System.StringComparison.OrdinalIgnoreCase);
        // Vaga gravada no meio do tutorial: volta ao começo dele, como o restart.lua (ResumePoint).
        var (stage, entry) = lobby ? (NewGameStage, NewGameEntry) : DW4.Script.Save.ResumePoint.Adjust(ram, character.Stage, character.Entry);
        OS.SetEnvironment("DW4_FASE", stage);
        OS.SetEnvironment("DW4_ENTRADA", entry.ToString());
        OS.SetEnvironment("DW4_INICIO", "");
        tree.ChangeSceneToFile(GameScene);
        return true;
    }

    // Última gravação que ainda não foi confirmada pelo servidor (vai de novo quando a conexão volta) e o número da mais nova.
    static (string Account, int Slot, CharacterSave Save)? s_unsaved;
    static int s_saveSeq;
    static bool s_hooked;
    /// <summary>Gravação no servidor que falhou (motivo para mostrar na tela).</summary>
    public static event Action<string>? SaveFailed;
    /// <summary>Há progresso que o servidor ainda não confirmou.</summary>
    public static bool HasUnsaved => s_unsaved != null;
    /// <summary>Motivo da última gravação que falhou.</summary>
    public static string? LastError { get; private set; }

    static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        // A conexão voltou: o que não gravou vai agora.
        Online.Connected += () => { if (s_unsaved is { } u && Character != null && u.Slot == Slot) _ = Persist(u.Account, u.Slot, u.Save); };
        // A conta entrou em outro aparelho: o que ficou sem gravar aqui é descartado (o outro aparelho vale).
        Online.KickedOut += () => s_unsaved = null;
    }

    static async Task<bool> Persist(string account, int slot, CharacterSave save)
    {
        Hook();
        s_unsaved = (account, slot, save);
        int mine = ++s_saveSeq;
        try
        {
            await Store.SaveAsync(account, slot, save);
            if (mine == s_saveSeq) s_unsaved = null;
            return true;
        }
        catch (Exception e) when (e is OnlineCharacterStore.StoreException or System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[save] não gravou: {e.Message}");
            LastError = e.Message;
            if (mine == s_saveSeq) SaveFailed?.Invoke(e.Message);
            return false;
        }
    }

    /// <summary>
    /// Grava o progresso na vaga (troca de fase, F5, evolução, saída). Sem sessão (abertura direta por DW4_FASE), não faz nada.
    /// Volta na hora; a tarefa termina com true quando o servidor confirmou (false: não gravou, o motivo vai em <see cref="SaveFailed"/>
    /// e a gravação vai de novo quando a conexão voltar).
    /// </summary>
    public static Task<bool> SaveProgress(GameRam ram, string stage, int entry)
    {
        if (Character is not { } c) return Task.FromResult(false);
        if (Fatia.IsMenuStage(stage)) (stage, entry) = (NewGameStage, NewGameEntry); // nunca grava numa fase de menu
        // Nem no tutorial (o original não grava lá): fica a última fase segura da vaga (ResumePoint é a rede de segurança).
        if (stage.Equals(DW4.Script.Save.ResumePoint.Tutorial, System.StringComparison.OrdinalIgnoreCase))
            (stage, entry) = c.Stage.Length == 0 || c.Stage.Equals(DW4.Script.Save.ResumePoint.Tutorial, System.StringComparison.OrdinalIgnoreCase) || Fatia.IsMenuStage(c.Stage) ? (NewGameStage, NewGameEntry) : (c.Stage, c.Entry);
        // O original grava pela stagIS12, que passa o enredo para as flags permanentes; sem isso a gameStart.lua apaga o
        // progresso no próximo começo de jogo (DW4.Script.Save.PermanentFlags).
        DW4.Script.Save.PermanentFlags.Commit(ram);
        var s = CharacterSave.Capture(ram, 0, c.Name, stage, entry);
        s.Starter = c.Starter;
        s.Model = Fatia.PlayerModel; // muda na evolução
        Character = s;
        if (Account is not { } account) return Task.FromResult(false);
        return Persist(account, Slot, s);
    }

    /// <summary>
    /// Grava (ou não, no "End the game" sem gravar da stagIS13) e volta ao menu. Com gravação, só sai depois que o servidor
    /// confirma; se não gravou, fica no jogo e chama <paramref name="failed"/> com o motivo (o jogador escolhe tentar de novo ou
    /// sair sem gravar).
    /// </summary>
    public static async void ReturnToMenu(SceneTree tree, GameRam ram, string stage, int entry, bool save = true, Action<string>? failed = null)
    {
        if (save && !await SaveProgress(ram, stage, entry) && failed != null)
        {
            failed(LastError ?? "Não deu para gravar no servidor.");
            return;
        }
        Leave(tree);
    }

    /// <summary>Volta ao menu sem gravar nada agora.</summary>
    public static void Leave(SceneTree tree)
    {
        Gate.Leave();
        s_unsaved = null;
        // Sai do mundo (os outros param de ver o Digimon parado na fase); o menu reconecta sem personagem para as vagas.
        Online.Disconnect();
        Character = null;
        Slot = -1;
        OS.SetEnvironment("DW4_FASE", ""); // senão o menu pularia direto para a fase (atalho de depuração)
        tree.ChangeSceneToFile(MenuScene);
    }
}
