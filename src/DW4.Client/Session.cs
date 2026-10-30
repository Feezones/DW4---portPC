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
    /// FATO: a gatedisc.lua da stagIS10 (título, onde começa o jogo) e a da stagIS13 (criar/chamar Digimon) saem com
    /// SETGENERATEPOS I&lt;3&gt; e NEXTSTAGE "stagML11". Personagem novo começa ali: lobby, entrada 3.
    /// </summary>
    public const string NewGameStage = "stagml11";
    public const int NewGameEntry = 3;

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
    public static string Account => PortSettings.Current.Account;

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
        if (!Gate.Enter(Account, slot, character, out error)) return false;
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
        // Vaga gravada numa fase de menu (stagIS*, sem cenário no port) ou sem fase: volta ao lobby, entrada 3.
        bool lobby = character.Stage.Length == 0 || Fatia.IsMenuStage(character.Stage);
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
        var s = CharacterSave.Capture(ram, 0, c.Name, stage, entry);
        s.Starter = c.Starter;
        s.Model = Fatia.PlayerModel; // muda na evolução
        Store.Save(Account, Slot, s);
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
