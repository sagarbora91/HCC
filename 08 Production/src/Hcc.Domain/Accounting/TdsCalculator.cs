using System.Globalization;
using Hcc.Domain.Money;

namespace Hcc.Domain.Accounting;

// ACC-2 · lane acc2-tds — TDS on a vendor bill (C9, DEC-146) and the TDS a company deducts from the hotel (A9). Pure; the purchase lane
// calls TdsCalculator.ForBill when it posts a bill (Expense command → tds_paise / tds_section) and credits the vendor net, Cr 2150 the TDS
// (SourceRules key TdsCalculator.PayableKey). Prototype twin: 03 Prototype/src/domain/tds.ts (tdsForBill, tdsCompanyExpected).

/// <summary>The two firm-profile switches (contract §4): the hotel deducts TDS (DEC-146, default on) and the rate column (default other).</summary>
public sealed record TdsSettings(bool HotelDeductsTds, TdsColumn Column)
{
    public static readonly TdsSettings Default = new(true, TdsColumn.Other);
}

/// <summary>The vendor master facts TDS needs: flagged <c>tds</c> and its section code (<c>rent</c>, <c>rent:plant</c>, <c>contractor</c> …).</summary>
public sealed record TdsVendor(string Name, bool Tds, string? Section);

/// <summary>The bill being posted: amount incl. GST, the GST on it, its business date.</summary>
public sealed record TdsBill(long AmountPaise, long GstPaise, DateOnly Date);

/// <summary>Another posted bill of the same vendor (any date; ForBill keeps the period's): amount, GST, the TDS it carried (0 = none).</summary>
public sealed record TdsPriorBill(DateOnly Date, long AmountPaise, long GstPaise, long TdsPaise);

/// <summary>Result: TDS in paise (0 = none), the vendor's section code, the bill's taxable value, what the rate applied to, the rate and why.</summary>
public sealed record TdsResult(long Paise, string Section, long TaxablePaise, long BasePaise, int Bps, string Reason);

public static class TdsCalculator
{
    /// <summary>Mapping key of the hotel's TDS payable (Cr on a bill) → 2150 by default (prototype ACC_TDS_PAYABLE_KEY).</summary>
    public const string PayableKey = "tax:tds-payable";

    /// <summary>Mapping key of the TDS a company deducted from the hotel (Dr on its payment) → 1360 by default (prototype ACC_TDS_RECEIVABLE_KEY).</summary>
    public const string ReceivableKey = "tax:tds-receivable";

    /// <summary>Highest share of a settlement a company's TDS may take (20 %, the no-PAN rate).</summary>
    public const int ReceivableMaxBps = 2000;

    /// <summary>Taxable value of a bill: amount less its GST (Circular 23/2017: no TDS on the GST shown separately); never below 0.</summary>
    public static long Taxable(long amountPaise, long gstPaise) => Math.Max(0, amountPaise - Math.Max(0, gstPaise));

    /// <summary>base × bps / 10000, half away from zero (prototype gstLineTax): integer arithmetic.</summary>
    public static long Round(long basePaise, int bps)
    {
        var p = checked(basePaise * bps);
        var a = Math.Abs(p);
        var q = a / 10000;
        var t = q + (a - q * 10000 >= 5000 ? 1 : 0);
        return p < 0 ? -t : t;
    }

    private static string Rs(long paise) => new Paise(paise).ToString();

