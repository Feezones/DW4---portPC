using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Modo espectador ao cair em grupo. DECISÃO DO PORT (pedido do responsável, 2026-11-06; não existe no original, que era em tela
/// dividida num só GameCube):
///  · sozinho, ou sem nenhum colega vivo na mesma fase, a queda segue como antes (Fatia.Death.cs: backup chip e lobby);
///  · com colega vivo na fase, depois da animação de queda o Digimon deixa o cristal (backup chip, com o BIT e a EXP perdida, como
///    antes) e a câmera passa a seguir um colega vivo; esquerda/direita (ou ataque) troca de colega, sem limite de tempo;
///  · segurar o pulo por <see cref="GiveUpHold"/> s desiste: vai ao lobby como antes e o cristal fica no chão;
///  · um colega perto do cristal usa um Raise Disk DELE (<see cref="Revive"/>): o caído levanta ali com metade do HP e do MP e
///    recebe de volta todo o BIT e toda a EXP do chip (nada vai para quem reviveu);
///  · caído não ganha EXP, BIT nem drops dos colegas (GainExp, DropItem e a coleta já olham o HP), mas os scripts da fase continuam
///    rodando aqui, então flags de mapa e história chegam iguais;
///  · todos caídos: cada um vai ao lobby como antes;
///  · os colegas vivos trocam de fase (<see cref="StageGo"/>) e não sobra ninguém vivo aqui: o caído vai junto, ainda espectador, e o
///    cristal reaparece no ponto de entrada da fase nova.
/// </summary>
public partial class Fatia
{
    const float GiveUpHold = 2f;
    // Sem colega vivo na fase por esse tempo (s), o espectador acaba (espera o StageGo de quem saiu e o grupo novo se formar).
    const float AloneGrace = 2f, ArriveGrace = 6f;

    bool _spectating;
    int _spectateId;
    float _spectateAlone, _spectateGrace = AloneGrace, _giveUpTimer;
    (string Stage, int Entry)? _followStage;
    Label? _spectateLabel;
    // Vai entrar na fase nova seguindo o grupo, ainda caído (estático: sobrevive à recarga da cena).
    static bool s_arriveSpectating;

    // Colegas vivos nesta fase (ids online), na ordem do grupo.
    List<int> AliveMates() => _coopMembers
        .Where(id => id != Online.MyId && Online.Remotes.TryGetValue(id, out var r) && (r.State.Hp > 0 || r.State.MaxHp == 0)
            && r.State.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    bool IsFieldStage => !_stageName.StartsWith("stagML", StringComparison.OrdinalIgnoreCase);

    // Chamado pelo TickDeath depois da animação de queda: true = virou espectador (não vai ao lobby agora).
    bool TryStartSpectating()
    {
        if (!CoopOn || !IsFieldStage || AliveMates().Count == 0) return false;
        // O chip fica na última plataforma pisada; o jogador fica lá também, para os colegas verem o cristal no lugar certo.
        if (_safeGround is { } safe) _player.GlobalPosition = safe;
        _player.Velocity = Vector3.Zero;
        DropBackupChip();
        SpawnBackupChip();
        BeginSpectating(AloneGrace);
        ScriptLog($"caiu no grupo: espectador ({_inventory.ChipBit} BIT no cristal)");
        return true;
    }

    void BeginSpectating(float grace)
    {
        _spectating = true;
        _spectateAlone = 0;
        _spectateGrace = grace;
        _giveUpTimer = 0;
        _followStage = null;
        _spectateId = AliveMates().FirstOrDefault();
        if (_model != null) _model.Visible = false; // os outros veem só o cristal
    }

    // Chegou à fase nova seguindo o grupo: continua caído, com o cristal no ponto de entrada.
    void TickArriveSpectating()
    {
        if (!s_arriveSpectating || _pf == null || _player == null) return;
        s_arriveSpectating = false;
        if (!IsFieldStage) return; // o grupo foi ao lobby: lá a queda acaba (HP cheio, como o DEADRESTART)
        _hp = 0;
        var p = _player.GlobalPosition;
        var inv = _inventory;
        (inv.ChipStage, inv.ChipPos) = (_stageName, [p.X, p.Y, p.Z]); // o BIT e a EXP do chip vêm junto
        SpawnBackupChip();
        _deathTimer = DeathDelay; // já passou da animação de queda
        BeginSpectating(ArriveGrace);
        ScriptLog($"seguiu o grupo até {_stageName}: espectador, cristal na entrada");
    }

    // A cada quadro enquanto espectador. true = continua espectador; false = acabou (vai ao lobby pelo fluxo de antes).
    bool TickSpectating(float dt)
    {
        var mates = AliveMates();
        if (mates.Count == 0)
        {
            _spectateAlone += dt;
            if (_followStage is { } go && _spectateAlone > 0.3f) { FollowGroup(go.Stage, go.Entry); return true; }
            if (_spectateAlone < _spectateGrace) { UpdateSpectateLabel(); return true; }
            ScriptLog("espectador: nenhum colega vivo na fase; volta ao lobby");
            return EndSpectating();
        }
        _spectateAlone = 0;
        _spectateGrace = AloneGrace;
        if (!mates.Contains(_spectateId)) _spectateId = mates[0];
        _giveUpTimer = _hasFocus && !ChatOpen && JumpDown() ? _giveUpTimer + dt : 0;
        if (_giveUpTimer >= GiveUpHold) { ScriptLog("espectador: desistiu; volta ao lobby"); return EndSpectating(); }
        UpdateSpectateLabel();
        if (OS.IsDebugBuild() && (int)(_deathTimer / 2) != (int)((_deathTimer - dt) / 2))
            GD.Print($"[espectador] assistindo {_spectateId}, câmera em {SpectateFocus()}, HP {_hp}, BIT {_inventory.Bit}, EXP {_exp}");
        return true;
    }

    bool EndSpectating()
    {
        _spectating = false;
        _followStage = null;
        if (_spectateLabel != null) _spectateLabel.Visible = false;
        return false;
    }

    void FollowGroup(string stage, int entry)
    {
        EndSpectating();
        _chipDropped = true; // se não der para trocar de fase, o fim de sempre leva ao lobby sem guardar o chip de novo
        _deathTimer = DeathDelay;
        s_arriveSpectating = true;
        _nextEntry = entry;
        ScriptLog($"espectador: segue o grupo para {stage}, entrada {entry}");
        NextStage(stage);
        if (!_leaving) s_arriveSpectating = false; // a fase não existe aqui: fica (e o fim de sempre leva ao lobby)
    }

    // Esquerda/direita (ou ataque) troca de colega.
    bool SpectatorInput(InputEvent e)
    {
        if (!_spectating) return false;
        int step = PortInput.Pressed(e, "direita") || PortInput.Pressed(e, "ataque") ? 1 : PortInput.Pressed(e, "esquerda") ? -1 : 0;
        if (step == 0) return false;
        var mates = AliveMates();
        if (mates.Count == 0) return true;
        int i = mates.IndexOf(_spectateId);
        _spectateId = mates[((i < 0 ? 0 : i + step) % mates.Count + mates.Count) % mates.Count];
        UpdateSpectateLabel();
        return true;
    }

    // Para onde a câmera olha: o colega assistido (null = o próprio jogador).
    Vector3? SpectateFocus() =>
        _spectating && _avatars.TryGetValue(_spectateId, out var a) ? a.Root.GlobalPosition : null;

    void UpdateSpectateLabel()
    {
        if (_spectateLabel == null)
        {
            var layer = new CanvasLayer { Layer = 24 };
            _spectateLabel = new Label
            {
                AnchorLeft = 0, AnchorRight = 1, OffsetTop = 110, OffsetLeft = 16, OffsetRight = -16,
                HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _spectateLabel.AddThemeFontSizeOverride("font_size", 24);
            _spectateLabel.AddThemeConstantOverride("outline_size", 8);
            _spectateLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            layer.AddChild(_spectateLabel);
            AddChild(layer);
            UseGameFont(_spectateLabel);
        }
        string who = Online.Remotes.TryGetValue(_spectateId, out var r) ? r.Character : "";
        string side = $"{DeviceIcons.Of("esquerda")}/{DeviceIcons.Of("direita")}";
        _spectateLabel.Text = who.Length == 0
            ? "Você caiu. Esperando o grupo..."
            : $"Você caiu. Assistindo: {who}   [{side} troca]\n" +
              $"Um colega pode te reviver usando um Raise Disk no seu cristal.   [segure {DeviceIcons.Of("pulo")}: voltar ao lobby" +
              (_giveUpTimer > 0 ? $" {GiveUpHold - _giveUpTimer:0.0} s" : "") + "]";
        _spectateLabel.Visible = true;
    }

    // Colega usou um Raise Disk no meu cristal: levanto aqui com metade do HP e do MP e recebo todo o BIT e a EXP do chip.
    void Revived(int by)
    {
        if (!_spectating || _pf == null) return;
        EndSpectating();
        var inv = _inventory;
        int bit = inv.ChipBit;
        long exp = inv.ChipExp;
        if (inv.ChipStage.Equals(_stageName, StringComparison.OrdinalIgnoreCase))
        {
            (inv.ChipStage, inv.ChipBit, inv.ChipExp) = ("", 0, 0);
            inv.Bit += bit;
            _chip?.SetSequence("SQ_EXIT");
            _chip = null;
        }
        else (bit, exp) = (0, 0); // o chip é de outra fase (não deveria acontecer): fica lá
        _hp = Math.Max(1, _pf.MaxHp / 2);
        _mp = _pf.MaxMp / 2;
        _deathTimer = -1;
        _gameOver = false;
        _runner?.Stop();
        if (_model != null) _model.Visible = true;
        if (exp > 0) AddExp((int)Math.Min(int.MaxValue, exp)); // subir de nível aqui enche o HP, como sempre
        PlayerEffect("x_250curehit01", "SQ_ATCK01");
        string who = Online.Remotes.TryGetValue(by, out var r) ? r.Character : "Um colega";
        Toast($"{who} reviveu você! +{bit} BIT, +{exp} EXP", 4);
        ScriptLog($"revivido por {by}: +{bit} BIT, +{exp} EXP, HP {_hp}/{_pf.MaxHp}");
    }

    // Raise Disk perto do cristal de um colega caído: gasta o MEU disco e avisa o grupo. true = era para um colega.
    bool ReviveMateNear(int row)
    {
        if (!CoopOn) return false;
        var me = _player.GlobalPosition;
        var target = _avatars
            .Where(kv => kv.Value.Chip is { Erased: false } && !kv.Value.ReviveSent && kv.Value.Chip.Actor.Root.GlobalPosition.DistanceTo(me) <= ChipReach)
            .OrderBy(kv => kv.Value.Chip!.Actor.Root.GlobalPosition.DistanceTo(me))
            .Select(kv => (int?)kv.Key).FirstOrDefault();
        if (target is not { } id) return false;
        if (!_inventory.RemoveDisks(row, 1)) return true;
        _avatars[id].ReviveSent = true;
        Online.SendCoop(new Revive(id), true);
        PlayerEffect("x_250curehit01", "SQ_ATCK01");
        string who = Online.Remotes.TryGetValue(id, out var r) ? r.Character : "colega";
        Toast($"Você reviveu {who}.");
        ScriptLog($"Raise Disk no cristal de {id} ({who})");
        return true;
    }

    // DEPURAÇÃO (só no build Debug): DW4_REVIVER=1 — 2 s depois de aparecer o cristal de um colega, vai até ele, ganha um Raise
    // Disk e usa (conferir o reviver sem janela, com outro jogo usando DW4_MORRER).
    float _reviverClock = -1;

    void TickReviverDebug(float dt)
    {
        if (!OS.IsDebugBuild() || OS.GetEnvironment("DW4_REVIVER") != "1" || _db == null || _hp <= 0) return;
        var chip = _avatars.Values.FirstOrDefault(a => a.Chip is { Erased: false } && !a.ReviveSent)?.Chip;
        if (chip == null) { _reviverClock = -1; return; }
        if (_reviverClock < 0) _reviverClock = 0;
        if ((_reviverClock += dt) < 2) return;
        _reviverClock = -1;
        var items = _db["conItem"];
        int row = Enumerable.Range(0, items.Rows).FirstOrDefault(r => IsRaiseDisk(_db, r), -1);
        if (row < 0) return;
        _player.GlobalPosition = chip.Actor.Root.GlobalPosition + new Vector3(0, 0, 10);
        _inventory.Add(new DW4.Gameplay.ItemRef("conItem", row));
        int before = _inventory.Disks(row);
        bool used = UseRaiseDisk(row);
        GD.Print($"[espectador] reviver: Raise Disk usado={used}, discos {before}->{_inventory.Disks(row)}");
    }

    // Cristal de um colega caído: aparece no lugar do modelo dele depois da animação de queda; some quando ele levanta ou sai.
    void UpdateMateChip(Avatar a, RemotePlayer r)
    {
        bool down = r.State.Hp <= 0 && r.State.MaxHp > 0;
        if (!down)
        {
            a.DeadSince = -1;
            a.ReviveSent = false;
            if (a.Chip != null) { a.Chip.SetSequence("SQ_EXIT"); a.Chip = null; }
            if (a.Model != null) a.Model.Visible = true;
            return;
        }
        double now = CoopNow;
        if (a.DeadSince < 0) a.DeadSince = now;
        if (a.Chip != null || now - a.DeadSince < DeathDelay + 0.3) return;
        a.Chip = MakeChip(a.Root.GlobalPosition);
        if (a.Model != null) a.Model.Visible = false;
    }

    void RemoveMateChip(Avatar a)
    {
        if (a.Chip == null) return;
        a.Chip.SetSequence("SQ_EXIT");
        a.Chip = null;
    }

    // Antes de trocar de fase (vivo, no grupo): avisa para onde vai, para quem está caído aqui poder ir junto.
    void AnnounceStageGo(string stage)
    {
        if (CoopOn && _hp > 0 && !_spectating) Online.SendCoop(new StageGo(stage, _nextEntry), true);
    }

    void OnSpectatorCoop(int from, object msg)
    {
        switch (msg)
        {
            case Revive v when v.TargetId == Online.MyId:
                Revived(from);
                break;
            case StageGo g when _spectating && !g.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase)
                && g.Stage.IndexOfAny(['/', '\\', '.']) < 0:
                // Só vale ir junto para outra fase de campo; para o lobby a queda acaba lá, como antes.
                _followStage = g.Stage.StartsWith("stagML", StringComparison.OrdinalIgnoreCase) ? null : (g.Stage, g.Entry);
                break;
        }
    }
}
