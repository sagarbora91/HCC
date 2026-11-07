// F12-RPT-REVENUE · rpt.revenue.pickup-pace — Pickup and pace (catalogue §3.10, switch meas.rev.pickup). Server twin of meas-revenue/pickup-pace.ts:
// derived on-the-books figures from reservation created / cancelled dates; a stored OTB snapshot replaces the derived last-year figure and the 7-day baseline.
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class PickupPaceReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.pickup-pace";

    public string MeasKey => "meas.rev.pickup";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("month", "Month"), I("rooms", "Rooms on books"), I("lastYearRooms", "Last year same snapshot"), M("revenue", "Revenue on books"), M("lastYearRevenue", "Last year revenue"),
        P("pace", "Pace %"), I("pickup7", "Pickup last 7 days"), T("lead", "Lead-time bucket"),
    ];

    private const string Basis = "Business date now: room-nights on the books for each coming month as known today, against the same month last year as known one year (364 days) ago; revenue = nightly rate of each room-night. Pace % = (now - last year) / last year; red when worse than -10 %. Lead-time bucket = the most common gap between booking and arrival.";

    public ReportDefinition Definition { get; } = new(Id, "Pickup and pace", "revenue", "P2", "MEAS", true, MoneyGate, ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Now,
        [new("months", "Months ahead (1-12)", "text", Default: "6"), new("channel", "Channel", "text")],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, []);

    internal sealed record Night(DateOnly Date, DateOnly Booked, DateOnly? Cancelled, long Rate, int Lead);

    internal static List<Night> Nights(IReadOnlyList<RStay> stays, string channel)
    {
        var list = new List<Night>();
        foreach (var x in stays)
        {
            if (channel.Length > 0 && x.Channel != channel) continue;
            var lead = Math.Max(0, x.From.DayNumber - x.BookedOn.DayNumber);
            foreach (var d in MeasRevenueData.Nights(x)) list.Add(new Night(d, x.BookedOn, x.CancelledOn, x.Rate, lead));
        }
        return list;
    }

    private static string MonthOf(DateOnly d) => d.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static bool Known(Night x, DateOnly asOf) => x.Booked <= asOf && !(x.Cancelled is { } c && c <= asOf);

    internal static (int Rooms, long Revenue) At(List<Night> list, string month, DateOnly asOf)
    {
        var n = list.Where(x => MonthOf(x.Date) == month && Known(x, asOf)).ToList();
        return (n.Count, n.Sum(x => x.Rate));
    }

    internal static (int Rooms, long Revenue)? Snapshot(IReadOnlyList<ROtb> otb, DateOnly target, string month)
    {
        var days = otb.Select(r => r.TakenOn).Distinct().Where(d => d <= target && target.DayNumber - d.DayNumber <= 7).Order().ToList();
        if (days.Count == 0) return null;
        var day = days[^1];
        var rows = otb.Where(r => r.TakenOn == day && MonthOf(r.Night) == month).ToList();
        return (rows.Sum(r => r.Rooms), rows.Sum(r => r.RevenuePaise));
    }

    public static string MonthPlus(DateOnly d, int k)
    {
        var m = d.Month - 1 + k;
        return $"{d.Year + (m >= 0 ? m / 12 : (m - 11) / 12):0000}-{((m % 12) + 12) % 12 + 1:00}";
    }

    public static Computed Row(string month, int rooms, int lyRooms, long revenue, long lyRevenue, int pickup7, string lead)
    {
        var t = MeasRevenueLogic.PaceTenths(rooms, lyRooms);
        return new Computed(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["month"] = month, ["rooms"] = (long)rooms, ["lastYearRooms"] = (long)lyRooms, ["revenue"] = revenue, ["lastYearRevenue"] = lyRevenue,
            ["pace"] = t is { } v ? v / 10.0 : null, ["pickup7"] = (long)pickup7, ["lead"] = lead,
        }, MeasRevenueLogic.PaceRed(t));
    }

    public async Task<IReadOnlyList<Computed>> ComputeAsync(int propertyId, DateOnly today, int months, string channel, CancellationToken ct)
    {
        var cl = await MeasRevenueCommon.ClockAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var stays = await MeasRevenueData.StaysAsync(Platform, propertyId, cl, NowUtc(Clk), ct).ConfigureAwait(false);
        var otb = await MeasRevenueData.OtbAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var list = Nights(stays, channel);
        var ly = today.AddDays(-364);
        var wk = today.AddDays(-7);
        var rows = new List<Computed>();
        for (var k = 0; k < months; k++)
        {
            var month = MonthPlus(today, k);
            var cur = At(list, month, today);
            var lyM = $"{int.Parse(month[..4]) - 1:0000}{month[4..]}";
            var lyV = Snapshot(otb, ly, lyM) ?? At(list, lyM, ly);
            var before = Snapshot(otb, wk, month) ?? At(list, month, wk);
            var count = new Dictionary<string, int>();
            foreach (var x in list.Where(x => MonthOf(x.Date) == month && Known(x, today))) count[MeasRevenueLogic.LeadBucket(x.Lead)] = count.GetValueOrDefault(MeasRevenueLogic.LeadBucket(x.Lead)) + 1;
            var lead = MeasRevenueLogic.LeadBuckets.Aggregate(MeasRevenueLogic.LeadBuckets[0], (best, b) => count.GetValueOrDefault(b) > count.GetValueOrDefault(best) ? b : best);
            rows.Add(Row(month, cur.Rooms, lyV.Rooms, cur.Revenue, lyV.Revenue, cur.Rooms - before.Rooms, cur.Rooms > 0 ? lead : ""));
        }
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var months = IntParam(parameters, "months", "Months ahead", 6, 1, 12);
        var channel = Text(parameters, "channel");
        var stays = await MeasRevenueData.StaysAsync(Platform, caller.PropertyId, cl, NowUtc(Clk), ct).ConfigureAwait(false);
        var otb = await MeasRevenueData.OtbAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        if (stays.Count == 0 && otb.Count == 0) return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, [], CaptureNote(Basis, "weekly on-the-books snapshot"));
        var rows = await ComputeAsync(caller.PropertyId, cl.BusinessDate, months, channel, ct).ConfigureAwait(false);
        return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, rows.Select(MeasRevenueCommon.Row).ToList());
    }
}
