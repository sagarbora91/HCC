using System.Globalization;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

// ACC-2 · lane acc2-parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §7 item 8): the aggregated opening read of the
// reports. A closed month whose every day with accounting activity is locked holds fixed vouchers (the lock snapshots); its totals per ledger
// id are materialised once in acc.ledger_month_balance (migration 4257) and a report's opening = Σ those month rows + the vouchers of the days
// after the last stored month, instead of every voucher since the first source.

/// <summary>The fingerprint of a month's current period locks: how many, and the sum of their lock ids (an unlock / re-lock makes a new id).</summary>
public readonly record struct MonthLockPrint(int Count, long Sum);

/// <summary>
/// What a report's opening may take from the month rows: every month starting before <see cref="Cut"/> is stored and fixed, and
/// <see cref="Entries"/> = their totals per ledger id; the vouchers from <see cref="Cut"/> on are read day by day.
/// </summary>
public sealed record MonthOpening(DateOnly Cut, IReadOnlyList<VoucherEntry> Entries, int Months);

/// <summary>Read port over acc.ledger_month(_balance): builds the stored months that are missing or stale (lazily, idempotent) and sums them.</summary>
public interface ILedgerMonthBalanceStore
{
    /// <summary>The month opening for a report starting on <paramref name="from"/> on a property whose business date is <paramref name="businessDate"/>.</summary>
    Task<MonthOpening> OpeningAsync(int propertyId, DateOnly from, DateOnly businessDate, DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>The pure rules of the month rows.</summary>
public static class LedgerMonthBalances
{
    /// <summary>Source sub of the synthetic opening voucher that carries the month rows (an opening voucher, <see cref="AccountingReports.IsOpening"/>).</summary>
    public const string Sub = "month-balances";

    public static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Months may be stored only when they end before the report's month and before the business date's month (a closed month).</summary>
    public static DateOnly Limit(DateOnly from, DateOnly businessDate)
    {
        var a = MonthStart(from);
        var b = MonthStart(businessDate);
        return a < b ? a : b;
    }

    /// <summary>
    /// Which months the opening can take as stored rows. Walks the months from the first with activity up to <paramref name="limit"/>: the first
    /// month with a day that has sources but no current lock stops the walk (<c>Cut</c> = that month); every month before it is used, and the
    /// ones whose stored fingerprint is missing or differs from <paramref name="current"/> are (re)built first (<c>Build</c>).
    /// </summary>
    public static (DateOnly Cut, IReadOnlyList<DateOnly> Build) Plan(DateOnly? firstActivity, DateOnly limit, IReadOnlySet<DateOnly> monthsWithUnlockedDays,
        IReadOnlyDictionary<DateOnly, MonthLockPrint> current, IReadOnlyDictionary<DateOnly, MonthLockPrint> stored)
    {
        ArgumentNullException.ThrowIfNull(monthsWithUnlockedDays);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(stored);
        var build = new List<DateOnly>();
        if (firstActivity is not { } first) return (limit, build);
        for (var m = MonthStart(first); m < limit; m = m.AddMonths(1))
        {
            if (monthsWithUnlockedDays.Contains(m)) return (m, build);
            var now = current.TryGetValue(m, out var c) ? c : default;
            if (!stored.TryGetValue(m, out var s) || s != now) build.Add(m);
        }

        return (limit, build);
    }

    /// <summary>The month rows as one opening Journal dated the day before <see cref="MonthOpening.Cut"/> (null when there is nothing to carry).</summary>
    public static Voucher? Voucher(MonthOpening? o)
    {
        if (o is null || o.Entries.Count == 0) return null;
        return new Voucher("MONTH-BALANCES", o.Cut.AddDays(-1), VoucherType.Journal, string.Create(CultureInfo.InvariantCulture, $"Balances brought forward · {o.Months} locked month(s) before {o.Cut:yyyy-MM-dd}"),
            "MONTH-BALANCES", "opening", Sub, o.Entries, []);
    }
}
