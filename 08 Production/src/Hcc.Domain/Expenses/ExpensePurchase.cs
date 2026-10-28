using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Expenses;

/// <summary>
/// ACC-2 · lane acc2-purchase — purchase rules shared by the expense and vendor-payment commands (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3;
/// rulebook §4.4–4.5, C1, C3–C5, C8, C10; DEC-143, DEC-145). Twin of the prototype <c>src/domain/streams/expenses-vendor.ts</c> (same messages,
/// same rounding). No I/O.
/// </summary>
public static class ExpensePurchase
{
    /// <summary>The hotel's own GST state (DEC-136): a vendor with another two-letter state code bills IGST.</summary>
    public const string HomeState = "MH";

    public const string Intra = "intra";
    public const string Inter = "inter";

    /// <summary>Ledgers whose ITC is always refused on a capital purchase (rulebook §4.5 / C10: motor vehicles, construction).</summary>
    public static readonly IReadOnlyList<string> ItcBlockedAssetLedgers = ["1500", "1540"];

    public static string ItcBlockedMessage(string category) =>
        $"ITC cannot be allowed on {category}: the category is marked blocked (CGST s.17(5)) — DEC-143: ITC only on a bill directly for an 18 % supply; this GST stays in the cost.";

    public const string ReturnNotReversible = "A posted vendor return cannot be reversed — record the goods again as a new bill.";

    public const string RcmGstMessage = "A reverse-charge bill carries no vendor GST (DEC-145): the hotel pays the GST itself under reverse charge — clear the GST on the bill.";

    /// <summary>TDS rate-table rows a vendor can be deducted under (acc2-tds lane's TdsRates keys; CA to confirm).</summary>
    public static readonly IReadOnlyList<string> TdsSections = ["rent", "contractor", "professional", "commission"];

    /// <summary>GST kind from the vendor's state: another state = inter (IGST), MH / none = intra.</summary>
    public static string GstKindOf(string? state)
    {
        var st = (state ?? string.Empty).Trim().ToUpperInvariant();
        return st.Length > 0 && st != HomeState ? Inter : Intra;
    }

    /// <summary>Why ITC cannot be allowed on this category (null = it can): the s.17(5) flag (DEC-143) or a capital vehicle / building.</summary>
    public static string? ItcBlockRefusal(ExpenseCategoryRow? category, bool itcAllowed)
    {
        if (!itcAllowed || category is null) return null;
        return category.ItcBlocked || (category.Capital && ItcBlockedAssetLedgers.Contains(category.Ledger ?? string.Empty)) ? ItcBlockedMessage(category.Name) : null;
    }

    /// <summary>Entry-time purchase fields (prototype <c>expPurchaseEntry</c>).</summary>
    public sealed record EntryFields(string? GstKind, bool Rcm, int? RcmBps, bool ItcAllowed);

    /// <summary>Entry-time purchase fields (prototype <c>expPurchaseEntry</c>): the GST kind (sent, else the vendor's state — kept only when sent or
    /// inter); for an RCM category the RCM flag and rate (the typed override 0–28 %, else the category default — re-stamped at approval) and the
    /// ITC flag of the RCM GST (kept without vendor GST). Throws the refusal.</summary>
    public static EntryFields Entry(ExpenseCategoryRow? category, ExpenseVendorRow? vendor, string? gstKind, int? rcmBps, long? gstPaise,
        bool itcAllowed, string? itcRequested)
    {
        var k = (gstKind ?? string.Empty).Trim();
        if (k.Length > 0 && k is not (Intra or Inter)) throw HccException.Validation("Choose the GST on the bill: intra-state (CGST + SGST) or inter-state (IGST).");
        var kind = k.Length > 0 ? k : GstKindOf(vendor?.State);
        string? stored = k.Length > 0 || kind == Inter ? kind : null;
        int? bps = null;
        var itc = itcAllowed;
        var rcm = category?.Rcm == true;
        if (rcm)
        {
            if (gstPaise is not null and not 0) throw HccException.Validation(RcmGstMessage);
            if (rcmBps is { } b && b is < 0 or > 2800) throw HccException.Validation("Reverse-charge GST rate: 0 to 28 %.");
            bps = rcmBps ?? category!.RcmBps ?? 0;
            itc = string.Equals(itcRequested, ExpenseScc.ItcAllowed, StringComparison.Ordinal);
        }

        if (ItcBlockRefusal(category, itc) is { } why) throw HccException.Validation(why);
        return new EntryFields(stored, rcm, bps, itc);
    }

    /// <summary>The fields stamped at approval (prototype <c>expPurchaseStamp</c>): RCM + rate (the bill's own, else the category default), capital.
    /// Refuses ITC on a blocked category and vendor GST on an RCM bill.</summary>
    public static (bool Rcm, int? RcmBps, bool Capital) Stamp(ExpenseCategoryRow? category, long? gstPaise, int? recordRcmBps, bool itcAllowed)
    {
        if (ItcBlockRefusal(category, itcAllowed) is { } why) throw HccException.Validation(why);
        if (category?.Rcm == true && gstPaise is not null and not 0) throw HccException.Validation(RcmGstMessage);
        return category?.Rcm == true ? (true, recordRcmBps ?? category.RcmBps ?? 0, category.Capital) : (false, null, category?.Capital == true);
    }

