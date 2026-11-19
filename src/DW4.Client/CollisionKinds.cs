using System;
using System.Collections.Generic;
using System.Linq;

namespace DW4.Client;

enum CollisionKind { Normal, Edge, EnemyOnly }

/// <summary>
/// Tipo das peças da colisão da fase pelo beHitLandModelInfo @10 (dw4 colisao-auditoria, docs/gameplay/paredes-invisiveis.md).
/// O colisao.gltf exportado só traz o nome do nó; a tabela abaixo diz, por fase, quais nomes são de cada tipo.
///  - 2050 (0x800 | 2), "parapeito": paredes altas (até ~320) nas bordas das áreas, sem nada visível, que nenhum script liga ou
///    desliga (FATO). HIPÓTESE: seguram quem anda (não se cai andando) e não seguram no pulo (pilares da stag0102).
///  - 8, "parede de inimigo": as wallE* das masmorras e afins (FATO: nome wallE; HIPÓTESE: E = inimigo); não seguram o jogador.
/// </summary>
static class CollisionKinds
{
    public const uint EdgeLayer = 2;
    public const uint EnemyLayer = 4;

    public sealed class Stage(HashSet<string> edge, HashSet<string> enemy)
    {
        public CollisionKind Of(IEnumerable<string> path)
        {
            foreach (var raw in path)
            {
                var name = raw.EndsWith("-colonly", StringComparison.Ordinal) ? raw[..^"-colonly".Length] : raw;
                if (edge.Contains(name)) return CollisionKind.Edge;
                if (enemy.Contains(name)) return CollisionKind.EnemyOnly;
            }
            return CollisionKind.Normal;
        }
    }

    public static Stage For(string stage) => Table.TryGetValue(stage, out var t)
        ? new Stage(t.Edge.ToHashSet(StringComparer.Ordinal), t.Enemy.Except(t.Edge).ToHashSet(StringComparer.Ordinal))
        : new Stage(new HashSet<string>(), new HashSet<string>());

