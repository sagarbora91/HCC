using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Reporting;

namespace Hcc.Application.Reporting;

/// <summary>Room types of the rooms and the stay segments (which room a folio's stay occupied on which night); the one read the daily pack does not have.</summary>
public interface IRevenueCategorySource
{
    Task<RevenueCategoryData> LoadAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// F4-T2: GET /reports/revenue-by-category-tender (catalogue id rpt.money.revenue-by-category-tender). Owner, Manager and Accountant: it needs both PERM-RPT-001
/// (property-wide report) and PERM-RPT-002 (finance) like the daily pack. Same source records as the daily pack, grouped by the room type of the stay on the night.
/// Up to 366 business dates (FY / month / day presets).
/// </summary>
public sealed class RevenueByCategoryQueries(IReportSource reports, IRevenueCategorySource categories, IAccessPolicy policy)
{
    public const int MaxRangeDays = 366;

    public async Task<RevenueByCategoryReport> GetAsync(Caller caller, DateOnly from, DateOnly to, string? basis, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportOperations, ct: ct).ConfigureAwait(false);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) throw HccException.Validation($"Choose at most {MaxRangeDays} business dates.");
        var b = string.IsNullOrEmpty(basis) ? RevenueByCategoryCalculator.BusinessDate : basis;
        if (b != RevenueByCategoryCalculator.BusinessDate && b != RevenueByCategoryCalculator.Stay) throw HccException.Validation("basis must be business-date or stay.");
        var data = await reports.LoadAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var cat = await categories.LoadAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return RevenueByCategoryCalculator.Compute(data, cat, from, to, b);
    }
}
