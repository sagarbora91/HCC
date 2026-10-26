using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.MoneyMasters;

namespace Hcc.Application.MoneyMasters;

/// <summary>
/// In-use provider of the four money masters (MA-0 <see cref="IMasterUsage"/>): payment methods, expense categories and expense vendors count
/// the records whose stored text resolves to the row (name, code or an earlier name); a bank account counts the payment methods settling to it.
/// </summary>
public sealed class MoneyMasterUsage(IMoneyMastersStore store) : IMasterUsage
{
    public IReadOnlyCollection<string> Masters { get; } = MoneyMasterIds.All.ToList();

    public async Task<MasterUsageCount> CountAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct = default)
    {
        var rows = await store.LockAllAsync(tx, propertyId, master, ct).ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => r.Id == rowId);
        if (row is null) return MasterUsageCount.None;
        if (master == MoneyMasterIds.BankAccounts)
        {
            var methods = (await store.LockAllAsync(tx, propertyId, MoneyMasterIds.PaymentMethods, ct).ConfigureAwait(false)).Where(m => m.Text(MoneyMastersRules.SettlesTo) == row.Code).ToList();
            return methods.Count == 0 ? MasterUsageCount.None : new MasterUsageCount(methods.Count, ["Payment methods " + string.Join(", ", methods.Select(m => m.Name))]);
        }

        var renames = await store.RenamesAsync(tx, propertyId, master, ct).ConfigureAwait(false);
        var counts = MoneyMastersRules.CountsByRow(rows, renames, await store.ValueCountsAsync(tx, propertyId, master, ct).ConfigureAwait(false));
        var n = counts.GetValueOrDefault(row.Id);
        return n == 0 ? MasterUsageCount.None : new MasterUsageCount(n, [master == MoneyMasterIds.PaymentMethods ? "Receipts, outlet bills and expenses" : "Expenses"]);
    }
}

