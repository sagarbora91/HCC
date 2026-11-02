using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

// ACC-4 · Year lock, carry-forward, reopen (02 Planning/agents/ACC4-CONTRACT.md §1; rulebook G3 step 5–6, G4; DEC-077 extended to a year).
// Prototype twin: 03 Prototype/src/domain/streams/accounting-year.ts + services/mock/handlers/accounting-year.ts (same figures, same rules).
// Contract codes travel as details.reason on INVALID_TRANSITION / VALIDATION (ACC_YEAR_LOCKED, ACC_YEAR_NOT_READY, ACC_YEAR_REOPEN_BLOCKED,
// ACC_CF_UNBALANCED). Tables: migrations 4280–4284.

/// <summary>PERM ids (migration 4284): prepare = Owner + Accountant; lock / reopen / relock = Owner.</summary>
public static class AccountingYearPermissions
{
    public const string Prepare = "PERM-ACC-014"; // accounting.year.preview / carry-forward draft
    public const string Lock = "PERM-ACC-015";    // accounting.year.lock / reopen / relock
}

public static class YearStatus
{
    public const string Open = "open", Closing = "closing", Locked = "locked", Reopened = "reopened";
}

public sealed record FiscalYearState(string Fy, string Status, DateTime? LockedUtc = null, int? LockedBy = null, DateTime? ReopenedUtc = null, int? ReopenedBy = null,
    string? ReopenReason = null, bool ProvisionalExports = false, string? CarryForwardId = null);

public sealed record CarryForwardLine(string Ledger, string? Party, long Dr, long Cr)
{
    public string Account => Party is null ? Ledger : $"{Ledger}/{Party}";
}

public sealed record CarryForward(string Id, string FromFy, string ToFy, string Status, IReadOnlyList<CarryForwardLine> Lines, long ProfitToCapital, long DrawingsToCapital,
    IReadOnlyList<VoucherEntry> Close, DateTime GeneratedUtc, int GeneratedBy, int? ApprovedBy = null, DateTime? ApprovedUtc = null, string? SupersededBy = null);

public sealed record CarryForwardDraft(IReadOnlyList<CarryForwardLine> Lines, long ProfitToCapital, long DrawingsToCapital, IReadOnlyList<VoucherEntry> Close, long Debit, long Credit)
{
    public bool Balanced => Debit == Credit;
}

public sealed record YearCheck(string Id, bool Ok, string Label, string Detail);

/// <summary>Inputs of the preview checklist gathered by the store (counts / balances at 31 March).</summary>
public sealed record YearCheckInput(bool FyEnded, IReadOnlyList<DateOnly> UnlockedDays, IReadOnlyList<string> UnlockedMonths, long Balance1400, long Balance9999,
    long Balance1010, long Balance1020, IReadOnlyList<string> Pending, bool OpeningDraft);

public sealed record YearPreview(string Fy, DateOnly From, DateOnly To, bool Ready, IReadOnlyList<YearCheck> Checks, CarryForwardDraft Draft);

/// <summary>The pure rules: FY arithmetic, the carry-forward, the checklist, the year-close voucher and the state transitions.</summary>
public static class YearLockRules
{
    public const string FirstFy = "2026-27"; // §1.3: no HCC book for 2025-26
    public const string Capital = "3100", Drawings = "3000";
    public const int ReasonMin = 10;
    public const string ProvisionalText = "PROVISIONAL — year reopened";
    public const string YearCloseSub = "year-close";

    public static (DateOnly From, DateOnly To) Range(string fy) => PlReports.FyRange(fy);
    public static string FyOf(DateOnly d) => PlReports.FyLabel(PlReports.FyStart(d));
    public static string Next(string fy) => FyOf(Range(fy).From.AddYears(1));
    public static string Prev(string fy) => FyOf(Range(fy).From.AddYears(-1));
    public static bool Valid(string? fy) => fy is { Length: 7 } && fy[4] == '-' && int.TryParse(fy.AsSpan(0, 4), out var y) && int.TryParse(fy.AsSpan(5, 2), out var n) && (y + 1) % 100 == n;