    /// <summary>
    /// TDS the hotel holds back on a vendor bill (contract §5): 0 unless the hotel deducts TDS and the vendor is flagged with a known section.
    /// taxable = amount − GST; the threshold is tested on the vendor's total for the period (calendar month for rent, financial year otherwise;
    /// contractor also per single bill) with this bill included; when this bill takes the total over the threshold the period's earlier bills
    /// without TDS join the base (old 194C / 194J / 194H provisos). Paise = round(base × rate), never more than the bill. CA to confirm.
    /// </summary>
    public static TdsResult ForBill(TdsBill bill, TdsVendor? vendor, IEnumerable<TdsPriorBill>? vendorBills, TdsSettings? settings = null,
        IReadOnlyList<TdsRateVersion>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var set = settings ?? TdsSettings.Default;
        static TdsResult None(string reason, string section = "", long taxable = 0) => new(0, section, taxable, 0, 0, reason);
        if (!set.HotelDeductsTds) return None("The hotel does not deduct TDS (firm profile).");
        if (vendor is null) return None("No vendor master row.");
        if (!vendor.Tds) return None($"{vendor.Name}: TDS not flagged on the vendor.");
        var code = vendor.Section ?? "";
        var b = TdsRates.BaseSection(code);
        if (b is null) return None($"{vendor.Name}: choose the TDS section on the vendor.");
        var taxable = Taxable(bill.AmountPaise, bill.GstPaise);
        if (taxable <= 0) return None("Nothing taxable on the bill.", code, taxable);
        var row = TdsRates.Row(b)!;
        var bps = TdsRates.RateBps(code, bill.Date, set.Column, overrides);
        var th = TdsRates.ValueOn($"tds.{b}.threshold", bill.Date, overrides);
        var fy = TdsRates.Fy(bill.Date);
        var (from, to) = row.Basis == TdsBasis.Month
            ? (new DateOnly(bill.Date.Year, bill.Date.Month, 1), new DateOnly(bill.Date.Year, bill.Date.Month, 1).AddMonths(1).AddDays(-1))
            : (fy.From, fy.To);
        var earlier = (vendorBills ?? []).Where(e => e.Date >= from && e.Date <= to && e.Date <= bill.Date).ToList();
        var earlierTaxable = earlier.Sum(e => Taxable(e.AmountPaise, e.GstPaise));
        var untaxed = earlier.Where(e => e.TdsPaise == 0).Sum(e => Taxable(e.AmountPaise, e.GstPaise));
        var total = earlierTaxable + taxable;
        var yearTh = row.Basis == TdsBasis.SingleAndYear ? TdsRates.ValueOn($"tds.{b}.thresholdYear", bill.Date, overrides) : th;
        var period = row.Basis == TdsBasis.Month ? "in " + bill.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture) : $"in FY {fy.Label}";
        long deductBase = 0;
        var reason = "";
        if (row.Basis == TdsBasis.SingleAndYear && taxable > th)
        {
            deductBase = taxable;
            reason = $"single bill {Rs(taxable)} above {Rs(th)}";
        }

        if (total > yearTh)
        {
            var already = earlierTaxable > yearTh;
            deductBase = already ? taxable : taxable + untaxed;
            reason = $"{Rs(total)} to {vendor.Name} {period} above {Rs(yearTh)}" + (!already && untaxed > 0 ? $" (earlier bills without TDS {Rs(untaxed)} included)" : "");
        }

        if (deductBase == 0)
            return new(0, code, taxable, 0, bps, $"{Rs(total)} to {vendor.Name} {period} — within the {Rs(row.Basis == TdsBasis.SingleAndYear ? th : yearTh)} threshold");
        var paise = Math.Min(bill.AmountPaise, Round(deductBase, bps));
        return new(paise, code, taxable, deductBase, bps, $"{row.Label} {TdsRates.Pct(bps)} on {Rs(deductBase)} · {reason}");
    }

    /// <summary>
    /// A9 check figure (rulebook T7a / T7b): what a company paying the hotel is expected to deduct — the rent row's other-column rate on the
    /// taxable value excluding GST when the month's taxable billing exceeds the monthly threshold. Deccan Agro ₹60,000 → ₹6,000; ₹40,000 → 0.
    /// </summary>
    public static (long Paise, int Bps, long Threshold) CompanyExpected(long taxablePaise, DateOnly date, long? monthTaxablePaise = null, IReadOnlyList<TdsRateVersion>? overrides = null)
    {
        var threshold = TdsRates.ValueOn("tds.rent.threshold", date, overrides);
        var bps = checked((int)TdsRates.ValueOn("tds.rent.bpsOther", date, overrides));
        var month = monthTaxablePaise ?? taxablePaise;
        return (month > threshold && taxablePaise > 0 ? Round(taxablePaise, bps) : 0, bps, threshold);
    }

    /// <summary>Why a company payment's TDS is refused (null = fine): not negative, at most 20 % of what it settles (amount + tds). Prototype tdsReceivableProblem.</summary>
    public static string? ReceivableProblem(long amountPaise, long tdsPaise)
    {
        if (tdsPaise < 0) return "TDS deducted must be whole paise, zero or more.";
        if (tdsPaise > 0 && checked(tdsPaise * 10000) > checked((amountPaise + tdsPaise) * ReceivableMaxBps))
            return $"TDS {Rs(tdsPaise)} is more than 20 % of the {Rs(amountPaise + tdsPaise)} settled — record what the company actually deducted (Form 168 shows it).";
        return null;
    }
}

