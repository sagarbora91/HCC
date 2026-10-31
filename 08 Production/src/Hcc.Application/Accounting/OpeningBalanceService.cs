using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

// ACC-1 · Opening balances entered by hand (02 Planning/agents/ACC1-POSTING-CONTRACT.md §5, §7; rulebook §G2; DEC-139 / DEC-148).
// Prototype twin: 03 Prototype/src/domain/streams/accounting-opening.ts + services/mock/handlers/accounting-opening.ts.

/// <summary>Opening-balance PERM ids (migration 4221): enter = Owner, Manager, Accountant; approve / reopen = Owner.</summary>
public static class AccountingOpeningPermissions
{
    public const string Enter = "PERM-ACC-008";   // accounting.opening.enter
    public const string Approve = "PERM-ACC-009"; // accounting.opening.approve (and reopen)
}

/// <summary>One stored opening line (paise; one side only). <see cref="Account"/> = ledger code or <c>&lt;control&gt;/&lt;party&gt;</c>.</summary>
public sealed record OpeningLine(string Account, long Debit, long Credit);

/// <summary>The property's opening entry (one per property). <see cref="Status"/> = <c>draft</c> | <c>approved</c>.</summary>
public sealed record OpeningEntry(int PropertyId, DateOnly AsOf, string Status, IReadOnlyList<OpeningLine> Lines, int EnteredBy, DateTime EnteredUtc,
    int? ApprovedBy, DateTime? ApprovedUtc, string? Note, string Version)
{
    public bool Approved => Status == OpeningBalanceRules.Approved;
}

/// <summary>Persistence of the opening entry (migration 4220: acc.opening_entry + acc.opening_line).</summary>
public interface IOpeningEntryStore
{
    /// <summary>Transaction-owned application lock <c>acc:opening:&lt;property&gt;</c> (save / approve / reopen and the period lock guard).</summary>
    Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<OpeningEntry?> GetAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Replaces the entry with a draft (lines rewritten, approval cleared); returns the new version.</summary>
    Task<string> SaveDraftAsync(ICommandTransaction tx, int propertyId, DateOnly asOf, IReadOnlyList<OpeningLine> lines, string? note, int actor, DateTime now,
        CancellationToken ct);

    /// <summary>Draft → approved; STALE_VERSION when <paramref name="expectedVersion"/> is given and not the row's version.</summary>
    Task<string> ApproveAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, int actor, DateTime now, CancellationToken ct);

    /// <summary>Approved → draft with the reason (kept on the row; audited); STALE_VERSION as <see cref="ApproveAsync"/>.</summary>
    Task<string> ReopenAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, string reason, int actor, DateTime now, CancellationToken ct);

    /// <summary>The first business date on or after <paramref name="date"/> with a current period lock, if any.</summary>
    Task<DateOnly?> FirstLockOnOrAfterAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);
}

/// <summary>The pure rules of the opening entry (contract §5): validation, the 1400 balancing line, the journal, the lock gate.</summary>
public static partial class OpeningBalanceRules
{
    public const string DifferenceAccount = "1400";
    public const string DifferenceName = "Opening-balance difference";
    public const string Draft = "draft";
    public const string Approved = "approved";
    /// <summary>Lines a hand entry (SaveOpeningBalances) may hold. LG-R8: the cap applies to hand entry only — the legacy importer's cut-over
    /// draft (IOpeningEntryStore.SaveDraftAsync) may exceed it; reading and approving never apply it (the Owner approves the draft whole).</summary>
    public const int MaxLines = 500;
    public const long MaxPaise = 10_000_000_000;
    public const string SourceKey = "opening";
    public const string SourceKind = "opening";
    public const string Sub = "opening-entry";

    [GeneratedRegex(@"^\d{4}(/[A-Za-z0-9][A-Za-z0-9._-]{0,35})?$")]
    private static partial Regex AccountPattern();

    /// <summary>A ledger code or a party ledger id <c>&lt;control&gt;/&lt;party&gt;</c> (contract §3; parties are plain strings here).</summary>
    public static bool IsAccount(string? account) => account is not null && AccountPattern().IsMatch(account);

