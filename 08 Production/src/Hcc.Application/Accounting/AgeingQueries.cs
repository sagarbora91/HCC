using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Import;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>
/// Read port for ACC-2's explicit vendor-payment allocations (<c>ops.vendor_payment_alloc</c>): payment voucher source key
/// (<c>vendor-payment:&lt;id&gt;</c>) → (bill ref, paise) in allocation order; bill ref = <c>expense:&lt;id&gt;</c> or <c>opening</c>.
/// Only payments that are not void are listed. Absent = oldest-first only.
/// </summary>
public interface IAgeingAllocationSource
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>> GetAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>One credit on a party ledger: source key, date, amount and ACC-2's explicit allocations.</summary>
public sealed record AgeingCredit(string Key, DateOnly Date, long Amount, IReadOnlyList<(string Ref, long Paise)>? Explicit = null);

/// <summary>
/// ACC-3 acc3-ageing: outstanding with ageing (rulebook §7.6; contract ACC3 §3; prototype <c>accounting-ageing.ts</c>). Receivables = parties under
/// 1100 / 1110 / 1115 / 1120, payables = 2200. Each Dr entry on a receivable party (Cr on a payable) is a bill (ref = source key; an opening voucher
/// = <c>opening</c>, always the oldest, DEC-172); credits apply first by ACC-2's explicit allocations, the rest oldest-first; a later payment void
/// (<c>…/void</c>) cancels its own payment. Row open = the ledger balance on asOf; Σ buckets ≠ open → flagged with a note.
/// </summary>
public static class AgeingRules
{
    public const string Receivable = "receivable";
    public const string Payable = "payable";
    public const string OpeningRef = "opening";

    public static IReadOnlyList<string> Controls(string side) => side == Payable ? ["2200"] : ["1100", "1110", "1115", "1120"];

    public static string Bucket(int days) => days <= 30 ? "0-30" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : "90+";

    private sealed class Bill(string reference, DateOnly date, long amount, int order)
    {
        public string Ref { get; } = reference;
        public DateOnly Date { get; } = date;
        public long Amount { get; set; } = amount;
        public long Paid { get; set; }
        public long Open { get; set; } = amount;
        public int Order { get; } = order;
    }

    /// <summary>Bill-wise allocation of one party ledger (bills in posting order). Returns the bills still open (≠ 0) and the unallocated credit.</summary>
    public static (IReadOnlyList<AgeingBillView> Bills, long Advance) Allocate(DateOnly asOf, IEnumerable<(string Ref, DateOnly Date, long Amount)> bills0, IReadOnlyList<AgeingCredit> credits)
    {
        ArgumentNullException.ThrowIfNull(bills0);
        ArgumentNullException.ThrowIfNull(credits);
        var bills = new List<Bill>();
        var byRef = new Dictionary<string, Bill>(StringComparer.Ordinal);
        var i = 0;
        foreach (var b in bills0)
        {
            if (byRef.TryGetValue(b.Ref, out var x))
            {
                x.Amount = checked(x.Amount + b.Amount);
                x.Open = checked(x.Open + b.Amount);
            }
            else
            {
                var n = new Bill(b.Ref, b.Date, b.Amount, b.Ref == OpeningRef ? -1 : i);
                bills.Add(n);
                byRef[b.Ref] = n;
            }

            i++;
        }

        static long Pay(Bill b, long max)
        {
            var n = Math.Min(Math.Max(0, b.Open), Math.Max(0, max));
            if (n > 0)
            {
                b.Paid += n;
                b.Open -= n;
            }

            return n;
        }

        long pool = 0;
        foreach (var c in credits)
        {
            var left = c.Amount;
            foreach (var a in c.Explicit ?? [])
            {
                if (left <= 0) break;
                if (byRef.TryGetValue(a.Ref, out var b)) left -= Pay(b, Math.Min(a.Paise, left));
            }

            pool = checked(pool + Math.Max(0, left));
        }

        foreach (var b in bills.OrderBy(b => b.Order == -1 ? 0 : 1).ThenBy(b => b.Date).ThenBy(b => b.Order))
        {
            if (pool <= 0) break;
            pool -= Pay(b, pool);
        }

        return (bills.Where(b => b.Open != 0).Select(b => new AgeingBillView(b.Ref, b.Date, b.Date, b.Amount, b.Paid, b.Open, asOf.DayNumber - b.Date.DayNumber)).ToList(), pool);
    }

