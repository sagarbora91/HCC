// F12-RPT-REVENUE · rpt.revenue.restriction-effect — Length-of-stay and minimum-stay controls (catalogue §3.10, switch meas.rev.restrictions).
// Server twin of meas-revenue/restriction-effect.ts. Restrictions come from the yield configuration, counters from meas.restriction_counter.
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Yield;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.MeasRevenue.MeasRevenueCommon;

namespace Hcc.Reporting.Reports.MeasRevenue;

public sealed class RestrictionEffectReport(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings = null, IMeasurementGate? gate = null, TimeProvider? clock = null)
    : MeasRevenueBase(platform, yield, settings, gate, clock), IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.revenue.restriction-effect";

    public string MeasKey => "meas.rev.restrictions";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("night", "Night"), T("restriction", "Restriction (MIN_STAY / CTA)"), I("refused", "Bookings refused"), I("reshaped", "Bookings re-shaped"), M("revenue", "Revenue on the night"),
        M("unrestricted", "Same nights unrestricted"), T("mode", "Enforced or advisory"),
    ];

    private const string Basis = "Business date, range up to 92 days: one row per restricted night and restriction kind. Refused / re-shaped = the counters kept when a booking met the restriction; revenue = nightly rate of the bookings that night; same nights unrestricted = the average revenue of the same weekday on nights of the range with no restriction (whole paise, rounded down). Red when the night stayed under 40 % occupancy.";

    public ReportDefinition Definition { get; } = new(Id, "Length-of-stay and minimum-stay controls", "revenue", "P2", "MEAS", true, MoneyGate, ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("kind", "Restriction kind", "enum", null, ["MIN_STAY", "CTA"])],
        Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, []);

    public async Task<IReadOnlyList<Computed>> ComputeAsync(int propertyId, DateOnly from, DateOnly to, string kind, CancellationToken ct)
    {
        var cl = await MeasRevenueCommon.ClockAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var rooms = await MeasRevenueData.RoomsAsync(Platform, propertyId, ct).ConfigureAwait(false);
        var stays = await MeasRevenueData.StaysAsync(Platform, propertyId, cl, NowUtc(Clk), ct).ConfigureAwait(false);
        var counters = await MeasRevenueData.CountersAsync(Platform, propertyId, from, to, ct).ConfigureAwait(false);
        var cfg = await Yield.LoadConfigAsync(propertyId, null, ct).ConfigureAwait(false);
        var mode = await MeasRevenueData.RestrictionsEnforcedAsync(Settings, propertyId, ct).ConfigureAwait(false) ? "Enforced" : "Advisory";
        var kindsOn = (DateOnly d) =>
        {
            var k = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in cfg.Restrictions.Where(r => r.From <= d && r.To >= d))
            {
                if (r.MinStay > 1) k.Add("MIN_STAY");
                if (r.ClosedToArrival) k.Add("CTA");
            }
            return k;
        };
        var dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        var rev = dates.ToDictionary(d => d, d => stays.Where(x => x.Live && x.From <= d && x.To > d).GroupBy(x => x.RoomId).Sum(g => g.First().Rate));
        var free = new Dictionary<int, List<long>>();
        foreach (var d in dates.Where(d => kindsOn(d).Count == 0)) { var w = MeasRevenueLogic.Dow(d); if (!free.TryGetValue(w, out var l)) free[w] = l = []; l.Add(rev[d]); }
        var list = new List<Computed>();
        foreach (var d in dates)
            foreach (var k in kindsOn(d))
            {
                if (kind.Length > 0 && k != kind) continue;
                var c = counters.FirstOrDefault(x => x.Night == d && x.Kind == k);
                free.TryGetValue(MeasRevenueLogic.Dow(d), out var peers);
                list.Add(new Computed(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["night"] = d, ["restriction"] = k, ["refused"] = (long)(c?.Refused ?? 0), ["reshaped"] = (long)(c?.Reshaped ?? 0), ["revenue"] = rev[d],
                    ["unrestricted"] = peers is { Count: > 0 } ? (long?)Math.Floor(peers.Sum() / (double)peers.Count) : null, ["mode"] = mode,
                }, MeasRevenueLogic.RestrictionRed(MeasRevenueData.RoomsOn(stays, d), MeasRevenueData.Sellable(rooms, d))));
            }
        return list;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var cl = await MeasRevenueCommon.ClockAsync(Platform, caller.PropertyId, ct).ConfigureAwait(false);
        var to = parameters.To ?? cl.BusinessDate;
        var from = parameters.From ?? new DateOnly(to.Year, to.Month, 1);
        if (to.DayNumber - from.DayNumber + 1 > 92) throw HccException.Validation("The range is longer than 92 days. Narrow the range.");
        var rows = await ComputeAsync(caller.PropertyId, from, to, Text(parameters, "kind"), ct).ConfigureAwait(false);
        return MeasRevenueCommon.Result(Id, Definition, parameters, Columns, rows.Select(MeasRevenueCommon.Row).ToList(), rows.Count == 0 ? CaptureNote(Basis, "restriction counters and yld.restrictionsEnforce") : null);
    }
}