    /// <summary>The control (ledger) code of an account: <c>1110/CMP-0007</c> → <c>1110</c>.</summary>
    public static string CodeOf(string account) => (account ?? "").Split('/')[0];

    /// <summary>1 April of the financial year that contains <paramref name="date"/>.</summary>
    public static DateOnly FyStart(DateOnly date) => new(date.Month >= 4 ? date.Year : date.Year - 1, 4, 1);

    /// <summary>Errors of the lines as entered (1-based line numbers; 1400 lines are ignored — recomputed). Empty = valid.</summary>
    public static IReadOnlyList<string> Errors(IReadOnlyList<OpeningLineRequest>? lines)
    {
        var errors = new List<string>();
        if (lines is null || lines.Count == 0)
        {
            errors.Add("Enter at least one opening line.");
            return errors;
        }

        if (lines.Count > MaxLines) errors.Add($"Enter at most {MaxLines} lines.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var own = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var n = i + 1;
            var l = lines[i];
            var account = (l?.Account ?? "").Trim();
            if (account == DifferenceAccount) continue;
            own++;
            if (!IsAccount(account))
            {
                errors.Add($"Line {n}: choose a ledger code or a party ledger (code/party).");
                continue;
            }

            if (CodeOf(account) == ChartOfAccounts.Suspense) errors.Add($"Line {n}: {ChartOfAccounts.Suspense} Suspense cannot carry an opening balance.");
            if (CodeOf(account) == DifferenceAccount) errors.Add($"Line {n}: {DifferenceAccount} is filled automatically with the difference.");
            if (l!.DebitPaise < 0 || l.CreditPaise < 0) errors.Add($"Line {n}: amounts are whole paise, zero or more.");
            else if ((l.DebitPaise > 0) == (l.CreditPaise > 0)) errors.Add($"Line {n}: enter either a debit or a credit.");
            else if (l.DebitPaise > MaxPaise || l.CreditPaise > MaxPaise) errors.Add($"Line {n}: the amount is too large.");
            if (!seen.Add(account)) errors.Add($"Line {n}: {account} is entered twice — one line per ledger and per party.");
        }

        if (own == 0) errors.Add("Enter at least one opening line.");
        return errors;
    }

    /// <summary>The balancing line on 1400 for <paramref name="lines"/> (null when they balance): the gap on the short side.</summary>
    public static OpeningLine? BalancingLine(IReadOnlyList<OpeningLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var d = lines.Sum(l => l.Debit);
        var c = lines.Sum(l => l.Credit);
        return d == c ? null : d > c ? new OpeningLine(DifferenceAccount, 0, d - c) : new OpeningLine(DifferenceAccount, c - d, 0);
    }

    /// <summary>What a save stores: the lines as entered (1400 dropped, accounts trimmed) plus the recomputed 1400 line.</summary>
    public static IReadOnlyList<OpeningLine> WithBalance(IReadOnlyList<OpeningLineRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var own = lines.Select(l => new OpeningLine((l.Account ?? "").Trim(), l.DebitPaise, l.CreditPaise)).Where(l => l.Account != DifferenceAccount).ToList();
        return BalancingLine(own) is { } b ? [.. own, b] : own;
    }

    /// <summary>Signed 1400 balance of the entry (debit positive); 0 without an entry.</summary>
    public static long Difference(OpeningEntry? entry) => entry?.Lines.Where(l => l.Account == DifferenceAccount).Sum(l => l.Debit - l.Credit) ?? 0;

    /// <summary>DEC-148: pending until an approved entry with 1400 = 0 exists.</summary>
    public static bool Pending(OpeningEntry? entry) => entry is null || !entry.Approved || Difference(entry) != 0;

    /// <summary>
    /// Why a period lock of <paramref name="date"/> is refused because of the opening entry (null = it is not): any date on or after the
    /// entry's date while it is a draft or 1400 ≠ 0. No entry → null (ledgers run from zero; see the ACC-1 opening DONE file, open item 1).
    /// </summary>
    public static string? LockBlocker(OpeningEntry? entry, DateOnly date)
    {
        if (entry is null || date < entry.AsOf) return null;
        if (!entry.Approved)
            return $"Opening balances pending: the opening entry as of {Iso(entry.AsOf)} is a draft — the Owner approves it before the first period lock.";
        var diff = Difference(entry);
        return diff == 0 ? null
            : $"Opening balances pending: ₹{SourceRules.Rupees(Math.Abs(diff))} {(diff > 0 ? "Dr" : "Cr")} sits in {DifferenceAccount} {DifferenceName} — the Owner reopens the opening entry and corrects it before the first period lock.";
    }

