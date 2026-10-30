using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Chefes (f_*, ex. f_apocaly do stagbs01) e o que a luta pede: o golpe do Guilmon roda o hit_&lt;modelo&gt;.lua do chefe (que
/// calcula pelos be:BLT* e escolhe a reação), rótulos por sequência (LABELCTRL, SETLABEL), troca de mapa de IA (AIMAP), a barra
/// de HP (i_bosshp01 + METERCTRL bshp01 SETPERCENT) e o MOVETRANS da cena da derrota. Levantamento: docs/gameplay/fase5-chefe-bs01.md.
/// </summary>
public partial class Fatia
{
    // Golpe que está sendo resolvido por um hit_*.lua (contexto dos BLT*).
    Enemy? _fightTarget;
    bool _fightCrit;
    int _fightDamage;

    bool UsesHitScript(Enemy e) => e.Kind.Model.StartsWith("f_", StringComparison.Ordinal) && _stage?.Has($"hit_{e.Kind.Model}.lua") == true;

    // Golpe do Guilmon num chefe: o próprio hit_<modelo>.lua decide (FATO: hit_f_apocaly.lua faz BLTisHitCheck, BLTisCritical,
    // BLTgetDamageValue, MDLaddHp(−dano), BLTgetDamageRank → SETLABEL + MDLsetSeq, e na morte a cena com BT_WIN02 e o portal).
    void StrikeWithScript(Enemy e, int code)
    {
        e.BulletKind = code;
        _fightTarget = e;
        _fightCrit = false;
        _fightDamage = 0;
        try { _stage!.RunAs($"hit_{e.Kind.Model}.lua", e); }
        finally { _fightTarget = null; }
    }

    public double? FightField(string method, string field) => PlayerFightField(method, field);

    public double? Fight(string method, double[] args)
    {
        if (PlayerFight(method, args) is { } onPlayer) return onPlayer;
        var e = _fightTarget;
        var ef = e?.Kind.Stats;
        switch (method)
        {
            case "BLTisHitCheck" when _pf != null && ef != null:
                return DamageFormula.PlayerHits(Pf!, ef, technique: false, new StatusMods(), _mt) ? 1 : 0;
            case "BLTisCritical" when _pf != null:
                return (_fightCrit = DamageFormula.PlayerCritical(Pf!, _mt)) ? 1 : 0;
            case "BLTgetDamageValue" when _pf != null && ef != null:
                return _fightDamage = DamageFormula.Physical(Pf!, ef, args.Length > 0 ? args[0] != 0 : _fightCrit, new StatusMods(), _mt).Damage;
            // FATO (hit_f_apocaly.lua): BLTgetDamageRank(MDLgetHp(), crítico): o 1º argumento é o HP que sobrou, não o dano;
            // o dano é o do BLTgetDamageValue deste mesmo golpe.
            case "BLTgetDamageRank" when ef != null:
                return DamageFormula.Rank(_fightDamage, (int)args.ElementAtOrDefault(0), ef.MaxHp);
            // BLTcalcSkillUp(acertou, …): HIPÓTESE pelo uso (1 depois de acertar, 0 na defesa) → P2ESKILLUP da arma.
            case "BLTcalcSkillUp":
                if (args.ElementAtOrDefault(0) != 0) SkillUp();
                return 0;
            case "BLTcalcEXPUp" when e != null:
                if (!e.Killed) { e.Killed = true; GainExp(e); }
                return 0;
            case "MDLdspDamage" when e != null:
                int dmg = (int)args.ElementAtOrDefault(0);
                Damage(e.Body, dmg, onPlayer: false, dmg.ToString(), Colors.White);
                _lastEvent = $"Guilmon: {dmg} em {e.Kind.Name} (HP {e.Hp}/{e.MaxHp})";
                return 0;
        }
        return null; // tempos de estado (sono, veneno…) e o resto: neutro no BeHost (0) com aviso
    }

    public string? FightText(string method, double[] args) => method switch
    {
        // FATO (hit_f_apocaly.lua): "PCBA" é o projétil que o chefe defende (SQ_GUARDR); o golpe de espada não tem nome aqui.
        "MDLgetBulletName" => "",
        // Bloco das sequências de dano pelo elemento; golpe físico = "common" (HIPÓTESE: técnicas teriam fire/ice/…).
        "BLTgetAttributeStr" => "common",
        _ => null,
    };

    // Comandos da sequência do chefe que o runner não executa.
    void EnemyCommand(Enemy e, SeqCommand c)
    {
        switch (c.Kind)
        {
            case "LABELCTRL" when c.S(0) is { } label:
                e.Labels[c.S(1) is { Length: > 0 } seq ? seq : "SQ_WAIT"] = label;
                break;
            case "AIMAP" when c.S(0) is { } map:
                e.Brain?.SetMap(map);
                break;
            case "MOVEEX" or "MOVERL":
                e.Motion.Apply(c);
                break;
            case "LUACALL":
                // FATO: SQ_ATCK2 do f_apocaly faz LUACALL "bit" e a fase tem bit_f_apocaly.lua (gera o f_bit se ele não está vivo,
                // senão manda o chefe de volta ao SQ_WAIT): o script é "<nome>_<modelo>.lua", no contexto do modelo.
                RunLuaCall(c.S(0), e.Kind.Model, e, c.S(1));
                break;
        }
    }
    readonly HashSet<string> _warnedLua = [];

    // LUACALL "<nome>" de um modelo: FATO "<nome>_<modelo>.lua" (bit_f_apocaly.lua). HIPÓTESE quando esse não existe e há um só
    // "<nome>_*.lua" na fase (FATO: f_devl1f2 chama "bmh" e a fase tem bmh_f_devl1h.lua): roda esse.
    // FATO (gr2_f_luce.lua): o 2º valor do LUACALL ("gr2" "GROUND"/"FLY"/"FLYSET") é o nome de chamada que o script testa com
    // MDLisCallName; sem ele o g_Ground do f_luce nunca vira 1 e o chefe não toma dano (hit_f_luce.lua só tira HP no chão).
    void RunLuaCall(string name, string model, IBeModel context, string? call = null)
    {
        if (_stage == null || name.Length == 0) return;
        string? script = $"{name}_{model}.lua";
        if (!_stage.Has(script))
        {
            var dir = Path.Combine(_dir, "lua");
            var only = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, $"{name}_*.lua").Select(Path.GetFileName).ToList() : [];
            script = only.Count == 1 ? only[0] : null;
        }
        if (script != null)
        {
            var host = _stage.Host;
            var old = host.CallName;
            host.CallName = string.IsNullOrEmpty(call) ? null : call;
            try { _stage.RunAs(script, context); }
            finally { host.CallName = old; }
        }
        else if (_warnedLua.Add($"{model}:{name}")) ScriptLog($"{model}: LUACALL \"{name}\" sem {name}_{model}.lua");
    }

    // MOVERL (FATO, ver MotionChannels): "JOINT" peça = copia a matriz da peça; "ACTOR" modelo junta = a matriz da junta do modelo.
    // HIPÓTESE: junta sem osso com o nome cai no nó com o nome (peles rígidas) e, sem ele, na raiz do modelo.
    Transform3D? AttachTarget(SeqCommand rl, string self)
    {
        string model = rl.S(1), joint = rl.S(2);
        if (model.Length == 0 || model == self) return null;
        Node3D? root = _enemies.FirstOrDefault(x => x.Kind.Model == model && !x.Dead)?.Model
            ?? _objects.FirstOrDefault(o => o.Name == model && !o.Erased)?.Actor.Root;
        if (root == null) return null; // sem resposta ao GETTRCDATA/GETMATRIX: não prende
        if (rl.S(0) == "JOINT" || joint.Length == 0) return root.GlobalTransform;
        foreach (var sk in FindAll<Skeleton3D>(root))
            if (sk.FindBone(joint) is var b and >= 0) return sk.GlobalTransform * sk.GetBoneGlobalPose(b);
        return FindAll<Node3D>(root).FirstOrDefault(n => n.Name == joint)?.GlobalTransform ?? root.GlobalTransform;
    }

    // Começa uma sequência do inimigo no rótulo guardado dela (SETLABEL/LABELCTRL), ou "common", "NORM", ou o primeiro rótulo.
    bool StartEnemySequence(Enemy e, string seq)
    {
        if (e.Runner == null) return false;
        if (DiagNpc) GD.Print($"[ini] {e.Kind.Model}: {seq} (mapa {e.Brain?.Map.Name ?? "-"})");
        e.Motion.SequenceChanged(); // 0x8030EA38: troca de sequência
        if (e.Labels.TryGetValue(seq, out var l) && e.Runner.Start(seq, l)) return true;
        if (e.Runner.Start(seq, "common") || e.Runner.Start(seq, "NORM")) return true;
        var first = e.Kind.Sequences.TryGetValue(seq, out var cmds) ? cmds.FirstOrDefault(c => c.Kind == "LABEL")?.S(0) : null;
        return first != null ? e.Runner.Start(seq, first) : e.Runner.Start(seq, null);
    }

    // MODELCTRL para inimigos pelo nome do modelo (f_apocaly SQ_WAIT, SETLABEL "SQ_SDMG/common", MOVETRANS da derrota…).
    bool DeliverToEnemies(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name == null) return false;
        var targets = _enemies.Where(e => e.Kind.Model == m.Name && !e.Dead).ToList();
        if (targets.Count == 0) return false;
        foreach (var e in targets)
        {
            switch (m.Command)
            {
                case "SETLABEL" when m.RawArgs?.Split('/') is [var seq, var label]:
                    e.Labels[seq] = label;
                    break;
                case "MOVETRANS" when m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } p:
                    e.Body.GlobalPosition = new Vector3((float)p.Numbers[0], (float)p.Numbers[1], (float)p.Numbers[2]);
                    if (m.Arg(1) is { Kind: 'V', Numbers.Length: 3 } r) e.Yaw = (float)r.Numbers[1] * MathF.PI / 180;
                    break;
                case var c when c.StartsWith("SQ_", StringComparison.Ordinal):
                    e.SetSequence(c);
                    break;
            }
        }
        return true;
    }

    // Objetos g_ gerados por script que têm hit_<nome>.lua (FATO: g_b01cpole, as 5 colunas do stagbs01, KIND_ENEMY; o
    // hit_g_b01cpole.lua tira 10 por golpe físico e passa a SQ_DDMG abaixo de 10; quebrar todas leva o chefe ao SQ_PHASE2).
    // HIPÓTESE (não lido): HP inicial 50 (5 golpes) e caixa de corpo 14 × 40 × 14.
    const int ScriptedObjectHp = 50;
    void MakeHittable(WorldObject o)
    {
        bool owned = o.Name.StartsWith("f_", StringComparison.Ordinal);
        if (!(o.Name.StartsWith("g_", StringComparison.Ordinal) || owned) || o.Name == "g_box" || o.Body != null) return;
        if (_stage?.Has($"hit_{o.Name}.lua") != true) return;
        o.Hp = ScriptedObjectHp; // f_bit: HIPÓTESE (hit_f_bit.lua tira 50 por golpe e passa a SQ_DDMG abaixo de 50)
        // A caixa vai presa ao ator (o f_bit acompanha o chefe); o f_bit não bloqueia o jogador (camada 0, só marca o alvo).
        var body = new StaticBody3D { Position = new Vector3(0, 20, 0) };
        if (owned) body.CollisionLayer = 0;
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(14, 40, 14) } });
        o.Actor.Root.AddChild(body);
        o.Body = body;
    }

    // ATALHO DE DEPURAÇÃO (só no build Debug, não é do jogo): F9 deixa os chefes f_* com 1 de HP para testar a cena da derrota.
    void DebugWeakenBoss()
    {
        foreach (var e in _enemies.Where(e => !e.Dead && e.Kind.Model.StartsWith("f_", StringComparison.Ordinal)))
        {
            e.Hp = 1;
            ScriptLog($"[debug] F9: {e.Kind.Model} com 1 de HP");
        }
    }

    // ATALHO DE DEPURAÇÃO (só no build Debug): F8 leva o Guilmon para perto do próximo alvo vivo (colunas g_ com hit script,
    // depois o chefe f_*), virado para ele, e mantém o cursor do mouse sobre ele para o golpe mirar ali.
    Node3D? _debugAim;
    void DebugNextTarget()
    {
        var targets = _objects.Where(o => o.Body != null && !o.Dead && o.Hp > 0).Select(o => (Node3D)o.Body!)
            .Concat(_enemies.Where(e => !e.Dead && e.Kind.Model.StartsWith("f_", StringComparison.Ordinal)).Select(e => (Node3D)e.Body)).ToList();
        if (targets.Count == 0) { _debugAim = null; return; }
        var t = targets[0].GlobalPosition;
        // Do lado de fora da arena (as colunas ficam na borda do pedestal), na altura da base do alvo.
        var away = new Vector3(t.X, 0, t.Z);
        away = away.LengthSquared() > 1 ? away.Normalized() : Vector3.Right;
        float baseY = targets[0] is StaticBody3D ? t.Y - 20 : t.Y;
        _player.GlobalPosition = new Vector3(t.X, baseY + 2, t.Z) + away * (targets[0] is StaticBody3D ? 16 : 12);
        _player.Velocity = Vector3.Zero;
        _move.Face(MathF.Atan2(-away.X, -away.Z));
        _debugAim = targets[0];
        ScriptLog($"[debug] F8: alvo em {t}");
    }

    void DebugKeepAim()
    {
        if (_debugAim == null) return;
        if (!IsInstanceValid(_debugAim) || !_debugAim.IsInsideTree()) { _debugAim = null; return; }
        GetViewport().WarpMouse(_camera.UnprojectPosition(_debugAim.GlobalPosition));
    }

    // ---------- barra de HP do chefe (i_bosshp01 + METERCTRL bshp01) ----------

    ProgressBar? _bossBar;
    bool _bossBarOpen;

    bool DeliverToBossBar(BeMessage m)
    {
        // FATO (varredura 2026-10-25): cada chefe tem a sua barra, i_bosshp01..06 com o medidor bshp01..06 (stagbs02: i_bosshp02 e
        // "SETMODELHP f_belvam/I<0>"). Todas vão para a mesma barra do port.
        string? name = m.Name is { } n0 && n0.StartsWith("i_bosshp", StringComparison.Ordinal) ? "i_bosshp01" : m.Name is { } n1 && n1.StartsWith("bshp", StringComparison.Ordinal) ? "bshp01" : m.Name;
        switch (m.Class, name, m.Command)
        {
            case ("MODELCTRL", "i_bosshp01", "GENERATE"):
                ShowBossBar(true);
                return true;
            case ("MODELCTRL", "i_bosshp01", "SQ_CLOSE"):
                ShowBossBar(false);
                return true;
            case ("METERCTRL", "bshp01", "SETMODELHP"):
                // "f_apocaly/I<0>": a barra segue o HP desse modelo (o hit_*.lua também manda SETPERCENT a cada golpe).
                if (_bossBar != null) _bossBar.Value = 100;
                return true;
            case ("METERCTRL", "bshp01", "SETPERCENT") when m.Arg(0) is { Numbers.Length: > 0 } pc:
                if (_bossBar != null) _bossBar.Value = Math.Clamp(pc.Numbers[0], 0, 100);
                return true;
        }
        return false;
    }

    // DECISÃO DA FATIA: o ator i_bosshp01 ainda não foi exportado; a barra é uma ProgressBar no topo (V<0,205,0> = alto da tela).
    void ShowBossBar(bool open)
    {
        _bossBarOpen = open;
        if (_bossBar == null)
        {
            var layer = new CanvasLayer { Layer = 1 };
            _bossBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(420, 18) };
            _bossBar.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            _bossBar.Position = new Vector2(-210, 16);
            var fill = new StyleBoxFlat { BgColor = new Color(0.85f, 0.15f, 0.15f) };
            _bossBar.AddThemeStyleboxOverride("fill", fill);
            layer.AddChild(_bossBar);
            AddChild(layer);
        }
        _bossBar.Visible = open;
    }
}
