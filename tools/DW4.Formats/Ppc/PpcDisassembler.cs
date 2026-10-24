using System.Globalization;

namespace DW4.Formats.Ppc;

/// <summary>
/// Desmontador PowerPC (Gekko, 32 bits, big-endian) só para leitura estática do main.dol. Cobre as instruções inteiras,
/// de desvio, de ponto flutuante e paired single mais comuns no código de jogo; o resto sai como ".word". Os mnemônicos
/// seguem o manual PowerPC (IBM "PowerPC Microprocessor Family: The Programming Environments") e o do Gekko.
/// </summary>
public static class PpcDisassembler
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Texto de uma instrução no endereço <paramref name="pc"/> (para resolver desvios relativos).</summary>
    public static string Disassemble(uint w, uint pc)
    {
        int op = (int)(w >> 26);
        int rd = (int)(w >> 21) & 31, ra = (int)(w >> 16) & 31, rb = (int)(w >> 11) & 31, rc = (int)(w >> 6) & 31;
        short simm = (short)(w & 0xFFFF);
        ushort uimm = (ushort)(w & 0xFFFF);
        bool rcBit = (w & 1) != 0;
        string R(int r) => r == 1 ? "sp" : $"r{r}";
        string F(int r) => $"f{r}";
        string H(int v) => v < 0 ? $"-0x{-v:X}" : $"0x{v:X}";
        string D(int r) => $"{H(simm)}({R(r)})";
        string Dot(string m) => rcBit ? m + "." : m;

        switch (op)
        {
            case 3: return $"twi {rd}, {R(ra)}, {H(simm)}";
            case 4: return PairedSingle(w, rd, ra, rb, rc);
            case 7: return $"mulli {R(rd)}, {R(ra)}, {H(simm)}";
            case 8: return $"subfic {R(rd)}, {R(ra)}, {H(simm)}";
            case 10: return $"cmplwi {(rd >> 2 == 0 ? "" : $"cr{rd >> 2}, ")}{R(ra)}, 0x{uimm:X}";
            case 11: return $"cmpwi {(rd >> 2 == 0 ? "" : $"cr{rd >> 2}, ")}{R(ra)}, {H(simm)}";
            case 12: return $"addic {R(rd)}, {R(ra)}, {H(simm)}";
            case 13: return $"addic. {R(rd)}, {R(ra)}, {H(simm)}";
            case 14: return ra == 0 ? $"li {R(rd)}, {H(simm)}" : $"addi {R(rd)}, {R(ra)}, {H(simm)}";
            case 15: return ra == 0 ? $"lis {R(rd)}, 0x{uimm:X}" : $"addis {R(rd)}, {R(ra)}, {H(simm)}";
            case 16:
            {
                int bo = rd, bi = ra;
                int bd = (short)(w & 0xFFFC);
                uint target = (w & 2) != 0 ? (uint)bd : (uint)(pc + bd);
                return BranchCond(bo, bi, (w & 1) != 0 ? "l" : "", $"0x{target:X8}");
            }
            case 17: return "sc";
            case 18:
            {
                int li = (int)(w & 0x03FFFFFC);
                if ((li & 0x02000000) != 0) li -= 0x04000000;
                uint target = (w & 2) != 0 ? (uint)li : (uint)(pc + li);
                return $"b{((w & 1) != 0 ? "l" : "")} 0x{target:X8}";
            }
            case 19: return Op19(w, rd, ra, rb);
            case 20: return Dot($"rlwimi {R(ra)}, {R(rd)}, {rb}, {rc}, {(w >> 1) & 31}");
            case 21:
            {
                int mb = rc, me = (int)(w >> 1) & 31;
                if (mb == 0 && me == 31 - rb) return Dot($"slwi {R(ra)}, {R(rd)}, {rb}");
                if (me == 31 && rb != 0 && mb == 32 - rb) return Dot($"srwi {R(ra)}, {R(rd)}, {mb}");
                if (rb == 0 && me == 31) return Dot($"clrlwi {R(ra)}, {R(rd)}, {mb}");
                return Dot($"rlwinm {R(ra)}, {R(rd)}, {rb}, {mb}, {me}");
            }
            case 23: return Dot($"rlwnm {R(ra)}, {R(rd)}, {R(rb)}, {rc}, {(w >> 1) & 31}");
            case 24: return w == 0x60000000 ? "nop" : $"ori {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 25: return $"oris {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 26: return $"xori {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 27: return $"xoris {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 28: return $"andi. {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 29: return $"andis. {R(ra)}, {R(rd)}, 0x{uimm:X}";
            case 31: return Op31(w, rd, ra, rb);
            case 32: return $"lwz {R(rd)}, {D(ra)}";
            case 33: return $"lwzu {R(rd)}, {D(ra)}";
            case 34: return $"lbz {R(rd)}, {D(ra)}";
            case 35: return $"lbzu {R(rd)}, {D(ra)}";
            case 36: return $"stw {R(rd)}, {D(ra)}";
            case 37: return $"stwu {R(rd)}, {D(ra)}";
            case 38: return $"stb {R(rd)}, {D(ra)}";
            case 39: return $"stbu {R(rd)}, {D(ra)}";
            case 40: return $"lhz {R(rd)}, {D(ra)}";
            case 41: return $"lhzu {R(rd)}, {D(ra)}";
            case 42: return $"lha {R(rd)}, {D(ra)}";
            case 43: return $"lhau {R(rd)}, {D(ra)}";
            case 44: return $"sth {R(rd)}, {D(ra)}";
            case 45: return $"sthu {R(rd)}, {D(ra)}";
            case 46: return $"lmw {R(rd)}, {D(ra)}";
            case 47: return $"stmw {R(rd)}, {D(ra)}";
            case 48: return $"lfs {F(rd)}, {D(ra)}";
            case 49: return $"lfsu {F(rd)}, {D(ra)}";
            case 50: return $"lfd {F(rd)}, {D(ra)}";
            case 51: return $"lfdu {F(rd)}, {D(ra)}";
            case 52: return $"stfs {F(rd)}, {D(ra)}";
            case 53: return $"stfsu {F(rd)}, {D(ra)}";
            case 54: return $"stfd {F(rd)}, {D(ra)}";
            case 55: return $"stfdu {F(rd)}, {D(ra)}";
            case 56: return $"psq_l {F(rd)}, {H((short)((w & 0xFFF) << 4) >> 4)}({R(ra)}), {(w >> 15) & 1}, qr{(w >> 12) & 7}";
            case 57: return $"psq_lu {F(rd)}, {H((short)((w & 0xFFF) << 4) >> 4)}({R(ra)}), {(w >> 15) & 1}, qr{(w >> 12) & 7}";
            case 59: return Op59(w, rd, ra, rb, rc);
            case 60: return $"psq_st {F(rd)}, {H((short)((w & 0xFFF) << 4) >> 4)}({R(ra)}), {(w >> 15) & 1}, qr{(w >> 12) & 7}";
            case 61: return $"psq_stu {F(rd)}, {H((short)((w & 0xFFF) << 4) >> 4)}({R(ra)}), {(w >> 15) & 1}, qr{(w >> 12) & 7}";
            case 63: return Op63(w, rd, ra, rb, rc);
        }
        return Word(w);
    }

    static string Word(uint w) => $".word 0x{w:X8}";

    static string BranchCond(int bo, int bi, string suffix, string? target)
    {
        string m;
        if ((bo & 0x14) == 0x14) m = "b";
        else if ((bo & 0x04) == 0) m = (bo & 0x02) != 0 ? "bdz" : "bdnz";
        else
        {
            string c = new[] { "lt", "gt", "eq", "so" }[bi & 3];
            m = "b" + ((bo & 0x08) != 0 ? c : c switch { "lt" => "ge", "gt" => "le", "eq" => "ne", _ => "ns" });
        }
        var ops = new List<string>();
        if ((bo & 0x10) == 0 && bi >> 2 != 0) ops.Add($"cr{bi >> 2}");
        if (target != null) ops.Add(target);
        return m + suffix + (ops.Count > 0 ? " " + string.Join(", ", ops) : "");
    }

    static string Op19(uint w, int bo, int bi, int rb)
    {
        int xo = (int)(w >> 1) & 0x3FF;
        bool lk = (w & 1) != 0;
        return xo switch
        {
            16 => BranchCond(bo, bi, lk ? "lrl" : "lr", null),
            528 => BranchCond(bo, bi, lk ? "ctrl" : "ctr", null),
            0 => $"mcrf cr{bo >> 2}, cr{bi >> 2}",
            50 => "rfi",
            150 => "isync",
            193 => $"crxor {bo}, {bi}, {rb}",
            449 => $"cror {bo}, {bi}, {rb}",
            289 => $"creqv {bo}, {bi}, {rb}",
            129 => $"crandc {bo}, {bi}, {rb}",
            257 => $"crand {bo}, {bi}, {rb}",
            33 => $"crnor {bo}, {bi}, {rb}",
            _ => Word(w),
        };
    }

    static string Op31(uint w, int rd, int ra, int rb)
    {
        int xo = (int)(w >> 1) & 0x3FF;
        bool rc = (w & 1) != 0;
        string R(int r) => r == 1 ? "sp" : $"r{r}";
        string Dot(string m) => rc ? m + "." : m;
        string Idx(string m) => $"{m} {R(rd)}, {(ra == 0 ? "0" : R(ra))}, {R(rb)}";
        string FIdx(string m) => $"{m} f{rd}, {(ra == 0 ? "0" : R(ra))}, {R(rb)}";
        int spr = ((rb << 5) | ra);
        string? Spr(int s) => s switch { 1 => "xer", 8 => "lr", 9 => "ctr", _ => null };
        // Formas XO (bit 10 = OE): usar os 9 bits baixos
        switch (xo & 0x1FF)
        {
            case 266: return Dot($"add {R(rd)}, {R(ra)}, {R(rb)}");
            case 10: return Dot($"addc {R(rd)}, {R(ra)}, {R(rb)}");
            case 138: return Dot($"adde {R(rd)}, {R(ra)}, {R(rb)}");
            case 202: return Dot($"addze {R(rd)}, {R(ra)}");
            case 234: return Dot($"addme {R(rd)}, {R(ra)}");
            case 40: return Dot($"subf {R(rd)}, {R(ra)}, {R(rb)}");
            case 8: return Dot($"subfc {R(rd)}, {R(ra)}, {R(rb)}");
            case 136: return Dot($"subfe {R(rd)}, {R(ra)}, {R(rb)}");
            case 200: return Dot($"subfze {R(rd)}, {R(ra)}");
            case 104: return Dot($"neg {R(rd)}, {R(ra)}");
            case 235: return Dot($"mullw {R(rd)}, {R(ra)}, {R(rb)}");
            case 75: return Dot($"mulhw {R(rd)}, {R(ra)}, {R(rb)}");
            case 11: return Dot($"mulhwu {R(rd)}, {R(ra)}, {R(rb)}");
            case 491: return Dot($"divw {R(rd)}, {R(ra)}, {R(rb)}");
            case 459: return Dot($"divwu {R(rd)}, {R(ra)}, {R(rb)}");
        }
        return xo switch
        {
            0 => $"cmpw {(rd >> 2 == 0 ? "" : $"cr{rd >> 2}, ")}{R(ra)}, {R(rb)}",
            32 => $"cmplw {(rd >> 2 == 0 ? "" : $"cr{rd >> 2}, ")}{R(ra)}, {R(rb)}",
            4 => $"tw {rd}, {R(ra)}, {R(rb)}",
            19 => $"mfcr {R(rd)}",
            83 => $"mfmsr {R(rd)}",
            144 => $"mtcrf 0x{(w >> 12) & 0xFF:X}, {R(rd)}",
            146 => $"mtmsr {R(rd)}",
            339 => Spr(spr) is { } n ? $"mf{n} {R(rd)}" : $"mfspr {R(rd)}, {spr}",
            467 => Spr(spr) is { } n ? $"mt{n} {R(rd)}" : $"mtspr {spr}, {R(rd)}",
            371 => $"mftb {R(rd)}",
            24 => Dot($"slw {R(ra)}, {R(rd)}, {R(rb)}"),
            536 => Dot($"srw {R(ra)}, {R(rd)}, {R(rb)}"),
            792 => Dot($"sraw {R(ra)}, {R(rd)}, {R(rb)}"),
            824 => Dot($"srawi {R(ra)}, {R(rd)}, {rb}"),
            26 => Dot($"cntlzw {R(ra)}, {R(rd)}"),
            28 => Dot($"and {R(ra)}, {R(rd)}, {R(rb)}"),
            60 => Dot($"andc {R(ra)}, {R(rd)}, {R(rb)}"),
            124 => Dot($"nor {R(ra)}, {R(rd)}, {R(rb)}"),
            284 => Dot($"eqv {R(ra)}, {R(rd)}, {R(rb)}"),
            316 => Dot($"xor {R(ra)}, {R(rd)}, {R(rb)}"),
            412 => Dot($"orc {R(ra)}, {R(rd)}, {R(rb)}"),
            444 => rd == rb ? Dot($"mr {R(ra)}, {R(rd)}") : Dot($"or {R(ra)}, {R(rd)}, {R(rb)}"),
            476 => Dot($"nand {R(ra)}, {R(rd)}, {R(rb)}"),
            922 => Dot($"extsh {R(ra)}, {R(rd)}"),
            954 => Dot($"extsb {R(ra)}, {R(rd)}"),
            23 => Idx("lwzx"),
            55 => Idx("lwzux"),
            87 => Idx("lbzx"),
            119 => Idx("lbzux"),
            151 => Idx("stwx"),
            183 => Idx("stwux"),
            215 => Idx("stbx"),
            247 => Idx("stbux"),
            279 => Idx("lhzx"),
            343 => Idx("lhax"),
            407 => Idx("sthx"),
            534 => Idx("lwbrx"),
            662 => Idx("stwbrx"),
            790 => Idx("lhbrx"),
            535 => FIdx("lfsx"),
            567 => FIdx("lfsux"),
            599 => FIdx("lfdx"),
            663 => FIdx("stfsx"),
            727 => FIdx("stfdx"),
            983 => FIdx("stfiwx"),
            20 => Idx("lwarx"),
            150 => Idx("stwcx."),
            86 => $"dcbf {(ra == 0 ? "0" : R(ra))}, {R(rb)}",
            54 => $"dcbst {(ra == 0 ? "0" : R(ra))}, {R(rb)}",
            470 => $"dcbi {(ra == 0 ? "0" : R(ra))}, {R(rb)}",
            1014 => $"dcbz {(ra == 0 ? "0" : R(ra))}, {R(rb)}",
            982 => $"icbi {(ra == 0 ? "0" : R(ra))}, {R(rb)}",
            598 => "sync",
            854 => "eieio",
            _ => Word(w),
        };
    }

    static string Op59(uint w, int fd, int fa, int fb, int fc)
    {
        bool rc = (w & 1) != 0;
        string Dot(string m) => rc ? m + "." : m;
        return ((w >> 1) & 31) switch
        {
            18 => Dot($"fdivs f{fd}, f{fa}, f{fb}"),
            20 => Dot($"fsubs f{fd}, f{fa}, f{fb}"),
            21 => Dot($"fadds f{fd}, f{fa}, f{fb}"),
            24 => Dot($"fres f{fd}, f{fb}"),
            25 => Dot($"fmuls f{fd}, f{fa}, f{fc}"),
            28 => Dot($"fmsubs f{fd}, f{fa}, f{fc}, f{fb}"),
            29 => Dot($"fmadds f{fd}, f{fa}, f{fc}, f{fb}"),
            30 => Dot($"fnmsubs f{fd}, f{fa}, f{fc}, f{fb}"),
            31 => Dot($"fnmadds f{fd}, f{fa}, f{fc}, f{fb}"),
            _ => Word(w),
        };
    }

    static string Op63(uint w, int fd, int fa, int fb, int fc)
    {
        bool rc = (w & 1) != 0;
        string Dot(string m) => rc ? m + "." : m;
        switch ((w >> 1) & 31)
        {
            case 18: return Dot($"fdiv f{fd}, f{fa}, f{fb}");
            case 20: return Dot($"fsub f{fd}, f{fa}, f{fb}");
            case 21: return Dot($"fadd f{fd}, f{fa}, f{fb}");
            case 23: return Dot($"fsel f{fd}, f{fa}, f{fc}, f{fb}");
            case 25: return Dot($"fmul f{fd}, f{fa}, f{fc}");
            case 26: return Dot($"frsqrte f{fd}, f{fb}");
            case 28: return Dot($"fmsub f{fd}, f{fa}, f{fc}, f{fb}");
            case 29: return Dot($"fmadd f{fd}, f{fa}, f{fc}, f{fb}");
            case 30: return Dot($"fnmsub f{fd}, f{fa}, f{fc}, f{fb}");
            case 31: return Dot($"fnmadd f{fd}, f{fa}, f{fc}, f{fb}");
        }
        return ((w >> 1) & 0x3FF) switch
        {
            0 => $"fcmpu cr{fd >> 2}, f{fa}, f{fb}",
            32 => $"fcmpo cr{fd >> 2}, f{fa}, f{fb}",
            12 => Dot($"frsp f{fd}, f{fb}"),
            14 => Dot($"fctiw f{fd}, f{fb}"),
            15 => Dot($"fctiwz f{fd}, f{fb}"),
            40 => Dot($"fneg f{fd}, f{fb}"),
            72 => Dot($"fmr f{fd}, f{fb}"),
            136 => Dot($"fnabs f{fd}, f{fb}"),
            264 => Dot($"fabs f{fd}, f{fb}"),
            583 => Dot($"mffs f{fd}"),
            711 => Dot($"mtfsf 0x{(w >> 17) & 0xFF:X}, f{fb}"),
            38 => Dot($"mtfsb1 {fd}"),
            70 => Dot($"mtfsb0 {fd}"),
            _ => Word(w),
        };
    }

    static string PairedSingle(uint w, int fd, int fa, int fb, int fc)
    {
        bool rc = (w & 1) != 0;
        string Dot(string m) => rc ? m + "." : m;
        switch ((w >> 1) & 31)
        {
            case 10: return Dot($"ps_sum0 f{fd}, f{fa}, f{fc}, f{fb}");
            case 11: return Dot($"ps_sum1 f{fd}, f{fa}, f{fc}, f{fb}");
            case 12: return Dot($"ps_muls0 f{fd}, f{fa}, f{fc}");
            case 13: return Dot($"ps_muls1 f{fd}, f{fa}, f{fc}");
            case 14: return Dot($"ps_madds0 f{fd}, f{fa}, f{fc}, f{fb}");
            case 15: return Dot($"ps_madds1 f{fd}, f{fa}, f{fc}, f{fb}");
            case 18: return Dot($"ps_div f{fd}, f{fa}, f{fb}");
            case 20: return Dot($"ps_sub f{fd}, f{fa}, f{fb}");
            case 21: return Dot($"ps_add f{fd}, f{fa}, f{fb}");
            case 23: return Dot($"ps_sel f{fd}, f{fa}, f{fc}, f{fb}");
            case 24: return Dot($"ps_res f{fd}, f{fb}");
            case 25: return Dot($"ps_mul f{fd}, f{fa}, f{fc}");
            case 26: return Dot($"ps_rsqrte f{fd}, f{fb}");
            case 28: return Dot($"ps_msub f{fd}, f{fa}, f{fc}, f{fb}");
            case 29: return Dot($"ps_madd f{fd}, f{fa}, f{fc}, f{fb}");
            case 30: return Dot($"ps_nmsub f{fd}, f{fa}, f{fc}, f{fb}");
            case 31: return Dot($"ps_nmadd f{fd}, f{fa}, f{fc}, f{fb}");
        }
        return ((w >> 1) & 0x3FF) switch
        {
            0 => $"ps_cmpu0 cr{fd >> 2}, f{fa}, f{fb}",
            32 => $"ps_cmpo0 cr{fd >> 2}, f{fa}, f{fb}",
            64 => $"ps_cmpu1 cr{fd >> 2}, f{fa}, f{fb}",
            96 => $"ps_cmpo1 cr{fd >> 2}, f{fa}, f{fb}",
            40 => Dot($"ps_neg f{fd}, f{fb}"),
            72 => Dot($"ps_mr f{fd}, f{fb}"),
            136 => Dot($"ps_nabs f{fd}, f{fb}"),
            264 => Dot($"ps_abs f{fd}, f{fb}"),
            528 => Dot($"ps_merge00 f{fd}, f{fa}, f{fb}"),
            560 => Dot($"ps_merge01 f{fd}, f{fa}, f{fb}"),
            592 => Dot($"ps_merge10 f{fd}, f{fa}, f{fb}"),
            624 => Dot($"ps_merge11 f{fd}, f{fa}, f{fb}"),
            1014 => $"dcbz_l {fa}, {fb}",
            _ => (w >> 1 & 0x3F) switch
            {
                6 => $"psq_lx f{fd}, r{fa}, r{fb}, {(w >> 10) & 1}, qr{(w >> 7) & 7}",
                7 => $"psq_stx f{fd}, r{fa}, r{fb}, {(w >> 10) & 1}, qr{(w >> 7) & 7}",
                _ => Word(w),
            },
        };
    }

    /// <summary>Desmonta <paramref name="count"/> instruções a partir de <paramref name="address"/>, com anotação de strings e floats.</summary>
    public static IEnumerable<string> Listing(DolFile dol, uint address, int count)
    {
        var hi = new Dictionary<int, uint>();
        for (int i = 0; i < count; i++)
        {
            uint pc = address + (uint)(i * 4);
            if (!dol.TryWord(pc, out uint w)) yield break;
            string text = Disassemble(w, pc);
            int op = (int)(w >> 26), rd = (int)(w >> 21) & 31, ra = (int)(w >> 16) & 31;
            short imm = (short)(w & 0xFFFF);
            string note = "";
            if (op == 15 && ra == 0) hi[rd] = (uint)(ushort)imm << 16;
            else if (hi.TryGetValue(ra, out uint h) && op is 14 or 24 or 32 or 48 or 50)
            {
                uint value = op == 24 ? h | (ushort)imm : (uint)(h + imm);
                note = Annotate(dol, value, op);
                if (op is 14 or 24 && rd != ra) hi.Remove(rd);
            }
            if (op is 18 or 16 && (w & 1) != 0) hi.Clear(); // chamada: registradores voláteis
            yield return $"{pc:X8}  {w:X8}  {text}{(note.Length > 0 ? "    ; " + note : "")}";
        }
    }

    static string Annotate(DolFile dol, uint value, int op)
    {
        if (op == 48 && dol.TryWord(value, out uint fw)) return $"{value:X8} = {BitConverter.Int32BitsToSingle((int)fw).ToString("R", Inv)}f";
        if (op == 50 && dol.TryWord(value, out uint d0) && dol.TryWord(value + 4, out uint d1))
            return $"{value:X8} = {BitConverter.Int64BitsToDouble((long)d0 << 32 | d1).ToString("R", Inv)}";
        if (dol.CString(value, 64) is { Length: >= 3 } s) return $"{value:X8} \"{s}\"";
        return dol.SectionAt(value) is { } sec ? $"{value:X8} ({sec.Name})" : "";
    }
}
