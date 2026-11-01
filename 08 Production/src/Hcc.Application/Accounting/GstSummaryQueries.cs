using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>Read port for the GSTR-1 working summary of a range (implemented over <c>InvoiceQueries.Gstr1Async</c> by the endpoint).</summary>
public interface IGstr1Source
{
    Task<Gstr1View> GetAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// ACC-3 acc3-ageing: GST summary (rulebook §7.7; contract ACC3 §3; prototype <c>accounting-gst-summary.ts</c>). Output by rate / section from
/// the GSTR-1 working summary, advances 11A / 11B from the advance-GST vouchers, ITC, RCM, set-off, TCS and the 1340 balance from
/// <see cref="GstMonthRules.Compute"/> (ACC-2's GST month). Ties: GSTR-1 tax = books output net of advance GST; the vouchers' output = the GST
/// month's output and credit used + cash = output per head. Output figures are tax paise.
/// </summary>
public static class GstSummaryRules
{
    private static string Rs(long p) => "₹" + (p / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static GstHeadsView H(GstHeads h) => new(h.Igst, h.Cgst, h.Sgst);

    public static GstSummaryView Build(string month, IEnumerable<Voucher> vouchers, IReadOnlyList<StoredGstSetoff> posted, Gstr1View gstr1)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(gstr1);
        var list = vouchers as IReadOnlyList<Voucher> ?? vouchers.ToList();
        var f = GstMonthRules.Compute(month, list, posted);
        long r5 = 0, r18 = 0, b2b = 0, b2cs = 0, cn = 0, t5 = 0, t18 = 0, tb2b = 0, tb2cs = 0, tcn = 0, g1 = 0;
        foreach (var r in gstr1.Rates)
        {
            var note = r.Section is "CDNR" or "CDNUR";
            long sign = note ? -1 : 1, tax = r.CgstPaise + r.SgstPaise + r.IgstPaise;
            g1 += sign * tax;
            if (r.TaxBps == 500) { r5 += sign * tax; t5 += sign * r.TaxablePaise; }
            else if (r.TaxBps == 1800) { r18 += sign * tax; t18 += sign * r.TaxablePaise; }
            if (r.Section == "B2B") { b2b += tax; tb2b += r.TaxablePaise; }
            else if (r.Section is "B2CS" or "B2CL") { b2cs += tax; tb2cs += r.TaxablePaise; }
            else if (note) { cn += tax; tcn += r.TaxablePaise; }
        }

        long oi = 0, oc = 0, os = 0, a11 = 0, b11 = 0, cashPaid = 0;
        foreach (var v in list.Where(v => v.Date >= f.From && v.Date <= f.To && !AccountingReports.IsOpening(v)))
        {
            var setoff = v.SourceKind == SourceKinds.Tax && v.Sub is SourceRules.GstSetoffSub or SourceRules.GstSetoffReversalSub;
            foreach (var e in v.Entries)
            {
                var a = AccountingReports.ControlOf(e.Account);
                var cr = e.CreditPaise - e.DebitPaise;
                var output = a is GstSetOff.LedgerOutputIgst or GstSetOff.LedgerOutputCgst or GstSetOff.LedgerOutputSgst;
                if (output && !setoff)
                {
                    if (a == GstSetOff.LedgerOutputIgst) oi += cr;
                    else if (a == GstSetOff.LedgerOutputCgst) oc += cr;
                    else os += cr;
                }

                if (output && v.Sub == SourceRules.AdvanceGstSub) a11 += cr;
                else if (output && v.Sub == SourceRules.AdvanceGstReversalSub) b11 -= cr;
                if (a == GstSetOff.LedgerCash && v.Sub == "gst-payment") cashPaid += e.DebitPaise;
            }
        }

        var diffs = new List<string>();
        var bookNet = oi + oc + os - a11 + b11;
        var tie1 = bookNet == g1;
        if (!tie1)
            diffs.Add($"Books output GST {Rs(bookNet)} (net of advances 11A {Rs(a11)} / 11B {Rs(b11)}) vs GSTR-1 working summary {Rs(g1)}: differ by {Rs(bookNet - g1)} — a sale booked without an invoice in the period (e.g. an Udhaar credit or a folio not yet invoiced) or an invoice dated in another month.");
        var tie3 = true;
        var book = new GstHeads(oi, oc, os);
        foreach (var (n, b, m) in new[] { ("IGST", book.Igst, f.Output.Igst), ("CGST", book.Cgst, f.Output.Cgst), ("SGST", book.Sgst, f.Output.Sgst) })
            if (b != m) { tie3 = false; diffs.Add($"Output {n} from the vouchers {Rs(b)} vs the GST month {Rs(m)}."); }
        var s = f.Setoff;
        foreach (var (n, used, cash, outp) in new[]
                 {
                     ("IGST", s.UseIgst.Igst + s.UseCgst.Igst + s.UseSgst.Igst, s.Cash.Igst, s.Output.Igst),
                     ("CGST", s.UseIgst.Cgst + s.UseCgst.Cgst + s.UseSgst.Cgst, s.Cash.Cgst, s.Output.Cgst),
                     ("SGST", s.UseIgst.Sgst + s.UseCgst.Sgst + s.UseSgst.Sgst, s.Cash.Sgst, s.Output.Sgst),
                 })
            if (used + cash != outp) { tie3 = false; diffs.Add($"{n}: credit used {Rs(used)} + cash {Rs(cash)} ≠ output {Rs(outp)}."); }

        return new GstSummaryView(month, new GstSummaryOutputView(r5, r18, b2b, b2cs, a11, b11, cn), new GstSummaryOutputView(t5, t18, tb2b, tb2cs, 0, 0, tcn),
            H(f.Input), f.Rcm, new GstSummarySetoffView(H(s.Output), H(s.Credit), H(s.Cash), H(s.CreditLeft), s.Payable, s.CashAvailable, s.Challan),
            cashPaid, f.Tcs, f.CashBalance, H(book), new GstSummaryTiesView(tie1, tie3, diffs));
    }
}

/// <summary>ACC-3 acc3-ageing: <c>GET /accounting/gst/summary?month</c> (permission as the trial balance, <see cref="AccountingPermissions.View"/>).</summary>
public sealed class GstSummaryQueries(IAccountingStore store, IGstMonthStore months, IAccessPolicy policy, IGstr1Source gstr1)
{
    private static readonly DateOnly Beginning = new(2000, 1, 1);

    public async Task<GstSummaryView> SummaryAsync(Caller caller, string? month, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var m = (month ?? "").Trim();
        if (!GstMonthRules.IsMonth(m)) throw HccException.Validation("Choose a month (YYYY-MM).");
        var (from, to) = GstMonthRules.Range(m);
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, Beginning, to, ct).ConfigureAwait(false);
        var posted = await months.SetoffsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var g1 = await gstr1.GetAsync(caller, from, to, ct).ConfigureAwait(false);
        return GstSummaryRules.Build(m, days.SelectMany(d => d.Vouchers).ToList(), posted, g1);
    }
}