    static readonly Dictionary<string, (string[] Edge, string[] Enemy)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Gerada de D:\dw4-work\auditoria-colisao\tipos-8-2050.json (2026-11-04). Fora do parapeito: as peças "hit*" do 2050
        // (portões hitcirclegatez03/hitgekoaz03, hithideliftz04, plataformas que andam hitmvbox*, hitflislandz04, hitmagnetflore*,
        // hitmaincorez*, hitcomber0Shape0), que são ligadas a objetos e scripts e continuam segurando sempre.
        // stag0203 (8): as polySurfaceShape427..432 caíram dentro do nó único "colisao-colonly" e não têm nó próprio; ficam como estão.
        ["dng103f2"] = (Array.Empty<string>(), new string[] { "wallEShape214" }),
        ["dng205b3"] = (Array.Empty<string>(), new string[] { "hitR320Shape", "hitR321Shape", "hitR322Shape" }),
        ["dng206f2"] = (Array.Empty<string>(), new string[] { "wallEShape206", "wallEShape210", "wallEShape213" }),
        ["dng309b1"] = (new string[] { "polySurfaceShape409" }, Array.Empty<string>()),
        ["dng309b2"] = (new string[] { "polySurfaceShape453" }, Array.Empty<string>()),
        ["dng309b3"] = (new string[] { "polySurfaceShape561", "polySurfaceShape588" }, Array.Empty<string>()),
        ["dng309b4"] = (new string[] { "polySurfaceShape177" }, Array.Empty<string>()),
        ["dng310f3"] = (Array.Empty<string>(), new string[] { "wallEShape305", "wallEShape310" }),
        ["dng412b4"] = (Array.Empty<string>(), new string[] { "wallEShape427", "wallEShape411" }),
        ["dngq3b1e"] = (new string[] { "polySurfaceShape133", "polySurfaceShape134" }, Array.Empty<string>()),
        ["dngq3b2e"] = (new string[] { "polySurfaceShape135" }, Array.Empty<string>()),
        ["dngq4b2e"] = (Array.Empty<string>(), new string[] { "hitR320Shape" }),
        ["dngq4b2h"] = (Array.Empty<string>(), new string[] { "hitR320Shape" }),
        ["dngq4b2v"] = (Array.Empty<string>(), new string[] { "hitR320Shape" }),
        ["dngq7b1e"] = (new string[] { "polySurfaceShape252", "polySurfaceShape253" }, Array.Empty<string>()),
        ["dngq7b2e"] = (new string[] { "polySurfaceShape162", "polySurfaceShape163" }, Array.Empty<string>()),
        ["stag0000"] = (new string[] { "Stage0101_polySurfaceShape9287" }, Array.Empty<string>()),
        ["stag0001"] = (new string[] { "Stage0101_polySurfaceShape9287" }, Array.Empty<string>()),
        ["stag0101"] = (new string[] { "Stage0101_polySurfaceShape9276", "Stage0101_polySurfaceShape9277", "Stage0101_polySurfaceShape9278" }, Array.Empty<string>()),
        ["stag0102"] = (new string[] { "Stage0102_polySurfaceShape290", "Stage0102_polySurfaceShape291", "Stage0102_polySurfaceShape292", "Stage0102_polySurfaceShape289" }, new string[] { "Stage0102_polySurfaceShape315" }),
        ["stag0103"] = (Array.Empty<string>(), new string[] { "polySurfaceShape67", "polySurfaceShape68", "polySurfaceShape69", "polySurfaceShape70", "polySurfaceShape71" }),
        ["stag0104"] = (new string[] { "Stage0104_polySurfaceShape50" }, Array.Empty<string>()),
        ["stag0201"] = (new string[] { "Stage0201_polySurfaceShape146", "Stage0201_polySurfaceShape174", "Stage0201_polySurfaceShape143", "Stage0201_polySurfaceShape175", "Stage0201_polySurfaceShape149", "Stage0201_polySurfaceShape176", "Stage0201_polySurfaceShape177", "Stage0201_polySurfaceShape178", "Stage0201_polySurfaceShape180", "Stage0201_polySurfaceShape181", "Stage0201_polySurfaceShape182" }, Array.Empty<string>()),
        ["stag0202"] = (new string[] { "Stage0202_polySurfaceShape368", "Stage0202_polySurfaceShape369", "Stage0202_polySurfaceShape370", "Stage0202_polySurfaceShape371", "Stage0202_polySurfaceShape372", "Stage0202_polySurfaceShape373", "Stage0202_polySurfaceShape374", "Stage0202_polySurfaceShape375", "Stage0202_polySurfaceShape376", "Stage0202_polySurfaceShape377", "Stage0202_polySurfaceShape378", "Stage0202_polySurfaceShape379", "Stage0202_polySurfaceShape344", "Stage0202_polySurfaceShape380" }, new string[] { "Stage0202_polySurfaceShape409" }),
        ["stag0203"] = (Array.Empty<string>(), new string[] { "polySurfaceShape427", "polySurfaceShape428", "polySurfaceShape429", "polySurfaceShape430", "polySurfaceShape431", "polySurfaceShape432" }),
        ["stag0204"] = (new string[] { "Stage0204_polySurfaceShape304", "Stage0204_polySurfaceShape305", "Stage0204_polySurfaceShape334", "Stage0204_polySurfaceShape335", "Stage0204_polySurfaceShape336", "Stage0204_polySurfaceShape337", "Stage0204_polySurfaceShape338", "Stage0204_polySurfaceShape354", "Stage0204_polySurfaceShape355" }, Array.Empty<string>()),
        ["stag0205"] = (new string[] { "Stage0205_polySurfaceShape44", "Stage0205_polySurfaceShape43" }, Array.Empty<string>()),
        ["stag0301"] = (new string[] { "Stage0301_polySurfaceShape1048", "Stage0301_polySurfaceShape1047", "Stage0301_polySurfaceShape1086", "Stage0301_polySurfaceShape1103", "Stage0301_polySurfaceShape1106" }, Array.Empty<string>()),
        ["stag0302"] = (new string[] { "Stage0302_polySurfaceShape1852", "Stage0302_polySurfaceShape2638", "Stage0302_polySurfaceShape1870", "Stage0302_polySurfaceShape1889", "Stage0302_polySurfaceShape2733", "Stage0302_polySurfaceShape2734", "Stage0302_polySurfaceShape2749", "Stage0302_polySurfaceShape2759", "Stage0302_polySurfaceShape2765", "Stage0302_polySurfaceShape2781", "Stage0302_polySurfaceShape2790", "Stage0302_polySurfaceShape2817", "Stage0302_polySurfaceShape2825", "Stage0302_polySurfaceShape2875", "Stage0302_polySurfaceShape2884", "Stage0302_polySurfaceShape2885" }, Array.Empty<string>()),
        ["stag0303"] = (new string[] { "Stage0303_polySurfaceShape1216", "Stage0303_polySurfaceShape1219" }, Array.Empty<string>()),
        ["stag0304"] = (new string[] { "Stage0304_polySurfaceShape971", "Stage0304_polySurfaceShape986", "Stage0304_polySurfaceShape1003", "Stage0304_polySurfaceShape313", "Stage0304_polySurfaceShape1019", "Stage0304_polySurfaceShape1035", "Stage0304_polySurfaceShape1051", "Stage0304_polySurfaceShape1059", "Stage0304_polySurfaceShape1064" }, Array.Empty<string>()),
        ["stag0401"] = (new string[] { "Stage0401_polySurfaceShape196", "Stage0401_polySurfaceShape183", "Stage0401_polySurfaceShape187", "Stage0401_polySurfaceShape205", "Stage0401_polySurfaceShape206" }, Array.Empty<string>()),
        ["stag0403"] = (new string[] { "Stage0403_polySurfaceShape40", "Stage0403_polySurfaceShape54", "Stage0403_polySurfaceShape43", "Stage0403_polySurfaceShape55", "Stage0403_polySurfaceShape45", "Stage0403_polySurfaceShape47", "Stage0403_polySurfaceShape56", "Stage0403_polySurfaceShape50", "Stage0403_polySurfaceShape57", "Stage0403_polySurfaceShape53", "Stage0403_polySurfaceShape365", "Stage0403_polySurfaceShape367", "Stage0403_polySurfaceShape369", "polySurfaceShape1", "polySurfaceShape122", "polySurfaceShape123", "polySurfaceShape124", "polySurfaceShape125", "polySurfaceShape126" }, Array.Empty<string>()),
        ["stag0404"] = (new string[] { "Stage0404_polySurfaceShape41", "Stage0404_polySurfaceShape116", "Stage0404_polySurfaceShape118", "Stage0404_polySurfaceShape126", "Stage0404_polySurfaceShape128", "Stage0404_polySurfaceShape136", "Stage0404_polySurfaceShape140", "Stage0404_polySurfaceShape148", "Stage0404_polySurfaceShape150", "Stage0404_polySurfaceShape158", "Stage0404_polySurfaceShape160", "Stage0404_polySurfaceShape169", "Stage0404_polySurfaceShape171", "Stage0404_polySurfaceShape48" }, Array.Empty<string>()),
        ["stagbs01"] = (new string[] { "StageBs01_polySurfaceShape171" }, new string[] { "StageBs01_polySurfaceShape51" }),
        ["stagbs02"] = (new string[] { "StageBs02_pCylinderShape10" }, Array.Empty<string>()),
        ["stagbs03"] = (new string[] { "StageBs01_pCubeShape3" }, Array.Empty<string>()),
        ["stagbs04"] = (new string[] { "StageBs01_polySurfaceShape6" }, Array.Empty<string>()),
        ["stagbs05"] = (new string[] { "StageBs01_pCylinderShape2" }, Array.Empty<string>()),
        ["stagbs06"] = (new string[] { "StageBs01_polySurfaceShape3", "StageBs01_polySurfaceShape27" }, new string[] { "StageBs01_polySurfaceShape27" }),
        ["staggf02"] = (new string[] { "StageGF02_polySurfaceShape310", "StageGF02_polySurfaceShape301", "StageGF02_polySurfaceShape303", "StageGF02_polySurfaceShape304", "StageGF02_polySurfaceShape305", "StageGF02_polySurfaceShape306", "StageGF02_polySurfaceShape307", "StageGF02_polySurfaceShape308" }, Array.Empty<string>()),
        ["staggf03"] = (new string[] { "StageGF03_polySurfaceShape78" }, Array.Empty<string>()),
    };
}
