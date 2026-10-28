using System.Globalization;
using System.Text.Json;
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
/// Expense use cases (WF-EXP-001): SubmitExpense, SubmitExpenseDraft, DecideExpense (approve posts / reject), ReverseExpense, and from the SCC
/// features (DEC-120) EditExpense and VoidExpense. Ports the prototype handlers in <c>handlers/expenses.ts</c>. Cash expenses move the shared
/// drawer (DEC-052) through <see cref="ICashDrawerPort"/> inside the same transaction, exactly once; every approved or reversed expense
/// publishes an outbox event for Accounting. HCC's approval rules stay: counted only once approved, the Manager's limit (or the category's),
/// nobody approves their own expense nor one they changed, the Cashier never reverses or voids. DEC-135 (SCC decisions wave): the bill's GST
/// carries ITC allowed / disallowed (default disallowed); the posted / reversed events carry the GST and the flag, so Accounting books allowed
/// GST to the GST input-credit ledger. DEC-131: a month the Owner unlocked takes writes without the override.
/// </summary>
public sealed class ExpenseCommands(ICommandRunner runner, IAccessPolicy policy, IExpenseStore store, ICashDrawerPort drawer, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null,
    MoneyMasters.IPaymentMethodSource? methods = null, IExpenseMasterSource? masters = null, IExpenseRegisterStore? registers = null,
    Accounting.CashGuardService? cashGuard = null)
{
    private async Task<IReadOnlyList<Hcc.Domain.MoneyMasters.TenderMethod>?> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        methods is null ? null : await methods.MethodsAsync(tx, propertyId, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<ExpenseCategoryRow>?> CategoriesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        masters is null ? null : await masters.CategoriesAsync(tx, propertyId, ct).ConfigureAwait(false);

    public const string OpSubmit = "SubmitExpense";
    public const string OpSubmitDraft = "SubmitExpenseDraft";
    public const string OpDecide = "DecideExpense";
    public const string OpReverse = "ReverseExpense";
    public const string OpEdit = "EditExpense";
    public const string OpVoid = "VoidExpense";

    public const string DrawerSourceExpense = "expense";
    public const string DrawerSourceReversal = "expense-reversal";

    /// <summary>Manager approval limit of the hotel (PERM-EXP-002 L 500000; prototype EXPENSE_MANAGER_LIMIT) — the category override replaces it.</summary>
    public const long ManagerLimitPaise = 500_000;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private Task<bool> IsOwnerAsync(CommandContext ctx, CancellationToken ct) => policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct);

    // ── Gates shared with ExpenseRegisterCommands ──

    /// <summary>SCC date rule + HCC's day-close and cash-book locks (prototype expDateCheck). Throws the refusal.</summary>
    internal static async Task DateGateAsync(Hcc.Application.Closing.IBusinessDayGuard? days, ICommandTransaction tx, int propertyId, DateOnly businessDate, DateOnly date,
        bool drawerCash, CancellationToken ct)
    {
        var why = await DateRefusalAsync(days, tx, propertyId, businessDate, date, drawerCash, ct).ConfigureAwait(false);
        if (why is not null) throw why;
    }

    internal static async Task<HccException?> DateRefusalAsync(Hcc.Application.Closing.IBusinessDayGuard? days, ICommandTransaction tx, int propertyId, DateOnly businessDate,
        DateOnly date, bool drawerCash, CancellationToken ct)
    {
        bool closed = false, reopened = false, cashLocked = false;
        if (days is not null && date <= businessDate)
        {
            if (date == businessDate)
            {
                try { await days.EnsureBusinessDayOpenAsync(tx, propertyId, ct).ConfigureAwait(false); }
                catch (HccException e) when (e.Code == HccErrorCode.DAY_CLOSED) { closed = true; }
            }
            else
            {
                reopened = await days.IsReopenedAsync(tx, propertyId, date, ct).ConfigureAwait(false);
            }

            try { await days.EnsureCashBookOpenAsync(tx, propertyId, date, "an expense on that day", ct).ConfigureAwait(false); }
            catch (HccException e) when (e.Code == HccErrorCode.DAY_CLOSED) { cashLocked = true; }
        }

        return ExpenseScc.DateRefusal(businessDate, date, closed, reopened, cashLocked, drawerCash);
    }

    /// <summary>
    /// SCC month-lock gate (isMonthLocked + Owner override + markFeedStale): a write dated in a tax-locked month needs the Owner and a reason;
    /// it marks the month's feed stale and is audited (EXPENSE_MONTH_OVERRIDE). Returns the reason (null when the month is not locked).
    /// </summary>
    internal static async Task<string?> MonthGateAsync(IExpenseRegisterStore? registers, ICommandTransaction tx, int propertyId, DateOnly date, bool owner, string? reason,
        DateTime nowUtc, CancellationToken ct)
    {
        if (registers is null) return null;
        var m = ExpenseScc.Ym(date);
        if (await registers.MonthLockAsync(tx, propertyId, m, ct).ConfigureAwait(false) is not { Unlocked: false }) return null; // DEC-131: unlocked = open
        if (!owner) throw new HccException(HccErrorCode.FORBIDDEN, ExpenseScc.MonthLockedMessage(m));
        var r = ExpenseScc.Optional(reason, ExpenseScc.NoteMax, "override reason")
                ?? throw HccException.Validation($"Month {m} is tax-locked. Give the Owner override reason (why this late change?).");
        await registers.MarkStaleAsync(tx, propertyId, m, nowUtc, ct).ConfigureAwait(false);
        await tx.AuditAsync("EXPENSE_MONTH_OVERRIDE", "expense-month:" + m, "locked-month override · " + r, ct).ConfigureAwait(false);
        return r;
    }

    /// <summary>SCC budget alert for a category's month (the expense just entered is <paramref name="extra"/> until approved).</summary>
    internal static async Task<string?> BudgetAlertAsync(IExpenseRegisterStore? registers, IExpenseStore store, ICommandTransaction tx, int propertyId, string category,
        DateOnly date, DateOnly businessDate, long extra, CancellationToken ct)
    {
        if (registers is null || ExpenseScc.Ym(date) != ExpenseScc.Ym(businessDate)) return null;
        var budgets = await registers.BudgetsAsync(tx, propertyId, ExpenseScc.Ym(date), ct).ConfigureAwait(false);
        if (!budgets.TryGetValue(category, out var budget) || budget <= 0) return null;
        var first = new DateOnly(date.Year, date.Month, 1);
        var spend = (await store.LedgerAsync(tx, propertyId, first, first.AddMonths(1).AddDays(-1), ct).ConfigureAwait(false))
            .Where(r => r.Counts && r.Category == category).Sum(r => r.Amount);
        return ExpenseScc.Alert(category, date, businessDate, budget, spend, extra)?.Message;
    }

    /// <summary>The bill photo of a request (optional): JPEG / PNG, at most 4 MB (SCC "Photo too large (max 4MB)").</summary>
    internal static (string ContentType, byte[] Bytes)? BillPhoto(string? base64, string? contentType) =>
        string.IsNullOrWhiteSpace(base64) ? null
            : CashOutRules.Photo(base64, contentType, ExpenseScc.PhotoMaxBytes, "Take a photo of the bill.", "The bill photo must be a JPEG or PNG image.",
                "Photo too large (max 4MB)", "The bill photo is not valid base64.");

    // ── Submit ──

    public async Task<CommandResult<ExpenseResult>> SubmitAsync(CommandContext ctx, SubmitExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        // Any scope: Reception / Cashier / Accountant hold the grant for their own submissions; the new record is theirs by construction.
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var n = ExpenseRules.NormalizeNew(req.Vendor, req.Category, req.Method, req.AmountPaise, req.Notes, req.Receipt, await MethodsAsync(null, ctx.PropertyId, ct).ConfigureAwait(false));
        var description = ExpenseScc.Description(req.Description, !req.Draft);
        var gst = ExpenseScc.Gst(req.GstRateBps, req.GstPaise, n.Amount.Value, req.ConfirmGst);
        var itc = ExpenseScc.Itc(req.Itc, gst.Paise, n.Amount.Value); // DEC-135
        var photo = BillPhoto(req.BillPhotoBase64, req.BillPhotoContentType);
        DateOnly? chosen = string.IsNullOrWhiteSpace(req.ExpenseDate) ? null : StayDates.Parse(req.ExpenseDate, "expenseDate");
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpSubmit }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier (DAY_CLOSED)
            var cat = ExpenseMasters.Category(await CategoriesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), n.Category);
            var vendors = masters is null ? null : await masters.VendorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var ven = masters is null ? (n.Vendor, false) : ExpenseMasters.Vendor(vendors, n.Vendor);
            var date = chosen ?? bd;
            var tillCash = Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), n.Method);
            await DateGateAsync(days, tx, ctx.PropertyId, bd, date, tillCash, c).ConfigureAwait(false);
            if (description is not null)
            {
                var dups = ExpenseScc.Duplicates(await store.FactsOnAsync(tx, ctx.PropertyId, date, c).ConfigureAwait(false), description, n.Amount, date);
                if (dups.Count > 0 && !req.ConfirmDuplicate) throw HccException.Validation(ExpenseScc.DuplicateMessage(date, n.Amount, description));
            }

            if (!req.Draft) ExpenseMasters.EnsureBill(cat, n.Receipt);
            var over = await MonthGateAsync(registers, tx, ctx.PropertyId, date, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false);
            var status = req.Draft ? ExpenseState.Draft : ExpenseState.Pending;
            if (ven.Item2 && masters is not null) await masters.AddVendorAsync(tx, ctx.PropertyId, ven.Item1, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var e = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, ven.Item1, cat.Name, n.Amount, n.Method, date, status, ctx.PrincipalId,
                n.Notes, n.Receipt, null, null, null, null, UtcNow, description, gst.RateBps, gst.Paise, ItcAllowed: itc), c).ConfigureAwait(false);
            if (photo is { } p) await store.PutBillPhotoAsync(tx, e.ExpenseId, p.ContentType, p.Bytes, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var dupOf = ExpenseScc.Duplicates(await store.FactsOnAsync(tx, ctx.PropertyId, date, c).ConfigureAwait(false), description, n.Amount, date, e.ExpenseId);
            var alert = await BudgetAlertAsync(registers, store, tx, ctx.PropertyId, cat.Name, date, bd, n.Amount.Value, c).ConfigureAwait(false);
            await tx.AuditAsync(req.Draft ? "EXPENSE_DRAFT" : "EXPENSE_SUBMITTED", Target(e.ExpenseId),
                $"{description ?? ven.Item1} · {cat.Name} · {n.Amount} · {n.Method}{(date != bd ? " · dated " + StayDates.ToWire(date) : string.Empty)}"
                + (photo is null ? string.Empty : " · bill photo") + (gst.Paise is null ? string.Empty : itc ? " · ITC allowed" : " · ITC disallowed")
                + (over is null ? string.Empty : " · locked-month override: " + over), c).ConfigureAwait(false);
            if (photo is not null) e = (await store.LockAsync(tx, ctx.PropertyId, e.ExpenseId, c).ConfigureAwait(false))!;
            return new ExpenseResult(ExpenseMapping.ToView(e), dupOf, null, alert);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The expense's own date while that day is open; otherwise the business date (HCC's earlier rule: the decision dates it).</summary>
    private async Task<DateOnly> LiveDateAsync(ICommandTransaction tx, ExpenseRecord e, DateOnly bd, CancellationToken c)
    {
        if (e.BusinessDate == bd) return bd;
        var tillCash = Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, e.PropertyId, c).ConfigureAwait(false), e.Method);
        return await DateRefusalAsync(days, tx, e.PropertyId, bd, e.BusinessDate, tillCash, c).ConfigureAwait(false) is null ? e.BusinessDate : bd;
    }

    public async Task<CommandResult<ExpenseResult>> SubmitDraftAsync(CommandContext ctx, SubmitExpenseDraftRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpSubmitDraft }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanSubmitDraft(e.Status, e.SubmittedBy, ctx.PrincipalId);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            if (string.IsNullOrWhiteSpace(e.Description)) throw HccException.Validation("Description is required — add it with Edit before submitting.");
            var cat = ExpenseMasters.FindCategory(await CategoriesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), e.Category);
            if (cat is not null) ExpenseMasters.EnsureBill(cat, e.Receipt);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier (DAY_CLOSED)
            var date = await LiveDateAsync(tx, e, bd, c).ConfigureAwait(false);
            var over = await MonthGateAsync(registers, tx, ctx.PropertyId, date, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false);
            var u = await store.UpdateAsync(tx, e with { Status = ExpenseState.Pending, BusinessDate = date }, expected, UtcNow, c).ConfigureAwait(false);
            var dups = ExpenseScc.Duplicates(await store.FactsOnAsync(tx, ctx.PropertyId, date, c).ConfigureAwait(false), u.Description, u.Amount, date, u.ExpenseId);
            await tx.AuditAsync("EXPENSE_SUBMITTED", Target(u.ExpenseId), $"{u.Description ?? u.Vendor} · {u.Amount} · draft sent for approval"
                + (over is null ? string.Empty : " · locked-month override: " + over), c).ConfigureAwait(false);
            return new ExpenseResult(ExpenseMapping.ToView(u), dups, null);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>True when the principal's PERM-EXP-002 grant carries an amount limit (the Manager's): the category's override then applies.</summary>
    private async Task<bool> LimitedAsync(CommandContext ctx, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Decide, null, ApiConventions.MaxPaisePerField, ct).ConfigureAwait(false);
            return false;
        }
        catch (HccException e) when (e.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            return true;
        }
        catch (HccException e) when (e.Code == HccErrorCode.FORBIDDEN)
        {
            return false; // the permission check below refuses
        }
    }

    public async Task<CommandResult<ExpenseResult>> DecideAsync(CommandContext ctx, DecideExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        // The amount is checked on the peeked row and again under the row lock below (an edit changes it; STALE_VERSION then).
        // PERM-EXP-002's limit applies to the amount approved. A rejection posts nothing, so it is checked at 0: any A or L cell (the Manager's
        // ₹5,000 limit included) may reject at any amount, as the prototype's EXPENSE_APPROVE (limit only when approving) and POS DecideVoid.
        // DEC-120 / MA-3 server gap: a category's approval-limit override replaces a limited approver's own limit (prototype mnyApprovalLimit).
        var peek = await store.GetAsync(ctx.PropertyId, req.ExpenseId, ct).ConfigureAwait(false);
        var checkedAmount = req.Approve && peek is not null ? peek.Amount.Value : 0L;
        var category = peek is null ? null : ExpenseMasters.FindCategory(await CategoriesAsync(null, ctx.PropertyId, ct).ConfigureAwait(false), peek.Category);
        if (req.Approve && category?.ApprovalLimitPaise is { } catLimit && await LimitedAsync(ctx, ct).ConfigureAwait(false))
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Decide, null, 0, ct).ConfigureAwait(false);
            if (checkedAmount > catLimit) throw new HccException(HccErrorCode.APPROVAL_REQUIRED, $"Expenses above {new Paise(catLimit)} in {category.Name} need Owner approval.");
        }
        else
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Decide, null, checkedAmount, ct).ConfigureAwait(false);
        }

        if (req.Approve) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.PostReverse, null, null, ct).ConfigureAwait(false);
        if (peek is null) throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
        var reason = ExpenseRules.OptionalReason(req.Reason);
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpDecide }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanDecide(e.Status, e.SubmittedBy, ctx.PrincipalId);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            if (req.Approve && e.Amount != peek.Amount) throw new HccException(HccErrorCode.STALE_VERSION, "This expense was changed by someone else. Reload and try again.");
            if (req.Approve && (await store.EditorsAsync(tx, e.ExpenseId, c).ConfigureAwait(false)).Contains(ctx.PrincipalId))
                throw new HccException(HccErrorCode.FORBIDDEN, "You changed this expense — another approver decides it (nobody approves their own).");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6: the business day must be open; DEC-120: the expense keeps its own date while that day is open, else the decision dates it bd.
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier (DAY_CLOSED)
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, e.BusinessDate, "deciding this expense", c).ConfigureAwait(false);
            var date = await LiveDateAsync(tx, e, bd, c).ConfigureAwait(false);
            // MA-11: drawer cash = the method settles to a till (Payment methods master; a retired method still resolves).
            var tillCash = Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), e.Method);
            if (days is not null && req.Approve && tillCash)
                await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, date, "a cash expense", c).ConfigureAwait(false);
            var over = req.Approve ? await MonthGateAsync(registers, tx, ctx.PropertyId, date, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false) : null;
            long? movementId = null;
            int? till = null;
            if (req.Approve && ExpenseRules.DrawerMovement(tillCash, e.Amount, reversal: false) is { } move)
            {
                till = req.TillId ?? throw HccException.Validation("Choose the till that pays this cash expense.");
                movementId = await drawer.RecordMovementAsync(tx, till.Value, move.Kind, DrawerSourceExpense, e.ExpenseId, move.Amount,
                    $"Cash expense · {Payee(e)}", e.Category, c).ConfigureAwait(false);
            }

            // ACC-2 flows (contract §7, B10): drawer cash above Rs 10,000 to one payee in a day posts with a warning and "deduction lost".
            Hcc.Domain.Accounting.CashGuardResult? cashWarn = null;
            if (req.Approve && tillCash && cashGuard is not null)
            {
                cashWarn = await cashGuard.PaymentAsync(tx, ctx.PropertyId, new Accounting.CashParty(Accounting.CashParty.Vendor, e.ExpenseId, e.Vendor), e.Amount.Value, date,
                    transport: false, Payee(e), c).ConfigureAwait(false);
                if (cashWarn.DeductionLost) await cashGuard.MarkDeductionLostAsync(tx, e.ExpenseId, c).ConfigureAwait(false);
            }

            var now = UtcNow;
            var u = await store.UpdateAsync(tx, e with
            {
                Status = req.Approve ? ExpenseState.Posted : ExpenseState.Rejected,
                BusinessDate = date,
                DecidedBy = ctx.PrincipalId,
                DecidedUtc = now,
                Reason = req.Approve ? e.Reason : reason,
                TillId = till,
            }, expected, now, c).ConfigureAwait(false);
            await tx.AuditAsync(req.Approve ? "EXPENSE_POSTED" : "EXPENSE_REJECTED", Target(u.ExpenseId),
                $"{u.Description ?? u.Vendor} · {u.Amount} · {u.Method}{(reason is null ? string.Empty : " · " + reason)}{(over is null ? string.Empty : " · locked-month override: " + over)}"
                + (cashWarn is { DeductionLost: true } ? " · " + cashWarn.Message : string.Empty), c)
                .ConfigureAwait(false);
            string? alert = null;
            if (req.Approve)
            {
                await tx.PublishAsync(ExpenseEvents.Posted, u.ExpenseId, new
                {
                    expenseId = u.ExpenseId,
                    funding = u.Method,
                    reversalOf = (long?)null,
                    vendor = Payee(u), // DEC-120: the vendor is optional — the voucher narration then names the description
                    category = u.Category,
                    method = u.Method,
                    amountPaise = u.Amount.Value,
                    businessDate = StayDates.ToWire(u.BusinessDate),
                    submittedBy = u.SubmittedBy,
                    approvedBy = ctx.PrincipalId,
                    tillId = till,
                    cashMovementId = movementId,
                    gstPaise = u.GstPaise,                             // DEC-135: Accounting books ITC-allowed GST to the input-credit ledger
                    itc = ExpenseScc.ItcText(u.GstPaise, u.ItcAllowed),
                }, c).ConfigureAwait(false);
                alert = await BudgetAlertAsync(registers, store, tx, ctx.PropertyId, u.Category, u.BusinessDate, bd, 0, c).ConfigureAwait(false);
            }

            return new ExpenseResult(ExpenseMapping.ToView(u), [], movementId, alert);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseReversalResult>> ReverseAsync(CommandContext ctx, ReverseExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.PostReverse, null, null, ct).ConfigureAwait(false);
        var reason = ExpenseRules.RequiredReversalReason(req.Reason);
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpReverse }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            ExpenseRules.EnsureCanReverse(e.Status, e.IsReversal);
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // P6: the reversal posts on the open business date; the original's cash-book day must not be locked (DEC-065, like Billing corrections).
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // P6 late-write barrier (DAY_CLOSED)
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, e.BusinessDate, "reversing this expense", c).ConfigureAwait(false);
            var over = await MonthGateAsync(registers, tx, ctx.PropertyId, bd, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false);
            var needsDrawer = ExpenseRules.DrawerMovement(Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), e.Method),
                e.Amount, reversal: true);
            int? till = null;
            if (needsDrawer is not null) till = req.TillId ?? throw HccException.Validation("Choose the till that takes this cash back.");

            var now = UtcNow;
            // The reversal row first (it points at the original), then the original flips to Reversed (CK_expense_reversed). DEC-120: it carries the
            // description, −GST and the source, so the month's GST paid on purchases and the petty-cash totals net to zero.
            var rev = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, e.Vendor, e.Category, -e.Amount, e.Method, bd, ExpenseState.Posted, ctx.PrincipalId,
                null, null, reason, ctx.PrincipalId, till, e.ExpenseId, now, e.Description, e.GstRateBps, e.GstPaise is { } g ? -g : null, e.Source, ItcAllowed: e.ItcAllowed), c).ConfigureAwait(false);
            long? movementId = null;
            if (needsDrawer is { } move)
            {
                movementId = await drawer.RecordMovementAsync(tx, till!.Value, move.Kind, DrawerSourceReversal, rev.ExpenseId, move.Amount,
                    $"Expense reversal · {Payee(e)}", $"Reverses {ExpenseMapping.Ref(e.ExpenseId)}", c).ConfigureAwait(false);
            }

            var orig = await store.UpdateAsync(tx, e with { Status = ExpenseState.Reversed, ReversedBy = rev.ExpenseId, Reason = reason }, expected, now, c).ConfigureAwait(false);
            await tx.AuditAsync("EXPENSE_REVERSED", Target(e.ExpenseId),
                $"{ExpenseMapping.Ref(e.ExpenseId)} reversed by {ExpenseMapping.Ref(rev.ExpenseId)} · {e.Amount} · {reason}{(over is null ? string.Empty : " · locked-month override: " + over)}", c)
                .ConfigureAwait(false);
            await tx.PublishAsync(ExpenseEvents.Reversed, rev.ExpenseId, new
            {
                expenseId = rev.ExpenseId,
                reversalOf = e.ExpenseId,
                reversalId = rev.ExpenseId,
                originalExpenseId = e.ExpenseId,
                vendor = Payee(e),
                category = e.Category,
                method = e.Method,
                funding = e.Method,
                amountPaise = rev.Amount.Value, // the reversal record (negative), as Accounting's ExpensePostedEvent expects
                originalAmountPaise = e.Amount.Value,
                reversalAmountPaise = rev.Amount.Value,
                businessDate = StayDates.ToWire(bd),
                originalBusinessDate = StayDates.ToWire(e.BusinessDate),
                reversedBy = ctx.PrincipalId,
                reason,
                tillId = till,
                cashMovementId = movementId,
                gstPaise = rev.GstPaise,                               // DEC-135: −GST, so the reversal mirrors the ITC split
                itc = ExpenseScc.ItcText(rev.GstPaise, rev.ItcAllowed),
            }, c).ConfigureAwait(false);
            return new ExpenseReversalResult(ExpenseMapping.ToView(orig), ExpenseMapping.ToView(rev), movementId);
        }, ct).ConfigureAwait(false);
    }

    // ── DEC-120 · Edit and void (SCC editEntry / voidEntry) through HCC's role and approval rules ──

    public async Task<CommandResult<ExpenseResult>> EditAsync(CommandContext ctx, EditExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = ExpenseScc.Optional(req.Reason, ExpenseScc.NoteMax, "reason") ?? throw HccException.Validation("Reason required");
        var peek = await store.GetAsync(ctx.PropertyId, req.ExpenseId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
        var own = peek.SubmittedBy == ctx.PrincipalId;
        if (own) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);
        else if (peek.Status == ExpenseState.Draft) throw new HccException(HccErrorCode.FORBIDDEN, "You can change only your own drafts.");
        else await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Decide, null, 0, ct).ConfigureAwait(false);
        var n = ExpenseRules.NormalizeNew(null, req.Category, req.Method, req.AmountPaise, null, req.Receipt, await MethodsAsync(null, ctx.PropertyId, ct).ConfigureAwait(false));
        var description = ExpenseScc.Optional(req.Description, ExpenseScc.DescriptionMax, "description");
        var gst = ExpenseScc.Gst(req.GstRateBps, req.GstPaise, n.Amount.Value, req.ConfirmGst);
        // DEC-135: the flag is kept while the request does not send it (prototype EXP_EDIT); never allowed above the amount.
        var itc = ExpenseScc.Itc(req.Itc ?? (peek.ItcAllowed ? ExpenseScc.ItcAllowed : null), gst.Paise, n.Amount.Value);
        var photo = BillPhoto(req.BillPhotoBase64, req.BillPhotoContentType);
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpEdit }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            if (e.Status is ExpenseState.Posted or ExpenseState.Reversed || e.IsReversal)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Approved entry is locked — a posted expense changes only by a reversal and a new entry.");
            if (e.Status == ExpenseState.Rejected) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A rejected expense cannot be changed — enter it again.");
            if (e.Status == ExpenseState.Void) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This expense is void.");
            if (e.SubmittedBy != peek.SubmittedBy || e.Status != peek.Status) throw new HccException(HccErrorCode.STALE_VERSION, "This expense was changed by someone else. Reload and try again.");
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            var cat = ExpenseMasters.Category(await CategoriesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), n.Category, e.Category);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var date = e.Status == ExpenseState.Draft ? await LiveDateAsync(tx, e, bd, c).ConfigureAwait(false) : e.BusinessDate;
            var tillCash = Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), n.Method);
            await DateGateAsync(days, tx, ctx.PropertyId, bd, date, tillCash, c).ConfigureAwait(false); // SCC: "Day locked — reopen first"
            if (e.Status == ExpenseState.Pending)
            {
                if (description is null) throw HccException.Validation("Description is required");
                ExpenseMasters.EnsureBill(cat, n.Receipt);
            }

            // SCC: the edit reason doubles as the locked-month override reason.
            var over = await MonthGateAsync(registers, tx, ctx.PropertyId, date, owner, reason, UtcNow, c).ConfigureAwait(false);
            var oldMap = new Dictionary<string, object?>
            {
                ["amount"] = e.Amount.Value, ["category"] = e.Category, ["method"] = e.Method, ["description"] = e.Description ?? string.Empty,
                ["gstPaise"] = e.GstPaise ?? 0, ["gstRateBps"] = e.GstRateBps ?? 0,
            };
            var newMap = new Dictionary<string, object?>
            {
                ["amount"] = n.Amount.Value, ["category"] = cat.Name, ["method"] = n.Method, ["description"] = description ?? string.Empty,
                ["gstPaise"] = gst.Paise ?? 0, ["gstRateBps"] = gst.RateBps ?? 0,
            };
            if (!string.Equals(e.Receipt ?? string.Empty, n.Receipt ?? string.Empty, StringComparison.Ordinal)) { oldMap["receipt"] = e.Receipt ?? string.Empty; newMap["receipt"] = n.Receipt ?? string.Empty; }
            if (photo is not null) { oldMap["billPhoto"] = e.HasBillPhoto ? "(present)" : "(none)"; newMap["billPhoto"] = "(changed)"; }
            // DEC-135: the ITC flag is logged when "allowed" is set or cleared (adding / removing GST alone changes only the GST fields).
            var itcWas = ExpenseScc.ItcText(e.GstPaise, e.ItcAllowed) ?? string.Empty;
            var itcNow = ExpenseScc.ItcText(gst.Paise, itc) ?? string.Empty;
            if (itcWas != itcNow && (itcWas == ExpenseScc.ItcAllowed || itcNow == ExpenseScc.ItcAllowed)) { oldMap["itc"] = itcWas; newMap["itc"] = itcNow; }
            var now = UtcNow;
            var u = await store.UpdateAsync(tx, e with
            {
                Amount = n.Amount, Category = cat.Name, Method = n.Method, Description = description, GstRateBps = gst.RateBps, GstPaise = gst.Paise, Receipt = n.Receipt,
                BusinessDate = date, ItcAllowed = itc,
            }, expected, now, c).ConfigureAwait(false);
            if (photo is { } p) await store.PutBillPhotoAsync(tx, e.ExpenseId, p.ContentType, p.Bytes, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await store.InsertEditAsync(tx, e.ExpenseId, ctx.PrincipalId, now, reason, JsonSerializer.Serialize(oldMap), JsonSerializer.Serialize(newMap), c).ConfigureAwait(false);
            await tx.AuditAsync("EXPENSE_EDITED", Target(e.ExpenseId), $"{ExpenseMapping.Ref(e.ExpenseId)} · {reason}{(over is null ? string.Empty : " · locked-month override")}", c)
                .ConfigureAwait(false);
            var alert = await BudgetAlertAsync(registers, store, tx, ctx.PropertyId, cat.Name, date, bd, u.Status == ExpenseState.Pending ? u.Amount.Value : 0, c).ConfigureAwait(false);
            u = (await store.LockAsync(tx, ctx.PropertyId, e.ExpenseId, c).ConfigureAwait(false))!;
            return new ExpenseResult(ExpenseMapping.ToView(u), [], null, alert);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseResult>> VoidAsync(CommandContext ctx, VoidExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (!ExpenseScc.VoidReasons.Contains(reason, StringComparer.Ordinal)) throw HccException.Validation($"Choose a reason: {string.Join(", ", ExpenseScc.VoidReasons)}.");
        var note = ExpenseScc.Optional(req.Note, ExpenseScc.NoteMax, "note");
        var peek = await store.GetAsync(ctx.PropertyId, req.ExpenseId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
        if (peek.Status == ExpenseState.Draft && peek.SubmittedBy == ctx.PrincipalId)
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Submit, AccessScopes.Any, null, ct).ConfigureAwait(false);
        else if (peek.Status == ExpenseState.Draft) throw new HccException(HccErrorCode.FORBIDDEN, "You can void only your own drafts.");
        else await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.PostReverse, null, null, ct).ConfigureAwait(false); // DEC-063 G5: not the Cashier
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpVoid }, req, async (tx, c) =>
        {
            var e = await store.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Expense not found.");
            if (e.Status is ExpenseState.Posted or ExpenseState.Reversed || e.IsReversal)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Approved entry is locked — reverse it instead.");
            if (e.Status is not (ExpenseState.Draft or ExpenseState.Pending)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a draft or pending expense can be voided.");
            if (e.Status != peek.Status) throw new HccException(HccErrorCode.STALE_VERSION, "This expense was changed by someone else. Reload and try again.");
            VersionToken.EnsureMatches(e.Version, expected, "This expense");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await DateGateAsync(days, tx, ctx.PropertyId, bd, e.BusinessDate, false, c).ConfigureAwait(false);
            string? over = null;
            if (registers is not null && await registers.MonthLockAsync(tx, ctx.PropertyId, ExpenseScc.Ym(e.BusinessDate), c).ConfigureAwait(false) is { Unlocked: false })
            {
                if (owner && note is null) throw HccException.Validation("Month is tax-locked — add a specific note for the override");
                over = await MonthGateAsync(registers, tx, ctx.PropertyId, e.BusinessDate, owner, note, UtcNow, c).ConfigureAwait(false);
            }

            var now = UtcNow;
            var voidReason = reason + (note is null ? string.Empty : " — " + note);
            var u = await store.UpdateAsync(tx, e with { Status = ExpenseState.Void, VoidReason = voidReason, VoidedBy = ctx.PrincipalId, VoidedUtc = now }, expected, now, c)
                .ConfigureAwait(false);
            await tx.AuditAsync("EXPENSE_VOIDED", Target(e.ExpenseId), $"{ExpenseMapping.Ref(e.ExpenseId)} · {voidReason}{(over is null ? string.Empty : " · locked-month override")}", c)
                .ConfigureAwait(false);
            return new ExpenseResult(ExpenseMapping.ToView(u), [], null);
        }, ct).ConfigureAwait(false);
    }

    internal static string Target(long id) => "expense:" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Who was paid, for the accounting narration: the vendor, else (DEC-120: optional vendor) the description, else "—".</summary>
    internal static string Payee(ExpenseRecord e) => e.Vendor.Length > 0 ? e.Vendor : e.Description ?? "—";
}

