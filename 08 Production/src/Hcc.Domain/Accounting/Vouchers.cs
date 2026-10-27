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

/// <summary>A trial-balance row. ACC-1: <paramref name="Control"/> is set on a party row (detail trial balance) — the control it sums into.</summary>
public sealed record TrialBalanceRow(string Account, string Name, AccountGroup Group, long DebitPaise, long CreditPaise, long BalancePaise, string? Control = null);

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
    public static IReadOnlyList<Voucher> Build(DateOnly date, IEnumerable<AccountingSource> sources, IReadOnlyDictionary<string, string> mapping,
        EffectiveChart? chart = null)
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
                var account = ChartOfAccounts.Resolve(mapping, e.MapKey, chart); // ACC-1: the property's chart (null = built-ins)
                if (account is null)
                {
                    if (!miss.Contains(e.MapKey, StringComparer.Ordinal)) miss.Add(e.MapKey);
                    account = ChartOfAccounts.Suspense;
                }

                // ACC-1 (contract §3): the mapping resolves the key, then the party suffix is appended when the ledger is a control ledger.
                if (e.DebitPaise != 0 || e.CreditPaise != 0) entries.Add(new VoucherEntry(PartyLedgers.Compose(account, e.Party), e.DebitPaise, e.CreditPaise));
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

    /// <summary>
    /// Trial balance over vouchers; balance = debit − credit; chart order, unknown codes last. ACC-1 (contract §3, prototype
    /// <c>accTrialBalance</c>): <paramref name="detail"/> false = one row per control ledger (its parties summed into it); true = a row per
    /// party (<see cref="TrialBalanceRow.Control"/> set, name "control name · party id") right after its control's own row (present only when
    /// something posted to the control itself). Both tie.
    /// </summary>
    public static IReadOnlyList<TrialBalanceRow> TrialBalance(IEnumerable<Voucher> vouchers, bool detail = false)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var m = new Dictionary<string, (long D, long C)>(StringComparer.Ordinal);
        foreach (var v in vouchers)
            foreach (var e in v.Entries)
            {
                var key = detail ? e.Account : PartyLedgers.ControlOf(e.Account);
                var x = m.GetValueOrDefault(key);
                m[key] = (checked(x.D + e.DebitPaise), checked(x.C + e.CreditPaise));
            }

        return m.OrderBy(kv => ChartOfAccounts.OrderOf(PartyLedgers.ControlOf(kv.Key))).ThenBy(kv => PartyLedgers.ControlOf(kv.Key), StringComparer.Ordinal)
            .ThenBy(kv => PartyLedgers.IsParty(kv.Key) ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                var control = PartyLedgers.ControlOf(kv.Key);
                var party = PartyLedgers.IsParty(kv.Key);
                return new TrialBalanceRow(kv.Key, party ? ChartOfAccounts.NameOf(control) + " · " + PartyLedgers.PartyOf(kv.Key) : ChartOfAccounts.NameOf(kv.Key),
                    ChartOfAccounts.Find(control)?.Group ?? AccountGroup.Suspense, kv.Value.D, kv.Value.C, kv.Value.D - kv.Value.C, party ? control : null);
            })
            .ToList();
    }

    /// <summary>
    /// ACC-1 (contract §3): <c>no-party</c> warnings — an entry on a control ledger itself (the source did not name the party: an event
    /// published before ACC-1, a method with no settles-to account). One per voucher and control; never blocks a lock.
    /// </summary>
    public static IEnumerable<AccountingIssue> NoPartyWarnings(IEnumerable<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        foreach (var v in vouchers)
            foreach (var c in v.Entries.Select(e => e.Account).Where(PartyLedgers.IsControl).Distinct(StringComparer.Ordinal))
                yield return new("noparty:" + v.Number + ":" + c, "warning", "no-party", $"{v.Number} posts to the control ledger {c} {ChartOfAccounts.NameOf(c)}",
                    "The source does not name the party (an old record, or a method with no settles-to account), so the amount sits on the control ledger, not on a party sub-ledger. It does not block the lock.",
                    v.Number, v.SourceKey);
    }

    /// <summary>Source ids with their debit totals (prototype fingerprint): differs when a locked day's sources changed, not when only the mapping did.</summary>
    public static string Fingerprint(IEnumerable<Voucher> vouchers)
        => string.Join("|", vouchers.Select(v => v.SourceKey + "=" + v.Debit.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));

    /// <summary>Title of the exception for parked (stuck) accounting events (Wave C3; the prototype's mock uses the same text).</summary>
    public const string StuckTitle = "Accounting events stuck — see System health";

    /// <summary>
    /// Exceptions for a date (spec §6): unbalanced voucher / day (error), unmapped key (error), snapshot drift (warning),
    /// events parked after repeated failures (error "stuck", production), events still pending (error, production), day open (info).
    /// </summary>
    public static IReadOnlyList<AccountingIssue> Exceptions(IReadOnlyList<Voucher> shown, IReadOnlyList<Voucher>? live, bool locked, DayStatus status, int pendingEvents = 0,
        int parkedEvents = 0)
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

        if (!locked) out_.AddRange(NoPartyWarnings(shown)); // ACC-1: not on a locked snapshot (frozen as locked)
        var (d, c) = Totals(shown);
        if (d != c) out_.Add(new("unbal:day", "error", "unbalanced", "Trial balance does not agree", $"Debits ₹{SourceRules.Rupees(d)} ≠ credits ₹{SourceRules.Rupees(c)}"));
        if (locked && live is not null && Fingerprint(shown) != Fingerprint(live))
            out_.Add(new("drift", "warning", "drift", "Live sources differ from the locked snapshot",
                "Source records for this locked date changed after locking. The snapshot is kept; review and unlock if a correction is needed."));
        // Wave C3 (fix wave A "Left open"): a parked event is not "still being processed" — it waits for a person to retry it.
        if (parkedEvents > 0 && !locked)
            out_.Add(new("stuck", "error", "stuck", StuckTitle,
                $"{parkedEvents} posting event(s) for this property could not be booked after repeated attempts and were set aside. The Owner or the Tech admin can retry them in Admin & Setup › System health; this date cannot be locked until they are booked."));
        if (pendingEvents > 0 && !locked)
            out_.Add(new("pending", "error", "pending", "Accounting events still being processed",
                $"{pendingEvents} posting event(s) for this property are not yet in the vouchers. Wait a moment, then lock."));
        if (status == DayStatus.Open)
            out_.Add(new("open", "info", "open-day", "Business day not closed", "Vouchers are provisional until the day is closed and locked."));
        return out_;
    }
}
