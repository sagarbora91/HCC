// F12-RPT-REVENUE · rpt.revenue.demand-calendar — Demand calendar (catalogue §3.10, switch meas.rev.demand-calendar). Server twin of meas-revenue/demand-calendar.ts.
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class DemandCalendarReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.demand-calendar";

    public string MeasKey => "meas.rev.demand-calendar";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("date", "Date"), T("weekday", "Weekday"), I("onBooks", "On the books"), P("forecast", "Forecast"), P("lastYear", "Same night last year"), T("event", "Event"),
        M("bar", "BAR"), M("suggested", "Suggested rate"), B("offerOpen", "Offer open"),
    ];

    private const string Basis = "Business date forward: forecast = the real forecast curve (last two years by weekday, pickup, events) when switched on, else rooms on the books. BAR = live tariff of the largest room type; red when forecast is 85 % or more with the BAR not above its base, or 25 % or less with no discount offer open.";

    public ReportDefinition Definition { get; } = new(Id, "Demand calendar", "revenue", "P1", "MEAS", true, MoneyGate, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("from", "From", "date", ReportDateClass.Forward), new("nights", "Nights (max 92)", "text", Default: "90")],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, []);

    public static Computed Row(DateOnly date, int onBooks, int forecastPct, int? lastYearPct, string ev, long bar, long suggested, long baseRate, bool offerOpen) => new(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["date"] = date, ["weekday"] = MeasRevenueLogic.Weekdays[MeasRevenueLogic.Dow(date)], ["onBooks"] = (long)onBooks, ["forecast"] = (double)forecastPct,
        ["lastYear"] = lastYearPct is { } l ? (double)l : null, ["event"] = ev, ["bar"] = bar, ["suggested"] = suggested, ["offerOpen"] = offerOpen,
    }, MeasRevenueLogic.DemandRed(forecastPct, bar <= baseRate, offerOpen));

    public async Task<IReadOnlyList<Computed>> ComputeAsync(int propertyId, DateOnly asOf, DateOnly from, int nights, CancellationToken ct)
    {
        var now = NowUtc(Clk);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var types = await MeasRevenueData.TypesAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var rooms = await MeasRevenueData.RoomsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var stays = await MeasRevenueData.StaysAsync(Platform, propertyId, cl, now, ct).ConfigureAwait(false);
        var events = await MeasRevenueData.EventsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var engine = await MeasRevenueData.EngineAsync(yield, propertyId, from, nights, now, ct).ConfigureAwait(false);
        var real = await MeasRevenueData.RealForecastOnAsync(Gate, propertyId, ct).ConfigureAwait(false);
        var fc = stays.Select(MeasRevenueData.ToFc).ToList();
        var fcEvents = events.Select(e => new MeasFcEvent(e.From, e.To, e.ImpactPct)).ToList();
        var counts = MeasForecast.NightCounts(fc);
        var histFrom = fc.Count == 0 ? (DateOnly?)null : fc.Min(x => x.From);
        var type = MeasRevenueData.RefType(types);
        var offers = engine.Config.Plans.Where(p => p.Code != "BAR" && p.AdjBps < 0).ToList();
        var list = new List<Computed>();
        for (var i = 0; i < nights; i++)
        {
            var date = from.AddDays(i);
            var occ = engine.Occ(date);
            var pct = occ.Pct;
            if (real && occ.Sellable > 0)
            {
                var f = MeasForecast.ForecastNight(fc, occ.Sellable, asOf, date, fcEvents, null, counts);
                pct = Math.Min(100, MeasRevenueLogic.Pct(Math.Max(occ.OnBooks, f.Rooms), occ.Sellable));
            }
            var ly = date.AddDays(-364);
            int? lyPct = histFrom is { } h && ly >= h && ly < asOf ? MeasRevenueLogic.Pct(counts.GetValueOrDefault(ly), MeasRevenueData.Sellable(rooms, ly) is var sl && sl > 0 ? sl : rooms.Count) : null;
            var night = type is null ? null : engine.Night(type.Id, date);
            var ev = string.Join(" · ", events.Where(e => e.From <= date && e.To >= date).Select(e => e.Name));
            list.Add(Row(date, occ.OnBooks, pct, lyPct, ev, night?.Live ?? 0, night?.Suggested ?? 0, night?.Base ?? 0, offers.Any(p => engine.PlanAvailable(p, date, pct).Ok)));
        }
        return list;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var n = IntParam(parameters, "nights", "Nights", 90, 1, 92);
        var from = parameters.From ?? cl.BusinessDate;
        var rows = await ComputeAsync(caller.PropertyId, cl.BusinessDate, from, n, ct).ConfigureAwait(false);
        var anyEvents = (await MeasRevenueData.EventsAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false)).Count > 0;
        return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, rows.Select(MeasRevenueCommon.Row).ToList(), anyEvents ? null : CaptureNote(Basis, "events master (yld.event)"), from, from.AddDays(n - 1));
    }
}