public static class ExpenseMapping
{
    public static string Ref(long id) => "EXP-" + id.ToString(CultureInfo.InvariantCulture);

    private static string? Wire(DateTime? d) => d is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : null;

    public static ExpenseView ToView(ExpenseRecord e, IReadOnlyList<ExpenseEditRecord>? edits = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new ExpenseView(e.ExpenseId, Ref(e.ExpenseId), e.Vendor, e.Category, e.Amount.Value, e.Method, StayDates.ToWire(e.BusinessDate), e.Status,
            e.SubmittedBy, e.Notes, e.Receipt, e.Reason, e.DecidedBy, Wire(e.DecidedUtc), e.ReversalOf, e.ReversedBy, VersionToken.ToWire(e.Version),
            e.Description, e.GstRateBps, e.GstPaise, e.Source, e.SourceRef, e.VoidReason, e.VoidedBy, Wire(e.VoidedUtc), e.HasBillPhoto, Wire(e.CreatedUtc),
            edits?.Select(ToView).ToList(), ExpenseScc.ItcText(e.GstPaise, e.ItcAllowed));
    }

    public static ExpenseEditView ToView(ExpenseEditRecord x)
    {
        ArgumentNullException.ThrowIfNull(x);
        return new ExpenseEditView(x.EditId, x.By, Wire(x.AtUtc)!, x.Reason, Map(x.OldJson), Map(x.NewJson));
    }

