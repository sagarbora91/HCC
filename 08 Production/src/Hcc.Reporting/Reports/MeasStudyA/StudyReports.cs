// F12 MEAS · meas-study-a · the nine legacy-study reports on the server (catalogue §3.10). Each is an adapter over a pure selector in Hcc.Reporting.Analytics
// (same ids, column keys, flags as 03 Prototype/src/domain/reports/study-a/*). The measurement switch is honoured through IMeasuredReport (MEAS_OFF).
using Hcc.Application.Configuration;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Analytics;

namespace Hcc.Reporting.Reports.MeasStudyA;

public abstract class StudyReport : IReportSelector, IMeasuredReport
{
    private readonly IAnalyticsInputSource _source;
    private readonly TimeProvider _clock;
    private readonly Func<AnInput, AnParams, ReportResult> _select;

    protected StudyReport(IAnalyticsInputSource source, TimeProvider clock, string id, string title, bool money, string measKey, ReportDateClass dateClass, IReadOnlyList<ReportParam> parameters,
        Func<AnInput, AnParams, ReportResult> select)
    {
        _source = source; _clock = clock; _select = select; MeasKey = measKey;
        var empty = new AnInput([], [], [], [], "2026-01-01", "2026-10-30", "2020-03-01", "2021-03-31");
        Definition = new ReportDefinition(id, title, "study", "P2", "MEAS", money, money ? "PERM-RPT-002" : null, money ? ["PERM-RPT-001"] : ["PERM-RPT-002", "PERM-RPT-001"], [], "PERM-RPT-003",
            dateClass, parameters, select(empty, new AnParams()).Columns,
            "Nights from the arrival date (nights x rooms, same-day stays count 1); revenue by bill month (billed = total incl. GST); receipts by receipt date. Legacy to 30 Oct 2026 / HCC live.",
            [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No);
    }

    public ReportDefinition Definition { get; }

    public string MeasKey { get; }

    protected static ReportParam Fy() => new("fy", "Financial year", "fy", ReportDateClass.FinancialYear);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var asOf = parameters.To ?? today;
        var input = await _source.LoadAsync(caller.PropertyId, asOf, ct).ConfigureAwait(false); // f12-int-an: the caller's property
        var v = parameters.Values;
        string? V(string k) => v.GetValueOrDefault(k);
        var p = new AnParams(V("fy"), parameters.From?.ToString("yyyy-MM-dd"), parameters.To?.ToString("yyyy-MM-dd"), V("baseFy"), V("topN"), V("asOf"));
        var r = _select(input, p);
        var about = r.About with { Legacy = true };
        return r with { About = about, Params = parameters };
    }
}

public sealed class RevenueMonthlyReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.revenue-monthly", "Monthly revenue and cash (FY history)", true, "meas.study.revenue-monthly",
    ReportDateClass.FinancialYear, [Fy()], LegacyRevenue.RevenueMonthly);

public sealed class YearComparisonReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.year-comparison", "Year comparison", true, "meas.study.year-comparison",
    ReportDateClass.Financial, [new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial)], LegacyRevenue.YearComparison);

public sealed class IndexVsBaseReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.index-vs-base", "Index vs a base year", true, "meas.study.covid-index",
    ReportDateClass.FinancialYear, [Fy(), new("baseFy", "Base year", "text", null, null, "2018-19")], LegacyRevenue.IndexVsBase);

public sealed class FyDeepDiveReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.fy-deep-dive", "FY deep dive (price-volume, segments, top bills)", true, "meas.study.deep-dive",
    ReportDateClass.FinancialYear, [Fy(), new("topN", "Top bills", "text", null, null, "10")], LegacyRevenue.FyDeepDive);

public sealed class OccupancyHistoryReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.occupancy-history", "Occupancy history (three denominators)", false, "meas.study.occupancy",
    ReportDateClass.FinancialYear, [Fy()], LegacyOccupancy.OccupancyHistory);

public sealed class OperationsPatternsReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.operations-patterns", "Operations patterns", false, "meas.study.patterns",
    ReportDateClass.FinancialYear, [Fy()], LegacyOccupancy.OperationsPatterns);

public sealed class RoomsAndRatesReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.rooms-and-rates", "Rooms, rates and discounts", true, "meas.study.rooms-rates",
    ReportDateClass.FinancialYear, [Fy()], LegacyOccupancy.RoomsAndRates);

public sealed class ZeroRateComplimentaryReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.zero-rate-complimentary", "Zero-rate and complimentary stays", true, "meas.study.zero-rate",
    ReportDateClass.FinancialYear, [Fy()], LegacyZero.ZeroRateComplimentary);

public sealed class ReceivablesBadDebtReport(IAnalyticsInputSource s, TimeProvider c) : StudyReport(s, c, "rpt.study.receivables-bad-debt", "Receivables and bad debt (legacy + live ageing)", true, "meas.study.receivables",
    ReportDateClass.AsOf, [new("asOf", "As of", "date", ReportDateClass.AsOf), new("topN", "Top N", "text", null, null, "100")], LegacyZero.ReceivablesBadDebt);
