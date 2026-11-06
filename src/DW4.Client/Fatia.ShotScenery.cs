using System;
using System.Collections.Generic;
using Godot;

namespace DW4.Client;

/// <summary>
/// Tiro do jogador e paredes invisíveis (relato do usuário, 2026-11-06: "o projétil do tiro não segue o trajeto todo quando atira
/// para fora do mapa"). Desde a 0.6.17 os tiros com LUACALL de batida somem ao cruzar a colisão da camada 1 (Fatia.Shots.cs,
/// CheckShotLand); antes o tiro do jogador nunca batia em nada.
/// FATO (main.dol, beModelCtrl): o LUACALL "BHit" fica no campo 0x30 e roda pela mensagem CHKBULLET (tiro contra modelo, junto de
/// HITPLAYERBULLET/HITENEMYBULLET, 0x8031029c), não pela colisão do cenário; "WHit" (campo 0x34) e "FHit" (0x38) rodam no
/// tratamento de contato com a colisão (0x80309c74), com a checagem de parede (0xd8, ONWALL/OFFWALL) e queda (0xda, onFall/noFall)
/// ligadas por padrão (0x8030ea00). O x_100gunv01 arma os três com bht_x_100gunv01.lua = MDLsetSeq "SQ_ERAS": no jogo ele some
/// ao bater em parede ou chão.
/// FATO (dw4 colisao-auditoria): todas as fases têm muita parede de colisão sem cenário a menos de 6 (dng101b1: 429 mil de área),
/// paredes altas por cima das visíveis e nas bordas do mapa.
/// HIPÓTESE (pelo relato do usuário, que joga o original): o tiro do jogador passa pelas paredes invisíveis; só some na colisão
/// que tem cenário perto (parede ou chão que se vê). Também não some no chão de veículo (o barco) nem no corpo das caixas: estas
/// levam o tiro pelo acerto (CheckPlayerShotHit), que agora vem antes da batida.
/// </summary>
public partial class Fatia
{
    // Distância do critério "sem cenário" da auditoria (docs/gameplay/paredes-invisiveis.md).
    const float SceneryNear = 6f;
    const float SceneryCell = 16f;

    static readonly bool DiagShot = OS.GetEnvironment("DW4_DIAG_TIRO") == "1";

    Node3D? _sceneryRoot;
    StaticBody3D? _stageBody;
    Dictionary<(int, int, int), List<int>>? _sceneryGrid;
    Vector3[] _sceneryTris = [];
    readonly List<int> _sceneryBig = [];

    /// <summary>A batida do tiro do jogador em <paramref name="body"/> no ponto <paramref name="at"/> vale (ver o resumo acima)?</summary>
    bool PlayerShotLands(StaticBody3D body, Vector3 at) => body == _stageBody && SceneryNearby(at);

    bool SceneryNearby(Vector3 p)
    {
        if (_sceneryRoot == null) return true; // sem cenário carregado: fica a batida de antes
        var grid = SceneryGrid();
        var c = Cell(p);
        float best = SceneryNear * SceneryNear;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (grid.TryGetValue((c.Item1 + x, c.Item2 + y, c.Item3 + z), out var list))
                        foreach (int t in list)
                            if (Near(t)) return true;
        foreach (int t in _sceneryBig) if (Near(t)) return true;
        return false;

        bool Near(int t) => DistSqToTriangle(p, _sceneryTris[t], _sceneryTris[t + 1], _sceneryTris[t + 2]) <= best;
    }

    static (int, int, int) Cell(Vector3 p) =>
        ((int)MathF.Floor(p.X / SceneryCell), (int)MathF.Floor(p.Y / SceneryCell), (int)MathF.Floor(p.Z / SceneryCell));

    // Grade dos triângulos do cenário no espaço do mundo, montada na primeira batida (o cenário não se mexe).
    Dictionary<(int, int, int), List<int>> SceneryGrid()
    {
        if (_sceneryGrid != null) return _sceneryGrid;
        _sceneryGrid = [];
        var tris = new List<Vector3>();
        foreach (var mi in FindAll<MeshInstance3D>(_sceneryRoot!))
        {
            if (mi.Mesh == null) continue;
            var xf = mi.GlobalTransform;
            var faces = mi.Mesh.GetFaces();
            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 a = xf * faces[f], b = xf * faces[f + 1], c = xf * faces[f + 2];
                int index = tris.Count;
                tris.Add(a); tris.Add(b); tris.Add(c);
                var min = new Vector3(MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), MathF.Min(a.Z, MathF.Min(b.Z, c.Z)));
                var max = new Vector3(MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Y, MathF.Max(b.Y, c.Y)), MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
                var lo = Cell(min);
                var hi = Cell(max);
                // Triângulo enorme (planos de fundo, chão de 3200 × 3200): fora da grade, conferido sempre.
                if ((long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4096) { _sceneryBig.Add(index); continue; }
                for (int x = lo.Item1; x <= hi.Item1; x++)
                    for (int y = lo.Item2; y <= hi.Item2; y++)
                        for (int z = lo.Item3; z <= hi.Item3; z++)
                        {
                            if (!_sceneryGrid.TryGetValue((x, y, z), out var list)) _sceneryGrid[(x, y, z)] = list = [];
                            list.Add(index);
                        }
            }
        }
        _sceneryTris = tris.ToArray();
        return _sceneryGrid;
    }

    // Distância ao quadrado de um ponto a um triângulo (Ericson, Real-Time Collision Detection, 5.1.5).
    static float DistSqToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();
        Vector3 bp = p - b;
        float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); return (p - (a + v * ab)).LengthSquared(); }
        Vector3 cp = p - c;
        float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); return (p - (a + w * ac)).LengthSquared(); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { float w = (d4 - d3) / (d4 - d3 + (d5 - d6)); return (p - (b + w * (c - b))).LengthSquared(); }
        float denom = 1 / (va + vb + vc);
        float vv = vb * denom, ww = vc * denom;
        return (p - (a + ab * vv + ac * ww)).LengthSquared();
    }
}