    private static Dictionary<string, JsonElement> Map(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
}

/// <summary>Expense reads. Holders of PERM-EXP-002/003 (or property-wide EXP-001) see every expense; own-submission holders see only theirs.</summary>
public sealed class ExpenseQueries(IAccessPolicy policy, IExpenseStore store, MoneyMasters.IPaymentMethodSource? methods = null, IExpenseMasterSource? masters = null)
{
    private async Task<IReadOnlyList<Hcc.Domain.MoneyMasters.TenderMethod>?> MethodsAsync(int propertyId, CancellationToken ct) =>
        methods is null ? null : await methods.MethodsAsync(null, propertyId, ct).ConfigureAwait(false);

    private async Task<bool> WideAsync(int propertyId, int principalId, CancellationToken ct)
    {
        foreach (var perm in new[] { ExpensePermissions.Decide, ExpensePermissions.PostReverse, ExpensePermissions.Submit })
            if (await policy.HasAsync(propertyId, principalId, perm, AccessScopes.Property(propertyId), ct).ConfigureAwait(false)) return true;
        return false;
    }

    internal async Task<bool> RequireViewerAsync(int propertyId, int principalId, CancellationToken ct)
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
        var edits = await store.EditsAsync(propertyId, [e.ExpenseId], ct).ConfigureAwait(false);
        return ExpenseMapping.ToView(e, edits.TryGetValue(e.ExpenseId, out var l) ? l : null);
    }

