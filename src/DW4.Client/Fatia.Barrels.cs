using System;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Barris do lançador da ladeira (stag0102, g_barrelshotz01).
/// FATO (disparos.json da stag0102, beActionStarterInfo do #621): a BLST01 do lançador gera o objeto g_bbarrelmz01 e a BLST02 o
/// g_bbarrelgz01 (mais o efeito x_bbarrelsz01), ambos no quadro 26 da ação, em (0, 8, 6) no espaço do lançador, na SQ_ATCK01.
/// FATO (matf00.lua, lua-trace com MATisPlayer=1): ao entrar no gatilho, demo "Slope" com SQ_FIREDEMO alternando os lançadores 0 e 1
/// (quadros 2, 12, 54, 74 … 262) e depois SQ_POWERON; ligado, o mapa MAP00 "NEAR" (300/400/500) sorteia SQ_FIRE/SQ_FIRE02/SQ_ON/SQ_ONGO,
/// que tocam BLST01/BLST02 (seq do g_barrelshotz01).
/// FATO (seq do g_bbarrelmz01/gz01): SQ_ATCK01 = WAIT01 (juntas 40..100 em laço, WAITCHECK "Frame" 250), HITBOX 70122 (0, −2, 0)
/// (8, 6, 6) por 1000 quadros, MOVE "Push" 0,9 100 (anda para a frente e freia até parar), MOVE "Jump" 2 2 (pulinho), e no fim da
/// ação SQ_DDMG (caixa 70122 15 × 15 × 15 por 6 quadros, DEAD01 some em 12) → SQ_ERAS. SQ_FALL = o mesmo com MOVE "Fall" 1 2.
/// FATO (eneatk 122): TYPE 2, TSUYOSA 5 (5% do HP máximo), MAHIRITSU 5. FATO (hit_g_bbarrelmz01.lua): golpe comum tira 100 e põe SQ_DDMG.
/// HIPÓTESE: o barril segue o chão (o código de chão dos objetos não foi lido): desce a ladeira colado nela e, sem chão logo abaixo,
/// cai com a conta do MOVE "Fall" 1 2 da SQ_FALL (até 1 por quadro, 0,5 por quadro²).
/// DECISÃO DO PORT: o barril que bate numa parede quebra (SQ_DDMG) em vez de ficar parado nela até o fim da ação.
/// </summary>
public partial class Fatia
{
    // Teto de barris vivos (a cadência do mapa de IA com dois lançadores fica bem abaixo disso).
    const int MaxRollingObjects = 24;
    // DEPURAÇÃO: DW4_DIAG_BARRIL=1 mostra cada barril que nasce, a troca de sequência e, a cada 15 quadros, posição e HP do jogador.
    static readonly bool DiagBarrel = OS.GetEnvironment("DW4_DIAG_BARRIL") == "1";
    readonly System.Collections.Generic.Dictionary<WorldObject, string?> _barrelSeqSeen = [];

    /// <summary>g_* do disparador que existe nos objetos da fase (exportado pelo fase-exportar).</summary>
    bool IsStarterObject(string name) =>
        name.StartsWith("g_", StringComparison.Ordinal) && ObjectData() is { } data && data.Actors.ContainsKey(name);

    void SpawnStarterObject(Starter s, Transform3D at)
    {
        if (_objects.Count(o => o.Rolling && !o.Erased) >= MaxRollingObjects) return;
        int index = 100;
        while (_objects.Any(o => o.Name == s.Effect && o.Index == index)) index++;
        var pos = at * s.Offset;
        float yaw = at.Basis.Orthonormalized().GetEuler().Y;
        if (Spawn(s.Effect, index, pos, new Vector3(0, Mathf.RadToDeg(yaw), 0)) is not { } o) return;
        o.Rolling = true;
        o.IgnoreRid = _player.GetRid();
        o.SetSequence(s.Sequence);
        MakeHittable(o);
        // O corpo só marca o alvo do golpe (CheckObjectHits usa as caixas do HIT): não segura o jogador nem os raios de chão.
        if (o.Body != null) o.Body.CollisionLayer = 0;
        if (DiagBarrel) GD.Print($"[barril] {s.Effect}/{index} nasceu em {pos.Round()} giro {Mathf.RadToDeg(yaw):0} ({s.Action} → {s.Sequence})");
    }

    // Depois do Tick: barril que bateu na parede quebra; o que caiu para fora do mapa some.
    void UpdateRolling(WorldObject o)
    {
        if (!o.Rolling || o.Erased) return;
        if (DiagBarrel && (!_barrelSeqSeen.TryGetValue(o, out var seen) || seen != o.Sequence))
        {
            _barrelSeqSeen[o] = o.Sequence;
            GD.Print($"[barril] {o.Name}/{o.Index} → {o.Sequence} em {o.Actor.Root.GlobalPosition.Round()}");
        }
        if (DiagBarrel && _tick % 15 == 0)
            GD.Print($"[barril] {o.Name}/{o.Index} {o.Actor.Root.GlobalPosition.Round()} {o.Sequence} ação {o.Seq?.Action} q{o.Seq?.Frame:0} queda {o.FallSpeed:0.00} jogador a {o.Actor.Root.GlobalPosition.DistanceTo(_player.GlobalPosition):0} HP {_hp}");
        if (o.Blocked && o.Sequence != "SQ_DDMG")
        {
            o.Blocked = false;
            if (DiagBarrel) GD.Print($"[barril] {o.Name}/{o.Index} bateu na parede em {o.Actor.Root.GlobalPosition.Round()}");
            o.SetSequence("SQ_DDMG");
        }
        if (o.FellOut) o.Seq?.Start("SQ_ERAS");
    }
}
