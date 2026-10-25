using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using VersionToken = Hcc.Application.Stays.VersionToken;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Application.Expenses;

/// <summary>
/// Expense use cases (WF-EXP-001): SubmitExpense, SubmitExpenseDraft, DecideExpense (approve posts / reject), ReverseExpense.
/// Ports the prototype handlers EXPENSE, EXPENSE_SUBMIT, EXPENSE_APPROVE, EXPENSE_REVERSE. Cash expenses move the shared drawer
/// (DEC-052) through <see cref="ICashDrawerPort"/> inside the same transaction, exactly once; every approved or reversed expense
/// publishes an outbox event for Accounting.
/// </summary>
public sealed class ExpenseCommands(ICommandRunner runner, IAccessPolicy policy, IExpenseStore store, ICashDrawerPort drawer, TimeProvider clock)
{
    public const string OpSubmit = "SubmitExpense";
    public const string OpSubmitDraft = "SubmitExpenseDraft";
    public const string OpDecide = "DecideExpense";
    public const string OpReverse = "ReverseExpense";

    public const string DrawerSourceExpense = "expense";
    public const string DrawerSourceReversal = "expense-reversal";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<ExpenseResult>> SubmitAsync(CommandContext ctx, SubmitExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        // Any scope: Reception / Cashier / Accountant hold the grant for their own submissions; the new record is theirs by construction.
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var n = ExpenseRules.NormalizeNew(req.Vendor, req.Category, req.Method, req.AmountPaise, req.Notes, req.Receipt);

        return await runner.RunAsync(ctx with { Operation = OpSubmit }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6-GUARD: refuse when business date bd is closed (IBusinessDayGuard, DAY_CLOSED).
            var status = req.Draft ? ExpenseState.Draft : ExpenseState.Pending;
            var e = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, n.Vendor, n.Category, n.Amount, n.Method, bd, status, ctx.PrincipalId,
                n.Notes, n.Receipt, null, null, null, null, UtcNow), c).ConfigureAwait(false);
            var dups = ExpenseRules.Duplicates(await store.FactsOnAsync(tx, ctx.PropertyId, bd, c).ConfigureAwait(false), n.Vendor, n.Amount, bd, e.ExpenseId);
            await tx.AuditAsync(req.Draft ? "EXPENSE_DRAFT" : "EXPENSE_SUBMITTED", Target(e.ExpenseId), $"{n.Vendor} · {n.Category} · {n.Amount} · {n.Method}", c).ConfigureAwait(false);
            return new ExpenseResult(ExpenseMapping.ToView(e), dups, null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseResult>> SubmitDraftAsync(CommandContext ctx, SubmitExpenseDraftRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpSubmitDraft }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanSubmitDraft(e.Status, e.SubmittedBy, ctx.PrincipalId);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6-GUARD: refuse when business date bd is closed (IBusinessDayGuard, DAY_CLOSED).
            var u = await store.UpdateAsync(tx, e with { Status = ExpenseState.Pending, BusinessDate = bd }, expected, UtcNow, c).ConfigureAwait(false);
            var dups = ExpenseRules.Duplicates(await store.FactsOnAsync(tx, ctx.PropertyId, bd, c).ConfigureAwait(false), u.Vendor, u.Amount, bd, u.ExpenseId);
            await tx.AuditAsync("EXPENSE_SUBMITTED", Target(u.ExpenseId), $"{u.Vendor} · {u.Amount} · draft sent for approval", c).ConfigureAwait(false);
            return new ExpenseResult(ExpenseMapping.ToView(u), dups, null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseResult>> DecideAsync(CommandContext ctx, DecideExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        // The amount is immutable after submission, so the limit check on the peeked amount holds under the row lock below.
        var peek = await store.GetAsync(ctx.PropertyId, req.ExpenseId, ct).ConfigureAwait(false);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Decide, null, req.Approve && peek is not null ? peek.Amount.Value : null, ct).ConfigureAwait(false);
        if (req.Approve) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.PostReverse, null, null, ct).ConfigureAwait(false);
        if (peek is null) throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
        var reason = ExpenseRules.OptionalReason(req.Reason);

        return await runner.RunAsync(ctx with { Operation = OpDecide }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanDecide(e.Status, e.SubmittedBy, ctx.PrincipalId);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            if (req.Approve && e.Amount != peek.Amount) throw new HccException(HccErrorCode.STALE_VERSION, "This expense was changed by someone else. Reload and try again.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6-GUARD: refuse when e.BusinessDate or bd is closed (IBusinessDayGuard, DAY_CLOSED; prototype DEC-065 checks the expense date).
            long? movementId = null;
            int? till = null;
            if (req.Approve && ExpenseRules.DrawerMovement(e.Method, e.Amount, reversal: false) is { } move)
            {
                till = req.TillId ?? throw HccException.Validation("Choose the till that pays this cash expense.");
                movementId = await drawer.RecordMovementAsync(tx, till.Value, move.Kind, DrawerSourceExpense, e.ExpenseId, move.Amount,
                    $"Cash expense · {e.Vendor}", e.Category, c).ConfigureAwait(false);
            }

            var now = UtcNow;
            var u = await store.UpdateAsync(tx, e with
            {
                Status = req.Approve ? ExpenseState.Posted : ExpenseState.Rejected,
                BusinessDate = bd,
                DecidedBy = ctx.PrincipalId,
                DecidedUtc = now,
                Reason = req.Approve ? e.Reason : reason,
                TillId = till,
            }, expected, now, c).ConfigureAwait(false);
            await tx.AuditAsync(req.Approve ? "EXPENSE_POSTED" : "EXPENSE_REJECTED", Target(u.ExpenseId),
                $"{u.Vendor} · {u.Amount} · {u.Method}{(reason is null ? string.Empty : " · " + reason)}", c).ConfigureAwait(false);
            if (req.Approve)
            {
                await tx.PublishAsync(ExpenseEvents.Approved, u.ExpenseId, new
                {
                    expenseId = u.ExpenseId,
                    vendor = u.Vendor,
                    category = u.Category,
                    method = u.Method,
                    amountPaise = u.Amount.Value,
                    businessDate = StayDates.ToWire(u.BusinessDate),
                    submittedBy = u.SubmittedBy,
                    approvedBy = ctx.PrincipalId,
                    tillId = till,
                    cashMovementId = movementId,
                }, c).ConfigureAwait(false);
            }

            return new ExpenseResult(ExpenseMapping.ToView(u), [], movementId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseReversalResult>> ReverseAsync(CommandContext ctx, ReverseExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.PostReverse, null, null, ct).ConfigureAwait(false);
        var reason = ExpenseRules.RequiredReversalReason(req.Reason);

        return await runner.RunAsync(ctx with { Operation = OpReverse }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanReverse(e.Status, e.IsReversal);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6-GUARD: refuse when e.BusinessDate or bd is closed (IBusinessDayGuard, DAY_CLOSED; prototype DEC-065 checks the original's date).
            var needsDrawer = ExpenseRules.DrawerMovement(e.Method, e.Amount, reversal: true);
            int? till = null;
            if (needsDrawer is not null) till = req.TillId ?? throw HccException.Validation("Choose the till that takes this cash back.");

            var now = UtcNow;
            // The reversal row first (it points at the original), then the original flips to Reversed (CK_expense_reversed).
            var rev = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, e.Vendor, e.Category, -e.Amount, e.Method, bd, ExpenseState.Posted, ctx.PrincipalId,
                null, null, reason, ctx.PrincipalId, till, e.ExpenseId, now), c).ConfigureAwait(false);
            long? movementId = null;
            if (needsDrawer is { } move)
            {
                movementId = await drawer.RecordMovementAsync(tx, till!.Value, move.Kind, DrawerSourceReversal, rev.ExpenseId, move.Amount,
                    $"Expense reversal · {e.Vendor}", $"Reverses {ExpenseMapping.Ref(e.ExpenseId)}", c).ConfigureAwait(false);
            }

            var orig = await store.UpdateAsync(tx, e with { Status = ExpenseState.Reversed, ReversedBy = rev.ExpenseId, Reason = reason }, expected, now, c).ConfigureAwait(false);
            await tx.AuditAsync("EXPENSE_REVERSED", Target(e.ExpenseId),
                $"{ExpenseMapping.Ref(e.ExpenseId)} reversed by {ExpenseMapping.Ref(rev.ExpenseId)} · {e.Amount} · {reason}", c).ConfigureAwait(false);
            await tx.PublishAsync(ExpenseEvents.Reversed, rev.ExpenseId, new
            {
                reversalId = rev.ExpenseId,
                originalExpenseId = e.ExpenseId,
                vendor = e.Vendor,
                category = e.Category,
                method = e.Method,
                amountPaise = e.Amount.Value,
                reversalAmountPaise = rev.Amount.Value,
                businessDate = StayDates.ToWire(bd),
                originalBusinessDate = StayDates.ToWire(e.BusinessDate),
                reversedBy = ctx.PrincipalId,
                reason,
                tillId = till,
                cashMovementId = movementId,
            }, c).ConfigureAwait(false);
            return new ExpenseReversalResult(ExpenseMapping.ToView(orig), ExpenseMapping.ToView(rev), movementId);
        }, ct).ConfigureAwait(false);
    }

    private static string Target(long id) => "expense:" + id.ToString(CultureInfo.InvariantCulture);
}

public static class ExpenseMapping
{
    public static string Ref(long id) => "EXP-" + id.ToString(CultureInfo.InvariantCulture);

