namespace DW4.Gameplay.Online;

/// <summary>Oferta de um lado da troca: itens (armas, armaduras, chips e discos) e BIT.</summary>
public sealed class TradeOffer
{
    public const int MaxItems = 8;
    public List<ItemRef> Items { get; } = [];
    public int Bit { get; set; }
    public bool Locked { get; set; }
    public bool Confirmed { get; set; }
}

public enum TradeState { Open, Committing, Done, Cancelled }

/// <summary>
/// Troca de itens entre dois jogadores (DECISÃO DO PORT, pedido do responsável em 2026-11-02 e 2026-11-07; o DW4 original não
/// tem troca, só o "Give" da pasta para o grupo local, com o ISJYOUDO das tabelas). Fluxo em duas etapas, para ninguém ser
/// enganado com uma oferta trocada na última hora:
///  1. cada um põe itens e BIT e trava a oferta;
///  2. com as duas travadas, cada um confirma. Qualquer mudança destrava e desconfirma os dois lados.
/// Roda no servidor do DW4 (o cliente só pede). A conferência e a troca de verdade (<see cref="Check"/> e <see cref="Move"/>) são
/// feitas nos saves guardados no servidor (<see cref="TradeLedger"/>), tudo ou nada. Itens importantes (impItem, enredo) e itens
/// equipados não são trocáveis.
/// </summary>
public sealed class TradeSession
{
    public int A { get; }
    public int B { get; }
    public TradeOffer OfferA { get; } = new();
    public TradeOffer OfferB { get; } = new();
    public TradeState State { get; private set; } = TradeState.Open;

    public TradeSession(int a, int b) { A = a; B = b; }

    public bool Has(int who) => who == A || who == B;
    public int OtherOf(int who) => who == A ? B : A;
    public TradeOffer? Of(int who) => who == A ? OfferA : who == B ? OfferB : null;

    /// <summary>Tabelas que podem ir numa troca (impItem é enredo; insItem nem fica guardado).</summary>
    public static bool Tradable(ItemRef item) => item.Table is "weapon" or "armor1" or "armor2" or "abChip" or "conItem" && item.Row >= 0;

    void Reset() { OfferA.Locked = OfferB.Locked = OfferA.Confirmed = OfferB.Confirmed = false; }

    SocialResult Edit(int who, Func<TradeOffer, SocialResult> f)
    {
        if (State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        var o = Of(who);
        if (o == null) return SocialResult.Fail("NaoParticipa", "Você não está nesta troca.");
        var r = f(o);
        if (r.Ok) Reset();
        return r;
    }

    public SocialResult AddItem(int who, ItemRef item) => Edit(who, o =>
    {
        if (!Tradable(item)) return SocialResult.Fail("NaoTrocavel", "Esse item não pode ser trocado.");
        if (o.Items.Count >= TradeOffer.MaxItems) return SocialResult.Fail("OfertaCheia", $"No máximo {TradeOffer.MaxItems} itens por troca.");
        o.Items.Add(item);
        return SocialResult.Success;
    });

    public SocialResult RemoveItem(int who, ItemRef item) => Edit(who, o =>
        o.Items.Remove(item) ? SocialResult.Success : SocialResult.Fail("SemItem", "Esse item não está na oferta."));

    public SocialResult SetBit(int who, int bit) => Edit(who, o =>
    {
        if (bit < 0 || bit > Inventory.BitMax) return SocialResult.Fail("SemBit", "Quantidade de BIT inválida.");
        o.Bit = bit;
        return SocialResult.Success;
    });

    public SocialResult Lock(int who)
    {
        var o = Of(who);
        if (o == null || State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        o.Locked = true;
        return SocialResult.Success;
    }

    /// <summary>Cancela (só antes da troca começar a gravar; depois ela termina sozinha).</summary>
    public bool Cancel()
    {
        if (State != TradeState.Open) return false;
        State = TradeState.Cancelled;
        return true;
    }

    /// <summary>Confirma. Com os dois confirmados, a troca passa a <see cref="TradeState.Committing"/> (quem chama grava).</summary>
    public SocialResult Confirm(int who)
    {
        var o = Of(who);
        if (o == null || State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        if (!OfferA.Locked || !OfferB.Locked) return SocialResult.Fail("NaoTravada", "Os dois precisam travar a oferta antes.");
        o.Confirmed = true;
        if (OfferA.Confirmed && OfferB.Confirmed) State = TradeState.Committing;
        return SocialResult.Success;
    }

    /// <summary>Fim da gravação: feita ou cancelada (o motivo vai para os dois).</summary>
    public void Finish(bool ok) { if (State == TradeState.Committing) State = ok ? TradeState.Done : TradeState.Cancelled; }

    /// <summary>Cópias livres (sem estar equipadas) de um item no inventário; discos pela DISKFOLDER.</summary>
    public static int Free(Inventory inv, Equipment? eq, ItemRef item) =>
        item.Table == "conItem" ? inv.Disks(item.Row) : EquipRules.Free(inv, eq, item);

    /// <summary>
    /// null se <paramref name="giver"/> tem livre o que oferece e recebe o que vem do outro lado sem estourar a pasta (36 peças, 9
    /// discos por tipo, 9 999 999 BIT); senão o motivo.
    /// </summary>
    public static string? Check(TradeOffer give, Inventory giver, Equipment? eq, TradeOffer get)
    {
        foreach (var i in give.Items.Concat(get.Items))
            if (!Tradable(i)) return "Um dos itens não pode ser trocado.";
        foreach (var g in give.Items.GroupBy(i => i))
            if (Free(giver, eq, g.Key) < g.Count()) return "Um dos itens oferecidos não está mais livre no inventário (usado, vendido ou equipado).";
        if (give.Bit < 0 || get.Bit < 0 || giver.Bit < give.Bit) return "O BIT oferecido não está mais disponível.";
        int devices = giver.Devices.Count - give.Items.Count(i => i.Table != "conItem") + get.Items.Count(i => i.Table != "conItem");
        if (devices > Inventory.DeviceMax) return $"Não cabe na pasta de equipamentos ({Inventory.DeviceMax}).";
        foreach (var d in get.Items.Where(i => i.Table == "conItem").GroupBy(i => i.Row))
        {
            int after = giver.Disks(d.Key) - give.Items.Count(i => i.Table == "conItem" && i.Row == d.Key) + d.Count();
            if (after > Inventory.DiskMax) return $"Não cabem mais discos desse tipo ({Inventory.DiskMax}).";
        }
        if ((long)giver.Bit - give.Bit + get.Bit > Inventory.BitMax) return "Passaria do limite de BIT.";
        return null;
    }

    /// <summary>Aplica um lado da troca no inventário: sai o que deu, entra o que recebeu (já conferido por <see cref="Check"/>).</summary>
    public static void Move(TradeOffer give, TradeOffer get, Inventory inv)
    {
        foreach (var i in give.Items) inv.Remove(i);
        foreach (var i in get.Items) inv.Add(i);
        inv.Bit = (int)Math.Clamp((long)inv.Bit - give.Bit + get.Bit, 0, Inventory.BitMax);
    }
}
