// F12-RPT-REVENUE · rpt.revenue.displacement — Displacement calculator, report shell (catalogue §3.10, switch meas.rev.displacement). Server twin of
// meas-revenue/displacement.ts; the arithmetic is meas-forecast's MeasForecast.Displacement (read, not changed).
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class DisplacementReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.displacement";

    public string MeasKey => "meas.rev.displacement";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("night", "Night"), P("forecast", "Forecast occupancy"), I("displaced", "Rooms displaced"), M("displacedRevenue", "Transient revenue displaced"), M("offerRevenue", "Offer revenue"),
        M("net", "Net gain or loss"), T("recommendation", "Recommendation (accept / counter-rate / decline)"),
    ];

    private const string Basis = "Business date forward (up to 92 nights, From to To inclusive). Displaced rooms = group rooms beyond the rooms still free on the forecast; displaced revenue = displaced rooms x the night suggested BAR (largest room type, or the chosen type); offer revenue = rooms x offered rate. Red when the net is a loss (the offered rate is below the counter-rate). Counter-rate = break-even rate rounded up to Rs 50; decline when it is more than 150 % of the offer.";

    public ReportDefinition Definition { get; } = new(Id, "Displacement calculator", "revenue", "P2", "MEAS", true, MoneyGate, ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("enquiry", "Enquiry", "enum", null, ["group", "long-stay", "hall"], "group"), new("from", "From", "date", ReportDateClass.Forward), new("to", "To", "date", ReportDateClass.Forward),
         new("rooms", "Rooms", "text", Default: "5"), new("rate", "Offered rate (Rs a night)", "text", Default: "1500"), new("roomType", "Room type", "text")],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Pdf], ReportPhone.Yes, false, []);

    public async Task<(IReadOnlyList<Computed> Rows, Computed Total)> ComputeAsync(int propertyId, DateOnly asOf, DateOnly from, int n, int rooms, long ratePaise, string typeName, CancellationToken ct)
    {
        var now = NowUtc(Clk);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var types = await MeasRevenueData.TypesAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var allRooms = await MeasRevenueData.RoomsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var stays = await MeasRevenueData.StaysAsync(Platform, propertyId, cl, now, ct).ConfigureAwait(false);
        var events = await MeasRevenueData.EventsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var engine = await MeasRevenueData.EngineAsync(Yield, propertyId, from, n, now, ct).ConfigureAwait(false);
        var real = await MeasRevenueData.RealForecastOnAsync(Gate, propertyId, ct).ConfigureAwait(false);
        var type = FindType(types, typeName);
        var adrType = type ?? MeasRevenueData.RefType(types);
        var fc = stays.Select(MeasRevenueData.ToFc).ToList();
        var fcEvents = events.Select(e => new MeasFcEvent(e.From, e.To, e.ImpactPct)).ToList();
        var counts = MeasForecast.NightCounts(fc);
        var nts = new List<MeasDispNight>();
        for (var i = 0; i < n; i++)
        {
            var date = from.AddDays(i);
            var occ = engine.Occ(date);
            var sold = Math.Max(occ.OnBooks, occ.Forecast);
            if (real && occ.Sellable > 0) sold = Math.Max(occ.OnBooks, MeasForecast.ForecastNight(fc, occ.Sellable, asOf, date, fcEvents, null, counts).Rooms);
            var sellable = type is null ? occ.Sellable : MeasRevenueData.Sellable(allRooms, date, type.Id);
            var forecastRooms = type is null ? sold : Math.Max(MeasRevenueData.RoomsOn(stays, date, type.Id), occ.Sellable > 0 ? MeasForecast.RoundHalfUp(sold * (double)sellable / occ.Sellable) : 0);
            nts.Add(new MeasDispNight(date, sellable, Math.Min(forecastRooms, sellable), adrType is null ? 0 : engine.Night(adrType.Id, date).Suggested));
        }
        var r = MeasForecast.Displacement(nts, rooms, ratePaise, 0);
        var rows = r.Rows.Select((x, i) =>
        {
            var net = x.GroupPaise - x.DisplacedPaise;
            var rec = MeasRevenueLogic.Recommendation(net, ratePaise, MeasRevenueLogic.CounterRate(x.DisplacedPaise, rooms));
            return new Computed(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["night"] = x.Date, ["forecast"] = (double)MeasRevenueLogic.Pct(nts[i].ForecastRooms, nts[i].Sellable), ["displaced"] = (long)x.Displaced, ["displacedRevenue"] = x.DisplacedPaise,
                ["offerRevenue"] = x.GroupPaise, ["net"] = net, ["recommendation"] = rec,
            }, MeasRevenueLogic.DisplacementRed(net));
        }).ToList();
        var totalRec = MeasRevenueLogic.Recommendation(r.NetPaise, ratePaise, r.BreakEvenRatePaise);
        var total = new Computed(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["night"] = $"Total · {n} night{(n == 1 ? "" : "s")}", ["displaced"] = (long)r.Rows.Sum(x => x.Displaced), ["displacedRevenue"] = r.DisplacedPaise, ["offerRevenue"] = r.GroupPaise, ["net"] = r.NetPaise,
            ["recommendation"] = totalRec == "counter-rate" ? $"counter-rate (at least {r.BreakEvenRatePaise / 100} a night)" : totalRec,
        }, MeasRevenueLogic.DisplacementRed(r.NetPaise));
        return (rows, total);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var from = parameters.From ?? cl.BusinessDate;
        var to = parameters.To ?? from.AddDays(6);
        var n = to.DayNumber - from.DayNumber + 1;
        if (n > 92) throw HccException.Validation("The range is longer than 92 days. Narrow the range.");
        var roomsN = IntParam(parameters, "rooms", "Rooms", 5, 1, 500);
        var rupees = IntParam(parameters, "rate", "Offered rate", 1500, 0, 1000000);
        var (rows, total) = await ComputeAsync(caller.PropertyId, cl.BusinessDate, from, n, roomsN, rupees * 100L, Text(parameters, "roomType"), ct).ConfigureAwait(false);
        var all = rows.Select(MeasRevenueCommon.Row).Append(new ReportRow(ReportRowKind.Total, total.Cells)).ToList();
        return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, all, null, from, to);
    }
}
