using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Golpe inimigo no jogador pelo hit_p_&lt;forma&gt;.lua do jogo (hit_p_agumon, hit_p_girumon…), como nos chefes. FATO pelo script:
/// golpes 70126..70133 do próprio jogador (ramNo 0..3) não contam; 80000..89999 → BLPcalcKoukaMagic; em SQ_GUARD ou sem
/// BLPisHitCheck → SQ_GUARDR; dano 0 → só estados (BLPgetStunTime(0,0), Sleep, Bind, AttackDown, PowerDown, nessa ordem,
/// o primeiro que pega); com dano → MDLaddHp(−dano), MDLdspDamageP, BLPgetDamageRank(HP, dano) → SETLABEL "SQ_xDMG/&lt;atributo&gt;" e
/// MDLsetSeq, e fora da faixa 3 os estados atordoar(dano, faixa), prender e veneno ("TIME", "DAMAGE", "GEDOKU").
/// Respostas BLP* = casos E2P* do FIGHTCALC (DamageFormula/StatusFormula). Estados em <see cref="PlayerStatus"/>.
/// Sem o script na fase, fica o cálculo direto de antes (EnemyStrikes).
/// </summary>
public partial class Fatia
{
    sealed class PlayerBeModel(Fatia f) : IBeModel
    {
        public int Index => 0;
        public int Hp { get => f._hp; set => f._hp = f._pf != null ? Math.Clamp(value, 0, f._pf.MaxHp) : value; }
        public int BulletKind => f._playerHitCode;
        public int MaxHp => f._pf?.MaxHp ?? 0;
        public string ModelName => Fatia.PlayerModel;
        public string? SequenceName => f._runner?.Running == true ? f._runner.Sequence : "SQ_WAIT";
        public void SetSequence(string name) => f.PlayerHitSequence(name);
    }

    readonly PlayerStatus _status = new();
    // Jogador para as contas de luta, com as quedas de lentidão/enfraquecer (PlayerStatus.Lowered).
    Fighter? Pf => _pf == null ? null : _status.Lowered(_pf);
    // Golpe que o hit_p_*.lua está resolvendo: código, atacante e o dano já sorteado (BLPgetDamageValue).
    int _playerHitCode;
    Fighter? _playerHitBy;
    int _playerHitDamage;
    bool _playerHitActive;
    string _playerHitWho = "";
    Vector3 _playerHitFrom;
    readonly Dictionary<string, string> _playerLabels = [];

    bool UsesPlayerHitScript => _stage?.Has($"hit_{PlayerModel}.lua") == true && _db != null && _pf != null;

    /// <summary>Roda o hit_p da forma para o golpe <paramref name="code"/>. false sem script (quem chama faz o cálculo direto).</summary>
    bool PlayerHitScript(int code, Fighter? attacker, string who, Vector3 from)
    {
        if (!UsesPlayerHitScript) return false;
        _playerHitCode = code;
        _playerHitBy = attacker ?? _pf;
        _playerHitDamage = 0;
        _playerHitWho = who;
        _playerHitFrom = from;
        _playerHitActive = true;
        try { _stage!.RunAs($"hit_{PlayerModel}.lua", new PlayerBeModel(this)); }
        finally { _playerHitActive = false; }
        return true;
    }

    // MDLsetSeq do script no jogador: bloco do SETLABEL (ex. "SQ_SDMG/common"), senão o do estilo da arma, senão "common".
    void PlayerHitSequence(string seq)
    {
        if (_runner == null) return;
        bool started = (_playerLabels.TryGetValue(seq, out var block) && _runner.Start(seq, block)) || _runner.Start(seq, StyleOf(seq)) || _runner.Start(seq, "common");
        if (!started) return;
        _wazaPending = null; // interrompida: o MP já foi gasto no painel
        var to = _playerHitFrom - _player.GlobalPosition;
        _move.Stop();
        if (to.LengthSquared() > 1e-4f) _move.Face(MathF.Atan2(to.X, to.Z));
    }