    /// <summary>The opening journal <c>JRN/&lt;asOf yyyymmdd&gt;/000</c> dated the entry's date (any status).</summary>
    public static Voucher Voucher(OpeningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new Voucher($"JRN/{entry.AsOf.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}/000", entry.AsOf, VoucherType.Journal,
            $"Opening balances as of {Iso(entry.AsOf)}" + (entry.Approved ? "" : " (draft, not approved)"), SourceKey, SourceKind, Sub,
            entry.Lines.Where(l => l.Debit != 0 || l.Credit != 0).Select(l => new VoucherEntry(l.Account, l.Debit, l.Credit)).ToList(), []);
    }

    /// <summary>Display name of an account: the chart name, the party suffix after it, 1400 named here until the chart carries it.</summary>
    public static string NameOf(string account)
    {
        var code = CodeOf(account);
        var name = code == DifferenceAccount && ChartOfAccounts.Find(code) is null ? DifferenceName : ChartOfAccounts.NameOf(code);
        return account.Contains('/', StringComparison.Ordinal) ? $"{name} · {account[(code.Length + 1)..]}" : name;
    }

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The period-lock gate (contract §5) for <see cref="AccountingService.LockAsync"/>: one call inside the lock's transaction.</summary>
public static class OpeningBalanceGuard
{
    /// <summary>INVALID_TRANSITION "Opening balances pending …" when the lock of <paramref name="date"/> must wait; nothing without a store.</summary>
    public static async Task RequireLockableAsync(IOpeningEntryStore? store, ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct)
    {
        if (store is null) return;
        ArgumentNullException.ThrowIfNull(tx);
        await store.SerializeAsync(tx, propertyId, ct).ConfigureAwait(false); // a concurrent reopen cannot slip past the lock
        if (OpeningBalanceRules.LockBlocker(await store.GetAsync(tx, propertyId, ct).ConfigureAwait(false), date) is { } why)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, why);
    }
}