    public async Task<ExpensePage> ListAsync(int propertyId, int principalId, string? status, string? from, string? to, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        if (status is not null && status is not (ExpenseState.Draft or ExpenseState.Pending or ExpenseState.Posted or ExpenseState.Rejected or ExpenseState.Reversed or ExpenseState.Void))
            throw HccException.Validation("Unknown expense status.");
        var wide = await RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var filter = new ExpenseFilter(status, from is null ? null : StayDates.Parse(from, "from"), to is null ? null : StayDates.Parse(to, "to"), wide ? null : principalId);
        var (items, total) = await store.ListAsync(propertyId, filter, page, pageSize, ct).ConfigureAwait(false);
        var edits = await store.EditsAsync(propertyId, items.Select(i => i.ExpenseId).ToList(), ct).ConfigureAwait(false);
        return new ExpensePage(items.Select(i => ExpenseMapping.ToView(i, edits.TryGetValue(i.ExpenseId, out var l) ? l : null)).ToList(), page, pageSize, total, !wide);
    }

    /// <summary>Posted figures for a business date (approvers and the Accountant; closing and reports read the same rule).</summary>
    public async Task<ExpenseSummaryView> SummaryAsync(int propertyId, int principalId, string date, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ExpensePermissions.Decide, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var d = StayDates.Parse(date);
        var t = ExpenseRules.Totals(await store.FactsOnAsync(propertyId, d, ct).ConfigureAwait(false), d, await MethodsAsync(propertyId, ct).ConfigureAwait(false));
        return new ExpenseSummaryView(StayDates.ToWire(d), t.Cash.Value, t.NonCash.Value, t.VendorCredit.Value, t.Total.Value, t.Reversals.Value, t.Count);
    }

