using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Baúzinho do item no chão (drop de inimigo, caixa, barril e caixote).
/// FATO (main.dol 0x804DE0A8, pares tabela → modelo): impItem → g_000key, weapon → g_000weapon, armor1 e armor2 → g_000armor,
/// abChip → g_000access. FATO (coluna str:MODEL das tabelas): conItem → g_000item (discos), g_000boost1 (Booster Pack, linha 10),
/// g_000penx (Key Chain, linha 11); insItem → g_000bit (BIT, linha 0), g_000xdata1 (X-Data, 1..3), g_000xdata2 (plugs ↑, 4..25).
/// FATO (IGB #179, igb-tree): só existem as raízes g_000item, g_000armor, g_000bit, g_000xdata1, g_000xdata2, g_000key e g_000boost1;
/// g_000weapon, g_000access e g_000penx têm beModelCtrlInfo e sequências, mas nenhum igAttrSet com o nome em nenhum IGB de fase.
/// HIPÓTESE: weapon e access usam o desenho do g_000armor (as três sequências são idênticas, quadros 0..330, e o armor tem 16 malhas);
/// o g_000penx usa o g_000item (mesma família de disco; as faixas de quadro do penx não batem com nenhum outro, então vai com as do item).
/// FATO (sequências *_seq.txt): SQ_WAIT começa em DEF → SQ_ENTR (TRAN01) → SQ_ENT2 (TRAN02, LABELCTRL ON) → SQ_WAIT/ON (WAIT01, laço
/// de 960 quadros) → SQ_BODING (FLASH1, pisca) → SQ_EXIT (EXIT01, some) → SQ_ERAS. O g_000key não tem SQ_BODING nem SQ_EXIT: fica.
/// HIPÓTESE: o item no chão some sozinho no fim do SQ_BODING (≈ 20 s), como as sequências mandam (o CTRL "isLive" não foi lido);
/// ao pegar, o baú faz SQ_EXIT (o código da coleta não foi lido; SQ_DDMG tem FLASHSET Damage e parece ser do golpe).
/// DECISÃO DO PORT: o nome do item continua flutuando em cima do baú.
/// </summary>
public partial class Fatia
{
    sealed class GroundItem
    {
        public required Node3D Node { get; init; }
        public required ItemRef Item { get; init; }
        /// <summary>Baú do jogo; null = cubo de reserva (falta o modelo).</summary>
        public WorldObject? Chest { get; init; }
        public bool Taken { get; set; }
    }

    HudData? _dropData;
    bool _dropDataTried;
    readonly Dictionary<string, string?> _dropSeqJson = [];

    // HIPÓTESE (ver acima): desenho emprestado quando o modelo não tem raiz no IGB.
    static readonly Dictionary<string, string> DropModelStandIn = new()
    {
        ["g_000weapon"] = "g_000armor", ["g_000access"] = "g_000armor", ["g_000penx"] = "g_000item",
    };

    // Baú do jogo em <fases>\_comum\g_000drop (dw4 igb-objetos + igb-seq ndmw.afs 179). null se faltar (o chamador usa o cubo).
    WorldObject? MakeDropChest(string model)
    {
        var dir = Path.Combine(DataPaths.Fases, "_comum", "g_000drop");
        if (!_dropDataTried)
        {
            _dropDataTried = true;
            _dropData = HudData.Load(dir, _problems, "objetos.json",
                "falta <fases>\\_comum\\g_000drop (dw4 igb-objetos ndmw.afs 179 … g_000item g_000armor g_000bit …): item no chão como cubo");
        }
        if (_dropData == null) return null;
        string drawn = _dropData.Actors.ContainsKey(model) ? model : DropModelStandIn.GetValueOrDefault(model, "g_000item");
        if (!_dropData.Actors.TryGetValue(drawn, out int root))
        {
            if (!_problems.Contains($"g_000drop sem {drawn}")) _problems.Add($"g_000drop sem {drawn}");
            return null;
        }
        if (!_dropSeqJson.TryGetValue(drawn, out var json))
        {
            var file = GameFiles.EnumerateFiles(dir, $"*_{drawn}_seq.json").FirstOrDefault();
            _dropSeqJson[drawn] = json = file != null ? GameFiles.ReadAllText(file) : null;
        }
        var chest = new WorldObject
        {
            Name = model, Actor = new HudActor(_dropData, root, slot: 0),
            Seq = json != null ? new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)) : null,
        };
        chest.SetSequence("SQ_WAIT"); // rótulo DEF → SQ_ENTR
        return chest;
    }

    // DEPURAÇÃO (só Debug): DW4_DROP_AQUI=1 com DW4_DROP=tabela:linha solta o item aos 3 s a 30 unidades do jogador (pega na hora)
    // e outro a 300 (fica no chão e some sozinho no fim do SQ_BODING); o log [script] mostra "caiu", "pegou" e "sumiu".
    float _dropHereClock = -1;

    void TickDropHereDebug(float dt)
    {
        if (_dropHereClock < 0) { if (!OS.IsDebugBuild() || OS.GetEnvironment("DW4_DROP_AQUI") != "1") return; _dropHereClock = 0; }
        float before = _dropHereClock;
        _dropHereClock += dt;
        if ((int)(before / 5) != (int)(_dropHereClock / 5))
            foreach (var g in _ground.Where(g => g.Chest != null))
                GD.Print($"[drop] {_dropHereClock:0} s: {g.Chest!.Name} {g.Chest.Sequence} {g.Chest.Seq?.Action} quadro {g.Chest.Seq?.Frame:0}");
        if (before > 3) return;
        if (_dropHereClock <= 3) return;
        var p = _player.GlobalPosition;
        DropAt(null, p + new Vector3(0, 0, 30));
        DropAt(null, p + new Vector3(0, 0, 300));
    }

    // Nó do item no chão: o baú do jogo ou, sem ele, o cubo dourado de antes; com o nome em cima.
    GroundItem MakeGroundItem(ItemRef item, string name)
    {
        var node = new Node3D();
        var chest = MakeDropChest(Drops.ChestModel(_db, item));
        if (chest != null) node.AddChild(chest.Actor.Root);
        else
            node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3, 3, 3) }, Position = new Vector3(0, 1.5f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Gold, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        node.AddChild(new Label3D { Text = name, FontSize = 48, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, PixelSize = 0.05f, Position = new Vector3(0, 6, 0) });
        return new GroundItem { Node = node, Item = item, Chest = chest };
    }
}
