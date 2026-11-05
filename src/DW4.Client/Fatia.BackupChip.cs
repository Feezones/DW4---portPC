using System;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Backup chip. FATO (textos mswk67043–67049, mswk202433–202436): ao cair, o Digimon deixa um backup chip onde foi derrotado e vai ao
/// lobby; todo o BIT fica no chip (fica com 0) e a EXP cai (nada no nível 1); o Raise Disk ou o MP Tech Raise usados no chip devolvem
/// todo o BIT e parte da EXP; cair de novo sem recuperar apaga o chip antigo. FATO (BKCHIPBITEXP 0x80377F70): o BIT e a EXP perdida
/// vão para a lista do chip; a perda de EXP está em PlayerGrowth.DeathExpLoss. FATO (IGB #179): o modelo é o g_000bcaps (bkcore,
/// mekafin, bkchip) com SQ_ENTR1P → SQ_WAIT e SQ_EXIT → SQ_ERAS; exportado em &lt;fases&gt;\_comum\g_000bcaps.
/// HIPÓTESES: o Raise devolve metade da EXP guardada (o código da devolução não foi lido); o disco vale a até 25 unidades do chip;
/// o chip nasce na posição exata da queda (as tabelas g_posTbl do end_g_000bcaps.lua, por área, não foram usadas).
/// </summary>
public partial class Fatia
{
    const float ChipReach = 25f;
    WorldObject? _chip;

    // Queda em campo ou masmorra: o chip guarda o BIT e a EXP perdida (substitui um chip antigo).
    void DropBackupChip()
    {
        var inv = _inventory;
        long loss = PlayerGrowth.DeathExpLoss(_level, _exp);
        var p = _hp <= 0 ? _safeFloor : _player.GlobalPosition;
        (inv.ChipStage, inv.ChipPos, inv.ChipBit, inv.ChipExp) = (_stageName, [p.X, p.Y, p.Z], inv.Bit, loss);
        _exp -= loss;
        inv.Bit = 0;
        ScriptLog($"backup chip em {_stageName} ({p.X:0}, {p.Y:0}, {p.Z:0}): {inv.ChipBit} BIT, {loss} EXP");
    }

