using System.Collections.Generic;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;
using DW4.Script.Save;

namespace DW4.Client;

/// <summary>
/// Entrada no jogo pela vaga escolhida em "Jogar online". PONTO DA FASE 6: a conexão ao servidor entra em <see cref="IOnlineGate"/>
/// (hoje <see cref="LocalGate"/>, que só deixa jogar local) e as vagas da conta passam a vir de um <see cref="ICharacterStore"/>
/// do servidor no lugar do <see cref="LocalCharacterStore"/>. Nada de rede antes da Fase 6 aprovada.
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
    public static ICharacterStore Store
    {
        get => s_store ??= new LocalCharacterStore(ProjectSettings.GlobalizePath("user://"));
        set => s_store = value;
    }
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

    /// <summary>Grava o progresso na vaga (troca de fase e saída). Sem sessão (abertura direta por DW4_FASE), não faz nada.</summary>
    public static void SaveProgress(GameRam ram, string stage, int entry)
    {
        if (Character is not { } c) return;
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
        if (Account is not { } account) return;
        Store.Save(account, Slot, s);
        Character = s;
    }

    /// <summary>Grava (ou não, no "End the game" sem gravar da stagIS13) e volta ao menu.</summary>
    public static void ReturnToMenu(SceneTree tree, GameRam ram, string stage, int entry, bool save = true)
    {
        if (save) SaveProgress(ram, stage, entry);
        Gate.Leave();
        Character = null;
        Slot = -1;
        OS.SetEnvironment("DW4_FASE", ""); // senão o menu pularia direto para a fase (atalho de depuração)
        tree.ChangeSceneToFile(MenuScene);
    }
}
