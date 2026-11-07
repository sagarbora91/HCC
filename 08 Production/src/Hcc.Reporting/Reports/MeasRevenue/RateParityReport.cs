// F12-RPT-REVENUE · rpt.revenue.rate-parity — Rate shop and parity (catalogue §3.10, switch meas.rev.rate-shop). Server twin of meas-revenue/rate-parity.ts.
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Domain.Yield;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class RateParityReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.rate-parity";

    public string MeasKey => "meas.rev.rate-shop";

    private static ReportColumn Comp(int i) => M("comp" + i, $"Competitor {i} rate");

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("stayDate", "Stay date"), M("direct", "Direct rate"), M("ota", "OTA rate"), Comp(1), Comp(2), Comp(3), Comp(4), Comp(5), Comp(6), M("median", "Set median"),
        P("position", "Our position"), T("exception", "Parity exception"),
    ];

    private const string Basis = "Business date forward (up to 92 nights): direct = live BAR of the room type (largest type when none chosen); OTA = lowest published channel rate (BAR plus markup, connected channels with the type mapped and not stop-sold); competitor columns = the newest shop entry per night, first six active competitors by name; set median of the shopped rates (whole paise); position = our direct rate against the median in whole percent. Red when the OTA rate is below direct, or our rate is more than 15 % above the set median with forecast occupancy under 40 %.";

    public ReportDefinition Definition { get; } = new(Id, "Rate shop and parity", "revenue", "P2", "MEAS", true, MoneyGate, ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("from", "From", "date", ReportDateClass.Forward), new("nights", "Nights (max 92)", "text", Default: "30"), new("roomType", "Room type", "text")],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, []);

    public async Task<(IReadOnlyList<Computed> Rows, IReadOnlyList<RCompetitor> Competitors)> ComputeAsync(int propertyId, DateOnly from, int n, string typeName, CancellationToken ct)
    {
        var now = NowUtc(Clk);
        var types = await MeasRevenueData.TypesAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var type = FindType(types, typeName) ?? MeasRevenueData.RefType(types);
        var comps = await MeasRevenueData.CompetitorsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        if (type is null) return ([], comps);
        var shop = await MeasRevenueData.ShopsAsync(Platform, propertyId, from, from.AddDays(n - 1), type.Id, ct).ConfigureAwait(false);
        var chans = (await MeasRevenueData.ChannelsAsync(Platform, propertyId, ct).ConfigureAwait(false)).Where(c => c.SellsTypes.Contains(type.Id)).ToList();
        var engine = await MeasRevenueData.EngineAsync(Yield, propertyId, from, n, now, ct).ConfigureAwait(false);
        var list = new List<Computed>();
        for (var i = 0; i < n; i++)
        {
            var date = from.AddDays(i);
            var night = engine.Night(type.Id, date);
            var occ = engine.Occ(date);
            long? direct = night.Live > 0 ? night.Live : null;
            long? ota = chans.Count > 0 && direct is not null ? chans.Min(c => YieldEngine.Adjust(night.Live, c.MarkupBps)) : null;
            var rates = comps.Select(c => shop.Where(x => x.CompetitorId == c.Id && x.StayDate == date).OrderByDescending(x => x.ShopDate).ThenByDescending(x => x.ClientId, StringComparer.Ordinal).FirstOrDefault()?.RatePaise).ToList();
            var med = MeasRevenueLogic.Median(rates.Where(x => x is not null).Select(x => x!.Value).ToList());
            var red = MeasRevenueLogic.ParityRed(direct, ota, med, Math.Max(occ.OnBooks, occ.Forecast), occ.Sellable);
            var why = string.Join(" · ", new[]
            {
                ota is { } o && direct is { } d && o < d ? "OTA below direct" : "",
                med is { } m && direct is { } d2 && d2 * 100 > m * 115 ? "Above set by more than 15 %" : "",
            }.Where(x => x.Length > 0));
            var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["stayDate"] = date, ["direct"] = direct, ["ota"] = ota, ["median"] = med, ["position"] = MeasRevenueLogic.PositionPct(direct, med) is { } p ? (double)p : null, ["exception"] = why,
            };
            for (var k = 1; k <= 6; k++) cells["comp" + k] = k <= rates.Count ? rates[k - 1] : null;
            list.Add(new Computed(cells, red));
        }
        return (list, comps);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var n = IntParam(parameters, "nights", "Nights", 30, 1, 92);
        var from = parameters.From ?? cl.BusinessDate;
        var comps = await MeasRevenueData.CompetitorsAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var none = comps.Count == 0 && !await MeasRevenueData.AnyShopAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var columns = Columns.Select(c => c.Key.StartsWith("comp", StringComparison.Ordinal) && int.TryParse(c.Key.AsSpan(4), out var i) && i <= comps.Count ? c with { Label = comps[i - 1].Name + " rate" } : c).ToList();
        var rows = none ? [] : (await ComputeAsync(caller.PropertyId, from, n, Text(parameters, "roomType"), ct).ConfigureAwait(false)).Rows;
        return MeasRevenueCommon.Result(Id, Definition, parameters, columns, rows.Select(MeasRevenueCommon.Row).ToList(), none ? CaptureNote(Basis, "rate-shop entries (competitors)") : null, from, from.AddDays(n - 1));
    }
}