    // Ao abrir a fase: o chip da vaga aparece onde o Digimon caiu.
    void SpawnBackupChip()
    {
        var inv = _inventory;
        if (inv.ChipStage.Length == 0 || !inv.ChipStage.Equals(_stageName, StringComparison.OrdinalIgnoreCase)) return;
        var dir = Path.Combine(DataPaths.Fases, "_comum", "g_000bcaps");
        if (HudData.Load(dir, _problems, "objetos.json", "falta <fases>\\_comum\\g_000bcaps (dw4 igb-objetos ndmw.afs 179 … g_000bcaps): backup chip sem modelo") is not { } data
            || !data.Actors.TryGetValue("g_000bcaps", out int root)) return;
        var seqFile = GameFiles.EnumerateFiles(dir, "*_seq.json").FirstOrDefault();
        var json = seqFile != null ? GameFiles.ReadAllText(seqFile) : null;
        var actor = new HudActor(data, root, slot: 0);
        _chip = new WorldObject
        {
            Name = "g_000bcaps", Actor = actor,
            Seq = json != null ? new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)) : null,
        };
        AddChild(actor.Root);
        actor.Root.Position = new Vector3(inv.ChipPos[0], inv.ChipPos[1], inv.ChipPos[2]);
        _chip.SetSequence("SQ_ENTR1P");
        _objects.Add(_chip);
    }

    // DECISÃO DO PORT PC (pedido do usuário, 2026-11-05): o chip fica na última plataforma em que o Digimon pisou, não lá embaixo
    // quando ele cai num buraco e morre no fundo (piso de dano, inimigos). Plataforma segura = chão em que ficou sem tomar dano; depois
    // de uma queda grande (mais de FallDrop abaixo do chão de onde saiu), o chão novo só vira seguro depois de SafeAfterFall segundos
    // nele sem dano. Qualquer dano no chão em observação recomeça a contagem; dano num chão já seguro não muda nada.
    const float FallDrop = 60f, SafeAfterFall = 4f;
    Vector3 _safeFloor;
    float _safeWait;
    bool _wasOnFloor;
    int _safeHp = -1;

    void TrackSafeFloor(float dt)
    {
        bool onFloor = _player.IsOnFloor();
        bool hurt = _safeHp >= 0 && _hp < _safeHp;
        _safeHp = _hp;
        if (_hp <= 0) { _wasOnFloor = onFloor; return; } // caído: o chip vai para o _safeFloor (DropBackupChip)
        if (onFloor && !_wasOnFloor && _player.GlobalPosition.Y < _lastFloor.Y - FallDrop) _safeWait = SafeAfterFall;
        _wasOnFloor = onFloor;
        if (!onFloor) return;
        if (_safeWait > 0)
        {
            _safeWait = hurt ? SafeAfterFall : _safeWait - dt;
            if (_safeWait > 0) return;
        }
        _safeFloor = _player.GlobalPosition;
    }

    bool NearChip => _chip is { Erased: false } c && c.Actor.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) <= ChipReach;

    static bool IsRaiseDisk(GameTables db, int row) => db["conItem"].Text(row, "NAME").Contains("Raise", StringComparison.OrdinalIgnoreCase);

    // Raise Disk: perto do chip, devolve o BIT e parte da EXP e o chip some; longe, avisa e não gasta o disco.
    bool UseRaiseDisk(int row)
    {
        if (_db == null || !IsRaiseDisk(_db, row)) return false;
        if (!NearChip) { Toast("Use o Raise Disk perto do seu backup chip."); return true; }
        if (!_inventory.RemoveDisks(row, 1)) return true;
        var inv = _inventory;
        int bit = inv.ChipBit;
        long exp = inv.ChipExp / 2;
        inv.Bit += bit;
        (inv.ChipStage, inv.ChipBit, inv.ChipExp) = ("", 0, 0);
        _chip?.SetSequence("SQ_EXIT");
        _chip = null;
        if (exp > 0) AddExp((int)Math.Min(int.MaxValue, exp));
        PlayerEffect("x_250curehit01", "SQ_ATCK01");
        Toast($"Backup chip recuperado: {bit} BIT e {exp} EXP.");
        ScriptLog($"Raise Disk no backup chip: +{bit} BIT, +{exp} EXP");
        return true;
    }

    // DEPURAÇÃO: DW4_RAISE=1 (com DW4_MORRER): no lobby volta à fase do chip aos 3 s; na fase do chip, aos 3 s, vai até o chip,
    // ganha um Raise Disk e usa. DW4_RAISE=ver: vai até o chip aos 3 s e só usa o disco aos 6 s (para o DW4_PRINT fotografar o chip).
    float _raiseClock;
    int _raiseStep;

    void TickRaiseDebug(float dt)
    {
        var mode = OS.IsDebugBuild() ? OS.GetEnvironment("DW4_RAISE") : "";
        if (mode is not ("1" or "ver") || _raiseStep >= 2) return;
        _raiseClock += dt;
        var inv = _inventory;
        if (_raiseStep == 0 && _raiseClock >= 3)
        {
            _raiseStep = 1;
            if (inv.ChipStage.Length == 0 || _db == null) { _raiseStep = 2; return; }
            if (_stageName.Equals(LobbyStage, StringComparison.OrdinalIgnoreCase))
            {
                GD.Print($"[chip] lobby: voltando a {inv.ChipStage}");
                _raiseStep = 2;
                _nextEntry = 1;
                NextStage(inv.ChipStage);
                return;
            }
            if (_chip == null) { GD.Print("[chip] fase do chip sem chip"); _raiseStep = 2; return; }
            _player.GlobalPosition = _chip.Actor.Root.GlobalPosition + new Vector3(0, 0, 15);
        }
        if (_raiseStep != 1 || _raiseClock < (mode == "ver" ? 6 : 3)) return;
        _raiseStep = 2;
        var items = _db!["conItem"];
        int row = Enumerable.Range(0, items.Rows).FirstOrDefault(r => IsRaiseDisk(_db, r), -1);
        if (row < 0) { GD.Print("[chip] sem Raise Disk no conItem"); return; }
        inv.Add(new ItemRef("conItem", row));
        int bit0 = inv.Bit; long exp0 = _exp;
        bool used = UseRaiseDisk(row);
        GD.Print($"[chip] Raise Disk (linha {row}): usado={used}, BIT {bit0}->{inv.Bit}, EXP {exp0}->{_exp}, chip={inv.ChipStage.Length > 0}");
    }
}
