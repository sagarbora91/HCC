using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Invoices;

/// <summary>A live line of the current invoice (charge id, kind, the night / service date it bills, net amount, GST snapshot).</summary>
public sealed record BillLine(long ChargeId, string Kind, DateOnly Night, Paise Amount, int Bps);

/// <summary>One requested change (LF-CONTRACT §4 BLCChange): a night's room rent, a removed line, an added line.</summary>
public abstract record BillChange
{
    public sealed record RoomRate(DateOnly Night, Paise NewRate) : BillChange;

    public sealed record RemoveLine(long ChargeId) : BillChange;

    public sealed record AddLine(string Label, Paise Amount, GstCategory Category, DateOnly ServiceDate) : BillChange;
}

/// <summary>The corrected invoice: old / new totals (net + GST per line), delta and the settlement it leaves.</summary>
public sealed record BillCorrectionPreview(Paise OldTotal, Paise NewTotal, Paise Delta, string Settlement, IReadOnlyList<(string Kind, DateOnly Night, Paise Amount, int Bps)> NewLines);

/// <summary>
/// LF A2 rules (legacy "Bill Updation", GST-safe form): a correction after check-out credits the WHOLE current invoice (CRN) and issues a new
/// tax invoice (INV, revision + 1) for the corrected folio — never an edit of a posted invoice. Same as the prototype's
/// domain/streams/bill-correction.ts: refusals, preview, the s.34(2) CGST Act window (30 November after the invoice's FY — CA to confirm, OQ-2).
/// </summary>
public static class BillCorrectionRules
{
    public const string RefundDue = "refund-due", BalanceDue = "balance-due", None = "none";

    /// <summary>Last date a credit note may issue for an invoice dated <paramref name="invoiceDate"/>: 30 November of the year after its FY start.</summary>
    public static DateOnly Deadline(DateOnly invoiceDate) => new((invoiceDate.Month >= 4 ? invoiceDate.Year : invoiceDate.Year - 1) + 1, 11, 30);

    public static string? WindowRefusal(DateOnly invoiceDate, DateOnly businessDate, string invoiceNo) =>
        businessDate > Deadline(invoiceDate)
            ? $"A credit note for invoice {invoiceNo} could be issued only until {Deadline(invoiceDate):yyyy-MM-dd} (s.34(2) CGST Act — CA to confirm)."
            : null;

    private static Paise Gross(IEnumerable<(Paise Amount, int Bps)> lines) => lines.Aggregate(Paise.Zero, (a, l) => a + l.Amount + GstMath.LineTax(l.Amount, l.Bps));

    /// <summary>Preview, or a refusal. <paramref name="roomBps"/> = GST slab of a night at a price (dated master); <paramref name="chargeBps"/> = by category and date.</summary>
    public static (BillCorrectionPreview? Preview, string? Refusal) Preview(DateOnly arrival, DateOnly departure, IReadOnlyList<BillLine> live, IReadOnlyList<BillChange> changes,
        Func<DateOnly, Paise, int> roomBps, Func<DateOnly, GstCategory, int> chargeBps)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(roomBps);
        ArgumentNullException.ThrowIfNull(chargeBps);
        if (changes is null || changes.Count == 0) return (null, "Add at least one change to the bill.");
        var rate = new Dictionary<DateOnly, Paise>();
        var remove = new HashSet<long>();
        var added = new List<(string, DateOnly, Paise, int)>();
        foreach (var c in changes)
        {
            switch (c)
            {
                case BillChange.RoomRate r:
                    if (r.Night < arrival || r.Night >= departure) return (null, "Choose a night of the stay.");
                    if (r.NewRate.Value < 0) return (null, "Enter the corrected room rent in whole paise (0 or more).");
                    var l = live.FirstOrDefault(x => x.Kind == "room" && x.Amount.Value > 0 && x.Night == r.Night);
                    if (l is null) return (null, $"No room rent is billed for the night of {r.Night:yyyy-MM-dd}.");
                    if (!rate.TryAdd(r.Night, r.NewRate)) return (null, $"The night of {r.Night:yyyy-MM-dd} is changed twice.");
                    if (r.NewRate == l.Amount) return (null, $"The room rent of {r.Night:yyyy-MM-dd} does not change.");
                    break;
                case BillChange.RemoveLine x:
                    var line = live.FirstOrDefault(y => y.ChargeId == x.ChargeId);
                    if (line is null) return (null, "This line is not on the current invoice (already reversed or unknown).");
                    if (line.Kind == "room") return (null, "Change a night’s room rent instead of removing it.");
                    if (!remove.Add(x.ChargeId)) return (null, "The same line is removed twice.");
                    break;
                case BillChange.AddLine a:
                    if (string.IsNullOrWhiteSpace(a.Label)) return (null, "Describe the added charge.");
                    if (a.Amount.Value <= 0) return (null, "Enter the added amount in whole paise (more than 0).");
                    if (a.Category == GstCategory.Room) return (null, "Choose a GST category: food, laundry, banquet or other.");
                    if (a.ServiceDate < arrival || a.ServiceDate > departure) return (null, "The service date is outside the stay.");
                    added.Add(("service", a.ServiceDate, a.Amount, chargeBps(a.ServiceDate, a.Category)));
                    break;
                default:
                    return (null, "Unknown change.");
            }
        }

        var next = new List<(string Kind, DateOnly Night, Paise Amount, int Bps)>();
        foreach (var l in live)
        {
            if (remove.Contains(l.ChargeId)) continue;
            if (l.Kind == "room" && l.Amount.Value > 0 && rate.TryGetValue(l.Night, out var nr)) next.Add(("room", l.Night, nr, roomBps(l.Night, nr)));
            else next.Add((l.Kind, l.Night, l.Amount, l.Bps));
        }

        next.AddRange(added);
        var oldTotal = Gross(live.Select(l => (l.Amount, l.Bps)));
        var newTotal = Gross(next.Select(l => (l.Amount, l.Bps)));
        var delta = newTotal - oldTotal;
        return (new BillCorrectionPreview(oldTotal, newTotal, delta, Settlement(delta), next), null);
    }

    /// <summary>Credit note lines for the whole current invoice: each live line negated at its own rate (the tax credited equals the invoice's).</summary>
    public static IReadOnlyList<(long ChargeId, Paise Amount, int Bps)> CreditLines(IEnumerable<BillLine> live) =>
        live.Select(l => (l.ChargeId, -l.Amount, l.Bps)).ToList();

    public static string Settlement(Paise delta) => delta.Value < 0 ? RefundDue : delta.Value > 0 ? BalanceDue : None;

    /// <summary>Print notes: "Supersedes …" on a revision, "Superseded by … (credit note …)" on the earlier invoice.</summary>
    public static string SupersedesNote(string oldNo) => "Supersedes " + oldNo;

    public static string SupersededByNote(string newNo, string creditNoteNo) => $"Superseded by {newNo} (credit note {creditNoteNo})";
}
