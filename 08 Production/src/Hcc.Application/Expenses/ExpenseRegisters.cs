using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;

namespace Hcc.Application.Expenses;

/// <summary>
/// DEC-120 SCC expense registers (prototype <c>handlers/expenses.ts</c> EXP_*): monthly budgets (SetExpenseBudget, CopyExpenseBudgets),
/// recurring templates (Save / Toggle / Delete / PostRecurringExpense — a post raises a DRAFT for approval), petty-cash float top-ups
/// (RequestPettyFloat, DecidePettyFloat — a second person approves) and disbursements (RecordPettyDisbursement — a drawer-cash expense for
/// approval), the Udhaar credit register (AddUdhaar, PayUdhaar, VoidUdhaar — never touches cash or income) and the month lock
/// (LockExpenseMonth — SCC "Generate &amp; lock tax feed", Owner). Every command runs through <see cref="ICommandRunner"/> (audit + idempotency).
/// SCC decisions wave (owner, 27 Oct 2026): DEC-129 budgets and recurring templates are the Owner's (PERM-MST-002; posting a due template stays
/// PERM-EXP-001); DEC-130 an Udhaar payment is a desk receipt — cash into the drawer of the open shift (<see cref="ICashDrawerPort"/>, movement
/// kind payment / source type udhaar), every mode booked in Accounting (UdhaarPaymentRecorded: Dr tender · Cr Udhaar receivables); DEC-131
/// UnlockExpenseMonth (Owner + reason, audited; the feed turns stale until LockExpenseMonth locks the month again).
/// </summary>
public sealed class ExpenseRegisterCommands(ICommandRunner runner, IAccessPolicy policy, IExpenseStore store, IExpenseRegisterStore registers, TimeProvider clock,
    Hcc.Application.Closing.IBusinessDayGuard? days = null, MoneyMasters.IPaymentMethodSource? methods = null, IExpenseMasterSource? masters = null,
    Billing.ICashDrawerPort? drawer = null, Accounting.CashGuardService? cashGuard = null)
{
    public const string OpSetBudget = "SetExpenseBudget";
    public const string OpCopyBudgets = "CopyExpenseBudgets";
    public const string OpSaveRecurring = "SaveRecurringExpense";
    public const string OpToggleRecurring = "ToggleRecurringExpense";
    public const string OpDeleteRecurring = "DeleteRecurringExpense";
    public const string OpPostRecurring = "PostRecurringExpense";
    public const string OpRequestPettyFloat = "RequestPettyFloat";
    public const string OpDecidePettyFloat = "DecidePettyFloat";
    public const string OpPettyDisbursement = "RecordPettyDisbursement";
    public const string OpAddUdhaar = "AddUdhaar";
    public const string OpPayUdhaar = "PayUdhaar";
    public const string OpVoidUdhaar = "VoidUdhaar";
    public const string OpLockMonth = "LockExpenseMonth";
    public const string OpUnlockMonth = "UnlockExpenseMonth";

    public static readonly IReadOnlyList<string> Operations =
        [OpSetBudget, OpCopyBudgets, OpSaveRecurring, OpToggleRecurring, OpDeleteRecurring, OpPostRecurring, OpRequestPettyFloat, OpDecidePettyFloat, OpPettyDisbursement,
         OpAddUdhaar, OpPayUdhaar, OpVoidUdhaar, OpLockMonth, OpUnlockMonth];

    /// <summary>DEC-129 refusals (prototype EXP_OWNER_ONLY_BUDGETS / EXP_OWNER_ONLY_RECURRING).</summary>
    public const string OwnerOnlyBudgets = "Only the Owner sets and copies the monthly budgets (money settings, DEC-058).";
    public const string OwnerOnlyRecurring = "Only the Owner keeps the recurring templates (money settings, DEC-058). Anyone who enters expenses can post a due template.";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    private Task Need(CommandContext ctx, string perm, CancellationToken ct, long? amount = null, AccessScope? scope = null) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, perm, scope, perm == ExpensePermissions.Decide ? amount ?? 0 : amount, ct);

    /// <summary>DEC-129: budgets and recurring templates are Owner-only money settings (DEC-058) = PERM-MST-002 (no new permission id).</summary>
    private async Task OwnerMoneyAsync(CommandContext ctx, string message, CancellationToken ct)
    {
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, message);
    }

    private async Task<IReadOnlyList<Hcc.Domain.MoneyMasters.TenderMethod>?> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        methods is null ? null : await methods.MethodsAsync(tx, propertyId, ct).ConfigureAwait(false);

    private async Task<ExpenseCategoryRow> CategoryAsync(ICommandTransaction tx, int propertyId, string? value, CancellationToken ct) =>
        ExpenseMasters.Category(masters is null ? null : await masters.CategoriesAsync(tx, propertyId, ct).ConfigureAwait(false), value);

    private async Task<(string Name, bool IsNew)> VendorAsync(ICommandTransaction tx, int propertyId, string? value, CancellationToken ct) =>
        masters is null ? ((value ?? string.Empty).Trim(), false) : ExpenseMasters.Vendor(await masters.VendorsAsync(tx, propertyId, ct).ConfigureAwait(false), value);

    // ── Budgets (SCC setBudget / copyBudgets) ──

    public async Task<CommandResult<ExpenseRegisterResult>> SetBudgetAsync(CommandContext ctx, SetExpenseBudgetRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await OwnerMoneyAsync(ctx, OwnerOnlyBudgets, ct).ConfigureAwait(false); // DEC-129
        if (req.AmountPaise < 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter the budget in rupees (0 = no budget).");
        return await runner.RunAsync(ctx with { Operation = OpSetBudget }, req, async (tx, c) =>
        {
            var cat = ExpenseMasters.FindCategory(masters is null ? null : await masters.CategoriesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), req.Category)
                      ?? throw HccException.Validation("Choose a category from the list.");
            var month = ExpenseScc.Ym(await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            await registers.SetBudgetAsync(tx, ctx.PropertyId, month, cat.Name, req.AmountPaise, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var msg = $"{month} · {cat.Name} = {new Paise(req.AmountPaise)}";
            await tx.AuditAsync("EXPENSE_BUDGET_SET", "expense-budget:" + month, msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> CopyBudgetsAsync(CommandContext ctx, CopyExpenseBudgetsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await OwnerMoneyAsync(ctx, OwnerOnlyBudgets, ct).ConfigureAwait(false); // DEC-129
        return await runner.RunAsync(ctx with { Operation = OpCopyBudgets }, req, async (tx, c) =>
        {
            var month = ExpenseScc.Ym(await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            var prev = ExpenseScc.PrevMonth(month);
            var src = await registers.BudgetsAsync(tx, ctx.PropertyId, prev, c).ConfigureAwait(false);
            var heads = ExpenseScc.CopyHeads(month, src, await registers.BudgetsAsync(tx, ctx.PropertyId, month, c).ConfigureAwait(false));
            foreach (var h in heads) await registers.SetBudgetAsync(tx, ctx.PropertyId, month, h, src[h], ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var msg = $"{prev} → {month} · {heads.Count} head{(heads.Count > 1 ? "s" : string.Empty)}";
            await tx.AuditAsync("EXPENSE_BUDGET_COPY", "expense-budget:" + month, msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg);
        }, ct).ConfigureAwait(false);
    }

    // ── Recurring templates (SCC P1-16) ──

    public async Task<CommandResult<ExpenseRegisterResult>> SaveRecurringAsync(CommandContext ctx, SaveRecurringExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await OwnerMoneyAsync(ctx, OwnerOnlyRecurring, ct).ConfigureAwait(false); // DEC-129
        var (name, day) = ExpenseScc.Template(req.Name, req.AmountPaise, req.DueDay);
        var method = Hcc.Domain.MoneyMasters.PaymentMethodBook.Pick(await MethodsAsync(null, ctx.PropertyId, ct).ConfigureAwait(false), req.Method,
            Hcc.Contracts.MoneyMasters.MoneyMasterValues.Expenses, null, requireReference: false).Name;
        return await runner.RunAsync(ctx with { Operation = OpSaveRecurring }, req, async (tx, c) =>
        {
            var cat = await CategoryAsync(tx, ctx.PropertyId, req.Category, c).ConfigureAwait(false);
            var ven = await VendorAsync(tx, ctx.PropertyId, req.Vendor, c).ConfigureAwait(false);
            var all = await registers.RecurringAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (req.TemplateId is { } id)
            {
                var t = await registers.LockRecurringAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
                await registers.UpdateRecurringAsync(tx, t with { Name = name, Amount = req.AmountPaise, DueDay = day, Category = cat.Name, Method = method, Vendor = ven.Name }, c)
                    .ConfigureAwait(false);
                var m = $"Template {Id(id)} · {name} · {new Paise(req.AmountPaise)} · day {day}";
                await tx.AuditAsync("EXPENSE_RECURRING_EDIT", "expense-recurring:" + Id(id), m, c).ConfigureAwait(false);
                return new ExpenseRegisterResult(m, id);
            }

            if (all.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) && !req.ConfirmDuplicate)
                throw HccException.Validation($"A template named \"{name}\" already exists. Add another?");
            var nid = await registers.InsertRecurringAsync(tx, new RecurringRecord(0, ctx.PropertyId, name, req.AmountPaise, day, cat.Name, method, ven.Name, true, ctx.PrincipalId, UtcNow), c)
                .ConfigureAwait(false);
            var msg = $"Template {Id(nid)} · {name} · {new Paise(req.AmountPaise)} · day {day}";
            await tx.AuditAsync("EXPENSE_RECURRING_ADD", "expense-recurring:" + Id(nid), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, nid);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> ToggleRecurringAsync(CommandContext ctx, RecurringExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await OwnerMoneyAsync(ctx, OwnerOnlyRecurring, ct).ConfigureAwait(false); // DEC-129
        return await runner.RunAsync(ctx with { Operation = OpToggleRecurring }, req, async (tx, c) =>
        {
            var t = await registers.LockRecurringAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
            await registers.UpdateRecurringAsync(tx, t with { Active = !t.Active }, c).ConfigureAwait(false);
            var msg = $"{t.Name} → {(t.Active ? "inactive" : "active")}";
            await tx.AuditAsync("EXPENSE_RECURRING_TOGGLE", "expense-recurring:" + Id(t.TemplateId), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, t.TemplateId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> DeleteRecurringAsync(CommandContext ctx, RecurringExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await OwnerMoneyAsync(ctx, OwnerOnlyRecurring, ct).ConfigureAwait(false); // DEC-129
        return await runner.RunAsync(ctx with { Operation = OpDeleteRecurring }, req, async (tx, c) =>
        {
            var t = await registers.LockRecurringAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
            await registers.DeleteRecurringAsync(tx, t.TemplateId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var msg = $"Template {t.Name} deleted · entries already posted from it are kept";
            await tx.AuditAsync("EXPENSE_RECURRING_DELETE", "expense-recurring:" + Id(t.TemplateId), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, t.TemplateId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> PostRecurringAsync(CommandContext ctx, PostRecurringExpenseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.Submit, ct, null, AccessScopes.Any).ConfigureAwait(false);
        if (req.AmountPaise <= 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter an amount greater than 0");
        var owner = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPostRecurring }, req, async (tx, c) =>
        {
            var t = await registers.LockRecurringAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
            if (!t.Active) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Template is inactive — activate it to post");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await ExpenseCommands.DateGateAsync(days, tx, ctx.PropertyId, bd, bd, false, c).ConfigureAwait(false);
            var month = ExpenseScc.Ym(bd);
            var refText = ExpenseScc.RecRef(t.TemplateId, month);
            var cat = await CategoryAsync(tx, ctx.PropertyId, t.Category, c).ConfigureAwait(false);
            var ven = await VendorAsync(tx, ctx.PropertyId, t.Vendor, c).ConfigureAwait(false);
            var method = Hcc.Domain.MoneyMasters.PaymentMethodBook.Pick(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), t.Method,
                Hcc.Contracts.MoneyMasters.MoneyMasterValues.Expenses, null, requireReference: false).Name;
            var (n, sum) = await store.SourceRefAsync(tx, ctx.PropertyId, refText, c).ConfigureAwait(false);
            if (n > 0 && !req.Again) throw HccException.Validation(ExpenseScc.AlreadyPostedMessage(month, n, new Paise(sum)));
            var over = await ExpenseCommands.MonthGateAsync(registers, tx, ctx.PropertyId, bd, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false);
            if (ven.IsNew && masters is not null) await masters.AddVendorAsync(tx, ctx.PropertyId, ven.Name, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var e = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, ven.Name, cat.Name, new Paise(req.AmountPaise), method, bd, ExpenseState.Draft, ctx.PrincipalId,
                null, null, null, null, null, null, UtcNow, ExpenseScc.RecurringDescription(t.Name, month), null, null, "recurring", refText), c).ConfigureAwait(false);
            var alert = await ExpenseCommands.BudgetAlertAsync(registers, store, tx, ctx.PropertyId, cat.Name, bd, bd, req.AmountPaise, c).ConfigureAwait(false);
            var msg = $"Posted {t.Name} {new Paise(req.AmountPaise)} as draft {ExpenseMapping.Ref(e.ExpenseId)} — submit it for approval";
            await tx.AuditAsync("EXPENSE_RECURRING_POST", ExpenseCommands.Target(e.ExpenseId), msg + (over is null ? string.Empty : " · locked-month override: " + over), c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, e.ExpenseId, alert);
        }, ct).ConfigureAwait(false);
    }

    // ── Petty cash (SCC setFloat / pettyOut through HCC's approval and cash rules) ──

    public async Task<CommandResult<ExpenseRegisterResult>> RequestPettyFloatAsync(CommandContext ctx, RequestPettyFloatRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.Submit, ct, null, AccessScopes.Any).ConfigureAwait(false);
        if (req.AmountPaise <= 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter amount");
        var note = ExpenseScc.Optional(req.Note, ExpenseScc.NoteMax, "note");
        return await runner.RunAsync(ctx with { Operation = OpRequestPettyFloat }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await ExpenseCommands.DateGateAsync(days, tx, ctx.PropertyId, bd, bd, false, c).ConfigureAwait(false);
            var id = await registers.InsertPettyAsync(tx, new PettyFloatRecord(0, ctx.PropertyId, req.AmountPaise, bd, "Pending", note, ctx.PrincipalId, UtcNow, null, null, null), c)
                .ConfigureAwait(false);
            var msg = $"PTY-{Id(id)} · float top-up {new Paise(req.AmountPaise)} · awaiting approval";
            await tx.AuditAsync("PETTY_FLOAT_REQUESTED", "petty-float:" + Id(id), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, id);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> DecidePettyFloatAsync(CommandContext ctx, DecidePettyFloatRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var reason = ExpenseScc.Optional(req.Reason, ExpenseScc.NoteMax, "reason");
        var peek = (await registers.PettyAsync(ctx.PropertyId, ct).ConfigureAwait(false)).FirstOrDefault(p => p.EntryId == req.EntryId)
                   ?? throw new HccException(HccErrorCode.NOT_FOUND, "Top-up not found.");
        // PERM-EXP-002 with its limit on the amount approved (the Manager's ₹5,000); a rejection is checked at 0 (as DecideExpense).
        await Need(ctx, ExpensePermissions.Decide, ct, req.Approve ? peek.Amount : 0).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpDecidePettyFloat }, req, async (tx, c) =>
        {
            var e = await registers.LockPettyAsync(tx, ctx.PropertyId, req.EntryId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Top-up not found.");
            if (e.Status != "Pending") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This top-up is no longer pending.");
            if (e.RecordedBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve your own request.");
            if (e.Amount != peek.Amount) throw new HccException(HccErrorCode.STALE_VERSION, "This top-up changed. Reload and try again.");
            await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await registers.DecidePettyAsync(tx, e.EntryId, req.Approve ? "Approved" : "Rejected", ctx.PrincipalId, UtcNow, reason, c).ConfigureAwait(false);
            var msg = $"PTY-{Id(e.EntryId)} · float top-up {new Paise(e.Amount)} {(req.Approve ? "approved" : "rejected")}";
            await tx.AuditAsync(req.Approve ? "PETTY_FLOAT_APPROVED" : "PETTY_FLOAT_REJECTED", "petty-float:" + Id(e.EntryId), msg + (reason is null ? string.Empty : " · " + reason), c)
                .ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, e.EntryId);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> PettyDisbursementAsync(CommandContext ctx, RecordPettyDisbursementRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.Submit, ct, null, AccessScopes.Any).ConfigureAwait(false);
        if (req.AmountPaise <= 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter amount");
        var what = ExpenseScc.Optional(req.For, 120, "text");
        var receipt = ExpenseScc.Optional(req.Receipt, ExpenseRules.ReceiptMax, "bill number");
        var owner = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPettyDisbursement }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await ExpenseCommands.DateGateAsync(days, tx, ctx.PropertyId, bd, bd, false, c).ConfigureAwait(false);
            var cat = await CategoryAsync(tx, ctx.PropertyId, req.Category, c).ConfigureAwait(false);
            ExpenseMasters.EnsureBill(cat, receipt);
            var list = await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var cash = Hcc.Domain.MoneyMasters.PaymentMethodBook.NamesAt(list, Hcc.Contracts.MoneyMasters.MoneyMasterValues.Expenses)
                           .FirstOrDefault(m => Hcc.Domain.MoneyMasters.PaymentMethodBook.SettlesToTill(list, m))
                       ?? throw HccException.Validation("No drawer-cash funding source is set up for expenses (Master configuration › Payment methods).");
            var over = await ExpenseCommands.MonthGateAsync(registers, tx, ctx.PropertyId, bd, owner, req.OverrideReason, UtcNow, c).ConfigureAwait(false);
            var e = await store.InsertAsync(tx, new NewExpense(ctx.PropertyId, string.Empty, cat.Name, new Paise(req.AmountPaise), cash, bd, ExpenseState.Pending, ctx.PrincipalId,
                null, receipt, null, null, null, null, UtcNow, ExpenseScc.PettyDescription(what), null, null, "petty", null), c).ConfigureAwait(false);
            var msg = $"{ExpenseMapping.Ref(e.ExpenseId)} · petty {new Paise(req.AmountPaise)} · {ExpenseScc.PettyDescription(what)} · awaiting approval";
            await tx.AuditAsync("PETTY_DISBURSEMENT", ExpenseCommands.Target(e.ExpenseId), msg + (over is null ? string.Empty : " · locked-month override: " + over), c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, e.ExpenseId);
        }, ct).ConfigureAwait(false);
    }

    // ── Udhaar (SCC Wave 3) ──

    public async Task<CommandResult<ExpenseRegisterResult>> AddUdhaarAsync(CommandContext ctx, AddUdhaarRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.Submit, ct, null, AccessScopes.Any).ConfigureAwait(false);
        var name = ExpenseScc.Optional(req.Name, 80, "name") ?? throw HccException.Validation("Customer name is required");
        if (req.AmountPaise <= 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter a valid amount");
        var note = ExpenseScc.Optional(req.Note, ExpenseScc.NoteMax, "note");
        DateOnly? due = string.IsNullOrWhiteSpace(req.DueDate) ? null : StayDates.Parse(req.DueDate, "dueDate");
        // ACC-1 (contract §4, rulebook A17, DEC-144): what was sold on credit and its GST rate; the credit is booked as a sale the same day.
        var kind = string.IsNullOrWhiteSpace(req.Kind) ? null : req.Kind.Trim();
        if (kind is not null and not ("restaurant" or "room" or "other")) throw HccException.Validation("Choose what was sold on credit: restaurant bill, room folio or other.");
        var gstBps = req.GstBps ?? 0;
        if (gstBps is < 0 or > 2800) throw HccException.Validation("GST rate: 0 to 28 %.");
        if (kind is null && gstBps != 0) throw HccException.Validation("Choose what was sold on credit before its GST rate.");
        return await runner.RunAsync(ctx with { Operation = OpAddUdhaar }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var dueDate = due ?? bd.AddDays(ExpenseScc.UdhaarDueDays);
            var mobile = ExpenseScc.UdhaarMobile(req.Mobile);
            var id = await registers.InsertUdhaarAsync(tx, new UdhaarRecord(0, ctx.PropertyId, name, mobile, req.AmountPaise, bd, dueDate, note,
                ctx.PrincipalId, UtcNow, false, null, null, null, [], kind, gstBps), c).ConfigureAwait(false);
            await tx.PublishAsync(Contracts.Accounting.AccountingEvents.UdhaarCreditRaised, id,
                new Contracts.Accounting.UdhaarCreditRaisedEvent(id, bd, name, mobile, req.AmountPaise, kind, gstBps), c).ConfigureAwait(false);
            var msg = $"UD-{Id(id)} · credit {name} {new Paise(req.AmountPaise)} · due {StayDates.ToWire(dueDate)}" + (kind is null ? "" : $" · {kind} · sale booked (Udhaar receivables)");
            await tx.AuditAsync("UDHAAR_ADDED", "udhaar:" + Id(id), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, id);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SCC payReceivable (more than outstanding + ₹0.50 is capped). DEC-130: a DESK RECEIPT — a front-desk payment method (Payment methods master;
    /// a non-cash method needs its reference, like other receipts); cash goes into the drawer of <c>TillId</c> under the rules of every cash
    /// receipt: the open business day and cash-book day, the till's open shift on the business date and PERM-PAY-001 on that shift (the
    /// Reception / Cashier's own); the movement is kind payment, source type udhaar (expected cash, the count, the day close and the cash
    /// book include it). Every mode publishes UdhaarPaymentRecorded (Dr tender · Cr Udhaar receivables): it settles the credit, never income.
    /// </summary>
    public async Task<CommandResult<ExpenseRegisterResult>> PayUdhaarAsync(CommandContext ctx, PayUdhaarRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.Submit, ct, null, AccessScopes.Any).ConfigureAwait(false);
        if (req.AmountPaise <= 0 || req.AmountPaise > ApiConventions.MaxPaisePerField) throw HccException.Validation("Enter an amount");
        var reference = ExpenseScc.Optional(req.Reference, 120, "reference");
        var list = await MethodsAsync(null, ctx.PropertyId, ct).ConfigureAwait(false);
        var method = Hcc.Domain.MoneyMasters.PaymentMethodBook.Pick(list, req.Mode, Hcc.Contracts.MoneyMasters.MoneyMasterValues.FrontDesk, reference);
        var cash = method.SettlesToTill;
        if (cash && req.TillId is null) throw HccException.Validation("Choose the till that receives the cash (Udhaar cash goes into the open shift's drawer).");
        if (cash && drawer is null) throw new InvalidOperationException("Udhaar cash needs the cash drawer port.");
        return await runner.RunAsync(ctx with { Operation = OpPayUdhaar }, req, async (tx, c) =>
        {
            var r = await registers.LockUdhaarAsync(tx, ctx.PropertyId, req.ReceivableId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Credit not found.");
            if (r.Void) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This credit was written off.");
            if (r.Outstanding <= 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Nothing is outstanding on this credit.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false); // a closed day cannot be changed
            int? till = null;
            if (cash)
            {
                if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, bd, "cash into the drawer", c).ConfigureAwait(false);
                long shiftId;
                try
                {
                    shiftId = await drawer!.OpenShiftIdAsync(tx, req.TillId!.Value, c).ConfigureAwait(false);
                }
                catch (HccException e) when (e.Code == HccErrorCode.INVALID_TRANSITION)
                {
                    throw new HccException(HccErrorCode.INVALID_TRANSITION, "Open cash shift required: Udhaar cash goes into the open shift's drawer. " + e.Message);
                }

                await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.DeskReceipt, new AccessScope(Billing.BillingPermissions.ShiftScope, shiftId), null, c)
                    .ConfigureAwait(false);
                till = req.TillId;
            }

            var p = ExpenseScc.UdhaarPayment(r.Outstanding, req.AmountPaise);
            if (cash && cashGuard is not null) // ACC-2 flows (contract §7, B9): the credit's cash reaching Rs 2,00,000 is refused (CASH_LIMIT)
                await cashGuard.EnsureReceiptAsync(tx, ctx.PropertyId, new Accounting.CashParty(Accounting.CashParty.Udhaar, r.ReceivableId), p.Amount, bd, r.Name, c).ConfigureAwait(false);
            var id = await registers.InsertSettlementAsync(tx, new UdhaarSettlementRecord(0, r.ReceivableId, p.Amount, bd, method.Name, ctx.PrincipalId, UtcNow, true, reference, till), c)
                .ConfigureAwait(false);
            long? movementId = null;
            if (cash)
                movementId = await drawer!.RecordMovementAsync(tx, till!.Value, Hcc.Domain.Payments.CashMovementKind.Payment, Payments.CashierService.UdhaarSource, id, new Paise(p.Amount),
                    "Udhaar received · " + r.Name, reference ?? "Udhaar UD-" + Id(r.ReceivableId), c).ConfigureAwait(false);
            await tx.PublishAsync(Contracts.Accounting.AccountingEvents.UdhaarPaymentRecorded, r.ReceivableId,
                new Contracts.Accounting.UdhaarPaymentRecordedEvent(r.ReceivableId, id, bd, method.Name, p.Amount, r.Name, reference, movementId), c).ConfigureAwait(false);
            var msg = $"UD-{Id(r.ReceivableId)} · {r.Name} paid {new Paise(p.Amount)}{(p.Capped ? " (more than outstanding — capped)" : string.Empty)} · "
                      + (cash ? "cash into the drawer (desk receipt)" : method.Name + " desk receipt");
            await tx.AuditAsync("UDHAAR_PAID", "udhaar:" + Id(r.ReceivableId), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, id);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseRegisterResult>> VoidUdhaarAsync(CommandContext ctx, VoidUdhaarRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Need(ctx, ExpensePermissions.PostReverse, ct).ConfigureAwait(false);
        var reason = ExpenseScc.Optional(req.Reason, ExpenseScc.NoteMax, "reason");
        return await runner.RunAsync(ctx with { Operation = OpVoidUdhaar }, req, async (tx, c) =>
        {
            var r = await registers.LockUdhaarAsync(tx, ctx.PropertyId, req.ReceivableId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Credit not found.");
            if (r.Void) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This credit is already written off.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await registers.VoidUdhaarAsync(tx, r.ReceivableId, reason, ctx.PrincipalId, bd, c).ConfigureAwait(false);
            var msg = $"UD-{Id(r.ReceivableId)} · {r.Name} written off · outstanding {new Paise(r.Outstanding)}{(reason is null ? string.Empty : " · " + reason)}";
            await tx.AuditAsync("UDHAAR_VOIDED", "udhaar:" + Id(r.ReceivableId), msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg, r.ReceivableId);
        }, ct).ConfigureAwait(false);
    }

    // ── Month lock (SCC genTaxFeed) ──

    public async Task<CommandResult<ExpenseRegisterResult>> LockMonthAsync(CommandContext ctx, LockExpenseMonthRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only the Owner can lock the month / generate the tax feed.");
        return await runner.RunAsync(ctx with { Operation = OpLockMonth }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var month = ExpenseScc.LockableMonth(req.Month, bd);
            var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            await registers.MonthLockAsync(tx, ctx.PropertyId, month, c).ConfigureAwait(false); // key-range lock: writers of the month wait
            var sum = ExpenseScc.Summary(await store.LedgerAsync(tx, ctx.PropertyId, first, first.AddMonths(1).AddDays(-1), c).ConfigureAwait(false), month);
            await registers.SaveMonthLockAsync(tx, ctx.PropertyId, new MonthLockRecord(month, sum.ExpensePaise, sum.Count, sum.GstPaidPaise, JsonSerializer.Serialize(sum.ByCategory),
                ctx.PrincipalId, UtcNow, false, null, sum.GstItcPaise), c).ConfigureAwait(false);
            var msg = $"{month} tax feed generated; expense {new Paise(sum.ExpensePaise)} · GST paid on purchases {new Paise(sum.GstPaidPaise)} (ITC allowed {new Paise(sum.GstItcPaise)}) · month locked";
            await tx.AuditAsync("EXPENSE_MONTH_LOCK", "expense-month:" + month, msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// DEC-131 (owner): the Owner unlocks a locked month with a reason (3–200 characters; audited EXPENSE_MONTH_UNLOCK, like Accounting's period
    /// unlock). Entries dated in it then need no override; the feed is marked stale and LockExpenseMonth locks it again (regenerating the feed).
    /// </summary>
    public async Task<CommandResult<ExpenseRegisterResult>> UnlockMonthAsync(CommandContext ctx, UnlockExpenseMonthRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only the Owner can unlock a tax-locked month.");
        var month = (req.Month ?? string.Empty).Trim();
        var reason = ExpenseScc.UnlockReason(req.Reason);
        return await runner.RunAsync(ctx with { Operation = OpUnlockMonth }, req, async (tx, c) =>
        {
            var f = await registers.MonthLockAsync(tx, ctx.PropertyId, month, c).ConfigureAwait(false)
                    ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Month {month} is not locked.");
            if (f.Unlocked) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Month {month} is already unlocked — lock it again after the changes.");
            await registers.UnlockMonthAsync(tx, ctx.PropertyId, month, ctx.PrincipalId, UtcNow, reason, c).ConfigureAwait(false);
            var msg = $"{month} unlocked · {reason} · entries dated in {month} need no override now; its feed is stale until the month is locked again";
            await tx.AuditAsync("EXPENSE_MONTH_UNLOCK", "expense-month:" + month, msg, c).ConfigureAwait(false);
            return new ExpenseRegisterResult(msg);
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>DEC-120 reads: GET /expenses/registers and the SCC CSV exports (/expenses/export/ledger, /expenses/export/month).</summary>
public sealed class ExpenseRegisterQueries(IAccessPolicy policy, IExpenseStore store, IExpenseRegisterStore registers, ExpenseQueries expenses)
{
    private static string Wire(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static string? Wire(DateTime? d) => d is { } x ? Wire(x) : null;

    public async Task<ExpenseRegistersView> RegistersAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await expenses.RequireViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var budgets = await registers.AllBudgetsAsync(propertyId, ct).ConfigureAwait(false);
        var rec = await registers.RecurringAsync(null, propertyId, ct).ConfigureAwait(false);
        var petty = await registers.PettyAsync(propertyId, ct).ConfigureAwait(false);
        var ud = await registers.UdhaarAsync(propertyId, ct).ConfigureAwait(false);
        var locks = await registers.MonthLocksAsync(null, propertyId, ct).ConfigureAwait(false);
        var unlocks = locks.Count == 0 ? [] : await registers.MonthUnlocksAsync(propertyId, ct).ConfigureAwait(false);
        var ledger = locks.Count == 0 ? [] : await store.LedgerAsync(null, propertyId, null, null, ct).ConfigureAwait(false);
        var bd = await BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        return new ExpenseRegistersView(bd,
            budgets.Select(b => new ExpenseBudgetView(b.Month, b.Category, b.Amount)).ToList(),
            rec.Select(t => new RecurringExpenseView(t.TemplateId, t.Name, t.Amount, t.DueDay, t.Category, t.Method, t.Vendor, t.Active, t.CreatedBy, Wire(t.CreatedUtc))).ToList(),
            petty.Select(p => new PettyFloatEntryView(p.EntryId, p.Amount, StayDates.ToWire(p.BusinessDate), p.Status, p.Note, p.RecordedBy, Wire(p.RecordedUtc), p.DecidedBy,
                Wire(p.DecidedUtc), p.Reason)).ToList(),
            ud.Select(r => new UdhaarView(r.ReceivableId, r.Name, r.Mobile, r.Amount, StayDates.ToWire(r.CreditDate), r.DueDate is { } d ? StayDates.ToWire(d) : null, r.Note,
                r.CreatedBy, Wire(r.CreatedUtc), r.Void, r.VoidReason, r.VoidedBy, r.VoidedDate is { } v ? StayDates.ToWire(v) : null,
                r.Settlements.Select(s => new UdhaarSettlementView(s.SettlementId, s.Amount, StayDates.ToWire(s.Date), s.Mode, s.RecordedBy, Wire(s.RecordedUtc), s.Desk, s.Reference, s.TillId))
                    .ToList())).ToList(),
            locks.Select(m => new ExpenseMonthLockView(m.Month, m.Expense, m.Count, m.GstPaid,
                JsonSerializer.Deserialize<Dictionary<string, long>>(m.ByCategoryJson) ?? [], m.GeneratedBy, Wire(m.GeneratedUtc),
                m.Unlocked || ExpenseScc.Stale(m.Stale, m.GeneratedUtc, ledger, m.Month), Wire(m.StaleUtc), m.GstItc, m.Unlocked, m.UnlockedBy, Wire(m.UnlockedUtc), m.UnlockReason,
                unlocks.Where(u => u.Month == m.Month).Select(u => new ExpenseMonthUnlockView(u.By, Wire(u.AtUtc), u.Reason)).ToList())).ToList());
    }

    private async Task<string> BusinessDateAsync(int propertyId, CancellationToken ct) =>
        StayDates.ToWire(await store.ReadBusinessDateAsync(propertyId, ct).ConfigureAwait(false));

    /// <summary>Who may export: an expense decider (Owner, Manager, Accountant — DEC-063 G13: the Cashier does not export reports).</summary>
    private async Task RequireExporterAsync(int propertyId, int principalId, CancellationToken ct) =>
        await policy.RequireAsync(propertyId, principalId, ExpensePermissions.Decide, AccessScopes.Any, null, ct).ConfigureAwait(false);

    public async Task<ExpenseExportView> LedgerCsvAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireExporterAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var rows = await store.LedgerAsync(null, propertyId, null, null, ct).ConfigureAwait(false);
        var f = ExpenseScc.LedgerCsv(await store.HotelNameAsync(propertyId, ct).ConfigureAwait(false), await store.ReadBusinessDateAsync(propertyId, ct).ConfigureAwait(false), rows);
        return new ExpenseExportView(f.FileName, f.Csv, f.Rows);
    }

    public async Task<ExpenseExportView> MonthCsvAsync(int propertyId, int principalId, string month, CancellationToken ct = default)
    {
        await RequireExporterAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var m = (month ?? string.Empty).Trim();
        if (!DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)) throw HccException.Validation("Choose a month (YYYY-MM).");
        var rows = await store.LedgerAsync(null, propertyId, first, first.AddMonths(1).AddDays(-1), ct).ConfigureAwait(false);
        var f = ExpenseScc.MonthCsv(await store.HotelNameAsync(propertyId, ct).ConfigureAwait(false), m, rows);
        return new ExpenseExportView(f.FileName, f.Csv, f.Rows);
    }
}
