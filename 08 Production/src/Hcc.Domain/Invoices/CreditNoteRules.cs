using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Invoices;

/// <summary>How a credit note is sized: the whole remaining invoice, a taxable amount spread over the lines, or named lines.</summary>
public enum CreditMode
{
    Full,
    Amount,
    Lines,
}

/// <summary>An invoice line with what earlier credit notes already credited against it (positive = reduces the line).</summary>
public sealed record CreditableLine(InvoiceLine Line, Paise CreditedTaxable, Paise CreditedTax, Paise CreditedCgst, Paise CreditedSgst)
{
    public Paise RemainingTaxable => Line.Taxable - CreditedTaxable;

    public Paise RemainingTax => Line.Tax - CreditedTax;

    public Paise RemainingCgst => Line.Cgst - CreditedCgst;

    public Paise RemainingSgst => Line.Sgst - CreditedSgst;
}

/// <summary>One credit-note line: positive amounts credited against an invoice line.</summary>
public sealed record CreditLine(int LineNo, int InvoiceLineNo, GstCategory Category, string Sac, Paise Taxable, int Bps, Paise Tax, Paise Cgst, Paise Sgst, Paise Igst);

public sealed record RequestedCreditLine(int InvoiceLineNo, Paise Taxable);

/// <summary>
/// Credit-note rules. Prototype (handlers/folio.ts CREDIT_NOTE / APPROVAL): only after departure (the invoice exists), a reason, the
/// amount never above the net charges still on the invoice, one pending request at a time, Owner approval (DEC-063 G3). Production:
/// the credit note reverses tax line by line at each line's own stored rate — the full mode credits every remaining line exactly; the
/// amount mode spreads a taxable amount over the lines in proportion to what remains on each (largest remainder, to the paisa); the
/// lines mode names the lines. Each credit line's tax = its taxable × the line's rate, half away from zero (G9 per-line rule), and the
/// cumulative tax credited on a line never exceeds the tax charged on it (refused, never silently adjusted). Crediting a line's whole
/// remaining taxable value (any mode) credits exactly the tax, CGST and SGST still on it (A03a-04), so a line credited in full ends at
/// exactly its invoiced figures even after a partial credit whose tax was exactly half a paisa. The prototype's "follow
/// the room-night rate" rule for a mixed bill (G10/G12) is reproduced by naming the room lines; open CA questions G10/G12.
/// </summary>
public static class CreditNoteRules
{
    public static CreditMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "full" => CreditMode.Full,
        "amount" => CreditMode.Amount,
        "lines" => CreditMode.Lines,
        _ => throw HccException.Validation("Credit-note mode must be full, amount or lines."),
    };

    public static string Code(CreditMode m) => m switch
    {
        CreditMode.Full => "full",
        CreditMode.Amount => "amount",
        _ => "lines",
    };

    /// <summary>Taxable value still creditable on the invoice (net of discounts on it and of earlier credit notes).</summary>
    public static Paise RemainingTaxable(IEnumerable<CreditableLine> lines) => lines.Aggregate(Paise.Zero, (a, l) => a + l.RemainingTaxable);

    public static Paise RemainingValue(IEnumerable<CreditableLine> lines) => lines.Aggregate(Paise.Zero, (a, l) => a + l.RemainingTaxable + l.RemainingTax);

    public static IReadOnlyList<CreditLine> Plan(IReadOnlyList<CreditableLine> lines, CreditMode mode, Paise? amount, IReadOnlyList<RequestedCreditLine>? requested, bool interState)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var remaining = RemainingTaxable(lines);
        if (remaining <= Paise.Zero) throw Invalid("Nothing is left to credit on this invoice.");
        var parts = mode switch
        {
            CreditMode.Full => lines.Where(l => l.RemainingTaxable != Paise.Zero).Select(l => (l, l.RemainingTaxable)).ToList(),
            CreditMode.Amount => Spread(lines, amount ?? throw HccException.Validation("Enter the taxable amount to credit."), remaining),
            _ => Named(lines, requested ?? throw HccException.Validation("Choose the invoice lines to credit."), remaining),
        };

        var result = new List<CreditLine>();
        var no = 0;
        foreach (var (line, taxable) in parts)
        {
            if (taxable == Paise.Zero) continue;
            var l = line.Line;
            IReadOnlyList<Paise> pieces;
            if (taxable == line.RemainingTaxable)
            {
                pieces = Pieces(taxable, l.Bps, line.RemainingTax)
                    ?? throw HccException.Validation($"Line {l.LineNo}: the remaining {taxable} cannot carry the remaining tax {line.RemainingTax} at this rate.");
            }
            else
            {
                var partTax = MoneyMath.TaxOf(taxable, l.Bps);
                if (Math.Abs((line.CreditedTax + partTax).Value) > Math.Abs(l.Tax.Value))
                    throw HccException.Validation($"Line {l.LineNo}: rounding would credit more tax than was charged. Credit the line's remaining {line.RemainingTaxable} in one note.");
                if (Pieces(line.RemainingTaxable - taxable, l.Bps, line.RemainingTax - partTax) is null)
                    throw HccException.Validation($"Line {l.LineNo}: this credit would leave a remainder that can no longer be credited exactly. Credit a different amount, or the line's remaining {line.RemainingTaxable} in one note.");
                pieces = [taxable];
            }

            var (cgstLeft, sgstLeft) = (line.RemainingCgst, line.RemainingSgst);
            foreach (var piece in pieces)
            {
                var tax = MoneyMath.TaxOf(piece, l.Bps);
                var split = interState ? new GstSplit(tax, Paise.Zero, Paise.Zero) : SplitWithin(tax, cgstLeft, sgstLeft);
                cgstLeft -= split.Cgst;
                sgstLeft -= split.Sgst;
                result.Add(new CreditLine(++no, l.LineNo, l.Category, l.Sac, piece, l.Bps, tax, split.Cgst, split.Sgst, interState ? tax : Paise.Zero));
            }
        }

        if (result.Count == 0) throw Invalid("Nothing is left to credit on this invoice.");
        var credited = result.Aggregate(Paise.Zero, (a, c) => a + c.Taxable + c.Tax);
        if (credited > RemainingValue(lines)) throw HccException.Validation("The credit note cannot exceed the amount still on the invoice.");
        return result;
    }

    public static InvoiceFigures Totals(IEnumerable<CreditLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Paise t = Paise.Zero, c = Paise.Zero, s = Paise.Zero, i = Paise.Zero;
        foreach (var l in lines)
        {
            t += l.Taxable;
            c += l.Cgst;
            s += l.Sgst;
            i += l.Igst;
        }

        return new InvoiceFigures(t, c, s, i, t + c + s + i);
    }

    /// <summary>
    /// Spreads <paramref name="amount"/> over the lines with a positive remainder, in proportion to that remainder (largest remainder,
    /// ties to the earlier line). Never above the invoice's remaining net taxable value (prototype: "cannot exceed the net charges").
    /// </summary>
    private static List<(CreditableLine, Paise)> Spread(IReadOnlyList<CreditableLine> lines, Paise amount, Paise remaining)
    {
        if (amount <= Paise.Zero) throw HccException.Validation("The credit amount must be more than zero.");
        if (amount > remaining) throw HccException.Validation($"The credit note cannot exceed the taxable value still on the invoice ({remaining}).");
        var pool = lines.Where(l => l.Line.Taxable > Paise.Zero && l.RemainingTaxable > Paise.Zero).ToList();
        var weight = pool.Aggregate(0L, (a, l) => checked(a + l.RemainingTaxable.Value));
        var shares = pool.Select(l =>
        {
            var num = checked((Int128)amount.Value * l.RemainingTaxable.Value);
            return (Line: l, Floor: (long)(num / weight), Remainder: (long)(num % weight));
        }).ToList();
        var left = amount.Value - shares.Sum(s => s.Floor);
        var order = shares.Select((s, i) => (s, i)).OrderByDescending(x => x.s.Remainder).ThenBy(x => x.i).Select(x => x.i).ToList();
        var extra = new long[shares.Count];
        for (var k = 0; k < left; k++) extra[order[k]]++;
        return shares.Select((s, i) => (s.Line, new Paise(s.Floor + extra[i]))).ToList();
    }

    /// <summary>
    /// A line's whole remainder as credit pieces that each follow the per-line rule (taxable × rate, half away from zero) and together carry
    /// exactly the tax still on the line. Usually one piece. After a partial credit whose tax was exactly half a paisa (₹333.30 at 5 % =
    /// 1666.5 → 1667 on a ₹1,000 line taxed ₹50), the remainder's own tax rounds a paisa above what is left (666.70 → 3333.5 → 3334 against
    /// 3333): the remainder is then credited as the remainder less <c>m</c> small pieces of <c>a</c> paise (here 666.69 at 3333 + 0.01 at 0).
    /// Null when no such split of at most five pieces exists (a partial credit that would leave such a remainder is refused).
    /// </summary>
    private static IReadOnlyList<Paise>? Pieces(Paise taxable, int bps, Paise tax)
    {
        if (MoneyMath.TaxOf(taxable, bps) == tax) return [taxable];
        var sign = taxable.Value < 0 ? -1L : 1L;
        for (var m = 1; m <= 4; m++)
        {
            for (var k = 1L; k <= 2 * MoneyMath.BpsScale; k++)
            {
                var a = new Paise(sign * (k <= MoneyMath.BpsScale ? k : MoneyMath.BpsScale - k)); // same sign first, then the opposite sign
                var rest = new Paise(checked(taxable.Value - (m * a.Value)));
                if (rest == Paise.Zero) continue;
                if (MoneyMath.TaxOf(rest, bps).Value + (m * MoneyMath.TaxOf(a, bps).Value) == tax.Value) return [rest, .. Enumerable.Repeat(a, m)];
            }
        }

        return null;
    }

    /// <summary>
    /// CGST / SGST of one credit line: the odd paisa goes to the head with more left on the invoice line (CGST on a tie, as
    /// <see cref="GstMath.Split"/>), so a line's remaining CGST and SGST stay within a paisa of each other and its last credit takes exactly
    /// what is left of each (never more CGST or SGST credited than was charged).
    /// </summary>
    private static GstSplit SplitWithin(Paise tax, Paise cgstLeft, Paise sgstLeft)
    {
        var s = GstMath.Split(tax);
        var swapped = new GstSplit(tax, s.Sgst, s.Cgst);
        static long Gap(GstSplit x, Paise c, Paise g) => Math.Abs((c - x.Cgst - (g - x.Sgst)).Value);
        return Gap(swapped, cgstLeft, sgstLeft) < Gap(s, cgstLeft, sgstLeft) ? swapped : s;
    }

    private static List<(CreditableLine, Paise)> Named(IReadOnlyList<CreditableLine> lines, IReadOnlyList<RequestedCreditLine> requested, Paise remaining)
    {
        if (requested.Count == 0) throw HccException.Validation("Choose at least one invoice line to credit.");
        if (requested.Select(r => r.InvoiceLineNo).Distinct().Count() != requested.Count) throw HccException.Validation("Name each invoice line once.");
        var list = new List<(CreditableLine, Paise)>();
        foreach (var r in requested)
        {
            var l = lines.FirstOrDefault(x => x.Line.LineNo == r.InvoiceLineNo) ?? throw HccException.Validation($"Invoice line {r.InvoiceLineNo} does not exist.");
            if (l.Line.Taxable <= Paise.Zero) throw HccException.Validation($"Line {r.InvoiceLineNo} is a discount or reversal; only a full credit note reverses it.");
            if (r.Taxable <= Paise.Zero) throw HccException.Validation("Each credited amount must be more than zero.");
            if (r.Taxable > l.RemainingTaxable) throw HccException.Validation($"Line {r.InvoiceLineNo} has only {l.RemainingTaxable} left to credit.");
            list.Add((l, r.Taxable));
        }

        var total = list.Aggregate(Paise.Zero, (a, x) => a + x.Item2);
        if (total > remaining) throw HccException.Validation($"The credit note cannot exceed the taxable value still on the invoice ({remaining}).");
        return list;
    }

    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);
}
