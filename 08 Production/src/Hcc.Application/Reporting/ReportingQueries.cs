using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Reporting;
using Hcc.Domain.Errors;
using Hcc.Domain.Reporting;

namespace Hcc.Application.Reporting;

/// <summary>
/// GetReport (contracts §3): the owner's daily pack for a business-date range, computed by the same <see cref="DailyPackCalculator"/>
/// over the same tables as the closing snapshot. For every closed date in the range the recalculation is compared with the
/// snapshot's figures (<see cref="ReportDayStatus.MatchesSnapshot"/>), so a later change to a closed date's records shows up.
/// </summary>
// ETP-REUSE: Excel/PDF export of the daily pack (PERM-RPT-003) and the owner e-mail from ETP's exporters (Hcc.Reporting) when V:\ETP is available.
public sealed class ReportingQueries(IReportSource reports, IClosingStore closing, IAccessPolicy policy)
{
    public const int MaxRangeDays = 31;

    public const string Note = "Figures from posted records: lines by service date, receipts by receipt date, cash by shift date, occupancy from stay intervals. GST illustrative — CA to confirm.";

    public async Task<DailyReportView> GetDailyReportAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) throw HccException.Validation($"Choose at most {MaxRangeDays} business dates.");
        var bd = await closing.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await reports.LoadAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var report = DailyPackCalculator.Compute(data, from, to);
        var closed = (await closing.ClosedDatesAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false)).ToHashSet();
        var status = new List<ReportDayStatus>();
        foreach (var d in DailyPackCalculator.Dates(from, to))
        {
            if (closed.Contains(d))
            {
                var snap = await closing.ReadSnapshotAsync(null, caller.PropertyId, d, ct).ConfigureAwait(false);
                bool? match = snap is null ? null
                    : CanonicalJson.Serialize(ClosingService.ParseRecord(snap.Json).Figures) == CanonicalJson.Serialize(DailyPackCalculator.Compute(data, d, d));
                status.Add(new ReportDayStatus(d, "Closed", match));
            }
            else status.Add(new ReportDayStatus(d, d == bd ? "Open" : d > bd ? "Future" : "Past", null));
        }

        return new DailyReportView(report, status, bd, Note);
    }
}
