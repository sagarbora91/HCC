using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>The state of one business date: live derivation, lock snapshot (if any), what is shown and its exceptions.</summary>
public sealed record AccountingDayState(DateOnly Date, DayStatus Status, PeriodLock? Lock, IReadOnlyList<Voucher> Live, IReadOnlyList<Voucher> Shown,
    IReadOnlyList<AccountingIssue> Issues, IReadOnlyDictionary<string, string> Mapping)
{
    public IReadOnlyList<AccountingIssue> Errors => Issues.Where(i => i.Severity == "error").ToList();
}

/// <summary>Shared day evaluation for commands and queries (prototype accVouchers / accExceptions / accLockBlocker).</summary>
public static class AccountingDays
{
    public static async Task<AccountingDayState> EvaluateAsync(IAccountingStore store, ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var businessDate = await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var mapping = ChartOfAccounts.Effective(await store.MappingOverridesAsync(tx, propertyId, ct).ConfigureAwait(false));
        var sources = await store.SourcesAsync(tx, propertyId, date, date, ct).ConfigureAwait(false);
        var live = VoucherBuilder.Build(date, sources, mapping);
        var lk = await store.CurrentLockAsync(tx, propertyId, date, ct).ConfigureAwait(false);
        var status = Status(lk is not null, date, businessDate);
        var pending = lk is null ? await store.PendingEventsAsync(tx, propertyId, ct).ConfigureAwait(false) : 0;
        var shown = lk?.Vouchers ?? live;
        return new AccountingDayState(date, status, lk, live, shown, VoucherBuilder.Exceptions(shown, live, lk is not null, status, pending), mapping);
    }

    /// <summary>
    /// P6-GUARD: a date counts as closed once the property's business date has moved past it (DEC-042 day close advances it).
    /// Replace with P6's IBusinessDayGuard (DAY_CLOSED) when it is merged.
    /// </summary>
    public static DayStatus Status(bool locked, DateOnly date, DateOnly businessDate)
        => locked ? DayStatus.Locked : date < businessDate ? DayStatus.Closed : DayStatus.Open;

    /// <summary>Shown vouchers (snapshot for locked dates, live otherwise) for every date with activity in [from, to].</summary>
    public static async Task<IReadOnlyList<(DateOnly Date, IReadOnlyList<Voucher> Vouchers)>> RangeAsync(IAccountingStore store, int propertyId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var mapping = ChartOfAccounts.Effective(await store.MappingOverridesAsync(null, propertyId, ct).ConfigureAwait(false));
        var sources = await store.SourcesAsync(null, propertyId, from, to, ct).ConfigureAwait(false);
        var locks = (await store.LocksAsync(null, propertyId, from, to, ct).ConfigureAwait(false)).ToDictionary(l => l.BusinessDate);
        var dates = sources.Select(s => s.BusinessDate).Concat(locks.Keys).Distinct().Order().ToList();
        return dates.Select(d => (d, locks.TryGetValue(d, out var l) ? l.Vouchers : VoucherBuilder.Build(d, sources, mapping))).ToList();
    }
}

