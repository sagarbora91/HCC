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

    /// <summary>Guest-folio due and advances held at the end of <paramref name="asOf"/> (opening = the day before <c>from</c>).</summary>
    Task<FolioBalances> BalancesAsync(int propertyId, DateOnly asOf, CancellationToken ct);

    /// <summary>Legacy bills and receipts dated in [from, to] (lgc.bill / lgc.receipt, LG contract §6) with the room-master type of each legacy room name; null when the range holds none.</summary>
    Task<RevenueLegacyData?> LegacyAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// F4-T2: GET /reports/revenue-by-category-tender (catalogue id rpt.money.revenue-by-category-tender). Owner, Manager and Accountant: it needs both PERM-RPT-001
/// (property-wide report) and PERM-RPT-002 (finance) like the daily pack. Same source records as the daily pack, grouped by the room type of the stay on the night.
/// Up to 366 business dates (FY / month / day presets).
/// </summary>
public sealed class RevenueByCategoryQueries(IReportSource reports, IRevenueCategorySource categories, IAccessPolicy policy, Hcc.Application.Accounting.AccountingReportQueries? accounting = null)
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
        // Legacy financial years (LG contract §6): when the range holds old Lodge bills, the same blocks are produced from lgc.bill / lgc.receipt under the legacy header.
        var legacy = await categories.LegacyAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        if (legacy is not null) return RevenueByCategoryCalculator.ComputeLegacy(legacy, from, to);
        var opening = await categories.BalancesAsync(caller.PropertyId, from.AddDays(-1), ct).ConfigureAwait(false);
        var closing = await categories.BalancesAsync(caller.PropertyId, to, ct).ConfigureAwait(false);
        var report = RevenueByCategoryCalculator.Compute(data, cat, from, to, b, opening, closing);
        if (accounting is null) return report;
        // Block C closes to the trial balance (ACC-1 six-column TB over the same range). A caller without the accounting view still gets the report, without the ledger rows.
        try
        {
            var tb = await accounting.TrialBalanceAsync(caller, from, to, false, ct).ConfigureAwait(false);
            LedgerFigures Led(string code)
            {
                var r = tb.Rows.FirstOrDefault(x => x.Account == code);
                return r is null ? new LedgerFigures(0, 0, 0) : new LedgerFigures(r.OpeningDebitPaise - r.OpeningCreditPaise, r.ClosingDebitPaise - r.ClosingCreditPaise, r.DebitPaise);
            }
            return report with { Ledgers = RevenueByCategoryCalculator.LedgerRows(report, Led) };
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.FORBIDDEN) { return report; }
    }
}
