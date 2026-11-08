// F8-RPT-P2-C · the reads the batch-c reports need and no existing query gives: GRN lines, POS sale and void lines, leave days with entitlements, cancelled folio lines.
// Plain SELECTs on the tables the commands write (stk.grn, pos.order_line, hr.leave_request, bill.charge); no new tables, no new figures.
using System.Data;
using System.Globalization;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.P2C;

public sealed class SqlP2CReportSource(SqlConnectionFactory connections) : IP2CReportSource
{
    private static void Add(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static DateOnly D(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);

    private static void Range(SqlCommand c, int p, DateOnly from, DateOnly to)
    {
        Add(c, "@p", SqlDbType.Int, p); Add(c, "@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)); Add(c, "@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
    }

    public async Task<IReadOnlyDictionary<int, string>> NamesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT user_id, display_name FROM plat.app_user WHERE property_id = @p;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var map = new Dictionary<int, string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetInt32(0)] = rd.GetString(1);
        return map;
    }

    public async Task<IReadOnlyList<GrnFact>> GrnsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT g.grn_id, g.business_date, g.supplier, g.ref, i.name, l.qty, l.cost_paise
              FROM stk.grn g JOIN stk.grn_line l ON l.grn_id = g.grn_id JOIN stk.item i ON i.item_id = l.item_id
             WHERE g.property_id = @p AND g.business_date BETWEEN @from AND @to ORDER BY g.business_date, g.grn_id, i.name;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<GrnFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var no = "GRN-" + rd.GetInt64(0).ToString(CultureInfo.InvariantCulture);
            var bill = S(rd, 3);
            list.Add(new GrnFact(no, D(rd, 1), S(rd, 2), S(rd, 4), rd.GetInt32(5), rd.GetInt64(6), string.Equals(bill, no, StringComparison.Ordinal) ? string.Empty : bill));
        }

        return list;
    }

    public async Task<IReadOnlyList<SaleLineFact>> SalesAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT l.item_id, l.name, COALESCE(mc.name, mi.category, N'Other'), l.qty, l.price_paise, o.name, b.settled_by
              FROM pos.bill b JOIN pos.check_order c ON c.order_id = b.order_id JOIN pos.order_line l ON l.order_id = c.order_id
              JOIN pos.menu_item mi ON mi.item_id = l.item_id LEFT JOIN pos.menu_category mc ON mc.category_id = mi.category_id
              JOIN pos.outlet o ON o.outlet_id = c.outlet_id
             WHERE b.property_id = @p AND b.status = 'Settled' AND c.status = 'Settled' AND l.voided = 0 AND b.business_date BETWEEN @from AND @to;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<SaleLineFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            long qty = rd.GetInt16(3);
            list.Add(new SaleLineFact(rd.GetInt32(0).ToString(CultureInfo.InvariantCulture), S(rd, 1), S(rd, 2), qty, qty * rd.GetInt64(4), S(rd, 5), rd.GetInt32(6)));
        }

        return list;
    }

    public async Task<IReadOnlyList<VoidFact>> VoidsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT l.line_id, c.order_id, l.name, l.qty, l.price_paise, l.void_reason, c.void_reason, l.void_by, l.void_date, c.status, c.voided_utc,
                   (SELECT TOP 1 b.bill_no FROM pos.bill b WHERE b.order_id = c.order_id), vr.decided_by
              FROM pos.order_line l JOIN pos.check_order c ON c.order_id = l.order_id LEFT JOIN pos.void_request vr ON vr.void_id = l.void_request_id AND vr.status = 'Executed'
             WHERE c.property_id = @p AND l.voided = 1 AND l.void_date BETWEEN @from AND @to;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<VoidFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var day = D(rd, 8);
            var whole = S(rd, 9) == "Void" && !rd.IsDBNull(10) && DateOnly.FromDateTime(rd.GetDateTime(10).AddMinutes(330)) == day;
            var time = whole ? P2CRules.Stamp(rd.GetDateTime(10)) : day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var reason = S(rd, 5).Length > 0 ? S(rd, 5) : S(rd, 6);
            var check = S(rd, 11).Length > 0 ? S(rd, 11) : "POS-" + rd.GetInt64(1).ToString(CultureInfo.InvariantCulture);
            long qty = rd.GetInt16(3);
            list.Add(new VoidFact(time, check, S(rd, 2), qty, qty * rd.GetInt64(4), reason, rd.IsDBNull(7) ? 0 : rd.GetInt32(7), rd.IsDBNull(12) ? null : rd.GetInt32(12),
                rd.GetInt64(1).ToString("D12", CultureInfo.InvariantCulture) + "/" + rd.GetInt64(0).ToString("D12", CultureInfo.InvariantCulture)));
        }

        return list;
    }

    public async Task<(IReadOnlyList<LeaveStaffFact> Staff, IReadOnlyList<LeaveDayFact> Days)> LeaveAsync(int propertyId, DateOnly fyFrom, DateOnly fyTo, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.staff_id, s.name, s.joined, s.active, s.exited_on,
                   COALESCE(o.cl_days, lp.cl_days, 12), COALESCE(o.sl_days, lp.sl_days, 7), COALESCE(o.el_days, lp.el_days, 15)
              FROM hr.staff s LEFT JOIN hr.leave_policy lp ON lp.property_id = s.property_id
              LEFT JOIN hr.leave_ent_override o ON o.property_id = s.property_id AND o.staff_id = s.staff_id
             WHERE s.property_id = @p ORDER BY s.name, s.staff_id;
            SELECT l.staff_id, l.leave_type, l.from_date, l.days_x2, l.status
              FROM hr.leave_request l WHERE l.property_id = @p AND l.from_date BETWEEN @from AND @to AND l.status IN ('Pending','Approved') AND l.leave_type IN ('CL','SL','EL');
            """, conn);
        Range(cmd, propertyId, fyFrom, fyTo);
        var staff = new List<LeaveStaffFact>(); var days = new List<LeaveDayFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            staff.Add(new LeaveStaffFact(rd.GetInt64(0), S(rd, 1), D(rd, 2), rd.GetBoolean(3), rd.IsDBNull(4) ? null : D(rd, 4), rd.GetDecimal(5), rd.GetDecimal(6), rd.GetDecimal(7)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            days.Add(new LeaveDayFact(rd.GetInt64(0), S(rd, 1), D(rd, 2), rd.GetInt16(3) / 2m, S(rd, 4)));
        return (staff, days);
    }

    public async Task<IReadOnlyList<CancelledFact>> CancelledAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT r.charge_id, r.service_date, rs.public_no,
                   ISNULL((SELECT TOP 1 rm.room_no FROM inv.room_night n JOIN inv.room rm ON rm.room_id = n.room_id WHERE n.reservation_id = rs.reservation_id ORDER BY n.night, rm.room_no), ''),
                   o.label, ABS(o.amount_paise), ap.requested_by, ap.reason
              FROM bill.charge r JOIN bill.charge o ON o.charge_id = r.reverses_charge_id JOIN bill.folio f ON f.folio_id = r.folio_id JOIN inv.reservation rs ON rs.reservation_id = f.reservation_id
              JOIN bill.approval ap ON ap.approval_id = r.approval_id AND ap.action = 'correction'
             WHERE rs.property_id = @p AND r.service_date BETWEEN @from AND @to;
            """, conn);
        Range(cmd, propertyId, from, to);
        var list = new List<CancelledFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new CancelledFact(D(rd, 1), rd.GetInt64(0).ToString("D12", CultureInfo.InvariantCulture), S(rd, 2), S(rd, 3), S(rd, 4), rd.GetInt64(5), rd.GetInt32(6), S(rd, 7)));
        return list;
    }
}