    // MDLsetSD do script no próprio jogador (MODELCTRL <forma> SETSTUN "I<t>"…, SETLABEL "SQ_SDMG/common").
    bool DeliverToPlayerStatus(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name != PlayerModel) return false;
        if (m.Command == "SETLABEL" && m.RawArgs?.Split('/') is { Length: 2 } l) { _playerLabels[l[0]] = l[1]; return true; }
        var ints = m.Args.Where(a => a.Kind == 'I' && a.Numbers.Length > 0).Select(a => (int)a.Numbers[0]).ToList();
        if (!_status.Apply(m.Command, ints)) return false;
        ScriptLog($"estado: {m.Command} {string.Join("/", ints)}");
        Float(_player, m.Command[3..], new Color(0.8f, 0.5f, 1f));
        return true;
    }

    // Respostas BLP* do golpe atual (null = não é golpe no jogador).
    double? PlayerFight(string method, double[] args)
    {
        if (!_playerHitActive || _db == null || _pf == null || _playerHitBy == null) return null;
        var ef = _playerHitBy;
        var atk = _db.EnemyAttackRow(_playerHitCode);
        var eff = _db.EnemyEffectRow(_playerHitCode);
        string Arg(int i) => i < args.Length ? args[i].ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        switch (method)
        {
            case "BLPisHitCheck":
                bool hit = DamageFormula.EnemyHits(ef, atk, Pf!, _mt);
                if (!hit) Float(_player, "MISS", Colors.LightGray);
                return hit ? 1 : 0;
            case "BLPgetDamageValue":
                return _playerHitDamage = DamageFormula.EnemyDamage(ef, atk, Pf!, new StatusMods(), _mt).Damage;
            // FATO (hit_p_*.lua): BLPgetDamageRank(MDLgetHp(), dano).
            case "BLPgetDamageRank":
                return DamageFormula.Rank((int)args.ElementAtOrDefault(1), (int)args.ElementAtOrDefault(0), _pf.MaxHp);
            case "BLPgetStunTime":
                return StatusFormula.EnemyStun(ef, eff, Pf!, _pf.Mahiteikou, (int)args.ElementAtOrDefault(0), (int)args.ElementAtOrDefault(1), new StatusMods(), _mt) ?? 0;
            case "BLPgetSleepTime": return StatusFormula.EnemyDisable(ef, eff, Pf!, "sleep", _mt) ?? 0;
            case "BLPgetBindTime": return StatusFormula.EnemyDisable(ef, eff, Pf!, "ashidome", _mt) ?? 0;
            case "BLPcalcKoukaMagic": return 0; // magia de apoio 80000..89999: não lida
            case "MDLdspDamageP":
                int dmg = (int)args.ElementAtOrDefault(0);
                Damage(_player, dmg, onPlayer: true, dmg.ToString(), Colors.Red);
                _lastEvent = $"{_playerHitWho}: {dmg} (eneatk {_playerHitCode % 10000})";
                return 0;
        }
        return null;
    }

    // BLP* com argumento de texto ("TIME", "DAMAGE", "GEDOKU", "SUBAYASA"…): o BeHost passa o nome do campo.
    double? PlayerFightField(string method, string field)
    {
        if (!_playerHitActive || _db == null || _pf == null || _playerHitBy == null) return null;
        var ef = _playerHitBy;
        var eff = _db.EnemyEffectRow(_playerHitCode);
        return (method, field) switch
        {
            ("BLPgetPoisonTime", "TIME") => StatusFormula.EnemyPoison(eff, _pf.Dokuteikou, _mt) ?? 0,
            ("BLPgetPoisonTime", "DAMAGE") => StatusFormula.EnemyPoisonTick(eff, _pf),
            ("BLPgetPoisonTime", "GEDOKU") => eff.Gedoku,
            ("BLPgetAttackDownTime", "TIME") => StatusFormula.EnemyDisable(ef, eff, Pf!, "freeze", _mt) ?? 0,
            ("BLPgetAttackDownTime", "SUBAYASA") => StatusFormula.EnemyStatDown(ef, eff, _pf, _pf.Subayasa),
            ("BLPgetAttackDownTime", "MEICHURITSU") => StatusFormula.EnemyStatDown(ef, eff, _pf, _pf.Meichuritsu),
            ("BLPgetPowerDownTime", "TIME") => StatusFormula.EnemyDisable(ef, eff, Pf!, "powerdown", _mt) ?? 0,
            ("BLPgetPowerDownTime", "TSUYOSA") => StatusFormula.EnemyStatDown(ef, eff, _pf, _pf.Tsuyosa),
            ("BLPgetPowerDownTime", "MAMORI") => StatusFormula.EnemyStatDown(ef, eff, _pf, _pf.Mamori),
            _ => null,
        };
    }

    // Veneno e tempos dos estados a cada quadro (HIPÓTESE: 1 tique de veneno por segundo).
    void TickPlayerStatus(float dt)
    {
        int poison = _status.Tick(dt * 60);
        if (poison <= 0 || _pf == null || _hp <= 0) return;
        _hp = Math.Max(0, _hp - poison);
        Damage(_player, poison, onPlayer: true, poison.ToString(), new Color(0.7f, 0.3f, 1f));
    }
}
