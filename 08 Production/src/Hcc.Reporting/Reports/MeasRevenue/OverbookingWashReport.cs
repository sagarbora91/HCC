// F12-RPT-REVENUE · rpt.revenue.overbooking-wash — Overbooking and wash (catalogue §3.10, switch meas.rev.overbooking). Server twin of meas-revenue/overbooking-wash.ts.
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class OverbookingWashReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.overbooking-wash";

    public string MeasKey => "meas.rev.overbooking";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("weekday", "Weekday"), T("channel", "Channel"), P("noShowPct", "No-show %"), P("cancelPct", "Cancellation %"), I("washRooms", "Expected wash rooms"),
        I("allowed", "Oversold allowed (yld.overbook)"), I("walked", "Walked guests"), M("walkCost", "Cost of walk"),
    ];

    private const string Basis = "Business date, range up to 366 days: bookings by arrival weekday and channel (held and expired excluded). No-show % and cancellation % = share of those bookings; expected wash rooms = (no-shows + cancellations) a week of that weekday, rounded; oversold allowed = the Owner setting per room type (0 until set); walked guests and their cost from the desk log. Red when rooms oversold on a night of that weekday are above the allowed number.";

    public ReportDefinition Definition { get; } = new(Id, "Overbooking and wash", "revenue", "P2", "MEAS", true, MoneyGate, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("from", "From", "date", ReportDateClass.Financial), new("to", "To", "date", ReportDateClass.Financial), new("roomType", "Room type", "text"), new("channel", "Channel", "text")],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, []);

    private sealed class Acc { public int Total; public int Ns; public int Cx; public int Walked; public long Cost; }

    public async Task<IReadOnlyList<Computed>> ComputeAsync(int propertyId, DateOnly from, DateOnly to, string typeName, string channel, CancellationToken ct)
    {
        var now = NowUtc(Clk);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var types = await MeasRevenueData.TypesAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var rooms = await MeasRevenueData.RoomsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var stays = await MeasRevenueData.StaysAsync(Platform, propertyId, cl, now, ct).ConfigureAwait(false);
        var walked = await MeasRevenueData.WalkedAsync(Platform, propertyId, from, to, ct).ConfigureAwait(false);
        var type = FindType(types, typeName);
        var chosen = type is null ? types : [type];
        var allowed = await AllowedAsync(Settings, propertyId, chosen.Select(t => t.Code), ct).ConfigureAwait(false);
        // Rooms sold above the rooms in service, per type and night (Confirmed / InHouse stays), worst figure per weekday.
        var sold = new Dictionary<(int Type, DateOnly Night), int>();
        foreach (var x in stays.Where(x => x.Status is "Confirmed" or "InHouse" && chosen.Any(t => t.Id == x.TypeId)))
            foreach (var d in MeasRevenueData.Nights(x).Where(d => d >= from && d <= to)) sold[(x.TypeId, d)] = sold.GetValueOrDefault((x.TypeId, d)) + 1;
        var worst = new int[7];
        var weeks = new int[7];
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            weeks[MeasRevenueLogic.Dow(d)]++;
            var over = chosen.Sum(t => Math.Max(0, sold.GetValueOrDefault((t.Id, d)) - MeasRevenueData.Sellable(rooms, d, t.Id)));
            if (over > worst[MeasRevenueLogic.Dow(d)]) worst[MeasRevenueLogic.Dow(d)] = over;
        }
        var acc = new Dictionary<(int W, string C), Acc>();
        Acc Get(int w, string c) { if (!acc.TryGetValue((w, c), out var a)) acc[(w, c)] = a = new Acc(); return a; }
        var byId = stays.ToDictionary(x => x.Id);
        foreach (var b in stays.Where(b => b.Status != "Held" && b.From >= from && b.From <= to && (type is null || b.TypeId == type.Id) && (channel.Length == 0 || b.Channel == channel)))
        {
            var a = Get(MeasRevenueLogic.Dow(b.From), b.Channel);
            a.Total++;
            if (b.Status == "NoShow") a.Ns++;
            if (b.Status == "Cancelled") a.Cx++;
        }
        foreach (var w in walked)
        {
            byId.TryGetValue(w.ReservationId, out var b);
            if (type is not null && (b is null || b.TypeId != type.Id)) continue;
            var c = b?.Channel ?? "Unknown";
            if (channel.Length > 0 && c != channel) continue;
            var a = Get(MeasRevenueLogic.Dow(w.Night), c);
            a.Walked++;
            a.Cost += w.CostPaise;
        }
        return acc.OrderBy(kv => kv.Key.W).ThenBy(kv => kv.Key.C, StringComparer.Ordinal).Select(kv =>
        {
            var (w, c) = kv.Key;
            var a = kv.Value;
            return new Computed(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["weekday"] = MeasRevenueLogic.Weekdays[w], ["channel"] = c, ["noShowPct"] = MeasRevenueLogic.Tenths(a.Ns, a.Total), ["cancelPct"] = MeasRevenueLogic.Tenths(a.Cx, a.Total),
                ["washRooms"] = (long)Math.Floor((a.Ns + a.Cx) / (double)Math.Max(1, weeks[w]) + 0.5), ["allowed"] = (long)allowed, ["walked"] = (long)a.Walked, ["walkCost"] = a.Cost,
            }, MeasRevenueLogic.OversoldRed(worst[w], allowed));
        }).ToList();
    }

    /// <summary>F12-MEAS2-C (M9): "Oversold allowed" = the sum of yld.overbook.&lt;code&gt; over the chosen room types (each 0 to 10, 0 until set).</summary>
    public static async Task<int> AllowedAsync(ISettingsReader? settings, int propertyId, IEnumerable<string> typeCodes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeCodes);
        var allowed = 0;
        foreach (var code in typeCodes) allowed += await MeasRevenueData.OverbookAllowedAsync(settings, propertyId, code, ct).ConfigureAwait(false);
        return allowed;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var to = parameters.To ?? cl.BusinessDate;
        var from = parameters.From ?? to.AddDays(-30);
        if (to.DayNumber - from.DayNumber + 1 > 366) throw HccException.Validation("The range is longer than 366 days. Narrow the range.");
        var rows = await ComputeAsync(caller.PropertyId, from, to, Text(parameters, "roomType"), Text(parameters, "channel"), ct).ConfigureAwait(false);
        return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, rows.Select(MeasRevenueCommon.Row).ToList(), rows.Count == 0 ? CaptureNote(Basis, "walked-guest log and yld.overbook") : null);
    }
}