    public static HccException Refuse(HccErrorCode code, string reason, string message) => new(code, message,
        new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(reason) });

    public static HccException Locked(string fy) => Refuse(HccErrorCode.INVALID_TRANSITION, "ACC_YEAR_LOCKED",
        $"Financial year {fy} is locked — post the correction as a journal in the next year that refers to {fy} (rulebook G4), or the Owner reopens the year.");

    private static bool IsPl(EffectiveChart chart, LedgerNames names, string account)
    {
        var c = PartyLedgers.ControlOf(account);
        return PlReports.Root(chart, names, c).Side == "PL" || c[0] is '4' or '5';
    }

    /// <summary>G3 step 5: closing balances of every voucher up to <paramref name="to"/> → balance-sheet lines (parties kept), P&amp;L and 3000 to 3100.</summary>
    public static CarryForwardDraft Build(IEnumerable<Voucher> vouchersUpTo, DateOnly to, EffectiveChart? chart = null, LedgerNames? names = null)
    {
        ArgumentNullException.ThrowIfNull(vouchersUpTo);
        chart ??= ChartOfAccounts.Builtin;
        names ??= new LedgerNames(chart: chart);
        var bal = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var v in vouchersUpTo.Where(v => v.Date <= to))
            foreach (var e in v.Entries) bal[e.Account] = checked(bal.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise);
        long pl = 0, drawings = 0;
        var close = new List<VoucherEntry>();
        var carried = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (acct, n) in bal)
        {
            if (n == 0) continue;
            if (IsPl(chart, names, acct)) { pl += n; close.Add(Reverse(acct, n)); continue; }
            if (PartyLedgers.ControlOf(acct) == Drawings) { drawings += n; close.Add(Reverse(acct, n)); continue; }
            carried[acct] = n;
        }

        var profit = -pl;
        var capDelta = -profit + drawings;
        if (capDelta != 0) close.Add(capDelta > 0 ? new VoucherEntry(Capital, capDelta, 0) : new VoucherEntry(Capital, 0, -capDelta));
        carried[Capital] = carried.GetValueOrDefault(Capital) + capDelta;
        var lines = carried.Where(kv => kv.Value != 0).Select(kv =>
        {
            var i = kv.Key.IndexOf('/', StringComparison.Ordinal);
            return new CarryForwardLine(i < 0 ? kv.Key : kv.Key[..i], i < 0 ? null : kv.Key[(i + 1)..], kv.Value > 0 ? kv.Value : 0, kv.Value < 0 ? -kv.Value : 0);
        }).ToList();
        return new CarryForwardDraft(lines, profit, drawings, close, lines.Sum(l => l.Dr), lines.Sum(l => l.Cr));
    }

    private static VoucherEntry Reverse(string acct, long n) => n > 0 ? new VoucherEntry(acct, 0, n) : new VoucherEntry(acct, -n, 0);

    public static IReadOnlyList<YearCheck> Checks(string fy, YearCheckInput x)
    {
        ArgumentNullException.ThrowIfNull(x);
        static string R(long p) => (Math.Abs(p) / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var (_, to) = Range(fy);
        return
        [
            new("opening-approved", !(fy == FirstFy && x.OpeningDraft), "Opening entry approved", fy == FirstFy && x.OpeningDraft ? "The hand-entered opening of 1 April is a draft." : ""),
            new("days-locked", x.FyEnded && x.UnlockedDays.Count == 0, "Every day of the year locked",
                !x.FyEnded ? $"The year ends on {to:yyyy-MM-dd}." : x.UnlockedDays.Count > 0 ? $"{x.UnlockedDays.Count} day(s) not locked, first {x.UnlockedDays.Min():yyyy-MM-dd}." : ""),
            new("months-gst-locked", x.UnlockedMonths.Count == 0, "Every month GST-locked", x.UnlockedMonths.Count > 0 ? $"Not locked: {string.Join(", ", x.UnlockedMonths)}." : ""),
            new("opening-difference", x.Balance1400 == 0, "1400 Opening-balance difference is zero", x.Balance1400 != 0 ? $"₹{R(x.Balance1400)} in 1400." : ""),
            new("suspense", x.Balance9999 == 0, "9999 Suspense is zero", x.Balance9999 != 0 ? $"₹{R(x.Balance9999)} in 9999." : ""),
            new("clearing", x.Balance1010 == 0 && x.Balance1020 == 0, "1010 UPI / 1020 card clearing zero at 31 March",
                x.Balance1010 != 0 || x.Balance1020 != 0 ? $"1010 ₹{R(x.Balance1010)} · 1020 ₹{R(x.Balance1020)} not settled to the bank." : ""),
            new("pending", x.Pending.Count == 0, "Nothing pending dated in the year", x.Pending.Count > 0 ? $"{x.Pending.Count} pending: {string.Join(", ", x.Pending.Take(3))}." : ""),
        ];
    }

    /// <summary>The approved carry-forward as the opening voucher of the next year (kind opening, sub year-close): P&amp;L and 3000 to 3100.</summary>
    public static Voucher CloseVoucher(CarryForward cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        var d = Range(cf.ToFy).From;
        return new Voucher($"JRN/{d:yyyyMMdd}/YC", d, VoucherType.Journal, $"Year close {cf.FromFy}: P&L and drawings to {Capital} (carry-forward {cf.Id})",
            $"year-close:{cf.Id}", "opening", YearCloseSub, cf.Close, []);
    }

    public static bool LockedOn(IEnumerable<FiscalYearState> years, DateOnly date)
    {
        var fy = FyOf(date);
        return years.Any(y => y.Fy == fy && y.Status == YearStatus.Locked);
    }

    /// <summary>Exports of a reopened year and of the year after carry the provisional mark.</summary>
    public static bool Provisional(IEnumerable<FiscalYearState> years, DateOnly date)
    {
        var fy = FyOf(date);
        var prev = Prev(fy);
        return years.Any(y => (y.Fy == fy || y.Fy == prev) && y.ProvisionalExports);
    }

    public static void GuardPost(IEnumerable<FiscalYearState> years, DateOnly date)
    {
        if (LockedOn(years, date)) throw Locked(FyOf(date));
    }

    public static void RequireReady(YearPreview p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var bad = p.Checks.Where(c => !c.Ok).ToList();
        if (bad.Count > 0)
            throw Refuse(HccErrorCode.INVALID_TRANSITION, "ACC_YEAR_NOT_READY",
                "ACC_YEAR_NOT_READY: " + string.Join("; ", bad.Select(c => c.Label + (c.Detail.Length > 0 ? $" ({c.Detail})" : ""))));
    }

    public static void RequireBalanced(CarryForwardDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!d.Balanced) throw Refuse(HccErrorCode.VALIDATION, "ACC_CF_UNBALANCED", $"ACC_CF_UNBALANCED: the carry-forward lines total {d.Debit} Dr and {d.Credit} Cr paise.");
    }

    /// <summary>Reopen rules: reason ≥ 10, the latest locked year only, and only while the next year has no locked month.</summary>
    public static string CheckReopen(string fy, IReadOnlyList<FiscalYearState> years, IEnumerable<string> lockedMonths, string? reason)
    {
        ArgumentNullException.ThrowIfNull(years);
        var y = years.FirstOrDefault(v => v.Fy == fy);
        if (y?.Status != YearStatus.Locked) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Financial year {fy} is not locked.");
        var r = (reason ?? "").Trim();
        if (r.Length < ReasonMin || r.Length > 400) throw HccException.Validation($"Give a reason of at least {ReasonMin} characters (DEC-077).");
        var latest = years.Where(v => v.Status == YearStatus.Locked).Select(v => v.Fy).Max(StringComparer.Ordinal);
        if (latest != fy) throw Refuse(HccErrorCode.INVALID_TRANSITION, "ACC_YEAR_REOPEN_BLOCKED", $"ACC_YEAR_REOPEN_BLOCKED: only the latest locked year ({latest}) can be reopened.");
        var (from, to) = Range(Next(fy));
        var m = lockedMonths.Where(x => string.CompareOrdinal(x, from.ToString("yyyy-MM")) >= 0 && string.CompareOrdinal(x, to.ToString("yyyy-MM")) <= 0).Order(StringComparer.Ordinal).FirstOrDefault();
        if (m is not null) throw Refuse(HccErrorCode.INVALID_TRANSITION, "ACC_YEAR_REOPEN_BLOCKED", $"ACC_YEAR_REOPEN_BLOCKED: {Next(fy)} has a locked month ({m}).");
        return r;
    }
}

