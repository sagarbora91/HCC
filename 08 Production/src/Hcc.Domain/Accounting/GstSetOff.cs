namespace Hcc.Domain.Accounting;

/// <summary>GST by head (paise): integrated, central, state (prototype <c>ACCGstHeads</c>).</summary>
public sealed record GstHeads(long Igst, long Cgst, long Sgst)
{
    public static readonly GstHeads Zero = new(0, 0, 0);

    public long Total => checked(Igst + Cgst + Sgst);

    public GstHeads Plus(GstHeads o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(checked(Igst + o.Igst), checked(Cgst + o.Cgst), checked(Sgst + o.Sgst));
    }

    public GstHeads Minus(GstHeads o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(checked(Igst - o.Igst), checked(Cgst - o.Cgst), checked(Sgst - o.Sgst));
    }
}

/// <summary>
/// One month's set-off (prototype <c>ACCGstSetoff</c>): <see cref="UseIgst"/> = IGST credit used against each output head, and so on;
/// <see cref="Cash"/> = output left after the credit, paid from the electronic cash ledger 1340 (the net payable per head);
/// <see cref="Challan"/> = that total less the cash already in 1340 (never below zero).
/// </summary>
public sealed record GstSetOffResult(GstHeads Output, GstHeads Credit, long CashAvailable, GstHeads UseIgst, GstHeads UseCgst, GstHeads UseSgst, GstHeads Cash,
    GstHeads CreditLeft, long Payable, long Challan)
{
    /// <summary>Credit used per input head (= credit − credit left).</summary>
    public GstHeads Used => Credit.Minus(CreditLeft);
}

/// <summary>
/// Month-end GST set-off in the order of CGST Act s.49(5), s.49A, s.49B and Rule 88A (rulebook §4.12, E1; prototype <c>accGstSetoff</c>,
/// <c>03 Prototype/src/domain/streams/accounting-gst.ts</c>): IGST credit first — against IGST, then against CGST / SGST ("in any order":
/// HCC puts it where the head's own credit falls short, then exhausts it, s.49A); CGST credit → CGST then IGST; SGST credit → SGST then IGST,
/// only what the CGST credit could not pay (s.49(5)(d) proviso); CGST and SGST credit never cross. Pure; paise.
/// </summary>
public static class GstSetOff
{
    public const string LedgerOutputCgst = "2110";
    public const string LedgerOutputSgst = "2120";
    public const string LedgerOutputIgst = "2130";
    public const string LedgerInputCgst = "1310";
    public const string LedgerInputSgst = "1320";
    public const string LedgerInputIgst = "1330";
    public const string LedgerCash = "1340";
    public const string LedgerAdvance = "1350";
    public const string LedgerRcm = "2140";

    /// <summary>Mapping keys of the tax ledgers (defaults in <see cref="ChartOfAccounts.DefaultMapping"/>, ACC-2 gst block).</summary>
    public const string KeyOutputCgst = "tax:output-cgst";
    public const string KeyOutputSgst = "tax:output-sgst";
    public const string KeyOutputIgst = "tax:output-igst";
    public const string KeyInputIgst = "tax:gst-input-igst";
    public const string KeyCash = "tax:gst-cash";
    public const string KeyAdvance = "tax:advance-gst";
    public const string KeyRcm = "tax:rcm-payable";

    private static long Pos(long n) => Math.Max(0, n);

    public static GstSetOffResult Compute(GstHeads output, GstHeads credit, long cashAvailable = 0)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(credit);
        // left[] = output still to pay, cr[] = credit still unused; index 0 IGST, 1 CGST, 2 SGST.
        var left = new[] { Pos(output.Igst), Pos(output.Cgst), Pos(output.Sgst) };
        var cr = new[] { Pos(credit.Igst), Pos(credit.Cgst), Pos(credit.Sgst) };
        var o = new GstHeads(left[0], left[1], left[2]);
        var c = new GstHeads(cr[0], cr[1], cr[2]);
        var use = new long[3, 3];

        void Take(int from, int to, long max = long.MaxValue)
        {
            var n = Math.Min(Math.Min(cr[from], left[to]), max);
            if (n <= 0) return;
            cr[from] -= n;
            left[to] -= n;
            use[from, to] += n;
        }

        const int I = 0, C = 1, S = 2;
        Take(I, I);
        Take(I, C, Math.Max(0, left[C] - cr[C]));
        Take(I, S, Math.Max(0, left[S] - cr[S]));
        Take(I, C);
        Take(I, S);
        Take(C, C);
        Take(C, I);
        Take(S, S);
        Take(S, I);
        var payable = checked(left[0] + left[1] + left[2]);
        var cashIn = Pos(cashAvailable);
        GstHeads Row(int r) => new(use[r, 0], use[r, 1], use[r, 2]);
        return new GstSetOffResult(o, c, cashIn, Row(I), Row(C), Row(S), new GstHeads(left[0], left[1], left[2]), new GstHeads(cr[0], cr[1], cr[2]), payable,
            Math.Max(0, payable - cashIn));
    }

    /// <summary>
    /// The set-off journal (E1) as keyed entries, merged per ledger (prototype <c>accGstSetoffEntries</c>): Dr each output head (credit used +
    /// cash) · Cr each input head used · Cr 1340 (cash). Zero lines are left out.
    /// </summary>
    public static IReadOnlyList<SourceEntry> Entries(GstSetOffResult x)
    {
        ArgumentNullException.ThrowIfNull(x);
        var list = new List<SourceEntry>
        {
            new(KeyOutputIgst, checked(x.UseIgst.Igst + x.UseCgst.Igst + x.UseSgst.Igst + x.Cash.Igst), 0),
            new(KeyOutputCgst, checked(x.UseIgst.Cgst + x.UseCgst.Cgst + x.Cash.Cgst), 0),
            new(KeyOutputSgst, checked(x.UseIgst.Sgst + x.UseSgst.Sgst + x.Cash.Sgst), 0),
            new(KeyInputIgst, 0, x.UseIgst.Total),
            new(SourceRules.GstInputKey, 0, checked(x.UseCgst.Cgst + x.UseCgst.Igst)),
            new(ChartOfAccounts.GstInputSgstKey, 0, checked(x.UseSgst.Sgst + x.UseSgst.Igst)),
            new(KeyCash, 0, x.Payable),
        };
        return list.Where(e => e.DebitPaise != 0 || e.CreditPaise != 0).ToList();
    }

    /// <summary>
    /// GST inside a cum-tax amount at <paramref name="bps"/> (prototype <c>accCumTaxGst</c>, server <see cref="SourceRules.UdhaarGst"/>):
    /// taxable rounded half away from zero, tax = amount − taxable, split CGST / SGST by <see cref="GstSplit"/>. Signed; 0 bps = nil.
    /// </summary>
    public static (long Tax, long Cgst, long Sgst) CumTax(long amountPaise, int bps)
    {
        if (bps <= 0 || amountPaise == 0) return (0, 0, 0);
        var (taxable, cgst, sgst) = SourceRules.UdhaarGst(amountPaise, bps);
        return (checked(amountPaise - taxable), cgst, sgst);
    }
}
