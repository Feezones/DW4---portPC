using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DW4.Net;
using DW4.Script.Save;

namespace DW4.Client;

/// <summary>
/// Vagas no servidor (DECISÃO DO RESPONSÁVEL, 2026-11-04: "saves no banco ... tudo tem que ser online, nada local"): o save inteiro
/// vai pela conexão do DW4 (<see cref="Online.Request"/>) ao servidor, que grava na DMO.API na conta do ticket. O nome da conta
/// que o jogo passa é ignorado: quem decide a conta é o servidor.
/// </summary>
public sealed class OnlineCharacterStore : ICharacterStore
{
    /// <summary>Falha de servidor (fora do ar, sem internet, conta derrubada): a mensagem é para mostrar ao jogador.</summary>
    public sealed class StoreException(string message) : Exception(message);

    public async Task<IReadOnlyList<CharacterSave?>> ListAsync(string account)
    {
        var r = await Online.Request(id => new SaveList(id));
        if (!r.Ok) throw new StoreException(r.Message);
        var slots = new CharacterSave?[ICharacterStore.Slots];
        foreach (var e in r.Slots)
            if (e.Slot is >= 0 and < ICharacterStore.Slots)
                try { slots[e.Slot] = CharacterSave.FromJson(SaveData.Unpack(e.Data)); }
                catch (FormatException) { Godot.GD.PushWarning($"[save] vaga {e.Slot + 1} do servidor ilegível"); }
        return slots;
    }

    public async Task SaveAsync(string account, int slot, CharacterSave save)
    {
        var data = SaveData.Pack(save.ToJson());
        var r = await Online.Request(id => new SaveWrite(id, slot, data));
        if (!r.Ok) throw new StoreException(r.Message);
    }

    public async Task DeleteAsync(string account, int slot)
    {
        var r = await Online.Request(id => new SaveDelete(id, slot));
        if (!r.Ok) throw new StoreException(r.Message);
    }
}
