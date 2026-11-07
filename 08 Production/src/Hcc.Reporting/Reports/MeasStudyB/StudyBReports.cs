// F12 MEAS · meas-study-b · the twelve ported study reports on the server (catalogue 3.10). One selector class driven by a spec table that mirrors
// 03 Prototype/src/domain/reports/study-b/index.ts (ids, titles, switches, parameters, columns = first section). Data comes through IAnalyticsInputSource.
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Reporting.Reports.MeasStudyB;

/// <summary>Builds the analytics input (live stays / bills / receipts + imported lgc history + rooms master) for a caller. The SQL implementation is owed by the integrator (DONE: Integration needed).</summary>
public interface IAnalyticsInputSource
{
    Task<(AnInput Input, BeyondExtras Extras)> LoadAsync(Caller caller, BoundParams parameters, CancellationToken ct = default);
}

/// <summary>Default until the SQL source is wired: an input with no rooms and no stays (every report renders its empty tables).</summary>
public sealed class EmptyAnalyticsInputSource(TimeProvider clock) : IAnalyticsInputSource
{
    public Task<(AnInput Input, BeyondExtras Extras)> LoadAsync(Caller caller, BoundParams parameters, CancellationToken ct = default) =>
        Task.FromResult((new AnInput { AsOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(330)).ToString("yyyy-MM-dd") }, new BeyondExtras()));
}

public sealed record StudyBSpec(string Id, string Title, string MeasKey, Func<AnInput, AnParams, AnResult> Select, bool Money, string Priority = "P2", ReportDateClass DateClass = ReportDateClass.FinancialYear,
    IReadOnlyList<ReportParam>? Params = null, ReportPhone Phone = ReportPhone.No);

public sealed class StudyBReport : IReportSelector, IMeasuredReport
{
    public const string Basis = "Ported from the management study workbooks over HCC data plus the imported legacy history. FY April-March; occupancy = distinct room-nights slept / (rooms in service x days); ADR = room charge before GST / charged nights (bill month); COVID window 1 Mar 2020 - 31 Mar 2021 excluded from normal years; room 211 = Suite. Legacy to 30 Oct 2026 / HCC live.";
    private static readonly ReportParam Fy = new("fy", "Financial year", "fy", ReportDateClass.FinancialYear);
    private static ReportParam Num(string key, string label, string? def = null) => new(key, label, "text", null, null, def);
    private static readonly AnInput Empty = new() { AsOf = "2026-01-01" };

    public static readonly IReadOnlyList<StudyBSpec> Specs =
    [
        new("rpt.study.revenue-kpi", "Occupancy, ADR, RevPAR by month, FY, type, channel", "rm-kpi", BeyondRevenue.RevenueKpi, true),
        new("rpt.study.channel-economics", "Channel economics and net ADR", "rm-channel", BeyondRevenue.ChannelEconomics, true, Params: [Fy, Num("commissionPct", "Commission % of OTA room revenue", "15")]),
        new("rpt.study.pricing-shape", "Day-of-week, length-of-stay, segment mix, rate ladder, upgrades", "rm-pricing", BeyondRevenue.PricingShape, true),
        new("rpt.study.guest-repeat-value", "Repeat guests, frequency, lifetime value, repeat customer list", "guest-repeat", BeyondGuest.GuestRepeatValue, true),
        new("rpt.study.guest-origin-plan", "Guest origin, nationality, meal plan, company guests", "guest-origin", BeyondGuest.GuestOriginPlan, false),
        new("rpt.study.housekeeping-operations", "Turnaround, idle streaks, check-in/out vs standard, daily load", "hk-ops", BeyondOps.HousekeepingOperations, false),
        new("rpt.study.fnb-halls-ancillary", "F&B on bills, breakfast covers, hall utilisation, ancillary", "fnb-halls", BeyondOps.FnbHallsAncillary, true),
        new("rpt.study.finance-cost", "USALI P&L, break-even, paid-outs, cash & bank, GST by slab", "finance", BeyondFinance.FinanceCost, true,
            Params: [Fy, Num("fixedPerMonth", "Fixed cost per month (paise)", "0"), Num("variablePerRn", "Variable cost per room-night (paise)", "0")]),
        new("rpt.study.corporate-accounts", "Corporate accounts, win-back list, account base", "sales", BeyondFinance.CorporateAccounts, true),
        new("rpt.study.compliance-risk", "Capture rates, audit-trail counts, risk register, licence calendar", "compliance", BeyondOps.ComplianceRisk, false),
        new("rpt.study.forecast-scenarios", "Seasonality indices, forecast (three methods), scenarios, budget vs actual, comp-set index", "forecast", BeyondFinance.ForecastScenarios, true,
            Params: [Fy, Num("budgetGrowthPct", "Budget growth % over last year", "10"), Num("adrDeltaPct", "Scenario ADR change %", "5"), Num("compOccPct", "Comp-set occupancy %"), Num("compAdr", "Comp-set ADR (paise)")]),
        new("rpt.study.owner-dashboard", "Owner 12-KPI monthly dashboard", "dashboard", BeyondFinance.OwnerDashboard, true, "P1", ReportDateClass.Month, [new ReportParam("month", "Month", "month", ReportDateClass.Month)], ReportPhone.Yes),
    ];

    private readonly StudyBSpec _spec;
    private readonly IAnalyticsInputSource? _source;

    public StudyBReport(StudyBSpec spec, IAnalyticsInputSource? source)
    {
        _spec = spec; _source = source;
        Definition = new ReportDefinition(spec.Id, spec.Title, "study", spec.Priority, "MEAS", spec.Money, spec.Money ? "PERM-RPT-002" : null, ["PERM-RPT-001", "PERM-RPT-002"], [], "PERM-RPT-003", spec.DateClass,
            spec.Params ?? [Fy], spec.Select(Empty, new AnParams()).Sections[0].Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], spec.Phone);
    }

    public ReportDefinition Definition { get; }

    /// <summary>The measurement switch (meas.study.&lt;key&gt;) the report runner consults through IMeasuredReport.</summary>
    public string MeasKey => "meas.study." + _spec.MeasKey;

    public static IEnumerable<IReportSelector> All(IAnalyticsInputSource? source) => Specs.Select(s => (IReportSelector)new StudyBReport(s, source));

    public static IServiceCollection AddStudyB(IServiceCollection services)
    {
        services.TryAddSingleton<IAnalyticsInputSource, EmptyAnalyticsInputSource>();
        foreach (var spec in Specs) services.AddSingleton<IReportSelector>(sp => new StudyBReport(spec, sp.GetRequiredService<IAnalyticsInputSource>()));
        return services;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var (input, extras) = await _source!.LoadAsync(caller, parameters, ct).ConfigureAwait(false);
        var r = _spec.Select(input, new AnParams { Values = new Dictionary<string, string>(parameters.Values, StringComparer.Ordinal), Extras = extras });
        var tables = r.Sections.Select(s => new ReportTable(s.Key, s.Title, s.Columns, s.Rows)).ToList();
        var about = new ReportAbout(DateTimeOffset.MinValue, string.Empty, Definition.DateBasis, false, r.Legacy || parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Definition.Id, Definition.Title, parameters, tables[0].Columns, tables[0].Rows, about, Sections: tables);
    }
}