    /// <summary>The ITC of a record's GST and its RCM GST (prototype <c>expPurchaseLines</c>): ITC on the bill only when not RCM; RCM = amount × rate.</summary>
    public static (long Itc, long Rcm, bool RcmItc) Lines(long amountPaise, long? gstPaise, bool itcAllowed, bool rcm, int? rcmBps)
    {
        var itc = !rcm && itcAllowed ? gstPaise ?? 0 : 0;
        var r = rcm ? SourceRules.BpsOf(amountPaise, rcmBps ?? 0) : 0;
        return (itc, r, r != 0 && itcAllowed);
    }

    // ── Vendor payments, open bills, ageing (contract §3.2) ──

    /// <summary>One open bill of a vendor: payable = bill − TDS, less posted returns and allocations (floored at 0).</summary>
    public sealed record OpenBill(long ExpenseId, DateOnly Date, long Payable, long Returned, long Allocated)
    {
        public long Open => Math.Max(0, Payable - Returned - Allocated);
    }

    /// <summary>Oldest-first allocation of <paramref name="amount"/> to the open bills (date, then id); the rest is an advance.</summary>
    public static IReadOnlyList<(long ExpenseId, long Amount)> Allocate(IEnumerable<OpenBill> bills, long amount)
    {
        ArgumentNullException.ThrowIfNull(bills);
        var left = Math.Max(0, amount);
        var list = new List<(long, long)>();
        foreach (var b in bills.OrderBy(b => b.Date).ThenBy(b => b.ExpenseId))
        {
            if (left <= 0) break;
            var a = Math.Min(left, b.Open);
            if (a <= 0) continue;
            list.Add((b.ExpenseId, a));
            left -= a;
        }

        return list;
    }

    /// <summary>Refusal of an explicit allocation list (null = fine): each an open bill of the vendor, at most its open amount, Σ ≤ the payment.</summary>
    public static string? AllocationRefusal(IEnumerable<OpenBill> bills, long amount, IReadOnlyList<(long ExpenseId, long Amount)> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var open = bills.Where(b => b.Open > 0).ToDictionary(b => b.ExpenseId, b => b.Open);
        var seen = new HashSet<long>();
        long total = 0;
        foreach (var (id, a) in allocations)
        {
            if (!open.TryGetValue(id, out var o)) return $"Bill {id} is not an open bill of this vendor.";
            if (!seen.Add(id)) return $"Bill {id} is allocated twice.";
            if (a <= 0) return $"Enter a positive amount for bill {id}.";
            if (a > o) return $"Bill {id} has only {SourceRules.Rupees(o)} open.";
            total = checked(total + a);
        }

        return total > amount ? "The allocations add up to more than the payment." : null;
    }

    public static readonly IReadOnlyList<string> AgeBuckets = ["0–30", "31–60", "61–90", "90+"];

    /// <summary>Ageing bucket of a bill <paramref name="days"/> old (prototype <c>expAgeBucket</c>).</summary>
    public static string AgeBucket(int days) => days <= 30 ? "0–30" : days <= 60 ? "31–60" : days <= 90 ? "61–90" : "90+";

    /// <summary>A void on the payment's own business day posts nothing; a later void posts the reversal on the void date.</summary>
    public static bool SameDayVoid(DateOnly paymentDate, DateOnly voidDate) => paymentDate == voidDate;

    // ── Returns (contract §3.5) ──

    /// <summary>Refusal of a return (null = fine): at most the bill amount and its GST less what was already returned; GST ≤ the amount.</summary>
    public static string? ReturnRefusal(long billAmount, long? billGst, long returnedAmount, long returnedGst, long amount, long gst)
    {
        if (amount <= 0) return "Enter the amount returned (more than zero).";
        if (gst < 0) return "Enter the GST on the credit note in rupees (0 or more).";
        if (amount > billAmount - returnedAmount) return $"At most {SourceRules.Rupees(billAmount - returnedAmount)} of this bill is left to return.";
        if (gst > (billGst ?? 0) - returnedGst) return $"At most {SourceRules.Rupees(Math.Max(0, (billGst ?? 0) - returnedGst))} of the bill's GST is left to reverse.";
        return gst > amount ? "The GST cannot be more than the amount returned." : null;
    }

    // ── Fixed-asset register (contract §3.4) ──

    /// <summary>Next register number for a purchase dated <paramref name="date"/>: FA-&lt;yyyy&gt;-&lt;nnn&gt; after the highest of that year.</summary>
    public static string NextAssetNo(DateOnly date, IEnumerable<string> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var pre = "FA-" + date.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + "-";
        var max = existing.Where(n => n.StartsWith(pre, StringComparison.Ordinal))
            .Select(n => int.TryParse(n[pre.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0)
            .DefaultIfEmpty(0).Max();
        return pre + (max + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A fixed-asset block ledger (15x0 in Tally's Fixed Assets group).</summary>
    public static bool IsAssetLedger(string? code) => code is { Length: 4 } c && c.StartsWith("15", StringComparison.Ordinal) && c[3] == '0' && char.IsDigit(c[2]);
}
