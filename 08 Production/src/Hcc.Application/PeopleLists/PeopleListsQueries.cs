using Hcc.Application.Access;
using Hcc.Application.Masters;
using Hcc.Application.Payroll;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.PeopleLists;

namespace Hcc.Application.PeopleLists;

/// <summary>
/// People lists reads: GET /hr/designations, /hr/shift-patterns, /hr/leave-types, /hr/holidays (any signed-in user — staff screens, the roster, leave requests
/// and the calendar label records with them; the designation's default pay rule only for PERM-MST-002 / PERM-HR-005 holders) and the change history
/// GET /hr/&lt;list&gt;/history, /hr/&lt;list&gt;/{id}/history (PERM-MST-001). The first read seeds an empty property (shift patterns, the six leave types, designations from staff).
/// </summary>
public sealed class PeopleListsQueries(IAccessPolicy policy, IPeopleListsReadStore reads, IMastersReadStore historyReads)
{
    private static (string Status, MastersRules.PageArgs Page) Args(int? page, int? pageSize, string? status)
    {
        var p = MastersRules.Page(page, pageSize);
        var st = status ?? "all";
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
        return (st, p);
    }

    private async Task<PeopleListPage<TView>> ListAsync<TView>(PeopleList l, int propertyId, int? page, int? pageSize, string? status, Func<ListRec, int, TView> view, CancellationToken ct)
    {
        var (st, p) = Args(page, pageSize, status);
        await reads.EnsureSeededAsync(propertyId, ct).ConfigureAwait(false);
        var rows = (await reads.RowsAsync(l, propertyId, ct).ConfigureAwait(false)).Where(x => st == "all" || x.Rec.Active == (st == "active")).ToList();
        return new PeopleListPage<TView>(p.Page, p.PageSize, rows.Count, rows.Skip(p.Skip).Take(p.PageSize).Select(x => view(x.Rec, x.Used)).ToList());
    }

    public async Task<PeopleListPage<DesignationView>> DesignationsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var showPay = await policy.HasAsync(propertyId, principalId, MastersPermissions.ManageMoney, null, ct).ConfigureAwait(false)
            || await policy.HasAsync(propertyId, principalId, PayrollPermissions.Salary, null, ct).ConfigureAwait(false);
        return await ListAsync(PeopleList.Designations, propertyId, page, pageSize, status, (r, u) => PeopleListsCommands.ToDesignation(r, u, showPay), ct).ConfigureAwait(false);
    }

    public Task<PeopleListPage<ShiftPatternView>> ShiftPatternsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // session checked by the endpoint group; no money, no guest data
        return ListAsync(PeopleList.ShiftPatterns, propertyId, page, pageSize, status, PeopleListsCommands.ToShift, ct);
    }

    public Task<PeopleListPage<LeaveTypeView>> LeaveTypesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // quotas are already part of the leave policy every staff member sees (balances)
        return ListAsync(PeopleList.LeaveTypes, propertyId, page, pageSize, status, PeopleListsCommands.ToLeave, ct);
    }

    public Task<PeopleListPage<HolidayView>> HolidaysAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // public holidays
        return ListAsync(PeopleList.Holidays, propertyId, page, pageSize, status, PeopleListsCommands.ToHoliday, ct);
    }

    public async Task<MasterHistoryList> HistoryAsync(PeopleList l, int propertyId, int principalId, int? rowId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MastersPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await historyReads.HistoryAsync(propertyId, PeopleListsRules.Id(l), rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}