    public async Task<ExpenseOptionsView> OptionsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        // MA-11: the active funding sources of the Payment methods master (the old fixed list when the property has none). DEC-120 server gap:
        // the categories are the ACTIVE rows of the Expense categories master (was a hard-coded list), the seed list while it is empty.
        var cats = masters is null ? null : await masters.CategoriesAsync(null, propertyId, ct).ConfigureAwait(false);
        var names = cats is { Count: > 0 } ? cats.Where(c => c.Active).Select(c => c.Name).ToList() : ExpenseMethods.SuggestedCategories.ToList();
        return new ExpenseOptionsView(Hcc.Domain.MoneyMasters.PaymentMethodBook.NamesAt(await MethodsAsync(propertyId, ct).ConfigureAwait(false),
            Hcc.Contracts.MoneyMasters.MoneyMasterValues.Expenses), names);
    }

    /// <summary>GET /expenses/{id}/bill-photo: whoever may read the expense.</summary>
    public async Task<ExpenseBillPhotoView> BillPhotoAsync(int propertyId, int principalId, long expenseId, CancellationToken ct = default)
    {
        await GetAsync(propertyId, principalId, expenseId, ct).ConfigureAwait(false); // the same visibility as the expense itself
        var p = await store.BillPhotoAsync(propertyId, expenseId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "No bill photo is stored for this expense.");
        return new ExpenseBillPhotoView(expenseId, p.ContentType, Convert.ToBase64String(p.Image));
    }
}
