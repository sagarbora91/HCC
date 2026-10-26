using Hcc.Application.Access;
using Hcc.Application.Masters;
using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.MoneyMasters;

namespace Hcc.Application.MoneyMasters;

/// <summary>
/// MoneyMasters reads: GET /masters/payment-methods | bank-accounts | expense-categories | expense-vendors (any signed-in user — the pick
/// lists of the folio, POS and expense forms; the opening balance and the PAN are null without PERM-MST-002, DEC-061) and the change history
/// GET /masters/&lt;list&gt;/history (PERM-MST-002, the Owner). An empty property is seeded with the default rows on the first read.
/// </summary>
public sealed class MoneyMastersQueries(IAccessPolicy policy, IMoneyMastersReadStore reads, IMastersReadStore history)
{
    private static List<MoneyRow> Filter(IReadOnlyList<MoneyRow> rows, string? status)
    {
        var st = status ?? "all";
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
        return rows.Where(r => st == "all" || r.Active == (st == "active")).ToList();
    }

    private async Task<IReadOnlyDictionary<int, int>> UsageAsync(int propertyId, string master, IReadOnlyList<MoneyRow> rows, CancellationToken ct)
    {
        if (master == MoneyMasterIds.BankAccounts)
        {
            var methods = await reads.RowsAsync(propertyId, MoneyMasterIds.PaymentMethods, ct).ConfigureAwait(false);
            return rows.ToDictionary(r => r.Id, r => methods.Count(m => m.Text(MoneyMastersRules.SettlesTo) == r.Code));
        }

        var renames = await reads.RenamesAsync(propertyId, master, ct).ConfigureAwait(false);
        return MoneyMastersRules.CountsByRow(rows, renames, await reads.ValueCountsAsync(propertyId, master, ct).ConfigureAwait(false));
    }

    private async Task<(List<MoneyRow> Page, int Total, IReadOnlyDictionary<int, int> Usage, MastersRules.PageArgs Args)> PageAsync(int propertyId, string master, int? page, int? pageSize, string? status, CancellationToken ct)
    {
        var p = MastersRules.Page(page, pageSize);
        var all = await reads.RowsAsync(propertyId, master, ct).ConfigureAwait(false);
        var usage = await UsageAsync(propertyId, master, all, ct).ConfigureAwait(false);
        var list = Filter(all, status);
        return (list.Skip(p.Skip).Take(p.PageSize).ToList(), list.Count, usage, p);
    }

    private Task<bool> MoneyAsync(int propertyId, int principalId, CancellationToken ct) => policy.HasAsync(propertyId, principalId, MastersPermissions.ManageMoney, null, ct);

    public async Task<PaymentMethodList> PaymentMethodsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        // MA-11: the surcharge % is money (DEC-061): null for Supervisor / Housekeeping / Maintenance / Tech admin / Restaurant.
        var money = await Masters.MastersMoney.SeesMoneyAsync(policy, propertyId, principalId, ct).ConfigureAwait(false);
        var (rows, total, usage, p) = await PageAsync(propertyId, MoneyMasterIds.PaymentMethods, page, pageSize, status, ct).ConfigureAwait(false);
        return new PaymentMethodList(p.Page, p.PageSize, total, rows.Select(r => MoneyMastersCommands.PaymentMethodView(r, usage.GetValueOrDefault(r.Id), money)).ToList());
    }

    public async Task<BankAccountList> BankAccountsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var (rows, total, usage, p) = await PageAsync(propertyId, MoneyMasterIds.BankAccounts, page, pageSize, status, ct).ConfigureAwait(false);
        return new BankAccountList(p.Page, p.PageSize, total, rows.Select(r => MoneyMastersCommands.BankAccountView(r, usage.GetValueOrDefault(r.Id), money)).ToList());
    }

    public async Task<ExpenseCategoryList> ExpenseCategoriesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var money = await Masters.MastersMoney.SeesMoneyAsync(policy, propertyId, principalId, ct).ConfigureAwait(false); // MA-11: approval limit is money
        var (rows, total, usage, p) = await PageAsync(propertyId, MoneyMasterIds.ExpenseCategories, page, pageSize, status, ct).ConfigureAwait(false);
        return new ExpenseCategoryList(p.Page, p.PageSize, total, rows.Select(r => MoneyMastersCommands.ExpenseCategoryView(r, usage.GetValueOrDefault(r.Id), money)).ToList());
    }

    public async Task<ExpenseVendorList> ExpenseVendorsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var (rows, total, usage, p) = await PageAsync(propertyId, MoneyMasterIds.ExpenseVendors, page, pageSize, status, ct).ConfigureAwait(false);
        return new ExpenseVendorList(p.Page, p.PageSize, total, rows.Select(r => MoneyMastersCommands.ExpenseVendorView(r, usage.GetValueOrDefault(r.Id), money)).ToList());
    }

    /// <summary>Change history of one money master (optionally one row), newest first — PERM-MST-002.</summary>
    public async Task<MasterHistoryList> HistoryAsync(int propertyId, int principalId, string master, long? rowId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        if (!MoneyMasterIds.All.Contains(master)) throw new HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "Unknown list.");
        await policy.RequireAsync(propertyId, principalId, MastersPermissions.ManageMoney, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await history.HistoryAsync(propertyId, master, rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}