/// <summary>Persistence (migrations 4280–4282) and the facts the checklist needs.</summary>
public interface IYearLockStore
{
    Task<IReadOnlyList<FiscalYearState>> YearsAsync(int propertyId, CancellationToken ct);
    Task SaveYearAsync(int propertyId, FiscalYearState year, int actor, string action, string? reason, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<CarryForward>> CarryForwardsAsync(int propertyId, CancellationToken ct);
    Task SaveCarryForwardAsync(int propertyId, CarryForward cf, CancellationToken ct);
    /// <summary>Every voucher up to <paramref name="date"/> (opening, locked snapshots, live days, approved year-close vouchers).</summary>
    Task<IReadOnlyList<Voucher>> VouchersUpToAsync(int propertyId, DateOnly date, CancellationToken ct);
    Task<YearCheckInput> CheckInputAsync(int propertyId, string fy, DateOnly businessDate, CancellationToken ct);
    Task<IReadOnlyList<string>> LockedMonthsAsync(int propertyId, CancellationToken ct);
    /// <summary>DEC-077: the regenerated opening date is flagged for the next Tally export.</summary>
    Task MarkReexportAsync(int propertyId, DateOnly date, string reason, DateTime now, CancellationToken ct);
}

/// <summary>Application service of §1.2 (roles checked here; the endpoint checks the PERM ids).</summary>
public sealed class YearLockService(IYearLockStore store, Func<DateTime> clock)
{
    private static void Prepare(string role)
    {
        if (role is not ("Owner" or "Accountant")) throw new HccException(HccErrorCode.FORBIDDEN, "Only the Owner or the Accountant prepares the year end (PERM-ACC-014).");
    }