    public static ExpenseView ToView(ExpenseRecord e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new ExpenseView(e.ExpenseId, Ref(e.ExpenseId), e.Vendor, e.Category, e.Amount.Value, e.Method, StayDates.ToWire(e.BusinessDate), e.Status,
            e.SubmittedBy, e.Notes, e.Receipt, e.Reason, e.DecidedBy,
            e.DecidedUtc is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : null,
            e.ReversalOf, e.ReversedBy, VersionToken.ToWire(e.Version));
    }
}

/// <summary>Expense reads. Holders of PERM-EXP-002/003 (or property-wide EXP-001) see every expense; own-submission holders see only theirs.</summary>
public sealed class ExpenseQueries(IAccessPolicy policy, IExpenseStore store)
{
    private async Task<bool> WideAsync(int propertyId, int principalId, CancellationToken ct)
    {
        foreach (var perm in new[] { ExpensePermissions.Decide, ExpensePermissions.PostReverse, ExpensePermissions.Submit })
            if (await policy.HasAsync(propertyId, principalId, perm, AccessScopes.Property(propertyId), ct).ConfigureAwait(false)) return true;
        return false;
    }

    private async Task<bool> RequireViewerAsync(int propertyId, int principalId, CancellationToken ct)
    {
        if (!await policy.HasAsync(propertyId, principalId, ExpensePermissions.Submit, AccessScopes.Any, ct).ConfigureAwait(false))
            await policy.RequireAsync(propertyId, principalId, ExpensePermissions.Decide, AccessScopes.Any, null, ct).ConfigureAwait(false);
        return await WideAsync(propertyId, principalId, ct).ConfigureAwait(false);
    }

