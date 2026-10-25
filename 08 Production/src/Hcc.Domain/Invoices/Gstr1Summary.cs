using Hcc.Domain.Money;

namespace Hcc.Domain.Invoices;

/// <summary>An issued invoice as the GSTR-1 summary needs it.</summary>
public sealed record Gstr1Invoice(string Number, InvoiceDocType DocType, SupplyType Supply, bool InterState, Paise Total, IReadOnlyList<InvoiceLine> Lines);

/// <summary>An issued credit note with the supply facts of its invoice.</summary>
public sealed record Gstr1CreditNote(string Number, SupplyType Supply, bool InterState, Paise InvoiceTotal, IReadOnlyList<CreditLine> Lines);

public sealed record Gstr1Row(string Section, int Bps, Paise Taxable, Paise Cgst, Paise Sgst, Paise Igst, int Documents);

public sealed record Gstr1SacRow(string Sac, int Bps, Paise Taxable, Paise Cgst, Paise Sgst, Paise Igst);

public sealed record Gstr1DocRow(string DocType, string First, string Last, int Count);

public sealed record Gstr1Result(IReadOnlyList<Gstr1Row> Rates, IReadOnlyList<Gstr1SacRow> Sac, IReadOnlyList<Gstr1DocRow> Documents);

/// <summary>
/// GSTR-1 style summary for a period (a working aid for the CA, not a return file): outward supplies by section and rate — B2B,
/// B2CL (unregistered, inter-state, invoice above <see cref="B2clThreshold"/>), B2CS (other unregistered, net of their credit notes),
/// CDNR / CDNUR (credit notes on B2B / B2CL invoices) — the SAC summary (table 12, net of credit notes) and documents issued (table 13).
/// Bill-of-supply lines (0 %) are reported as nil-rated under the same sections at rate 0. Thresholds: CA TO CONFIRM.
/// </summary>
public static class Gstr1Summary
{
    /// <summary>B2CL invoice-value threshold (₹1,00,000 from 1 Aug 2024, as read — CA TO CONFIRM).</summary>
    public static readonly Paise B2clThreshold = Paise.FromRupees(100_000);

    public static string SectionOf(SupplyType supply, bool interState, Paise invoiceTotal) =>
        supply == SupplyType.B2B ? "B2B" : interState && invoiceTotal > B2clThreshold ? "B2CL" : "B2CS";

    public static Gstr1Result Build(IReadOnlyList<Gstr1Invoice> invoices, IReadOnlyList<Gstr1CreditNote> notes)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(notes);
        var rates = new Dictionary<(string, int), Acc>();
        void Add(string section, int bps, Paise t, Paise c, Paise s, Paise i, string doc)
        {
            if (!rates.TryGetValue((section, bps), out var a)) rates[(section, bps)] = a = new Acc();
            a.Add(t, c, s, i);
            a.Docs.Add(doc);
        }

        var sac = new Dictionary<(string, int), Acc>();
        void Sac(string code, int bps, Paise t, Paise c, Paise s, Paise i, int sign)
        {
            if (!sac.TryGetValue((code, bps), out var a)) sac[(code, bps)] = a = new Acc();
            a.Add(Mul(t, sign), Mul(c, sign), Mul(s, sign), Mul(i, sign));
        }

        foreach (var inv in invoices)
        {
            var section = SectionOf(inv.Supply, inv.InterState, inv.Total);
            foreach (var l in inv.Lines)
            {
                Add(section, l.Bps, l.Taxable, l.Cgst, l.Sgst, l.Igst, inv.Number);
                Sac(l.Sac, l.Bps, l.Taxable, l.Cgst, l.Sgst, l.Igst, 1);
            }
        }

        foreach (var cn in notes)
        {
            var original = SectionOf(cn.Supply, cn.InterState, cn.InvoiceTotal);
            foreach (var l in cn.Lines)
            {
                // B2CS is reported net of its credit notes; B2B / B2CL credit notes go to CDNR / CDNUR.
                if (original == "B2CS") Add("B2CS", l.Bps, -l.Taxable, -l.Cgst, -l.Sgst, -l.Igst, cn.Number);
                else Add(original == "B2B" ? "CDNR" : "CDNUR", l.Bps, l.Taxable, l.Cgst, l.Sgst, l.Igst, cn.Number);
                Sac(l.Sac, l.Bps, l.Taxable, l.Cgst, l.Sgst, l.Igst, -1);
            }
        }

        string[] order = ["B2B", "B2CL", "B2CS", "CDNR", "CDNUR"];
        var rateRows = rates.OrderBy(kv => Array.IndexOf(order, kv.Key.Item1)).ThenBy(kv => kv.Key.Item2)
            .Select(kv => new Gstr1Row(kv.Key.Item1, kv.Key.Item2, kv.Value.T, kv.Value.C, kv.Value.S, kv.Value.I, kv.Value.Docs.Count)).ToList();
        var sacRows = sac.OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2)
            .Select(kv => new Gstr1SacRow(kv.Key.Item1, kv.Key.Item2, kv.Value.T, kv.Value.C, kv.Value.S, kv.Value.I)).ToList();
        var docs = invoices.GroupBy(i => InvoiceNumbering.Code(i.DocType)).Select(g => Docs(g.Key, g.Select(x => x.Number)))
            .Concat(notes.Count > 0 ? [Docs("CRN", notes.Select(n => n.Number))] : []).ToList();
        return new Gstr1Result(rateRows, sacRows, docs);
    }

    private sealed class Acc
    {
        public Paise T { get; private set; }

        public Paise C { get; private set; }

        public Paise S { get; private set; }

        public Paise I { get; private set; }

        public HashSet<string> Docs { get; } = new(StringComparer.Ordinal);

        public void Add(Paise t, Paise c, Paise s, Paise i)
        {
            T += t;
            C += c;
            S += s;
            I += i;
        }
    }

    private static Paise Mul(Paise p, int sign) => sign >= 0 ? p : -p;

    private static Gstr1DocRow Docs(string type, IEnumerable<string> numbers)
    {
        var list = numbers.OrderBy(n => n, StringComparer.Ordinal).ToList();
        return new Gstr1DocRow(type, list[0], list[^1], list.Count);
    }
}