/// <summary>SaveOpeningBalances / ApproveOpeningBalances / ReopenOpeningBalances (prototype ACC_OPENING_SAVE / _APPROVE / _REOPEN).</summary>
public sealed class OpeningBalanceService(ICommandRunner runner, IOpeningEntryStore store, IAccountingStore accounting, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpSave = "SaveOpeningBalances";
    public const string OpApprove = "ApproveOpeningBalances";
    public const string OpReopen = "ReopenOpeningBalances";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static OpeningBalancesResult Result(DateOnly asOf, string status, IReadOnlyList<OpeningLine> lines, string version)
    {
        var diff = lines.Where(l => l.Account == OpeningBalanceRules.DifferenceAccount).Sum(l => l.Debit - l.Credit);
        return new OpeningBalancesResult(asOf, status, lines.Count(l => l.Account != OpeningBalanceRules.DifferenceAccount), lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), diff, version);
    }

    private static string DiffText(IReadOnlyList<OpeningLine> lines)
        => lines.FirstOrDefault(l => l.Account == OpeningBalanceRules.DifferenceAccount) is { } d
            ? $" · {OpeningBalanceRules.DifferenceAccount} difference ₹{SourceRules.Rupees(d.Debit + d.Credit)} {(d.Debit > 0 ? "Dr" : "Cr")}" : "";

    public async Task<CommandResult<OpeningBalancesResult>> SaveAsync(Caller caller, string idempotencyKey, SaveOpeningBalancesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingOpeningPermissions.Enter, ct: ct).ConfigureAwait(false);
        var errors = OpeningBalanceRules.Errors(req.Lines);
        if (errors.Count > 0) throw HccException.Validation(errors[0] + (errors.Count > 1 ? $" ({errors.Count - 1} more problem{(errors.Count == 2 ? "" : "s")})" : ""));
        var note = (req.Note ?? "").Trim();
        if (note.Length > 200) throw HccException.Validation("Keep the note to 200 characters.");
        var lines = OpeningBalanceRules.WithBalance(req.Lines!);
        return await runner.RunAsync(Ctx(caller, OpSave, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var businessDate = await accounting.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
            var fy = OpeningBalanceRules.FyStart(businessDate);
            if (req.AsOf is { } asked && asked != fy)
                throw HccException.Validation($"Opening balances are dated the start of the business financial year ({OpeningBalanceRules.Iso(fy)}).");
            var cur = await store.GetAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            if (cur?.Approved == true)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "The opening balances are approved and read-only — the Owner reopens them first.");
            var version = await store.SaveDraftAsync(tx, caller.PropertyId, fy, lines, note.Length == 0 ? null : note, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var r = Result(fy, OpeningBalanceRules.Draft, lines, version);
            await tx.AuditAsync("ACC_OPENING_SAVED", "acc-opening:" + OpeningBalanceRules.Iso(fy),
                $"Opening balances as of {OpeningBalanceRules.Iso(fy)} saved (draft) · {r.Lines} line(s) · ₹{SourceRules.Rupees(r.DebitPaise)} Dr = ₹{SourceRules.Rupees(r.CreditPaise)} Cr{DiffText(lines)}",
                ct2).ConfigureAwait(false);
            return r;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<OpeningBalancesResult>> ApproveAsync(Caller caller, string idempotencyKey, ApproveOpeningBalancesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingOpeningPermissions.Approve, ct: ct).ConfigureAwait(false);
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpApprove, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var cur = await store.GetAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "No opening balances have been entered yet.");
            if (cur.Approved) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The opening balances are already approved.");
            if (!cur.Lines.Any(l => l.Account != OpeningBalanceRules.DifferenceAccount)) throw HccException.Validation("Enter at least one opening line.");
            if (cur.Lines.Sum(l => l.Debit) != cur.Lines.Sum(l => l.Credit)) throw HccException.Validation("The opening entry does not balance.");
            var version = await store.ApproveAsync(tx, caller.PropertyId, req.ExpectedVersion, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var r = Result(cur.AsOf, OpeningBalanceRules.Approved, cur.Lines, version);
            await tx.AuditAsync("ACC_OPENING_APPROVED", "acc-opening:" + OpeningBalanceRules.Iso(cur.AsOf),
                $"Opening balances as of {OpeningBalanceRules.Iso(cur.AsOf)} approved · ₹{SourceRules.Rupees(r.DebitPaise)} Dr = ₹{SourceRules.Rupees(r.CreditPaise)} Cr{DiffText(cur.Lines)}",
                ct2).ConfigureAwait(false);
            return r;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<OpeningBalancesResult>> ReopenAsync(Caller caller, string idempotencyKey, ReopenOpeningBalancesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingOpeningPermissions.Approve, ct: ct).ConfigureAwait(false);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required to reopen the opening balances.");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpReopen, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var cur = await store.GetAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "No opening balances have been entered yet.");
            if (!cur.Approved) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The opening balances are not approved.");
            if (await store.FirstLockOnOrAfterAsync(tx, caller.PropertyId, cur.AsOf, ct2).ConfigureAwait(false) is { } locked)
                throw new HccException(HccErrorCode.INVALID_TRANSITION,
                    $"A period is locked on or after {OpeningBalanceRules.Iso(cur.AsOf)} ({OpeningBalanceRules.Iso(locked)}): correct the opening balances with an Owner-approved journal instead.");
            var version = await store.ReopenAsync(tx, caller.PropertyId, req.ExpectedVersion, reason, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_OPENING_REOPENED", "acc-opening:" + OpeningBalanceRules.Iso(cur.AsOf),
                $"Opening balances as of {OpeningBalanceRules.Iso(cur.AsOf)} reopened · {reason}", ct2).ConfigureAwait(false);
            return Result(cur.AsOf, OpeningBalanceRules.Draft, cur.Lines, version);
        }, ct).ConfigureAwait(false);
    }
}
