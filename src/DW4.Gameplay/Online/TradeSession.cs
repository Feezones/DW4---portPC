namespace DW4.Gameplay.Online;

/// <summary>Oferta de um lado da troca: itens e BIT.</summary>
public sealed class TradeOffer
{
    public const int MaxItems = 8;
    public List<ItemRef> Items { get; } = [];
    public int Bit { get; set; }
    public bool Locked { get; set; }
    public bool Confirmed { get; set; }
}

public enum TradeState { Open, Done, Cancelled }

/// <summary>
/// Troca de itens entre dois jogadores (DECISÃO DO PORT, pedido do responsável em 2026-11-02; o DW4 original não tem troca).
/// Fluxo em duas etapas, para ninguém ser enganado com uma oferta trocada na última hora:
///  1. cada um põe itens e BIT e trava a oferta;
///  2. com as duas travadas, cada um confirma. Qualquer mudança destrava e desconfirma os dois lados.
/// A troca só acontece se os dois inventários ainda têm o oferecido e cabem o que recebem; é tudo ou nada. Itens importantes
/// (impItem, enredo) não são trocáveis. Roda no servidor: o cliente só pede, o servidor aplica nos inventários dele.
/// </summary>
public sealed class TradeSession
{
    public string A { get; }
    public string B { get; }
    public TradeOffer OfferA { get; } = new();
    public TradeOffer OfferB { get; } = new();
    public TradeState State { get; private set; } = TradeState.Open;

    public TradeSession(string a, string b) { A = a; B = b; }

    TradeOffer? Of(string who) => PartyGroup.Names.Equals(who, A) ? OfferA : PartyGroup.Names.Equals(who, B) ? OfferB : null;

    public static bool Tradable(ItemRef item) => item.Table is "weapon" or "armor1" or "armor2" or "abChip" or "conItem";

    void Reset() { OfferA.Locked = OfferB.Locked = OfferA.Confirmed = OfferB.Confirmed = false; }

    SocialResult Edit(string who, Func<TradeOffer, SocialResult> f)
    {
        if (State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        var o = Of(who);
        if (o == null) return SocialResult.Fail("NaoParticipa", "Você não está nesta troca.");
        var r = f(o);
        if (r.Ok) Reset();
        return r;
    }

    public SocialResult AddItem(string who, ItemRef item, Inventory inv) => Edit(who, o =>
    {
        if (!Tradable(item)) return SocialResult.Fail("NaoTrocavel", "Esse item não pode ser trocado.");
        if (o.Items.Count >= TradeOffer.MaxItems) return SocialResult.Fail("OfertaCheia", $"No máximo {TradeOffer.MaxItems} itens por troca.");
        if (Owned(inv, item) <= o.Items.Count(i => i == item)) return SocialResult.Fail("SemItem", "Você não tem esse item.");
        o.Items.Add(item);
        return SocialResult.Success;
    });

    public SocialResult RemoveItem(string who, ItemRef item) => Edit(who, o =>
        o.Items.Remove(item) ? SocialResult.Success : SocialResult.Fail("SemItem", "Esse item não está na oferta."));

    public SocialResult SetBit(string who, int bit, Inventory inv) => Edit(who, o =>
    {
        if (bit < 0 || bit > inv.Bit) return SocialResult.Fail("SemBit", "Você não tem esse BIT.");
        o.Bit = bit;
        return SocialResult.Success;
    });

    public SocialResult Lock(string who)
    {
        var o = Of(who);
        if (o == null || State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        o.Locked = true;
        return SocialResult.Success;
    }

    public void Cancel() { if (State == TradeState.Open) State = TradeState.Cancelled; }

    /// <summary>Confirma. Quando os dois confirmaram, executa a troca nos inventários (tudo ou nada).</summary>
    public SocialResult Confirm(string who, Inventory invA, Inventory invB)
    {
        var o = Of(who);
        if (o == null || State != TradeState.Open) return SocialResult.Fail("TrocaFechada", "A troca já acabou.");
        if (!OfferA.Locked || !OfferB.Locked) return SocialResult.Fail("NaoTravada", "Os dois precisam travar a oferta antes.");
        o.Confirmed = true;
        if (!OfferA.Confirmed || !OfferB.Confirmed) return SocialResult.Success;

        var check = Check(OfferA, invA, OfferB) ?? Check(OfferB, invB, OfferA);
        if (check != null) { Cancel(); return SocialResult.Fail("TrocaInvalida", check); }

        foreach (var i in OfferA.Items) invA.Remove(i);
        foreach (var i in OfferB.Items) invB.Remove(i);
        foreach (var i in OfferB.Items) invA.Add(i);
        foreach (var i in OfferA.Items) invB.Add(i);
        invA.Bit += OfferB.Bit - OfferA.Bit;
        invB.Bit += OfferA.Bit - OfferB.Bit;
        State = TradeState.Done;
        return SocialResult.Success;
    }

    static int Owned(Inventory inv, ItemRef item) => item.Table == "conItem" ? inv.Disks(item.Row) : inv.Devices.Count(d => d == item);

    /// <summary>null se <paramref name="giver"/> tem o que oferece e recebe o que vem do outro lado sem estourar a pasta.</summary>
    static string? Check(TradeOffer give, Inventory giver, TradeOffer get)
    {
        foreach (var g in give.Items.GroupBy(i => i))
            if (Owned(giver, g.Key) < g.Count()) return "Um dos itens oferecidos não está mais no inventário.";
        if (giver.Bit < give.Bit) return "BIT oferecido não está mais disponível.";
        int devices = giver.Devices.Count - give.Items.Count(i => i.Table != "conItem") + get.Items.Count(i => i.Table != "conItem");
        if (devices > Inventory.DeviceMax) return "Não cabe na pasta de equipamentos.";
        foreach (var d in get.Items.Where(i => i.Table == "conItem").GroupBy(i => i.Row))
        {
            int after = giver.Disks(d.Key) - give.Items.Count(i => i.Table == "conItem" && i.Row == d.Key) + d.Count();
            if (after > Inventory.DiskMax) return "Não cabem mais discos desse tipo.";
        }
        if ((long)giver.Bit - give.Bit + get.Bit > Inventory.BitMax) return "Passaria do limite de BIT.";
        return null;
    }
}
