namespace DW4.Gameplay;

/// <summary>Estado salvo de uma forma (BASE_*[d]): liberada (BASE_ISUSE), nível, EXP, as 9 skills e os 11 UPCNT.</summary>
public sealed class FormState
{
    public bool Unlocked { get; set; }
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int[] Skill { get; } = Enumerable.Repeat(1, 9).ToArray();
    public int[] UpCount { get; } = new int[11];
}

/// <summary>
/// Digievolução, lida por desmontagem estática em 2026-10-24 (docs/gameplay/fase4-levantamento-evolucao.md).
/// FATO: cada uma das 16 formas (linhas da shinka = DIGIMONNO) guarda o próprio nível, EXP, skills e UPCNT; o script
/// libera a forma (SYSctlShinka "SETSHINKA", 0x802EC590) e a troca no menu (0x8039CA4C) salva a atual (CURRENT2BASE),
/// carrega a nova (BASE2CURRENT), desequipa armaduras e chips e re-escolhe a arma. Sem custo nem duração no código lido.
/// Começo do jogo (GAMEDATAINIT): todas as formas no nível 1 com skills 1; as 4 iniciais (0..3) são as escolhíveis.
/// </summary>
public sealed class Evolution
{
    public const int Forms = 16;
    readonly FormState[] _forms = Enumerable.Range(0, Forms).Select(_ => new FormState()).ToArray();

    public FormState this[int digimonNo] => _forms[digimonNo];

    /// <summary>Forma atual (DIGIMONNO) e o estado dela em uso (LEVEL, EXP, SKILL, UPCNT do registro do jogador).</summary>
    public int Current { get; private set; } = -1;

    /// <summary>Começa com a inicial escolhida, liberada.</summary>
    public void Start(int digimonNo)
    {
        _forms[digimonNo].Unlocked = true;
        Current = digimonNo;
    }

    /// <summary>SETSHINKA: libera a forma; true se ela ainda não estava liberada.</summary>
    public bool Unlock(int digimonNo)
    {
        if (_forms[digimonNo].Unlocked) return false;
        _forms[digimonNo].Unlocked = true;
        return true;
    }

    /// <summary>GETEXP do script: EXP da forma, ou −1 se ela não está liberada (usado para achar a inicial mais usada).</summary>
    public long ExpOf(int digimonNo) => _forms[digimonNo].Unlocked ? _forms[digimonNo].Exp : -1;

    /// <summary>
    /// Troca no menu (só formas liberadas). O chamador já manteve o estado da forma atual em <see cref="this"/> (é o mesmo
    /// objeto, o que equivale ao CURRENT2BASE); depois da troca ele deve refazer o RECALC, tirar armaduras e chips e
    /// escolher a arma mais forte usável pela nova forma.
    /// </summary>
    public bool Switch(int digimonNo)
    {
        if (!_forms[digimonNo].Unlocked || digimonNo == Current) return false;
        Current = digimonNo;
        return true;
    }

    /// <summary>
    /// WEAPONEQUIPSTRONG (HIPÓTESE: a mais forte por TSUYOSA entre as equipáveis): linha da arma escolhida, ou −1.
    /// </summary>
    public static int StrongestWeapon(GameTables db, IEnumerable<int> weaponRows, int digimonNo, IReadOnlyList<int> skill) =>
        weaponRows.Where(r => db.CanEquipWeapon(r, digimonNo, skill)).OrderByDescending(r => db["weapon"].Int(r, "TSUYOSA")).DefaultIfEmpty(-1).First();
}
