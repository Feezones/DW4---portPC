using Godot;
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
    /// FATO: a gatedisc.lua da stagIS13 (tela de criar/chamar Digimon) sai com SETGENERATEPOS I&lt;3&gt; e NEXTSTAGE "stagML11".
    /// Personagem novo começa ali: lobby, entrada 3.
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
    public static string Account => PortSettings.Current.Account;

    public static int Slot { get; private set; } = -1;
    public static CharacterSave? Character { get; private set; }
    public static bool Active => Character != null;

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
        OS.SetEnvironment("DW4_FASE", character.Stage.Length > 0 ? character.Stage : NewGameStage);
        OS.SetEnvironment("DW4_ENTRADA", (character.Stage.Length > 0 ? character.Entry : NewGameEntry).ToString());
        OS.SetEnvironment("DW4_INICIO", "");
        tree.ChangeSceneToFile(GameScene);
        return true;
    }

    /// <summary>Grava o progresso na vaga (troca de fase e saída). Sem sessão (abertura direta por DW4_FASE), não faz nada.</summary>
    public static void SaveProgress(GameRam ram, string stage, int entry)
    {
        if (Character is not { } c) return;
        var s = CharacterSave.Capture(ram, 0, c.Name, stage, entry);
        s.Starter = c.Starter;
        s.Model = c.Model;
        Store.Save(Account, Slot, s);
        Character = s;
    }

    /// <summary>Grava e volta ao menu.</summary>
    public static void ReturnToMenu(SceneTree tree, GameRam ram, string stage, int entry)
    {
        SaveProgress(ram, stage, entry);
        Gate.Leave();
        Character = null;
        Slot = -1;
        OS.SetEnvironment("DW4_FASE", ""); // senão o menu pularia direto para a fase (atalho de depuração)
        tree.ChangeSceneToFile(MenuScene);
    }
}