    private static void Owner(string role, string what)
    {
        if (role != "Owner") throw new HccException(HccErrorCode.FORBIDDEN, $"Only the Owner {what} a financial year (PERM-ACC-015).");
    }

    private static string Fy(string? fy)
    {
        if (!YearLockRules.Valid(fy)) throw HccException.Validation("Choose a financial year such as 2026-27.");
        if (string.CompareOrdinal(fy, YearLockRules.FirstFy) < 0) throw HccException.Validation($"The books start in {YearLockRules.FirstFy} (DEC-148): there is no earlier HCC year.");
        return fy!;
    }

    public async Task<YearPreview> PreviewAsync(int propertyId, string role, string? fyIn, DateOnly businessDate, CancellationToken ct)
    {
        Prepare(role);
        var fy = Fy(fyIn);
        var (from, to) = YearLockRules.Range(fy);
        var checks = YearLockRules.Checks(fy, await store.CheckInputAsync(propertyId, fy, businessDate, ct).ConfigureAwait(false));
        var draft = YearLockRules.Build(await store.VouchersUpToAsync(propertyId, to, ct).ConfigureAwait(false), to);
        return new YearPreview(fy, from, to, checks.All(c => c.Ok), checks, draft);
    }

    private static FiscalYearState Of(IReadOnlyList<FiscalYearState> ys, string fy) => ys.FirstOrDefault(y => y.Fy == fy) ?? new FiscalYearState(fy, YearStatus.Open);

    public async Task<CarryForward> CarryForwardAsync(int propertyId, int actor, string role, string? fyIn, CancellationToken ct)
    {
        Prepare(role);
        var fy = Fy(fyIn);
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        var y = Of(years, fy);
        if (y.Status == YearStatus.Locked) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Financial year {fy} is locked.");
        var to = YearLockRules.Range(fy).To;
        var d = YearLockRules.Build(await store.VouchersUpToAsync(propertyId, to, ct).ConfigureAwait(false), to);
        YearLockRules.RequireBalanced(d);
        var now = clock();
        var cf = new CarryForward($"CF-{fy}-{Guid.NewGuid():N}", fy, YearLockRules.Next(fy), "draft", d.Lines, d.ProfitToCapital, d.DrawingsToCapital, d.Close, now, actor);
        await store.SaveCarryForwardAsync(propertyId, cf, ct).ConfigureAwait(false);
        if (y.Status == YearStatus.Open) await store.SaveYearAsync(propertyId, y with { Status = YearStatus.Closing }, actor, "carry-forward", null, now, ct).ConfigureAwait(false);
        return cf;
    }

