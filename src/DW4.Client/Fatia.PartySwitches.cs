using System;
using System.Linq;

namespace DW4.Client;

/// <summary>
/// Interruptores de portão (g_sbutton: Goblin Fortress na stag0101, portões das stag0104/0202/0204/0304/0403).
/// FATO (start.lua/gene07.lua, hit_g_sbutton.lua, off_g_sbutton.lua): com as ID Passes, o script liga (SQ_POWERON) um interruptor
/// por jogador (SYSisJoinPlayer) e acende de vez (SQ_ONLOOP, g_sbutton0N = 1) as vagas sem jogador; o portão abre quando os 4
/// estão acesos ao mesmo tempo, e pisar/sair (mapa de IA "NEAR") acende/apaga.
/// DECISÃO DO RESPONSÁVEL (2026-11-04, "se for 1 pessoa, só ele; se o grupo de 4, vai precisar dos 4"): no online, o número de
/// interruptores ligados segue os membros da party nesta fase. Os scripts da fase só enxergam o jogador 0, então o port refaz a
/// divisão depois deles (e quando alguém do grupo entra ou sai), e o interruptor reage a qualquer membro da party (o mais
/// perto), não só a quem joga neste PC. Cada PC roda o mesmo script com as posições sincronizadas, e todos abrem juntos.
/// </summary>
public partial class Fatia
{
    WorldObject? _partySwitchFirst;
    int _partySwitchCount;

    static bool SwitchPowered(WorldObject o) => o.Seq?.Label("SQ_WAIT") is "OFF" or "ON";

    void SyncPartySwitches()
    {
        if (_stage == null) return;
        var switches = _objects.Where(o => o.Name == "g_sbutton" && !o.Erased && o.Seq != null).OrderBy(o => o.Index).ToList();
        if (switches.Count < 2) return;
        if (!ReferenceEquals(switches[0], _partySwitchFirst)) { _partySwitchFirst = switches[0]; _partySwitchCount = 0; } // fase nova
        // Nenhum ligado: sem as ID Passes (DEF) ou portão já aberto (todos em ONLOOP). Nada a dividir.
        if (!switches.Any(SwitchPowered)) return;
        int n = Math.Clamp(CoopOn ? _coopMembers.Length : 1, 1, switches.Count);
        if (n == _partySwitchCount) return;
        _partySwitchCount = n;
        foreach (var o in switches)
        {
            string global = $"g_sbutton{o.Index:00}";
            if (o.Index < n && o.Seq!.Label("SQ_WAIT") == "ONLOOP")
            {
                o.SetSequence("SQ_POWERON");
                _stage.Vm.Globals[global] = 0.0;
            }
            else if (o.Index >= n && SwitchPowered(o))
            {
                o.SetSequence("SQ_ONLOOP");
                _stage.Vm.Globals[global] = 1.0;
            }
        }
        ScriptLog($"interruptores do portão: {n} para a party");
    }
}
