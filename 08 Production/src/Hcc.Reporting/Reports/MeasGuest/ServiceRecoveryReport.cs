// F12-RPT-GUEST · rpt.guests.service-recovery — Service recovery (ops.guest_complaint, meas.root_cause_set; switch meas.guest.recovery). Twin of meas-guest/service-recovery.ts.
// Money: the compensation column only (PERM-RPT-002 gate; removed for roles without it, DEC-061).
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class ServiceRecoveryReport(SqlPlatform platform, ISettingsReader? settings = null, TimeProvider? time = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.service-recovery";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.T("category", "Category"), MeasGuestRules.T("room", "Room"), MeasGuestRules.I("complaints", "Complaints"), MeasGuestRules.T("hours", "Hours to resolve"),
        MeasGuestRules.P("withinSla", "Within SLA %"), MeasGuestRules.T("rootCause", "Root cause"), MeasGuestRules.M("compensation", "Compensation"), MeasGuestRules.T("approver", "Approver"),
        MeasGuestRules.I("repeat", "Repeat in 90 days"),
    ];

    public override string MeasKey => "meas.guest.recovery";

    public override ReportDefinition Definition { get; } = new(Id, "Service recovery", "guests", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("category", "Category", "text"), new("room", "Room", "room")],
        Columns, "Complaints by the day they were raised (range up to 92 days), one row per category and room. Hours to resolve = average of resolved complaints; within SLA % over complaints whose category has an SLA; repeat = complaints with an earlier one for the same room and category in the 90 days before. Compensation is the recovery amount posted.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private sealed record Complaint(long Id, string Category, string Key, string Room, DateTime Created, DateTime? Resolved, long Recovery, long? ApprovalId, string Approver, string Cause, int? SlaHours);

    private const string Sql = """
        SELECT c.complaint_id, c.category, COALESCE(c.category_code, k.code, c.category), COALESCE(rm.room_no, ''), c.created_utc, c.resolved_utc, COALESCE(c.recovery_amount_paise, 0),
               c.recovery_approval_id, COALESCE(ua.display_name, ''), COALESCE(rc.label, rcs.code, ''), k.sla_hours
          FROM ops.guest_complaint c
          LEFT JOIN inv.room rm ON rm.room_id = c.room_id
          LEFT JOIN gsv.complaint_category k ON k.property_id = c.property_id AND (k.code = c.category_code OR (c.category_code IS NULL AND k.name = c.category))
          LEFT JOIN bill.approval ap ON ap.approval_id = c.recovery_approval_id
          LEFT JOIN plat.app_user ua ON ua.user_id = ap.decided_by
          LEFT JOIN meas.root_cause_set rcs ON rcs.property_id = c.property_id AND rcs.complaint_id = CAST(c.complaint_id AS VARCHAR(40))
          LEFT JOIN meas.root_cause rc ON rc.property_id = rcs.property_id AND rc.code = rcs.code
         WHERE c.property_id = @p AND c.created_utc >= DATEADD(DAY, -95, @from) AND c.created_utc < DATEADD(DAY, 2, @to)
         ORDER BY c.created_utc, c.complaint_id;
        """;

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(Platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        string from = GuestsSql.Iso(parameters.From!.Value), to = GuestsSql.Iso(parameters.To!.Value), category = Param(parameters, "category"), room = Param(parameters, "room");
        var all = new List<Complaint>();
        await using (var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            Add(cmd, "@from", SqlDbType.DateTime2, GuestsSql.D(parameters.From!.Value));
            Add(cmd, "@to", SqlDbType.DateTime2, GuestsSql.D(parameters.To!.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                all.Add(new Complaint(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetDateTime(4), rd.IsDBNull(5) ? null : rd.GetDateTime(5), rd.GetInt64(6),
                    rd.IsDBNull(7) ? null : rd.GetInt64(7), rd.GetString(8), rd.GetString(9), rd.IsDBNull(10) ? null : Convert.ToInt32(rd.GetValue(10), CultureInfo.InvariantCulture)));
        }

        string Day(Complaint c) => LocalOf(clock, c.Created).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var nowUtc = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        bool Over(Complaint c) => c.SlaHours is { } h && (c.Resolved ?? nowUtc) > c.Created.AddHours(h);
        bool Repeat(Complaint c) => c.Room.Length > 0 && all.Any(p => p.Id != c.Id && p.Room == c.Room && p.Key == c.Key && p.Created < c.Created && (c.Created - p.Created).TotalDays <= MeasGuestRules.RepeatDays);
        var list = all.Where(c => string.CompareOrdinal(Day(c), from) >= 0 && string.CompareOrdinal(Day(c), to) <= 0
            && (category.Length == 0 || c.Category == category || c.Key == category) && (room.Length == 0 || c.Room == room)).ToList();
        var rows = list.GroupBy(c => c.Category + "\0" + c.Room).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var cs = g.ToList();
            var done = cs.Where(c => c.Resolved is not null).ToList();
            var sla = cs.Where(c => c.SlaHours is not null).ToList();
            return Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["category"] = cs[0].Category, ["room"] = cs[0].Room, ["complaints"] = (long)cs.Count,
                ["hours"] = MeasGuestRules.Avg1(done.Sum(c => (decimal)(c.Resolved!.Value - c.Created).TotalMinutes / 60m), done.Count),
                ["withinSla"] = MeasGuestRules.Pct1(sla.Count(c => !Over(c)), sla.Count),
                ["rootCause"] = string.Join(", ", cs.Select(c => c.Cause).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                ["compensation"] = cs.Sum(c => c.Recovery),
                ["approver"] = string.Join(", ", cs.Where(c => c.ApprovalId is not null).Select(c => c.Approver).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                ["repeat"] = (long)cs.Count(Repeat),
            });
        }).ToList();
        return Result(parameters, rows, "rootCause code list (meas-cap-guest)");
    }
}