    public async Task<FiscalYearState> LockAsync(int propertyId, int actor, string role, string? fyIn, string? carryForwardId, DateOnly businessDate, CancellationToken ct)
    {
        Owner(role, "locks");
        var fy = Fy(fyIn);
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        var y = Of(years, fy);
        if (y.Status is YearStatus.Locked or YearStatus.Reopened)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, y.Status == YearStatus.Locked ? $"Financial year {fy} is already locked." : $"Financial year {fy} was reopened — relock it.");
        if (fy != YearLockRules.FirstFy && Of(years, YearLockRules.Prev(fy)).Status != YearStatus.Locked)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Lock {YearLockRules.Prev(fy)} first.");
        var p = await PreviewAsync(propertyId, role, fy, businessDate, ct).ConfigureAwait(false);
        YearLockRules.RequireReady(p);
        var cf = (await store.CarryForwardsAsync(propertyId, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == carryForwardId && c.FromFy == fy && c.Status == "draft")
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Draft the carry-forward of this year first.");
        if (!cf.Lines.SequenceEqual(p.Draft.Lines)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The books changed after the carry-forward was drafted — draft it again.");
        var now = clock();
        await store.SaveCarryForwardAsync(propertyId, cf with { Status = "approved", ApprovedBy = actor, ApprovedUtc = now }, ct).ConfigureAwait(false);
        var locked = y with { Status = YearStatus.Locked, LockedUtc = now, LockedBy = actor, CarryForwardId = cf.Id, ProvisionalExports = false };
        await store.SaveYearAsync(propertyId, locked, actor, "lock", null, now, ct).ConfigureAwait(false);
        return locked;
    }

    public async Task<FiscalYearState> ReopenAsync(int propertyId, int actor, string role, string? fyIn, string? reason, CancellationToken ct)
    {
        Owner(role, "reopens");
        var fy = Fy(fyIn);
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        var r = YearLockRules.CheckReopen(fy, years, await store.LockedMonthsAsync(propertyId, ct).ConfigureAwait(false), reason);
        var now = clock();
        var y = Of(years, fy) with { Status = YearStatus.Reopened, ReopenedUtc = now, ReopenedBy = actor, ReopenReason = r, ProvisionalExports = true };
        await store.SaveYearAsync(propertyId, y, actor, "reopen", r, now, ct).ConfigureAwait(false);
        return y;
    }

    public async Task<FiscalYearState> RelockAsync(int propertyId, int actor, string role, string? fyIn, DateOnly businessDate, CancellationToken ct)
    {
        Owner(role, "relocks");
        var fy = Fy(fyIn);
        var years = await store.YearsAsync(propertyId, ct).ConfigureAwait(false);
        var y = Of(years, fy);
        if (y.Status != YearStatus.Reopened) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Financial year {fy} is not reopened.");
        var p = await PreviewAsync(propertyId, role, fy, businessDate, ct).ConfigureAwait(false);
        YearLockRules.RequireReady(p);
        YearLockRules.RequireBalanced(p.Draft);
        var now = clock();
        var id = $"CF-{fy}-{Guid.NewGuid():N}";
        var all = await store.CarryForwardsAsync(propertyId, ct).ConfigureAwait(false);
        if (all.FirstOrDefault(c => c.Id == y.CarryForwardId) is { } old) await store.SaveCarryForwardAsync(propertyId, old with { Status = "superseded", SupersededBy = id }, ct).ConfigureAwait(false);
        await store.SaveCarryForwardAsync(propertyId, new CarryForward(id, fy, YearLockRules.Next(fy), "approved", p.Draft.Lines, p.Draft.ProfitToCapital, p.Draft.DrawingsToCapital,
            p.Draft.Close, now, actor, actor, now), ct).ConfigureAwait(false);
        var locked = y with { Status = YearStatus.Locked, LockedUtc = now, LockedBy = actor, CarryForwardId = id, ProvisionalExports = false };
        await store.SaveYearAsync(propertyId, locked, actor, "relock", null, now, ct).ConfigureAwait(false);
        await store.MarkReexportAsync(propertyId, YearLockRules.Range(YearLockRules.Next(fy)).From, $"Year {fy} relocked: opening regenerated", now, ct).ConfigureAwait(false);
        return locked;
    }
}
