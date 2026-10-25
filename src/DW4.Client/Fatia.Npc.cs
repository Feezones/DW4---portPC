using System;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Passeio das NPCs pela IA do jogo. FATO (seq das NPCs, ex.: n_digilfm):
///  · SQ_WALKON liga o mapa (AIMAP "MAP01") e os rótulos WLK; o MAP01 é do tipo "WPOS" (raios² 25 / 10000 / 40000, giro 0,1745
///    rad por quadro). Perto do ponto (&lt; 5) sorteia SQ_NEXT ou SQ_WAIT; longe, SQ_WALK;
///  · o ponto vem do aim_&lt;nome&gt;.lua, que o motor roda com o nome de chamada "WPos" (MDLisCallName) e que devolve o alvo por
///    MDLsetWorldPosTarget;
///  · SQ_WALK = MOVE "Move" 0,14 6 + WALK01 (walk01 em laço); SQ_WAIT = MOVE "Stop" + WAIT01 (wait01 em laço); SQ_NEXT anda e
///    espera 60 quadros antes de voltar ao SQ_WALK.
/// HIPÓTESES: o motor pede um ponto novo a cada SQ_NEXT e quando não há ponto; a IA para enquanto um script manda na NPC
/// (qualquer sequência fora de SQ_WAIT/SQ_WALK/SQ_NEXT, ex.: SQ_TALK). DECISÃO DO PORT: parede ou falta de chão à frente
/// (raios da Godot) cancelam o passo e pedem outro ponto.
/// </summary>
public partial class Fatia
{
    void UpdateNpcAi(WorldObject o, float dt)
    {
        if (o.Brain is not { } brain || o.Seq is not { } seq) return;
        if (seq.TakeAiMap() is { } map) { brain.SetMap(map); brain.Release(); o.Wander = true; o.Target = null; }
        if (!o.Wander) return;
        string s = seq.Sequence ?? "";
        if (s is not ("SQ_WAIT" or "SQ_WALK" or "SQ_NEXT")) { brain.Release(); return; } // o script manda (conversa etc.)
        if (s == "SQ_NEXT" && seq.Running) return;
        if (o.Blocked) { o.Blocked = false; o.Target = null; brain.Release(); }
        o.Target ??= AskAim(o);
        if (o.Target is not { } target) return;
        var root = o.Actor.Root;
        var p = root.GlobalPosition;
        brain.Tick(dt, new System.Numerics.Vector3(p.X, p.Y, p.Z), new System.Numerics.Vector3(target.X, target.Y, target.Z), root.GlobalRotation.Y);
        if (brain.External is { } ext)
        {
            // SQ_NEXT (chegou ao ponto): roda a sequência do jogo e pede outro ponto.
            brain.Release();
            o.Target = null;
            o.SetSequence(ext); // sem a sequência, o Start não faz nada
            return;
        }
        string want = brain.Walking ? "SQ_WALK" : "SQ_WAIT";
        if (s != want) o.SetSequence(want);
        if (brain.Walking) root.Rotation = new Vector3(root.Rotation.X, brain.Yaw, root.Rotation.Z);
    }

    // aim_<nome>.lua no contexto da NPC com o nome de chamada "WPos": devolve o ponto do MDLsetWorldPosTarget.
    Vector3? AskAim(WorldObject o)
    {
        var script = $"aim_{o.Name}.lua";
        if (_stage == null || !_stage.Has(script)) return null;
        var host = _stage.Host;
        var (oldName, oldTarget) = (host.CallName, host.WorldPosTarget);
        host.CallName = "WPos";
        host.WorldPosTarget = null;
        try
        {
            _stage.RunAs(script, o);
            return host.WorldPosTarget is { } t ? new Vector3((float)t.X, (float)t.Y, (float)t.Z) : null;
        }
        finally { (host.CallName, host.WorldPosTarget) = (oldName, oldTarget); }
    }
}