/// <summary>
/// MoneyMasters commands (prototype MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE for the masters paymentMethods,
/// bankAccounts, expenseCategories, expenseVendors): Save*, Retire*, Reactivate*, Delete*, Reorder*. Every change needs PERM-MST-002 (Owner,
/// DEC-058), runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox "MasterChanged" in one transaction), checks
/// expectedVersion (STALE_VERSION) on edits and writes one mst.master_history row per changed field. Configuration: no business-day lock.
/// </summary>
public sealed class MoneyMastersCommands(ICommandRunner runner, IAccessPolicy policy, IMoneyMastersStore store, IMastersStore masters, IEnumerable<IMasterUsage> usages, TimeProvider clock)
{
    private readonly IReadOnlyList<IMasterUsage> _usages = usages.ToList();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private Task RequireAsync(CommandContext ctx, CancellationToken ct) => policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MastersPermissions.ManageMoney, null, null, ct);

    /// <summary>Sum of every registered provider of the master.</summary>
    internal async Task<MasterUsageCount> UsageAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct)
    {
        var count = 0;
        var where = new List<string>();
        foreach (var u in _usages.Where(u => u.Masters.Contains(master)))
        {
            var r = await u.CountAsync(tx, propertyId, master, rowId, ct).ConfigureAwait(false);
            count += r.Count;
            where.AddRange(r.Where);
        }

        return new MasterUsageCount(count, where);
    }

    /// <summary>Records that pass through an account: the records of every payment method settling to it.</summary>
    private async Task<int> RecordsThroughAsync(ICommandTransaction tx, int propertyId, string accountCode, CancellationToken ct)
    {
        var methods = await store.LockAllAsync(tx, propertyId, MoneyMasterIds.PaymentMethods, ct).ConfigureAwait(false);
        var n = 0;
        foreach (var m in methods.Where(m => m.Text(MoneyMastersRules.SettlesTo) == accountCode))
            n += (await UsageAsync(tx, propertyId, MoneyMasterIds.PaymentMethods, m.Id, ct).ConfigureAwait(false)).Count;
        return n;
    }

    // ── Payment methods ──
    public const string OpSavePaymentMethod = "SavePaymentMethod";
    public const string OpRetirePaymentMethod = "RetirePaymentMethod";
    public const string OpReactivatePaymentMethod = "ReactivatePaymentMethod";
    public const string OpDeletePaymentMethod = "DeletePaymentMethod";
    public const string OpReorderPaymentMethods = "ReorderPaymentMethods";

    public Task<CommandResult<PaymentMethodView>> SavePaymentMethodAsync(CommandContext ctx, SavePaymentMethodRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(ctx, OpSavePaymentMethod, MoneyMasterIds.PaymentMethods, req, req.PaymentMethodId, req.ExpectedVersion, () => MoneyMastersRules.PaymentMethod(req),
            async (tx, d, old, usage, c) => MoneyMastersRules.CheckPaymentMethod(d, old, usage, await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.BankAccounts, c).ConfigureAwait(false)),
            (tx, id, c) => PaymentMethodViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<PaymentMethodView>> RetirePaymentMethodAsync(CommandContext ctx, PaymentMethodIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpRetirePaymentMethod, MoneyMasterIds.PaymentMethods, req, req.PaymentMethodId, req.ExpectedVersion, false,
            (row, rows, _, _) => { MoneyMastersRules.EnsureMethodMayRetire(row, rows); return Task.CompletedTask; }, (tx, id, c) => PaymentMethodViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<PaymentMethodView>> ReactivatePaymentMethodAsync(CommandContext ctx, PaymentMethodIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpReactivatePaymentMethod, MoneyMasterIds.PaymentMethods, req, req.PaymentMethodId, req.ExpectedVersion, true, null,
            (tx, id, c) => PaymentMethodViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<MoneyMasterDeleteResult>> DeletePaymentMethodAsync(CommandContext ctx, PaymentMethodIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(ctx, OpDeletePaymentMethod, MoneyMasterIds.PaymentMethods, req, req.PaymentMethodId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<PaymentMethodList>> ReorderPaymentMethodsAsync(CommandContext ctx, ReorderPaymentMethodsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(ctx, OpReorderPaymentMethods, MoneyMasterIds.PaymentMethods, req, req.PaymentMethodIds, async (tx, c) =>
        {
            var views = new List<PaymentMethodView>();
            foreach (var r in await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.PaymentMethods, c).ConfigureAwait(false))
                views.Add(PaymentMethodView(r, (await UsageAsync(tx, ctx.PropertyId, MoneyMasterIds.PaymentMethods, r.Id, c).ConfigureAwait(false)).Count));
            return new PaymentMethodList(1, views.Count, views.Count, views);
        }, ct);
    }

    // ── Bank accounts and tills ──
    public const string OpSaveBankAccount = "SaveBankAccount";
    public const string OpRetireBankAccount = "RetireBankAccount";
    public const string OpReactivateBankAccount = "ReactivateBankAccount";
    public const string OpDeleteBankAccount = "DeleteBankAccount";
    public const string OpReorderBankAccounts = "ReorderBankAccounts";

    public Task<CommandResult<BankAccountView>> SaveBankAccountAsync(CommandContext ctx, SaveBankAccountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(ctx, OpSaveBankAccount, MoneyMasterIds.BankAccounts, req, req.BankAccountId, req.ExpectedVersion, () => MoneyMastersRules.BankAccount(req),
            async (tx, d, old, usage, c) =>
            {
                var accounts = await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.BankAccounts, c).ConfigureAwait(false);
                var through = old is null ? 0 : await RecordsThroughAsync(tx, ctx.PropertyId, old.Code, c).ConfigureAwait(false);
                MoneyMastersRules.CheckBankAccount(d, old, usage, through, accounts);
            },
            (tx, id, c) => BankAccountViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<BankAccountView>> RetireBankAccountAsync(CommandContext ctx, BankAccountIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpRetireBankAccount, MoneyMasterIds.BankAccounts, req, req.BankAccountId, req.ExpectedVersion, false,
            async (row, _, tx, c) => MoneyMastersRules.EnsureAccountMayRetire(row, await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.PaymentMethods, c).ConfigureAwait(false)),
            (tx, id, c) => BankAccountViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<BankAccountView>> ReactivateBankAccountAsync(CommandContext ctx, BankAccountIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpReactivateBankAccount, MoneyMasterIds.BankAccounts, req, req.BankAccountId, req.ExpectedVersion, true, null,
            (tx, id, c) => BankAccountViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<MoneyMasterDeleteResult>> DeleteBankAccountAsync(CommandContext ctx, BankAccountIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(ctx, OpDeleteBankAccount, MoneyMasterIds.BankAccounts, req, req.BankAccountId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<BankAccountList>> ReorderBankAccountsAsync(CommandContext ctx, ReorderBankAccountsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(ctx, OpReorderBankAccounts, MoneyMasterIds.BankAccounts, req, req.BankAccountIds, async (tx, c) =>
        {
            var views = new List<BankAccountView>();
            foreach (var r in await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.BankAccounts, c).ConfigureAwait(false))
                views.Add(BankAccountView(r, (await UsageAsync(tx, ctx.PropertyId, MoneyMasterIds.BankAccounts, r.Id, c).ConfigureAwait(false)).Count, true));
            return new BankAccountList(1, views.Count, views.Count, views);
        }, ct);
    }

    // ── Expense categories ──
    public const string OpSaveExpenseCategory = "SaveExpenseCategory";
    public const string OpRetireExpenseCategory = "RetireExpenseCategory";
    public const string OpReactivateExpenseCategory = "ReactivateExpenseCategory";
    public const string OpDeleteExpenseCategory = "DeleteExpenseCategory";
    public const string OpReorderExpenseCategories = "ReorderExpenseCategories";

    public Task<CommandResult<ExpenseCategoryView>> SaveExpenseCategoryAsync(CommandContext ctx, SaveExpenseCategoryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(ctx, OpSaveExpenseCategory, MoneyMasterIds.ExpenseCategories, req, req.ExpenseCategoryId, req.ExpectedVersion, () => MoneyMastersRules.ExpenseCategory(req),
            null, (tx, id, c) => ExpenseCategoryViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<ExpenseCategoryView>> RetireExpenseCategoryAsync(CommandContext ctx, ExpenseCategoryIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpRetireExpenseCategory, MoneyMasterIds.ExpenseCategories, req, req.ExpenseCategoryId, req.ExpectedVersion, false, null,
            (tx, id, c) => ExpenseCategoryViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<ExpenseCategoryView>> ReactivateExpenseCategoryAsync(CommandContext ctx, ExpenseCategoryIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpReactivateExpenseCategory, MoneyMasterIds.ExpenseCategories, req, req.ExpenseCategoryId, req.ExpectedVersion, true, null,
            (tx, id, c) => ExpenseCategoryViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<MoneyMasterDeleteResult>> DeleteExpenseCategoryAsync(CommandContext ctx, ExpenseCategoryIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(ctx, OpDeleteExpenseCategory, MoneyMasterIds.ExpenseCategories, req, req.ExpenseCategoryId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<ExpenseCategoryList>> ReorderExpenseCategoriesAsync(CommandContext ctx, ReorderExpenseCategoriesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(ctx, OpReorderExpenseCategories, MoneyMasterIds.ExpenseCategories, req, req.ExpenseCategoryIds, async (tx, c) =>
        {
            var views = new List<ExpenseCategoryView>();
            foreach (var r in await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.ExpenseCategories, c).ConfigureAwait(false))
                views.Add(ExpenseCategoryView(r, (await UsageAsync(tx, ctx.PropertyId, MoneyMasterIds.ExpenseCategories, r.Id, c).ConfigureAwait(false)).Count));
            return new ExpenseCategoryList(1, views.Count, views.Count, views);
        }, ct);
    }

    // ── Expense vendors ──
    public const string OpSaveExpenseVendor = "SaveExpenseVendor";
    public const string OpRetireExpenseVendor = "RetireExpenseVendor";
    public const string OpReactivateExpenseVendor = "ReactivateExpenseVendor";
    public const string OpDeleteExpenseVendor = "DeleteExpenseVendor";
    public const string OpReorderExpenseVendors = "ReorderExpenseVendors";

    public Task<CommandResult<ExpenseVendorView>> SaveExpenseVendorAsync(CommandContext ctx, SaveExpenseVendorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(ctx, OpSaveExpenseVendor, MoneyMasterIds.ExpenseVendors, req, req.ExpenseVendorId, req.ExpectedVersion, () => MoneyMastersRules.ExpenseVendor(req),
            null, (tx, id, c) => ExpenseVendorViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<ExpenseVendorView>> RetireExpenseVendorAsync(CommandContext ctx, ExpenseVendorIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpRetireExpenseVendor, MoneyMasterIds.ExpenseVendors, req, req.ExpenseVendorId, req.ExpectedVersion, false, null,
            (tx, id, c) => ExpenseVendorViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<ExpenseVendorView>> ReactivateExpenseVendorAsync(CommandContext ctx, ExpenseVendorIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpReactivateExpenseVendor, MoneyMasterIds.ExpenseVendors, req, req.ExpenseVendorId, req.ExpectedVersion, true, null,
            (tx, id, c) => ExpenseVendorViewAsync(tx, ctx.PropertyId, id, c), ct);
    }

    public Task<CommandResult<MoneyMasterDeleteResult>> DeleteExpenseVendorAsync(CommandContext ctx, ExpenseVendorIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(ctx, OpDeleteExpenseVendor, MoneyMasterIds.ExpenseVendors, req, req.ExpenseVendorId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<ExpenseVendorList>> ReorderExpenseVendorsAsync(CommandContext ctx, ReorderExpenseVendorsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(ctx, OpReorderExpenseVendors, MoneyMasterIds.ExpenseVendors, req, req.ExpenseVendorIds, async (tx, c) =>
        {
            var views = new List<ExpenseVendorView>();
            foreach (var r in await store.LockAllAsync(tx, ctx.PropertyId, MoneyMasterIds.ExpenseVendors, c).ConfigureAwait(false))
                views.Add(ExpenseVendorView(r, (await UsageAsync(tx, ctx.PropertyId, MoneyMasterIds.ExpenseVendors, r.Id, c).ConfigureAwait(false)).Count, true));
            return new ExpenseVendorList(1, views.Count, views.Count, views);
        }, ct);
    }

    // ── Generic engine (one per command kind, shared by the four masters) ──

    private delegate Task CheckFn(ICommandTransaction tx, MoneyDraft draft, MoneyRow? old, int usage, CancellationToken ct);

    private async Task<CommandResult<TView>> SaveAsync<TReq, TView>(CommandContext ctx, string op, string master, TReq req, int? id, string? version, Func<MoneyDraft> build,
        CheckFn? check, Func<ICommandTransaction, int, CancellationToken, Task<TView>> view, CancellationToken ct)
        where TReq : notnull
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var draft = build();
        byte[]? expected = id is null ? null : VersionToken.Parse(version);
        var (label, singular) = MoneyMastersRules.Describe(master);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            await store.SeedAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockAllAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            MoneyRow? old = null;
            var usage = 0;
            if (id is { } rowId)
            {
                old = rows.FirstOrDefault(r => r.Id == rowId) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {singular} was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This " + singular);
                MastersRules.EnsureActive(old.Active, old.Name);
                usage = (await UsageAsync(tx, ctx.PropertyId, master, rowId, c).ConfigureAwait(false)).Count;
                MastersRules.EnsureCodeMayChange(old.Code, draft.Row.Code, usage, "records");
            }

            if (rows.Any(r => r.Id != old?.Id && string.Equals(r.Code, draft.Row.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {draft.Row.Code} is already used in {label} (retired rows keep their code).", "code");
            if (rows.Any(r => r.Id != old?.Id && r.Active && string.Equals(r.Name, draft.Row.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {singular} is already called “{draft.Row.Name}”.", "name");
            if (check is not null) await check(tx, draft, old, usage, c).ConfigureAwait(false);
            var sort = draft.Row.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var changes = MoneyMastersRules.Diff(master, old, draft, sort);
            int rowIdOut;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                rowIdOut = old.Id;
                await store.UpdateAsync(tx, ctx.PropertyId, master, old.Id, expected!, draft, sort, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(label, changes);
            }
            else
            {
                rowIdOut = await store.InsertAsync(tx, ctx.PropertyId, master, draft, sort, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master {label}: added {draft.Row.Code} · {draft.Row.Name}";
            }

            foreach (var (field, o, n) in changes)
                await masters.AddHistoryAsync(tx, ctx.PropertyId, master, rowIdOut, draft.Row.Code, field, o, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await FinishAsync(tx, master, rowIdOut, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
            return await view(tx, rowIdOut, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<TView>> SetActiveAsync<TReq, TView>(CommandContext ctx, string op, string master, TReq req, int id, string? version, bool active,
        Func<MoneyRow, IReadOnlyList<MoneyRow>, ICommandTransaction, CancellationToken, Task>? canRetire, Func<ICommandTransaction, int, CancellationToken, Task<TView>> view, CancellationToken ct)
        where TReq : notnull
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var (label, singular) = MoneyMastersRules.Describe(master);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This " + singular);
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{r.Name} is already active." : $"{r.Name} is already retired.");
            if (!active && canRetire is not null) await canRetire(r, rows, tx, c).ConfigureAwait(false);
            if (active && rows.Any(x => x.Id != r.Id && x.Active && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {singular} is already called “{r.Name}” — rename one first.", "name");
            var date = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, master, r.Id, expected, active, active ? null : date, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.Id, r.Code, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var used = await UsageAsync(tx, ctx.PropertyId, master, r.Id, c).ConfigureAwait(false);
            var detail = $"Master {label}: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.Code} · {r.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            await FinishAsync(tx, master, r.Id, active ? "reactivated" : "retired", detail, c).ConfigureAwait(false);
            return await view(tx, r.Id, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<MoneyMasterDeleteResult>> DeleteAsync<TReq>(CommandContext ctx, string op, string master, TReq req, int id, string? version, CancellationToken ct)
        where TReq : notnull
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var (label, singular) = MoneyMastersRules.Describe(master);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This " + singular);
            var used = await UsageAsync(tx, ctx.PropertyId, master, r.Id, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(r.Name, used.Count, "records");
            await store.DeleteAsync(tx, master, r.Id, expected, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.Id, r.Code, "deleted", r.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync(AuditAction(master), Target(master, r.Id), $"Master {label}: {r.Code} · {r.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", r.Id, new { master, rowId = r.Id, action = "deleted" }, c).ConfigureAwait(false);
            return new MoneyMasterDeleteResult(master, r.Id, r.Code, true);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<TList>> ReorderAsync<TReq, TList>(CommandContext ctx, string op, string master, TReq req, IReadOnlyList<int>? ids,
        Func<ICommandTransaction, CancellationToken, Task<TList>> list, CancellationToken ct)
        where TReq : notnull
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var (label, singular) = MoneyMastersRules.Describe(master);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(r => r.Id).ToList(), ids, singular);
            var moved = new List<string>();
            foreach (var r in rows)
            {
                var n = order[r.Id];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, master, r.Id, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.Id, r.Code, "sortOrder", r.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(r => order[r.Id]).Select(r => r.Code);
            await tx.AuditAsync(AuditAction(master), "mst-" + master + ":order", $"Master {label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master, action = "reordered" }, c).ConfigureAwait(false);
            return await list(tx, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static async Task FinishAsync(ICommandTransaction tx, string master, int id, string action, string detail, CancellationToken ct)
    {
        await tx.AuditAsync(AuditAction(master), Target(master, id), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", id, new { master, rowId = id, action }, ct).ConfigureAwait(false);
    }

    private static string AuditAction(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => "MST_PAYMENT_METHOD",
        MoneyMasterIds.BankAccounts => "MST_BANK_ACCOUNT",
        MoneyMasterIds.ExpenseCategories => "MST_EXPENSE_CATEGORY",
        _ => "MST_EXPENSE_VENDOR",
    };

    private static string Target(string master, int id) => "mst-" + master + ":" + id.ToString(CultureInfo.InvariantCulture);

    // ── Views (commands run only for PERM-MST-002 holders: money and PAN unmasked) ──

    private async Task<MoneyRow> RowAsync(ICommandTransaction tx, int propertyId, string master, int id, CancellationToken ct) =>
        (await store.LockAllAsync(tx, propertyId, master, ct).ConfigureAwait(false)).First(r => r.Id == id);

    private async Task<PaymentMethodView> PaymentMethodViewAsync(ICommandTransaction tx, int propertyId, int id, CancellationToken ct) =>
        PaymentMethodView(await RowAsync(tx, propertyId, MoneyMasterIds.PaymentMethods, id, ct).ConfigureAwait(false), (await UsageAsync(tx, propertyId, MoneyMasterIds.PaymentMethods, id, ct).ConfigureAwait(false)).Count);

    private async Task<BankAccountView> BankAccountViewAsync(ICommandTransaction tx, int propertyId, int id, CancellationToken ct) =>
        BankAccountView(await RowAsync(tx, propertyId, MoneyMasterIds.BankAccounts, id, ct).ConfigureAwait(false), (await UsageAsync(tx, propertyId, MoneyMasterIds.BankAccounts, id, ct).ConfigureAwait(false)).Count, true);

    private async Task<ExpenseCategoryView> ExpenseCategoryViewAsync(ICommandTransaction tx, int propertyId, int id, CancellationToken ct) =>
        ExpenseCategoryView(await RowAsync(tx, propertyId, MoneyMasterIds.ExpenseCategories, id, ct).ConfigureAwait(false), (await UsageAsync(tx, propertyId, MoneyMasterIds.ExpenseCategories, id, ct).ConfigureAwait(false)).Count);

    private async Task<ExpenseVendorView> ExpenseVendorViewAsync(ICommandTransaction tx, int propertyId, int id, CancellationToken ct) =>
        ExpenseVendorView(await RowAsync(tx, propertyId, MoneyMasterIds.ExpenseVendors, id, ct).ConfigureAwait(false), (await UsageAsync(tx, propertyId, MoneyMasterIds.ExpenseVendors, id, ct).ConfigureAwait(false)).Count, true);

    private static string Retired(MoneyRow r) => r.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)!;

    private static string Utc(MoneyRow r) => r.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    internal static PaymentMethodView PaymentMethodView(MoneyRow r, int usage, bool money = true) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn is null ? null : Retired(r),
        r.Text(MoneyMastersRules.Kind)!, r.Text(MoneyMastersRules.SettlesTo), r.Flag(MoneyMastersRules.NeedsReference), r.List(MoneyMastersRules.UsableAt),
        money ? (r[MoneyMastersRules.SurchargePct] is int bps ? bps : 0) : null, r.Text(MoneyMastersRules.Ledger), usage, Utc(r), VersionToken.ToWire(r.RowVer));

    internal static BankAccountView BankAccountView(MoneyRow r, int usage, bool money) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn is null ? null : Retired(r),
        r.Text(MoneyMastersRules.Kind)!, r.Text(MoneyMastersRules.Bank), r.Text(MoneyMastersRules.AccountLast4), r.Text(MoneyMastersRules.Ifsc),
        money ? (r[MoneyMastersRules.OpeningBalance] is long p ? p : 0) : null, r.List(MoneyMastersRules.DefaultFor), usage, Utc(r), VersionToken.ToWire(r.RowVer));

    internal static ExpenseCategoryView ExpenseCategoryView(MoneyRow r, int usage, bool money = true) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn is null ? null : Retired(r),
        r.Text(MoneyMastersRules.Ledger)!, r.Flag(MoneyMastersRules.NeedsBill), money ? r[MoneyMastersRules.ApprovalLimit] as long? : null, usage, Utc(r), VersionToken.ToWire(r.RowVer));

    internal static ExpenseVendorView ExpenseVendorView(MoneyRow r, int usage, bool money) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn is null ? null : Retired(r),
        r.Text(MoneyMastersRules.Type)!, r.Text(MoneyMastersRules.Gstin), money ? r.Text(MoneyMastersRules.Pan) : null, r.Flag(MoneyMastersRules.Tds),
        r.Text(MoneyMastersRules.Contact), r.Text(MoneyMastersRules.Terms), usage, Utc(r), VersionToken.ToWire(r.RowVer));
}
