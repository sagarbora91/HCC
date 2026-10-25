using System.Globalization;

namespace Hcc.Domain.Accounting;

public sealed record VoucherEntry(string Account, long DebitPaise, long CreditPaise);

/// <summary>A derived (or locked) voucher. Numbers are provisional until the day is locked (spec §5).</summary>
public sealed record Voucher(string Number, DateOnly Date, VoucherType Type, string Narration, string SourceKey, string SourceKind, string Sub,
    IReadOnlyList<VoucherEntry> Entries, IReadOnlyList<string> Unmapped)
{
    public long Debit => Entries.Sum(e => e.DebitPaise);

    public long Credit => Entries.Sum(e => e.CreditPaise);

    public bool Balanced => Debit == Credit;
}

public sealed record TrialBalanceRow(string Account, string Name, AccountGroup Group, long DebitPaise, long CreditPaise, long BalancePaise);

public enum DayStatus
{
    Open,
    Closed,
    Locked,
}

public sealed record AccountingIssue(string Id, string Severity, string Kind, string Title, string Detail, string? Voucher = null, string? Source = null, string? Key = null);

/// <summary>Voucher derivation, totals, trial balance and exceptions (prototype accLiveVouchers / accTrialBalance / accExceptions).</summary>
public static class VoucherBuilder
{
    private static readonly VoucherType[] TypeOrder = [VoucherType.Sales, VoucherType.Receipt, VoucherType.Payment, VoucherType.Journal];

    public static string Prefix(VoucherType t) => t switch
    {
        VoucherType.Sales => "SAL",
        VoucherType.Receipt => "RCT",
        VoucherType.Payment => "PMT",
        _ => "JRN",
    };

    /// <summary>
    /// Vouchers for one business date from its sources (in arrival order) with the effective mapping. Unmapped keys post to 9999
    /// Suspense; zero entries are dropped and an all-zero source yields no voucher; numbering is per type in source order
    /// (<c>SAL|RCT|PMT|JRN/yyyymmdd/nnn</c>), types in the order Sales, Receipt, Payment, Journal.
    /// </summary>
    public static IReadOnlyList<Voucher> Build(DateOnly date, IEnumerable<AccountingSource> sources, IReadOnlyDictionary<string, string> mapping)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(mapping);
        var built = new List<Voucher>();
        foreach (var s in sources.Where(x => x.BusinessDate == date))
        {
            var miss = new List<string>();
            var entries = new List<VoucherEntry>();
            foreach (var e in s.Entries)
            {
                var account = ChartOfAccounts.Resolve(mapping, e.MapKey);
                if (account is null)
                {
                    if (!miss.Contains(e.MapKey, StringComparer.Ordinal)) miss.Add(e.MapKey);
                    account = ChartOfAccounts.Suspense;
                }

                if (e.DebitPaise != 0 || e.CreditPaise != 0) entries.Add(new VoucherEntry(account, e.DebitPaise, e.CreditPaise));
            }

            if (entries.Count > 0) built.Add(new Voucher("", date, s.Type, s.Narration, s.SourceKey, s.Kind, s.Sub, entries, miss));
        }

        var ymd = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var result = new List<Voucher>(built.Count);
        foreach (var t in TypeOrder)
        {
            var n = 0;
            foreach (var v in built.Where(x => x.Type == t))
                result.Add(v with { Number = $"{Prefix(t)}/{ymd}/{(++n).ToString("000", CultureInfo.InvariantCulture)}" });
        }

        return result;
    }

    public static (long Debit, long Credit) Totals(IEnumerable<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        long d = 0, c = 0;
        foreach (var v in vouchers)
        {
            d = checked(d + v.Debit);
            c = checked(c + v.Credit);
        }

        return (d, c);
    }

    /// <summary>Trial balance over vouchers; balance = debit − credit; chart order, unknown codes last.</summary>
    public static IReadOnlyList<TrialBalanceRow> TrialBalance(IEnumerable<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var m = new Dictionary<string, (long D, long C)>(StringComparer.Ordinal);
        foreach (var v in vouchers)
            foreach (var e in v.Entries)
            {
                var x = m.GetValueOrDefault(e.Account);
                m[e.Account] = (checked(x.D + e.DebitPaise), checked(x.C + e.CreditPaise));
            }

        return m.OrderBy(kv => ChartOfAccounts.OrderOf(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new TrialBalanceRow(kv.Key, ChartOfAccounts.NameOf(kv.Key), ChartOfAccounts.Find(kv.Key)?.Group ?? AccountGroup.Suspense,
                kv.Value.D, kv.Value.C, kv.Value.D - kv.Value.C))
            .ToList();
    }

    /// <summary>Source ids with their debit totals (prototype fingerprint): differs when a locked day's sources changed, not when only the mapping did.</summary>
    public static string Fingerprint(IEnumerable<Voucher> vouchers)
        => string.Join("|", vouchers.Select(v => v.SourceKey + "=" + v.Debit.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));

    /// <summary>
    /// Exceptions for a date (spec §6): unbalanced voucher / day (error), unmapped key (error), snapshot drift (warning),
    /// events still pending (error, production), day open (info).
    /// </summary>
    public static IReadOnlyList<AccountingIssue> Exceptions(IReadOnlyList<Voucher> shown, IReadOnlyList<Voucher>? live, bool locked, DayStatus status, int pendingEvents = 0)
    {
        ArgumentNullException.ThrowIfNull(shown);
        var out_ = new List<AccountingIssue>();
        foreach (var v in shown)
        {
            if (!v.Balanced)
                out_.Add(new("unbal:" + v.Number, "error", "unbalanced", $"Voucher {v.Number} is unbalanced",
                    $"Debits ₹{SourceRules.Rupees(v.Debit)} ≠ credits ₹{SourceRules.Rupees(v.Credit)}", v.Number, v.SourceKey));
            foreach (var k in v.Unmapped)
                out_.Add(new($"unmap:{v.Number}:{k}", "error", "unmapped", $"No ledger mapped for “{ChartOfAccounts.KeyValue(k)}”",
                    $"{ChartOfAccounts.KeyGroup(k)} → posted to Suspense in {v.Number}. Map it in Mapping, then lock.", v.Number, v.SourceKey, k));
        }

        var (d, c) = Totals(shown);
        if (d != c) out_.Add(new("unbal:day", "error", "unbalanced", "Trial balance does not agree", $"Debits ₹{SourceRules.Rupees(d)} ≠ credits ₹{SourceRules.Rupees(c)}"));
        if (locked && live is not null && Fingerprint(shown) != Fingerprint(live))
            out_.Add(new("drift", "warning", "drift", "Live sources differ from the locked snapshot",
                "Source records for this locked date changed after locking. The snapshot is kept; review and unlock if a correction is needed."));
        if (pendingEvents > 0 && !locked)
            out_.Add(new("pending", "error", "pending", "Accounting events still being processed",
                $"{pendingEvents} posting event(s) for this property are not yet in the vouchers. Wait a moment, then lock."));
        if (status == DayStatus.Open)
            out_.Add(new("open", "info", "open-day", "Business day not closed", "Vouchers are provisional until the day is closed and locked."));
        return out_;
    }
}
