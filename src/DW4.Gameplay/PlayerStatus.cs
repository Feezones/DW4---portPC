namespace DW4.Gameplay;

/// <summary>
/// Estados do jogador que o hit_p_&lt;forma&gt;.lua liga com MDLsetSD (enum MsgAction do main.dol: SETPOISON 0, SETSLEEP 1,
/// SETSTUN 2, SETBIND 3, SETATTACKDOWN 4, SETPOWERDOWN 5, …, ALLCLEARSTATUS 14; tabela de casos 0x804EDE64).
/// FATO (0x803AF424..0x803AF8A0):
///  · SETPOISON "I&lt;t&gt;/I&lt;dano&gt;[/I&lt;gedoku&gt;]": contador = t / 60 + 1 (segundos), dano por tique em +0x100, GEDOKU em +0x104;
///  · SETSLEEP, SETSTUN e SETBIND "I&lt;t&gt;": quadros, guardados como estão (casas 8, 9 e 3 da lista de estados);
///  · SETATTACKDOWN "I&lt;t&gt;/I&lt;subayasa&gt;/I&lt;meichuritsu&gt;/…" (casa 1) e SETPOWERDOWN "I&lt;t&gt;/I&lt;tsuyosa&gt;/I&lt;mamori&gt;" (casa 2);
///  · a atualização (0x803AD074..) tira o dano do veneno do HP (sem passar de 0, SQ_DEAD no fim), atualiza hpmt/hpnm e põe
///    SQ_SLEP no sono e SQ_STUN no atordoamento; volta a SQ_WAIT quando acabam.
/// HIPÓTESES: o tique do veneno é 1 por segundo (o contador é em segundos); atordoado e dormindo não age, preso não anda; os
/// valores do ATTACKDOWN/POWERDOWN são quanto o atributo cai enquanto dura (DOWNDATA do RECALCPLAYER); GEDOKU não é usado aqui.
/// </summary>
public sealed class PlayerStatus
{
    public int PoisonSeconds { get; private set; }
    public int PoisonDamage { get; private set; }
    public int Gedoku { get; private set; }
    public int SleepFrames { get; private set; }
    public int StunFrames { get; private set; }
    public int BindFrames { get; private set; }
    public int AttackDownFrames { get; private set; }
    public int PowerDownFrames { get; private set; }
    public int SpeedDown { get; private set; }
    public int AccuracyDown { get; private set; }
    public int StrengthDown { get; private set; }
    public int DefenseDown { get; private set; }
    float _poisonClock;

    public bool CanAct => SleepFrames <= 0 && StunFrames <= 0;
    public bool CanMove => CanAct && BindFrames <= 0;
    public bool Poisoned => PoisonSeconds > 0;
    public bool AttackDown => AttackDownFrames > 0;
    public bool PowerDown => PowerDownFrames > 0;

    /// <summary>
    /// Jogador com as quedas de atributo em vigor (RECALCPLAYER: UPDATA − DOWNDATA, total entre 1 e 9999). HIPÓTESE: os valores do
    /// SETATTACKDOWN/SETPOWERDOWN são o DOWNDATA de SUBAYASA/MEICHURITSU e TSUYOSA/MAMORI.
    /// </summary>
    public Fighter Lowered(Fighter f) => SpeedDown == 0 && AccuracyDown == 0 && StrengthDown == 0 && DefenseDown == 0 ? f : f with
    {
        Subayasa = Math.Max(1, f.Subayasa - SpeedDown),
        Meichuritsu = Math.Max(0, f.Meichuritsu - AccuracyDown),
        Tsuyosa = Math.Max(1, f.Tsuyosa - StrengthDown),
        Mamori = Math.Max(1, f.Mamori - DefenseDown),
    };

    /// <summary>Comando do MDLsetSD com os inteiros dos argumentos. false se não é um estado.</summary>
    public bool Apply(string command, IReadOnlyList<int> a)
    {
        int A(int i) => i < a.Count ? a[i] : 0;
        switch (command)
        {
            case "SETPOISON":
                PoisonSeconds = A(0) / 60 + 1;
                PoisonDamage = A(1);
                if (a.Count == 3) Gedoku = A(2);
                _poisonClock = 0;
                return true;
            case "SETSLEEP": SleepFrames = A(0); return true;
            case "SETSTUN": StunFrames = A(0); return true;
            case "SETBIND": BindFrames = A(0); return true;
            case "SETATTACKDOWN": AttackDownFrames = A(0); SpeedDown = A(1); AccuracyDown = A(2); return true;
            case "SETPOWERDOWN": PowerDownFrames = A(0); StrengthDown = A(1); DefenseDown = A(2); return true;
            case "CLEARSTATUS" or "ALLCLEARSTATUS": Clear(); return true;
            default: return false;
        }
    }

    public void Clear()
    {
        PoisonSeconds = PoisonDamage = Gedoku = SleepFrames = StunFrames = BindFrames = 0;
        AttackDownFrames = PowerDownFrames = SpeedDown = AccuracyDown = StrengthDown = DefenseDown = 0;
        _poisonClock = 0;
    }

    /// <summary>Avança <paramref name="frames"/> quadros de 60 Hz. Devolve o dano de veneno deste passo (0 sem tique).</summary>
    public int Tick(float frames)
    {
        int Down(int v) => Math.Max(0, v - (int)MathF.Ceiling(frames));
        SleepFrames = Down(SleepFrames);
        StunFrames = Down(StunFrames);
        BindFrames = Down(BindFrames);
        AttackDownFrames = Down(AttackDownFrames);
        PowerDownFrames = Down(PowerDownFrames);
        if (AttackDownFrames == 0) SpeedDown = AccuracyDown = 0;
        if (PowerDownFrames == 0) StrengthDown = DefenseDown = 0;
        if (PoisonSeconds <= 0) return 0;
        _poisonClock += frames;
        int damage = 0;
        while (_poisonClock >= 60 && PoisonSeconds > 0)
        {
            _poisonClock -= 60;
            PoisonSeconds--;
            damage += PoisonDamage;
        }
        if (PoisonSeconds == 0) PoisonDamage = 0;
        return damage;
    }
}