    private static string Rs(long p) => "₹" + (p / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The ageing of one side from every voucher up to and including <paramref name="asOf"/> (opening entry included).</summary>
    public static AgeingView Build(DateOnly asOf, string side, IEnumerable<Voucher> vouchers, LedgerNames? names = null,
        IReadOnlyDictionary<string, IReadOnlyList<(string Ref, long Paise)>>? allocations = null)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        names ??= LedgerNames.Chart;
        var controls = Controls(side);
        long sign = side == Receivable ? 1 : -1;
        var per = new Dictionary<string, (long Balance, List<(string Ref, DateOnly Date, long Amount)> Bills, List<AgeingCredit> Credits)>(StringComparer.Ordinal);
        foreach (var v in vouchers.Where(v => v.Date <= asOf).OrderBy(v => v.Date).ThenBy(v => AccountingReports.IsOpening(v) ? 0 : 1).ThenBy(v => v.Number, StringComparer.Ordinal))
        {
            foreach (var e in v.Entries)
            {
                if (!controls.Contains(AccountingReports.ControlOf(e.Account))) continue;
                var amt = checked(sign * (e.DebitPaise - e.CreditPaise));
                if (amt == 0) continue;
                if (!per.TryGetValue(e.Account, out var x)) per[e.Account] = x = (0, [], []);
                x.Balance = checked(x.Balance + amt);
                per[e.Account] = x;
                var reference = AccountingReports.IsOpening(v) ? OpeningRef : v.SourceKey;
                if (amt > 0 && v.SourceKey.EndsWith("/void", StringComparison.Ordinal))
                {
                    var k = v.SourceKey[..^5];
                    var ci = x.Credits.FindIndex(c => c.Key == k && c.Amount > 0);
                    if (ci >= 0)
                    {
                        var c = x.Credits[ci];
                        var n = Math.Min(c.Amount, amt);
                        x.Credits[ci] = c with { Amount = c.Amount - n };
                        if (n < amt) x.Bills.Add((reference, v.Date, amt - n));
                        continue;
                    }
                }

                if (amt > 0) x.Bills.Add((reference, v.Date, amt));
                else x.Credits.Add(new AgeingCredit(v.SourceKey, v.Date, -amt,
                    side == Payable && allocations is not null && allocations.TryGetValue(v.SourceKey, out var al) ? al : null));
            }
        }

        var rows = new List<AgeingRowView>();
        foreach (var (account, x) in per)
        {
            var (bills, advance) = Allocate(asOf, x.Bills, x.Credits);
            var open = bills.Where(b => b.Open > 0).ToList();
            long b30 = 0, b60 = 0, b90 = 0, b90p = 0;
            foreach (var b in open)
                switch (Bucket(b.Days))
                {
                    case "0-30": b30 += b.Open; break;
                    case "31-60": b60 += b.Open; break;
                    case "61-90": b90 += b.Open; break;
                    default: b90p += b.Open; break;
                }

            var sum = b30 + b60 + b90 + b90p;
            if (x.Balance == 0 && sum == 0) continue;
            var party = AccountingReports.PartyOf(account) ?? "";
            var name = party.Length > 0 ? names.PartyName(account) : names.Name(account);
            var flagged = sum != x.Balance;
            string? note = !flagged ? null
                : advance > 0 ? $"{(side == Receivable ? "Advance received" : "Advance paid")} {Rs(advance)} not allocated to any bill; ledger balance {Rs(x.Balance)} vs open bills {Rs(sum)}."
                : $"Ledger balance {Rs(x.Balance)} does not tie to the open bills {Rs(sum)}.";
            rows.Add(new AgeingRowView(account, party, name, AccountingReports.ControlOf(account), open, x.Balance, b30, b60, b90, b90p, flagged, note, flagged ? advance : 0));
        }

        rows = [.. rows.OrderBy(r => r.Control, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Account, StringComparer.Ordinal)];
        var t = new AgeingTotalsView(rows.Sum(r => r.Open), rows.Sum(r => r.B0_30), rows.Sum(r => r.B31_60), rows.Sum(r => r.B61_90), rows.Sum(r => r.B90));
        return new AgeingView(asOf, side, rows, t);
    }
}

/// <summary>
/// ACC-3 acc3-ageing: <c>GET /accounting/ageing?asOf&amp;side</c> over the postings (contract §1: the same voucher read as
/// <see cref="AccountingReportQueries"/> — every day since the first source, lock snapshots for locked dates, plus the opening entry).
/// Permission = the trial balance's (<see cref="AccountingPermissions.View"/>).
/// </summary>
public sealed class AgeingQueries(IAccountingStore store, IAccessPolicy policy, IOpeningBalanceSource? imported = null, IReportOpeningSource? openingEntry = null,
    IReportPartyNames? partyNames = null, IAgeingAllocationSource? allocations = null)
{
    public async Task<AgeingView> AgeingAsync(Caller caller, DateOnly asOf, string? side, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var sd = (side ?? AgeingRules.Receivable).Trim().ToLowerInvariant();
        if (sd is not (AgeingRules.Receivable or AgeingRules.Payable)) throw HccException.Validation("Choose a side: receivable or payable.");
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, DateOnly.MinValue, asOf, ct).ConfigureAwait(false);
        var all = days.SelectMany(d => d.Vouchers).ToList();
        var extra = new Dictionary<string, (string Name, string Group)>(StringComparer.Ordinal);
        var entry = openingEntry is null ? null : await openingEntry.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        if (entry?.Voucher is { } ov) all.Add(ov);
        else if (imported is not null && AccountingReports.ImportedVoucher(await imported.GetAsync(caller.PropertyId, ct).ConfigureAwait(false), extra) is { } iv) all.Add(iv);
        var parties = partyNames is null ? null : await partyNames.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var alloc = allocations is null ? null : await allocations.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return AgeingRules.Build(asOf, sd, all, new LedgerNames(parties, extra, chart), alloc);
    }
}