    public async Task<ExpenseView> GetAsync(int propertyId, int principalId, long expenseId, CancellationToken ct = default)
    {
        var wide = await RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var e = await store.GetAsync(propertyId, expenseId, ct).ConfigureAwait(false);
        if (e is null || (!wide && e.SubmittedBy != principalId)) throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
        return ExpenseMapping.ToView(e);
    }

    public async Task<ExpensePage> ListAsync(int propertyId, int principalId, string? status, string? from, string? to, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        if (status is not null && status is not (ExpenseState.Draft or ExpenseState.Pending or ExpenseState.Posted or ExpenseState.Rejected or ExpenseState.Reversed))
            throw HccException.Validation("Unknown expense status.");
        var wide = await RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var filter = new ExpenseFilter(status, from is null ? null : StayDates.Parse(from, "from"), to is null ? null : StayDates.Parse(to, "to"), wide ? null : principalId);
        var (items, total) = await store.ListAsync(propertyId, filter, page, pageSize, ct).ConfigureAwait(false);
        return new ExpensePage(items.Select(ExpenseMapping.ToView).ToList(), page, pageSize, total, !wide);
    }

    /// <summary>Posted figures for a business date (approvers and the Accountant; closing and reports read the same rule).</summary>
    public async Task<ExpenseSummaryView> SummaryAsync(int propertyId, int principalId, string date, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ExpensePermissions.Decide, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var d = StayDates.Parse(date);
        var t = ExpenseRules.Totals(await store.FactsOnAsync(propertyId, d, ct).ConfigureAwait(false), d);
        return new ExpenseSummaryView(StayDates.ToWire(d), t.Cash.Value, t.NonCash.Value, t.VendorCredit.Value, t.Total.Value, t.Reversals.Value, t.Count);
    }

    public async Task<ExpenseOptionsView> OptionsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        return new ExpenseOptionsView(ExpenseMethods.All, ExpenseMethods.SuggestedCategories);
    }
}
