// F12 MEAS · rpt.rooms.ooo-days — Out-of-order and lost inventory (switch meas.hk.ooo). Server twin of meas-hk/ooo-days.ts.
// Money: "Lost revenue at ADR" is money, so the report carries moneyFlag with gate PERM-RPT-002 (Housekeeping / Maintenance / Supervisor see no money, DEC-061).
// Red row: out of order more than 7 days without a work order; a sellable room idle 14+ nights.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class OooDaysReport(SqlPlatform platform, Hcc.Application.Configuration.ISettingsReader? settings = null) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.rooms.ooo-days";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("room", "Room"), T("reason", "Reason"), I("roomDays", "Room-days"), T("workOrder", "Work order"), I("daysNoTicket", "Days without ticket"), M("lostRevenue", "Lost revenue at ADR"), I("idleNights", "Idle 14+ nights while sellable"),
    ];

    private const string Basis = "Room-days out of the sellable inventory on the business dates of the range (up to 92 days), from the room out-of-order window or block; reason and work order as captured by the housekeeping supervisor. Days without ticket = room-days when no work order is linked. Lost revenue = room-days × the ADR target before GST. Idle = consecutive unoccupied nights of a sellable room up to the To date (never before the From date). Red: out of order more than 7 days without a work order, or idle 14+ nights.";

    private static readonly Dictionary<string, string> Label = new(StringComparer.Ordinal) { ["OOO"] = "Out of order", ["OOS"] = "Out of service", ["house-use"] = "House use", ["renovation"] = "Renovation" };

    public override string MeasKey => "meas.hk.ooo";

    public override ReportDefinition Definition { get; } = new(Id, "Out-of-order and lost inventory", "rooms", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-HK-001"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("reason", "Reason", "enum", null, ["OOO", "OOS", "house-use", "renovation"])],
        Columns, Basis, Std, ReportPhone.Yes);

    /// <summary>Nights of [from, to] (inclusive) covered by the window [wFrom, wTo) (null bound = open).</summary>
    public static int Days(DateOnly? wFrom, DateOnly? wTo, DateOnly from, DateOnly to)
    {
        var a = wFrom is { } f && f > from ? f : from;
        var endEx = wTo is { } t && t <= to.AddDays(1) ? t : to.AddDays(1);
        return a < endEx ? endEx.DayNumber - a.DayNumber : 0;
    }

    /// <summary>Consecutive unoccupied nights ending on <paramref name="to"/>, never before <paramref name="from"/>.</summary>
    public static int Idle(ISet<DateOnly> occupiedNights, DateOnly from, DateOnly to)
    {
        var n = 0;
        for (var d = to; d >= from; d = d.AddDays(-1))
        {
            if (occupiedNights.Contains(d)) break;
            n++;
        }

        return n;
    }

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await ClockAsync(caller, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var captured = (await QueryAsync("SELECT COUNT(*) FROM meas.room_offline WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))[0][0];
        if (L(captured) == 0) return Done(parameters, Columns, [], CaptureNote(Basis, "the offline reason, expected return and work-order link (meas-cap-hk)"));
        var wanted = P(parameters, "reason");
        var rooms = await QueryAsync("""
            SELECT rm.room_id, rm.room_no, b.from_date, b.to_date, o.reason, o.work_order_no,
                   (SELECT TOP (1) CAST(w.wo_id AS VARCHAR(30)) FROM mnt.work_order w WHERE w.property_id = rm.property_id AND w.room_id = rm.room_id AND w.status <> 'Cancelled'
                     AND CAST(w.created_utc AS DATE) <= @to ORDER BY w.created_utc DESC), b.block_id
              FROM inv.room rm
              LEFT JOIN ops.room_block b ON b.room_id = rm.room_id AND b.released_utc IS NULL
              LEFT JOIN meas.room_offline o ON o.property_id = rm.property_id AND o.room_id = rm.room_id
             WHERE rm.property_id = @p ORDER BY rm.room_no;
            """, caller.PropertyId, c => BindRange(c, parameters), ct).ConfigureAwait(false);
        var stays = await QueryAsync("""
            SELECT n.room_id, n.night FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
             WHERE r.property_id = @p AND r.status IN ('InHouse','Departed') AND n.night BETWEEN @from AND @to;
            """, caller.PropertyId, c => BindRange(c, parameters), ct).ConfigureAwait(false);
        var occupied = stays.GroupBy(s => L(s[0])).ToDictionary(g => g.Key, g => g.Select(s => Dt(s[1])).ToHashSet());
        // f12-int-an (MEAS-RPT-HK item 5): lost revenue at the Owner's stored meas.target.adr (paise before GST), else the contract default.
        var adr = await Hcc.Reporting.Reports.MeasDaily.MeasTargets.GetAsync(settings, caller.PropertyId, "meas.target.adr", DefaultAdrPaise, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        foreach (var r in rooms.OrderBy(r => S(r[1]), StringComparer.Ordinal))
        {
            var isBlocked = r[7] is not null; // an active block row (open-ended bounds are null)
            var days = isBlocked ? Days(r[2] is null ? null : Dt(r[2]), r[3] is null ? null : Dt(r[3]), from, to) : 0;
            if (days > 0)
            {
                var code = r[4] is null ? "OOO" : S(r[4]);
                if (wanted != "" && wanted != code) continue;
                var wo = r[5] is null ? S(r[6]) : S(r[5]);
                var noTicket = wo == "" ? days : 0;
                rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["room"] = S(r[1]), ["reason"] = Label.GetValueOrDefault(code, code), ["roomDays"] = (long)days, ["workOrder"] = wo, ["daysNoTicket"] = (long)noTicket,
                    ["lostRevenue"] = days * adr, ["idleNights"] = 0L,
                }, OooNoTicketRed(noTicket)));
            }
            else if (!isBlocked && wanted == "")
            {
                var idle = Idle(occupied.GetValueOrDefault(L(r[0])) ?? [], from, to);
                if (IdleRed(idle))
                    rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["room"] = S(r[1]), ["reason"] = "", ["roomDays"] = 0L, ["workOrder"] = "", ["daysNoTicket"] = 0L, ["lostRevenue"] = 0L, ["idleNights"] = (long)idle,
                    }, true));
            }
        }

        return Done(parameters, Columns, rows);
    }
}