/// <summary>
/// Accounting commands (module spec 20 §8; prototype ACC_LOCK / ACC_UNLOCK / ACC_MAP_SET / ACC_MAP_RESET). Vouchers are never
/// keyed by hand: they are derived from the sources the outbox consumer records.
/// </summary>
public sealed class AccountingService(ICommandRunner runner, IAccountingStore store, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpLock = "LockAccountingPeriod";
    public const string OpUnlock = "UnlockAccountingPeriod";
    public const string OpMapSet = "SetLedgerMapping";
    public const string OpMapReset = "ResetLedgerMapping";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<CommandResult<PeriodLockResult>> LockAsync(Caller caller, string idempotencyKey, LockPeriodRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Lock, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpLock, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, $"acc:period:{caller.PropertyId}:{D(req.BusinessDate)}", ct2).ConfigureAwait(false);
            var day = await AccountingDays.EvaluateAsync(store, tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false);
            if (day.Lock is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This date is already locked.");
            if (day.Status == DayStatus.Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Close the business day before locking it.");
            var errors = day.Errors;
            if (errors.Count > 0)
                throw HccException.Validation($"Resolve {errors.Count} exception{(errors.Count == 1 ? "" : "s")} first ({errors[0].Title}).");
            var (debit, credit) = VoucherBuilder.Totals(day.Live);
            if (debit != credit) throw HccException.Validation("Debits and credits do not agree.");
            var lk = await store.InsertLockAsync(tx, caller.PropertyId, req.BusinessDate, day.Live, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_PERIOD_LOCKED", "acc-period:" + D(req.BusinessDate),
                $"{day.Live.Count} voucher(s) · ₹{SourceRules.Rupees(debit)} Dr = ₹{SourceRules.Rupees(credit)} Cr · lock {lk.LockId}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.AccountingPeriodLocked, lk.LockId,
                new { lockId = lk.LockId, businessDate = req.BusinessDate, voucherCount = day.Live.Count, debitPaise = debit, creditPaise = credit }, ct2).ConfigureAwait(false);
            return new PeriodLockResult(req.BusinessDate, lk.LockId, nameof(DayStatus.Locked), lk.VoucherCount, lk.Debit, lk.Credit, lk.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<PeriodLockResult>> UnlockAsync(Caller caller, string idempotencyKey, UnlockPeriodRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Unlock, ct: ct).ConfigureAwait(false);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required to unlock a period.");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpUnlock, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, $"acc:period:{caller.PropertyId}:{D(req.BusinessDate)}", ct2).ConfigureAwait(false);
            var lk = await store.CurrentLockAsync(tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This date is not locked.");
            await store.UnlockAsync(tx, lk.LockId, req.ExpectedVersion, reason, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_PERIOD_UNLOCKED", "acc-period:" + D(req.BusinessDate), $"lock {lk.LockId} · {reason}", ct2).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.AccountingPeriodUnlocked, lk.LockId, new { lockId = lk.LockId, businessDate = req.BusinessDate, reason }, ct2).ConfigureAwait(false);
            var businessDate = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false) ?? req.BusinessDate;
            return new PeriodLockResult(req.BusinessDate, lk.LockId, AccountingDays.Status(false, req.BusinessDate, businessDate).ToString(), lk.VoucherCount, lk.Debit, lk.Credit, lk.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LedgerMappingResult>> SetMappingAsync(Caller caller, string idempotencyKey, SetLedgerMappingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Mapping, ct: ct).ConfigureAwait(false);
        var key = req.MapKey ?? "";
        if (!ChartOfAccounts.IsValidKey(key)) throw new HccException(HccErrorCode.NOT_FOUND, "Unknown source classification.");
        var account = ChartOfAccounts.Find(req.AccountCode);
        if (account is null || account.Code == ChartOfAccounts.Suspense) throw HccException.Validation("Choose a ledger from the chart of accounts (not Suspense).");
        return await runner.RunAsync(Ctx(caller, OpMapSet, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, $"acc:mapping:{caller.PropertyId}", ct2).ConfigureAwait(false);
            if (!ChartOfAccounts.DefaultMapping.ContainsKey(key) && !(await store.KeyUsesAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false)).ContainsKey(key))
                throw new HccException(HccErrorCode.NOT_FOUND, "Unknown source classification.");
            var overrides = await store.MappingOverridesAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var before = ChartOfAccounts.Effective(overrides).GetValueOrDefault(key);
            if (before == account.Code) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This classification already maps to that ledger.");
            await store.WriteMappingAsync(tx, caller.PropertyId, key, account.Code, before, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MAPPING_SET", "acc-map:" + Clip(key), $"{key} → {account.Code} {account.Name}"
                + (before is null ? "" : $" (was {before} {ChartOfAccounts.NameOf(before)})") + " · locked periods unchanged", ct2).ConfigureAwait(false);
            return new LedgerMappingResult(key, before, account.Code, ChartOfAccounts.DefaultMapping.GetValueOrDefault(key) == account.Code);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LedgerMappingResult>> ResetMappingAsync(Caller caller, string idempotencyKey, ResetLedgerMappingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Mapping, ct: ct).ConfigureAwait(false);
        var key = req.MapKey ?? "";
        return await runner.RunAsync(Ctx(caller, OpMapReset, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, $"acc:mapping:{caller.PropertyId}", ct2).ConfigureAwait(false);
            var overrides = await store.MappingOverridesAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            if (!overrides.TryGetValue(key, out var before)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This classification already uses its default ledger.");
            var after = ChartOfAccounts.DefaultMapping.GetValueOrDefault(key);
            await store.WriteMappingAsync(tx, caller.PropertyId, key, null, before, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MAPPING_RESET", "acc-map:" + Clip(key), $"{key} reset to default" + (after is null ? " (unmapped)" : $" {after} {ChartOfAccounts.NameOf(after)}"), ct2).ConfigureAwait(false);
            return new LedgerMappingResult(key, before, after, true);
        }, ct).ConfigureAwait(false);
    }

    private static string Clip(string s) => s.Length <= 52 ? s : s[..52];
}
