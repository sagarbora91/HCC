using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payroll;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Invoices;

/// <summary>A source line to invoice (a posted folio charge): its stored tax snapshot and, when recorded, the stored CGST / SGST.</summary>
public sealed record SourceLine(long? SourceLineId, string Kind, DateOnly ServiceDate, string Description, GstCategory Category, Paise Taxable, int Bps,
    Paise Tax, Paise? Cgst, Paise? Sgst);

/// <summary>One invoice line as issued.</summary>
public sealed record InvoiceLine(int LineNo, long? SourceLineId, string Kind, DateOnly ServiceDate, string Description, GstCategory Category, string Sac,
    Paise Taxable, int Bps, Paise Tax, Paise Cgst, Paise Sgst, Paise Igst);

public sealed record InvoiceFigures(Paise Taxable, Paise Cgst, Paise Sgst, Paise Igst, Paise Total)
{
    public Paise Tax => Cgst + Sgst + Igst;
}

/// <summary>B2C (unregistered recipient) or B2B (registered recipient with a GSTIN).</summary>
public enum SupplyType
{
    B2C,
    B2B,
}

/// <summary>Recipient and place of supply as validated for the document.</summary>
public sealed record Recipient(SupplyType Type, string? Name, string? Gstin, string? Address, string PlaceOfSupply, bool InterState);

/// <summary>
/// Invoice rules (port of the prototype sample invoice, <c>03 Prototype/src/features/folio/modals.tsx InvoiceModal</c>, with the GST
/// document rules production needs). Tax is never recomputed: every line keeps the rate and tax stored when it was posted (DEC-069); the
/// CGST / SGST halves are the stored per-charge split (odd paisa to CGST, CA question G9). Inter-state supply puts the whole line tax in IGST.
/// </summary>
public static class InvoiceRules
{
    /// <summary>
    /// CGST Rules r.46(e) (as read): an invoice to an unregistered recipient of ₹50,000 or more carries the recipient's name and address.
    /// CA TO CONFIRM (list of CA questions in the W3-6 report).
    /// </summary>
    public static readonly Paise B2cNameAddressThreshold = Paise.FromRupees(50_000);

    public static IReadOnlyList<InvoiceLine> BuildLines(IReadOnlyList<SourceLine> source, bool interState)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0) throw new HccException(Hcc.Contracts.HccErrorCode.INVALID_TRANSITION, "There is nothing to invoice on this folio.");
        var lines = new List<InvoiceLine>(source.Count);
        var no = 0;
        foreach (var s in source)
        {
            if (MoneyMath.TaxOf(s.Taxable, s.Bps) != s.Tax)
                throw new InvalidOperationException($"Line {s.SourceLineId}: stored tax {s.Tax.Value} disagrees with its rate {s.Bps} bps on {s.Taxable.Value}.");
            Paise cgst, sgst, igst = Paise.Zero;
            if (interState)
            {
                (cgst, sgst, igst) = (Paise.Zero, Paise.Zero, s.Tax);
            }
            else if (s.Cgst is { } c && s.Sgst is { } g)
            {
                if (c + g != s.Tax) throw new InvalidOperationException($"Line {s.SourceLineId}: stored CGST + SGST differs from the line tax.");
                (cgst, sgst) = (c, g);
            }
            else
            {
                var split = GstMath.Split(s.Tax);
                (cgst, sgst) = (split.Cgst, split.Sgst);
            }

            lines.Add(new InvoiceLine(++no, s.SourceLineId, s.Kind, s.ServiceDate, s.Description, s.Category, SacCodes.For(s.Category), s.Taxable, s.Bps, s.Tax, cgst, sgst, igst));
        }

        return lines;
    }

    public static InvoiceFigures Totals(IEnumerable<InvoiceLine> lines)
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

    /// <summary>Bill of supply when no line carries tax (exempt / nil-rated supplies only); otherwise a tax invoice.</summary>
    public static InvoiceDocType DocTypeFor(IEnumerable<InvoiceLine> lines) => lines.All(l => l.Bps == 0) ? InvoiceDocType.BillOfSupply : InvoiceDocType.TaxInvoice;

    /// <summary>Amount in words, Indian system (prototype <c>hrAmountWords</c>, the same port the payslip uses).</summary>
    public static string Words(Paise amount) => PayrollFiles.AmountInWords(amount.Value);

    /// <summary>
    /// Validates the recipient. Place of supply defaults to the supplier's state: for hotel accommodation and restaurant service it is
    /// the location of the hotel (IGST Act s.12(3)/(4), as read — CA question G9). A different place of supply makes the supply
    /// inter-state (IGST) — CA TO CONFIRM; the prototype had no IGST.
    /// </summary>
    public static Recipient Recipient(string? supplyType, string? name, string? gstin, string? address, string? placeOfSupply, string supplierState, Paise total)
    {
        var type = supplyType?.Trim().ToUpperInvariant() switch
        {
            null or "" or "B2C" => SupplyType.B2C,
            "B2B" => SupplyType.B2B,
            _ => throw HccException.Validation("Supply type must be B2C or B2B."),
        };
        var n = Clean(name, 120, "Keep the recipient name to 120 characters.");
        var a = Clean(address, 200, "Keep the recipient address to 200 characters.");
        var pos = GstStates.ParsePlaceOfSupply(placeOfSupply) ?? supplierState;
        string? g = null;
        if (type == SupplyType.B2B)
        {
            g = Gstin.Validate(gstin, "recipient");
            if (n is null) throw HccException.Validation("Enter the recipient's legal name for a B2B invoice.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(gstin)) throw HccException.Validation("A recipient GSTIN makes this a B2B invoice: choose B2B.");
            if (total >= B2cNameAddressThreshold && (n is null || a is null))
                throw HccException.Validation("An invoice of ₹50,000 or more to an unregistered guest needs the recipient's name and address.");
        }

        return new Recipient(type, n, g, a, pos, !string.Equals(pos, supplierState, StringComparison.Ordinal));
    }

    private static string? Clean(string? v, int max, string message)
    {
        var t = v?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > max) throw HccException.Validation(message);
        return t;
    }
}