/// <summary>A posted bill that carries TDS (the purchase lane's ops.expense tds_paise / tds_section; a reversal carries its negative TDS).</summary>
public sealed record TdsBillRow(long ExpenseId, DateOnly Date, string Vendor, string? Pan, string Section, long TaxablePaise, long TdsPaise);

/// <summary>A TDS remittance (acc.tds_remit): the row of the table, the month the TDS was held back in, the amount.</summary>
public sealed record TdsRemitRow(string Section, string Month, long AmountPaise);

/// <summary>One deductee line of the report: vendor and section, bills, taxable, TDS, remitted (allocated), pending.</summary>
public sealed record TdsDeducteeLine(string Vendor, string? Pan, string Section, int Bills, long TaxablePaise, long TdsPaise, long RemittedPaise, long PendingPaise);

/// <summary>The deductee report (contract §5; prototype tdsDeducteeReport): remitted amounts allocated per row of the table and month, oldest bill first.</summary>
public static class TdsReports
{
    public static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Per vendor and section for the bills dated <paramref name="from"/> … <paramref name="to"/>; <paramref name="bills"/> = every bill with TDS (all
    /// dates, so the allocation of a month's remittance is the same whatever the period asked), <paramref name="remits"/> = the remittances.
    /// </summary>
    public static IReadOnlyList<TdsDeducteeLine> Deductees(IEnumerable<TdsBillRow> bills, IEnumerable<TdsRemitRow> remits, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(remits);
        var pool = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in remits) pool[r.Section + "|" + r.Month] = pool.GetValueOrDefault(r.Section + "|" + r.Month) + r.AmountPaise;
        var rows = bills.Where(b => b.TdsPaise != 0 && TdsRates.BaseSection(b.Section) is not null).OrderBy(b => b.Date).ThenBy(b => b.ExpenseId).ToList();
        var remitted = new Dictionary<long, long>();
        foreach (var b in rows)
        {
            var k = TdsRates.BaseSection(b.Section) + "|" + Month(b.Date);
            var left = pool.GetValueOrDefault(k);
            if (left <= 0 || b.TdsPaise <= 0) continue;
            var take = Math.Min(left, b.TdsPaise);
            remitted[b.ExpenseId] = take;
            pool[k] = left - take;
        }

        return rows.Where(b => b.Date >= from && b.Date <= to)
            .GroupBy(b => (b.Vendor, b.Section))
            .Select(g =>
            {
                var tds = g.Sum(b => b.TdsPaise);
                var rem = g.Sum(b => remitted.GetValueOrDefault(b.ExpenseId));
                return new TdsDeducteeLine(g.Key.Vendor, g.Select(b => b.Pan).FirstOrDefault(p => !string.IsNullOrEmpty(p)), g.Key.Section, g.Count(), g.Sum(b => b.TaxablePaise), tds, rem, tds - rem);
            })
            .OrderBy(l => l.Vendor, StringComparer.Ordinal).ThenBy(l => l.Section, StringComparer.Ordinal).ToList();
    }
}
