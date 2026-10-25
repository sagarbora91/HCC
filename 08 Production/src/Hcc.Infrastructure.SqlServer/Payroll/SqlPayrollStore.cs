using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Payroll;

/// <summary>SQL Server implementation of <see cref="IPayrollStore"/> (migration 0900).</summary>
public sealed class SqlPayrollStore(SqlConnectionFactory connections) : IPayrollStore
{
    public IPayrollDb In(ICommandTransaction tx)
    {
        var sql = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The payroll store needs a SQL command transaction.");
        return new SqlPayrollDb(connections, sql.Connection, sql.Transaction, owns: false);
    }

    public async Task<IPayrollReadDb> OpenReadAsync(CancellationToken ct)
        => new SqlPayrollDb(connections, await connections.OpenAsync(ct).ConfigureAwait(false), null, owns: true);
}

/// <summary>
/// Payroll statements over one connection (+ the command transaction when writing). Locks: UPDLOCK (+ HOLDLOCK where a key range matters)
/// on the anchor, department, staff, leave, run and F&amp;F rows; no insert-and-catch inside a transaction (contracts §4 rule 8).
/// </summary>
internal sealed class SqlPayrollDb(SqlConnectionFactory factory, SqlConnection conn, SqlTransaction? tx, bool owns) : IPayrollReadDb
{
    private const string StaffColumns = """
        staff_id, property_id, name, dept_code, designation, user_id, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, exited_on,
        active, phone, gender, structure, pf_applicable, esic_applicable, bank_name, account_no, ifsc, row_ver
        """;
    private const string LeaveColumns = "leave_id, staff_id, leave_type, from_date, to_date, half, days_x2, status, property_id, reason, requested_by, requested_utc, decided_by, note, row_ver";
    private const string RunColumns = "run_id, property_id, pay_month, status, rates_json, warnings_json, lock_seq, prepared_by, locked_by, row_ver";
    private const string FnfColumns = """
        fnf_id, property_id, staff_id, pay_month, salary_included, line_json, advance_outstanding_paise, adjustments_json, additions_paise, earnings_paise,
        recoveries_paise, net_paise, status, prepared_by, row_ver
        """;

    public async ValueTask DisposeAsync()
    {
        if (owns) await conn.DisposeAsync().ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql) => factory.Command(sql, conn, tx);

    private static void P(SqlCommand c, string name, SqlDbType type, object? value) => c.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    private static void P(SqlCommand c, string name, SqlDbType type, int size, object? value) => c.Parameters.Add(name, type, size).Value = value ?? DBNull.Value;

    private static string V(SqlDataReader r, int i) => SqlValues.RowVersionToWire((byte[])r[i]);

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static DateOnly D(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Ids(IEnumerable<long> ids) => string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    private async Task<int> ExecAsync(SqlCommand c, CancellationToken ct)
    {
        await using (c) return await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<object?> ScalarAsync(SqlCommand c, CancellationToken ct)
    {
        await using (c) return await c.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static async Task<List<T>> ReadAsync<T>(SqlCommand c, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using (c)
        {
            await using var r = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var list = new List<T>();
            while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(map(r));
            return list;
        }
    }

    // ── property / anchor / departments ──

    public async Task<PropertyClock> PropertyAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("SELECT business_date, time_zone FROM inv.property WHERE property_id = @p;");
        P(c, "@p", SqlDbType.Int, propertyId);
        var rows = await ReadAsync(c, r => new PropertyClock(D(r, 0), r.GetString(1)), ct).ConfigureAwait(false);
        return rows.Count == 1 ? rows[0] : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task LockAnchorAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("""
            IF NOT EXISTS (SELECT 1 FROM hr.payroll_anchor WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p)
              INSERT hr.payroll_anchor(property_id) VALUES (@p);
            SELECT property_id FROM hr.payroll_anchor WITH (UPDLOCK) WHERE property_id = @p;
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        await ScalarAsync(c, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DepartmentRow>> DepartmentsAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("SELECT dept_code, name, leave_cap FROM hr.department WHERE property_id = @p ORDER BY dept_code;");
        P(c, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(c, r => new DepartmentRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetByte(2)), ct).ConfigureAwait(false);
    }

    public async Task<DepartmentRow?> LockDepartmentAsync(int propertyId, string code, CancellationToken ct)
    {
        var c = Cmd("SELECT dept_code, name, leave_cap FROM hr.department WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND dept_code = @d;");
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@d", SqlDbType.VarChar, 10, code);
        return (await ReadAsync(c, r => new DepartmentRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetByte(2)), ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task UpsertDepartmentAsync(int propertyId, DepartmentRow dept, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dept);
        var c = Cmd("""
            UPDATE hr.department SET name = @n, leave_cap = @c WHERE property_id = @p AND dept_code = @d;
            IF @@ROWCOUNT = 0 INSERT hr.department(property_id, dept_code, name, leave_cap) VALUES (@p, @d, @n, @c);
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@d", SqlDbType.VarChar, 10, dept.Code);
        P(c, "@n", SqlDbType.NVarChar, 60, dept.Name);
        P(c, "@c", SqlDbType.TinyInt, dept.LeaveCap is { } x ? (byte)x : null);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    public async Task<bool> UserAtPropertyAsync(int propertyId, int userId, CancellationToken ct)
    {
        var c = Cmd("SELECT COUNT(*) FROM plat.app_user WHERE user_id = @u AND property_id = @p;");
        P(c, "@u", SqlDbType.Int, userId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return Convert.ToInt32(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    // ── staff ──

    private static StaffRow MapStaff(SqlDataReader r)
    {
        static T? E<T>(SqlDataReader r, int i) where T : struct, Enum => r.IsDBNull(i) ? null : Enum.Parse<T>(r.GetString(i));
        var profile = new StaffProfile(r.GetInt64(0), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(6) == "daily" ? PayType.Daily : PayType.Monthly,
            r.GetInt64(7), r.GetInt64(8), Enum.Parse<ShiftCode>(r.GetString(9)), r.GetByte(10), D(r, 11), r.IsDBNull(12) ? null : D(r, 12), r.GetBoolean(13),
            E<Gender>(r, 15), E<PayStructure>(r, 16), r.IsDBNull(17) ? null : r.GetBoolean(17), r.IsDBNull(18) ? null : r.GetBoolean(18));
        return new StaffRow(profile, r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(14), S(r, 19), S(r, 20), S(r, 21), V(r, 22));
    }

    public async Task<StaffRow?> StaffAsync(int propertyId, long staffId, bool lockRow, CancellationToken ct)
    {
        var c = Cmd($"SELECT {StaffColumns} FROM hr.staff {(lockRow ? "WITH (UPDLOCK)" : "")} WHERE staff_id = @s AND property_id = @p;");
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(c, MapStaff, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<StaffRow?> StaffOfUserAsync(int propertyId, int userId, CancellationToken ct)
    {
        var c = Cmd($"SELECT {StaffColumns} FROM hr.staff WHERE user_id = @u AND property_id = @p;");
        P(c, "@u", SqlDbType.Int, userId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(c, MapStaff, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<StaffRow>> AllStaffAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd($"SELECT {StaffColumns} FROM hr.staff WHERE property_id = @p ORDER BY staff_id;");
        P(c, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(c, MapStaff, ct).ConfigureAwait(false);
    }

    private static void StaffParams(SqlCommand c, StaffProfile p, int? userId, string phone, string? bank, string? account, string? ifsc)
    {
        P(c, "@n", SqlDbType.NVarChar, 100, p.Name);
        P(c, "@d", SqlDbType.VarChar, 10, p.Dept);
        P(c, "@g", SqlDbType.NVarChar, 80, p.Designation);
        P(c, "@u", SqlDbType.Int, userId);
        P(c, "@pt", SqlDbType.VarChar, 7, p.PayType == PayType.Daily ? "daily" : "monthly");
        P(c, "@m", SqlDbType.BigInt, p.MonthlyPaise);
        P(c, "@dw", SqlDbType.BigInt, p.DailyPaise);
        P(c, "@sh", SqlDbType.Char, 1, p.DefaultShift.ToString());
        P(c, "@wo", SqlDbType.TinyInt, (byte)p.WeeklyOff);
        P(c, "@j", SqlDbType.Date, p.Joined.ToDateTime(TimeOnly.MinValue));
        P(c, "@x", SqlDbType.Date, p.ExitedOn?.ToDateTime(TimeOnly.MinValue));
        P(c, "@a", SqlDbType.Bit, p.Active);
        P(c, "@ph", SqlDbType.VarChar, 20, phone);
        P(c, "@ge", SqlDbType.Char, 1, p.Gender?.ToString());
        P(c, "@st", SqlDbType.Char, 1, p.Structure?.ToString());
        P(c, "@pf", SqlDbType.Bit, p.PfApplicable);
        P(c, "@es", SqlDbType.Bit, p.EsicApplicable);
        P(c, "@bn", SqlDbType.NVarChar, 80, bank);
        P(c, "@ac", SqlDbType.VarChar, 18, account);
        P(c, "@if", SqlDbType.Char, 11, ifsc);
    }

    public async Task<long> InsertStaffAsync(NewStaff staff, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staff);
        var c = Cmd("""
            INSERT hr.staff(property_id, name, dept_code, designation, user_id, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, exited_on,
                            active, phone, gender, structure, pf_applicable, esic_applicable, bank_name, account_no, ifsc, created_by, created_utc)
            OUTPUT INSERTED.staff_id
            VALUES (@p, @n, @d, @g, @u, @pt, @m, @dw, @sh, @wo, @j, @x, @a, @ph, @ge, @st, @pf, @es, @bn, @ac, @if, @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, staff.PropertyId);
        StaffParams(c, staff.Profile, staff.UserId, staff.Phone, staff.BankName, staff.AccountNo, staff.Ifsc);
        P(c, "@by", SqlDbType.Int, staff.By);
        P(c, "@at", SqlDbType.DateTime2, staff.Utc);
        return Convert.ToInt64(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<string> UpdateStaffAsync(StaffRow row, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        var c = Cmd("""
            UPDATE hr.staff SET name = @n, dept_code = @d, designation = @g, user_id = @u, pay_type = @pt, monthly_paise = @m, daily_paise = @dw, default_shift = @sh,
                   weekly_off = @wo, joined = @j, exited_on = @x, active = @a, phone = @ph, gender = @ge, structure = @st, pf_applicable = @pf, esic_applicable = @es,
                   bank_name = @bn, account_no = @ac, ifsc = @if
            OUTPUT INSERTED.row_ver
            WHERE staff_id = @id AND row_ver = @v;
            """);
        StaffParams(c, row.Profile, row.UserId, row.Phone, row.BankName, row.AccountNo, row.Ifsc);
        P(c, "@id", SqlDbType.BigInt, row.StaffId);
        P(c, "@v", SqlDbType.Binary, 8, SqlValues.RowVersionFromWire(expectedVersion));
        var v = await ScalarAsync(c, ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The staff record");
        return SqlValues.RowVersionToWire(v!);
    }

    public async Task<int> LockedPayslipCountAsync(long staffId, CancellationToken ct)
    {
        var c = Cmd("SELECT COUNT(*) FROM hr.pay_run_line l JOIN hr.pay_run r ON r.run_id = l.run_id WHERE l.staff_id = @s AND r.status = 'Locked';");
        P(c, "@s", SqlDbType.BigInt, staffId);
        return Convert.ToInt32(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // ── roster / attendance / leave ──

    private static LeaveSpan MapSpan(SqlDataReader r) => new(r.GetInt64(0), r.GetInt64(1), Enum.Parse<LeaveType>(r.GetString(2)), D(r, 3), D(r, 4),
        r.IsDBNull(5) ? null : Enum.Parse<LeaveHalf>(r.GetString(5)), r.GetInt16(6) / 2m, Enum.Parse<LeaveStatus>(r.GetString(7)));

    private static LeaveRow MapLeave(SqlDataReader r) => new(MapSpan(r), r.GetInt32(8), r.GetString(9), r.GetInt32(10), r.GetDateTime(11),
        r.IsDBNull(12) ? null : r.GetInt32(12), S(r, 13), V(r, 14));

    public async Task<IReadOnlyDictionary<long, StaffDays>> DaysAsync(int propertyId, IReadOnlyCollection<long> staffIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staffIds);
        var result = new Dictionary<long, StaffDays>();
        if (staffIds.Count == 0) return result;
        var ids = Ids(staffIds);
        var c1 = Cmd("""
            SELECT r.staff_id, r.work_date, r.shift_code FROM hr.roster r JOIN STRING_SPLIT(@ids, ',') s ON r.staff_id = CAST(s.value AS BIGINT)
            WHERE r.work_date BETWEEN @f AND @t;
            """);
        P(c1, "@ids", SqlDbType.VarChar, -1, ids);
        P(c1, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        P(c1, "@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        var roster = await ReadAsync(c1, r => (r.GetInt64(0), D(r, 1), Enum.Parse<ShiftCode>(r.GetString(2))), ct).ConfigureAwait(false);
        var c2 = Cmd("""
            SELECT a.staff_id, a.work_date, a.status, a.ot_minutes FROM hr.attendance a JOIN STRING_SPLIT(@ids, ',') s ON a.staff_id = CAST(s.value AS BIGINT)
            WHERE a.work_date BETWEEN @f AND @t;
            """);
        P(c2, "@ids", SqlDbType.VarChar, -1, ids);
        P(c2, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        P(c2, "@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        var att = await ReadAsync(c2, r => new AttendanceMark(D(r, 1), Enum.Parse<AttendanceStatus>(r.GetString(2)), r.GetInt16(3)) is var m ? (r.GetInt64(0), m) : default, ct)
            .ConfigureAwait(false);
        var c3 = Cmd($"""
            SELECT {LeaveColumns} FROM hr.leave_request l JOIN STRING_SPLIT(@ids, ',') s ON l.staff_id = CAST(s.value AS BIGINT)
            WHERE l.property_id = @p ORDER BY l.leave_id;
            """);
        P(c3, "@ids", SqlDbType.VarChar, -1, ids);
        P(c3, "@p", SqlDbType.Int, propertyId);
        var leaves = await ReadAsync(c3, MapSpan, ct).ConfigureAwait(false);
        foreach (var id in staffIds)
            result[id] = new StaffDays(roster.Where(x => x.Item1 == id).ToDictionary(x => x.Item2, x => x.Item3),
                att.Where(x => x.Item1 == id).ToDictionary(x => x.m.Date, x => x.m), leaves.Where(l => l.StaffId == id).ToList());
        return result;
    }

    public async Task SetRosterAsync(long staffId, DateOnly date, ShiftCode shift, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.roster WITH (HOLDLOCK) SET shift_code = @c, set_by = @by, set_utc = @at WHERE staff_id = @s AND work_date = @d;
            IF @@ROWCOUNT = 0 INSERT hr.roster(staff_id, work_date, shift_code, set_by, set_utc) VALUES (@s, @d, @c, @by, @at);
            """);
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        P(c, "@c", SqlDbType.VarChar, 3, shift.ToString());
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    public async Task<bool> UpsertAttendanceAsync(long staffId, AttendanceMark mark, string? note, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var c = Cmd("""
            DECLARE @existed BIT = 0;
            IF EXISTS (SELECT 1 FROM hr.attendance WITH (UPDLOCK, HOLDLOCK) WHERE staff_id = @s AND work_date = @d)
            BEGIN
              UPDATE hr.attendance SET status = @st, ot_minutes = @ot, marked_by = @by, marked_utc = @at, note = @n WHERE staff_id = @s AND work_date = @d;
              SET @existed = 1;
            END
            ELSE INSERT hr.attendance(staff_id, work_date, status, ot_minutes, marked_by, marked_utc, note) VALUES (@s, @d, @st, @ot, @by, @at, @n);
            SELECT @existed;
            """);
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@d", SqlDbType.Date, mark.Date.ToDateTime(TimeOnly.MinValue));
        P(c, "@st", SqlDbType.VarChar, 3, mark.Status.ToString());
        P(c, "@ot", SqlDbType.SmallInt, (short)mark.OtMinutes);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        P(c, "@n", SqlDbType.NVarChar, 120, note);
        return (bool)(await ScalarAsync(c, ct).ConfigureAwait(false))!;
    }

    public async Task<bool> AnyAttendanceAsync(long staffId, DateOnly from, DateOnly to, bool exceptHalfDayMarks, CancellationToken ct)
    {
        var c = Cmd("SELECT COUNT(*) FROM hr.attendance WHERE staff_id = @s AND work_date BETWEEN @f AND @t AND (@x = 0 OR status NOT IN ('H','A'));");
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        P(c, "@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        P(c, "@x", SqlDbType.Bit, exceptHalfDayMarks);
        return Convert.ToInt32(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<LeaveRow> InsertLeaveAsync(int propertyId, LeaveSpan span, string reason, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(span);
        var c = Cmd("""
            INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, half, days_x2, reason, status, requested_by, requested_utc)
            OUTPUT INSERTED.leave_id
            VALUES (@p, @s, @ty, @f, @t, @h, @dx, @r, 'Pending', @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@s", SqlDbType.BigInt, span.StaffId);
        P(c, "@ty", SqlDbType.VarChar, 3, span.Type.ToString());
        P(c, "@f", SqlDbType.Date, span.From.ToDateTime(TimeOnly.MinValue));
        P(c, "@t", SqlDbType.Date, span.To.ToDateTime(TimeOnly.MinValue));
        P(c, "@h", SqlDbType.Char, 2, span.Half?.ToString());
        P(c, "@dx", SqlDbType.SmallInt, (short)(span.Days * 2));
        P(c, "@r", SqlDbType.NVarChar, 200, reason);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        var id = Convert.ToInt64(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return (await LockLeaveAsync(propertyId, id, ct).ConfigureAwait(false))!;
    }

    public async Task<LeaveRow?> LockLeaveAsync(int propertyId, long leaveId, CancellationToken ct)
    {
        var c = Cmd($"SELECT {LeaveColumns} FROM hr.leave_request WITH (UPDLOCK) WHERE leave_id = @l AND property_id = @p;");
        P(c, "@l", SqlDbType.BigInt, leaveId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(c, MapLeave, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<string> UpdateLeaveStatusAsync(long leaveId, string expectedVersion, LeaveStatus status, int by, DateTime utc, string? note, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.leave_request SET status = @st, decided_by = @by, decided_utc = @at, note = COALESCE(@n, note)
            OUTPUT INSERTED.row_ver WHERE leave_id = @l AND row_ver = @v;
            """);
        P(c, "@l", SqlDbType.BigInt, leaveId);
        P(c, "@st", SqlDbType.VarChar, 9, status.ToString());
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        P(c, "@n", SqlDbType.NVarChar, 200, note);
        P(c, "@v", SqlDbType.Binary, 8, SqlValues.RowVersionFromWire(expectedVersion));
        var v = await ScalarAsync(c, ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The leave request");
        return SqlValues.RowVersionToWire(v!);
    }

    public async Task<IReadOnlyList<(StaffProfile Staff, LeaveSpan Leave)>> DepartmentLeavesAsync(int propertyId, string dept, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var c = Cmd($"""
            SELECT {string.Join(", ", LeaveColumns.Split(", ").Select(x => "l." + x))}, {string.Join(", ", StaffColumns.Split(',').Select(x => "s." + x.Trim()))}
            FROM hr.leave_request l JOIN hr.staff s ON s.staff_id = l.staff_id
            WHERE s.property_id = @p AND s.dept_code = @d AND l.status IN ('Pending','Approved') AND l.from_date <= @t AND l.to_date >= @f
            ORDER BY l.leave_id;
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@d", SqlDbType.VarChar, 10, dept);
        P(c, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        P(c, "@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        return await ReadAsync(c, r => (MapStaffAt(r, 15), MapSpan(r)), ct).ConfigureAwait(false);
    }

    private static StaffProfile MapStaffAt(SqlDataReader r, int o)
    {
        static T? E<T>(SqlDataReader r, int i) where T : struct, Enum => r.IsDBNull(i) ? null : Enum.Parse<T>(r.GetString(i));
        return new StaffProfile(r.GetInt64(o), r.GetString(o + 2), r.GetString(o + 3), r.GetString(o + 4), r.GetString(o + 6) == "daily" ? PayType.Daily : PayType.Monthly,
            r.GetInt64(o + 7), r.GetInt64(o + 8), Enum.Parse<ShiftCode>(r.GetString(o + 9)), r.GetByte(o + 10), D(r, o + 11), r.IsDBNull(o + 12) ? null : D(r, o + 12),
            r.GetBoolean(o + 13), E<Gender>(r, o + 15), E<PayStructure>(r, o + 16), r.IsDBNull(o + 17) ? null : r.GetBoolean(o + 17), r.IsDBNull(o + 18) ? null : r.GetBoolean(o + 18));
    }

    public async Task<LeavePolicy> LeavePolicyAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("SELECT cl_days, sl_days, el_days, default_cap FROM hr.leave_policy WHERE property_id = @p;");
        P(c, "@p", SqlDbType.Int, propertyId);
        var row = (await ReadAsync(c, r => (r.GetByte(0), r.GetByte(1), r.GetByte(2), r.GetByte(3)), ct).ConfigureAwait(false)).FirstOrDefault();
        var caps = (await DepartmentsAsync(propertyId, ct).ConfigureAwait(false)).Where(d => d.LeaveCap is not null).ToDictionary(d => d.Code, d => d.LeaveCap!.Value);
        var d = LeavePolicy.Defaults();
        return row == default ? d with { DeptCap = caps } : new LeavePolicy(row.Item1, row.Item2, row.Item3, row.Item4, caps);
    }

    public async Task SetLeavePolicyAsync(int propertyId, LeavePolicy policy, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var c = Cmd("""
            UPDATE hr.leave_policy SET cl_days = @cl, sl_days = @sl, el_days = @el, default_cap = @cap, updated_by = @by, updated_utc = @at WHERE property_id = @p;
            IF @@ROWCOUNT = 0 INSERT hr.leave_policy(property_id, cl_days, sl_days, el_days, default_cap, updated_by, updated_utc) VALUES (@p, @cl, @sl, @el, @cap, @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@cl", SqlDbType.TinyInt, (byte)policy.Cl);
        P(c, "@sl", SqlDbType.TinyInt, (byte)policy.Sl);
        P(c, "@el", SqlDbType.TinyInt, (byte)policy.El);
        P(c, "@cap", SqlDbType.TinyInt, (byte)policy.DefaultCap);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    // ── rates ──

    public async Task<IReadOnlyList<PayrollRates>> RateVersionsAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("SELECT rates_json FROM hr.rate_version WHERE property_id = @p ORDER BY effective_from;");
        P(c, "@p", SqlDbType.Int, propertyId);
        var list = await ReadAsync(c, r => PayrollJson.Read<PayrollRates>(r.GetString(0)), ct).ConfigureAwait(false);
        return list.Count > 0 ? list : [PayrollRates.Defaults()];
    }

    public async Task ReplaceRateVersionsAsync(int propertyId, DateOnly from, IReadOnlyList<PayrollRates> versions, PayrollRates? before, IReadOnlyList<string> changed,
        IReadOnlyList<string> carried, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(carried);
        var existing = (await RateVersionsAsync(propertyId, ct).ConfigureAwait(false)).ToDictionary(v => v.From);
        foreach (var v in versions)
        {
            if (existing.TryGetValue(v.From, out var old) && old == v) continue;
            var c = Cmd("""
                UPDATE hr.rate_version SET rates_json = @j, set_by = @by, set_utc = @at WHERE property_id = @p AND effective_from = @f;
                IF @@ROWCOUNT = 0 INSERT hr.rate_version(property_id, effective_from, rates_json, set_by, set_utc) VALUES (@p, @f, @j, @by, @at);
                """);
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@f", SqlDbType.Date, v.From.ToDateTime(TimeOnly.MinValue));
            P(c, "@j", SqlDbType.NVarChar, 2000, PayrollJson.Write(v));
            P(c, "@by", SqlDbType.Int, by);
            P(c, "@at", SqlDbType.DateTime2, utc);
            await ExecAsync(c, ct).ConfigureAwait(false);
        }

        var saved = versions.First(v => v.From == from);
        var h = Cmd("""
            INSERT hr.rate_change(property_id, effective_from, before_json, after_json, changed_fields, carried, set_by, set_utc)
            VALUES (@p, @f, @b, @a, @c, @cr, @by, @at);
            """);
        P(h, "@p", SqlDbType.Int, propertyId);
        P(h, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        P(h, "@b", SqlDbType.NVarChar, 2000, before is null ? null : PayrollJson.Write(before));
        P(h, "@a", SqlDbType.NVarChar, 2000, PayrollJson.Write(saved));
        P(h, "@c", SqlDbType.NVarChar, 400, string.Join(", ", changed));
        P(h, "@cr", SqlDbType.NVarChar, 1000, string.Join(", ", carried));
        P(h, "@by", SqlDbType.Int, by);
        P(h, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(h, ct).ConfigureAwait(false);
    }

    // ── advances ──

    public async Task<IReadOnlyList<AdvanceRow>> AdvancesAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd("SELECT advance_id, staff_id, amount_paise, installment_paise, recovered_paise, advance_date, from_month FROM hr.advance WHERE property_id = @p ORDER BY advance_id;");
        P(c, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(c, r => new AdvanceRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), D(r, 5), PayMonth.Of(D(r, 6))), ct)
            .ConfigureAwait(false);
    }

    public async Task<long> InsertAdvanceAsync(int propertyId, long staffId, long amount, long installment, DateOnly date, PayMonth fromMonth, string reason, int by,
        DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            INSERT hr.advance(property_id, staff_id, amount_paise, installment_paise, advance_date, from_month, reason, given_by, given_utc)
            OUTPUT INSERTED.advance_id VALUES (@p, @s, @a, @i, @d, @f, @r, @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@a", SqlDbType.BigInt, amount);
        P(c, "@i", SqlDbType.BigInt, installment);
        P(c, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        P(c, "@f", SqlDbType.Date, fromMonth.First.ToDateTime(TimeOnly.MinValue));
        P(c, "@r", SqlDbType.NVarChar, 200, reason);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        return Convert.ToInt64(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task RecordRecoveryAsync(long advanceId, long? runId, long? fnfId, long amount, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.advance SET recovered_paise = recovered_paise + @a WHERE advance_id = @id;
            INSERT hr.advance_recovery(advance_id, run_id, fnf_id, amount_paise, recorded_utc) VALUES (@id, @r, @f, @a, @at);
            """);
        P(c, "@id", SqlDbType.BigInt, advanceId);
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@f", SqlDbType.BigInt, fnfId);
        P(c, "@a", SqlDbType.BigInt, amount);
        P(c, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    public async Task<int> ReverseRunRecoveriesAsync(long runId, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE a SET recovered_paise = a.recovered_paise - x.total
            FROM hr.advance a JOIN (SELECT advance_id, SUM(amount_paise) AS total FROM hr.advance_recovery WHERE run_id = @r AND reversed_utc IS NULL GROUP BY advance_id) x
              ON x.advance_id = a.advance_id;
            UPDATE hr.advance_recovery SET reversed_by = @by, reversed_utc = @at WHERE run_id = @r AND reversed_utc IS NULL;
            SELECT @@ROWCOUNT;
            """);
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        return Convert.ToInt32(await ScalarAsync(c, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // ── runs ──

    private static RunRow MapRun(SqlDataReader r) => new(r.GetInt64(0), r.GetInt32(1), PayMonth.Of(D(r, 2)), r.GetString(3) == "Locked", PayrollJson.Read<PayrollRates>(r.GetString(4)),
        PayrollJson.Read<List<string>>(r.GetString(5)), r.GetInt32(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetInt32(8), V(r, 9));

    public async Task<IReadOnlyList<RunRow>> RunsAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd($"SELECT {RunColumns} FROM hr.pay_run WHERE property_id = @p ORDER BY pay_month;");
        P(c, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(c, MapRun, ct).ConfigureAwait(false);
    }

    public async Task<RunRow?> RunAsync(int propertyId, long runId, bool lockRow, CancellationToken ct)
    {
        var c = Cmd($"SELECT {RunColumns} FROM hr.pay_run {(lockRow ? "WITH (UPDLOCK)" : "")} WHERE run_id = @r AND property_id = @p;");
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(c, MapRun, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<PayLine>> RunLinesAsync(long runId, CancellationToken ct)
    {
        var c = Cmd("SELECT line_json FROM hr.pay_run_line WHERE run_id = @r ORDER BY staff_id;");
        P(c, "@r", SqlDbType.BigInt, runId);
        return await ReadAsync(c, r => PayrollJson.Read<PayLine>(r.GetString(0)), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LockedCoverage>> LockedCoverageAsync(int propertyId, PayMonth from, PayMonth toExclusive, CancellationToken ct)
    {
        var c = Cmd("""
            SELECT l.staff_id, r.pay_month, l.esic_covered FROM hr.pay_run_line l JOIN hr.pay_run r ON r.run_id = l.run_id
            WHERE r.property_id = @p AND r.status = 'Locked' AND r.pay_month >= @f AND r.pay_month < @t;
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@f", SqlDbType.Date, from.First.ToDateTime(TimeOnly.MinValue));
        P(c, "@t", SqlDbType.Date, toExclusive.First.ToDateTime(TimeOnly.MinValue));
        return await ReadAsync(c, r => new LockedCoverage(r.GetInt64(0), PayMonth.Of(D(r, 1)), r.IsDBNull(2) ? null : r.GetBoolean(2)), ct).ConfigureAwait(false);
    }

    private async Task InsertLinesAsync(long runId, IReadOnlyList<PayLine> lines, CancellationToken ct)
    {
        foreach (var l in lines)
        {
            var c = Cmd("""
                INSERT hr.pay_run_line(run_id, staff_id, line_json, earned_paise, gross_paise, pf_paise, pf_er_paise, esic_paise, esic_er_paise, pt_paise, advance_paise,
                                       net_paise, esic_covered)
                VALUES (@r, @s, @j, @e, @g, @pf, @pfe, @es, @ese, @pt, @ad, @n, @cov);
                """);
            P(c, "@r", SqlDbType.BigInt, runId);
            P(c, "@s", SqlDbType.BigInt, l.StaffId);
            P(c, "@j", SqlDbType.NVarChar, 4000, PayrollJson.Write(l));
            P(c, "@e", SqlDbType.BigInt, l.Earned);
            P(c, "@g", SqlDbType.BigInt, l.Gross);
            P(c, "@pf", SqlDbType.BigInt, l.Pf);
            P(c, "@pfe", SqlDbType.BigInt, l.PfEr);
            P(c, "@es", SqlDbType.BigInt, l.Esic);
            P(c, "@ese", SqlDbType.BigInt, l.EsicEr);
            P(c, "@pt", SqlDbType.BigInt, l.Pt);
            P(c, "@ad", SqlDbType.BigInt, l.Advance);
            P(c, "@n", SqlDbType.BigInt, l.Net);
            P(c, "@cov", SqlDbType.Bit, l.EsicCovered);
            await ExecAsync(c, ct).ConfigureAwait(false);
        }
    }

    public async Task<(long RunId, string Version)> InsertRunAsync(int propertyId, PayMonth month, PayrollRates rates, IReadOnlyList<PayLine> lines,
        IReadOnlyList<string> warnings, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var c = Cmd("""
            INSERT hr.pay_run(property_id, pay_month, status, rates_json, warnings_json, prepared_by, prepared_utc)
            OUTPUT INSERTED.run_id, INSERTED.row_ver VALUES (@p, @m, 'Draft', @rj, @wj, @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, propertyId);
        P(c, "@m", SqlDbType.Date, month.First.ToDateTime(TimeOnly.MinValue));
        P(c, "@rj", SqlDbType.NVarChar, 2000, PayrollJson.Write(rates));
        P(c, "@wj", SqlDbType.NVarChar, -1, PayrollJson.Write(warnings));
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        var row = (await ReadAsync(c, r => (r.GetInt64(0), V(r, 1)), ct).ConfigureAwait(false))[0];
        await InsertLinesAsync(row.Item1, lines, ct).ConfigureAwait(false);
        return row;
    }

    public async Task<string> ReplaceDraftAsync(long runId, PayrollRates rates, IReadOnlyList<PayLine> lines, IReadOnlyList<string> warnings, int by, DateTime utc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var c = Cmd("""
            UPDATE hr.pay_run SET rates_json = @rj, warnings_json = @wj, prepared_by = @by, prepared_utc = @at OUTPUT INSERTED.row_ver WHERE run_id = @r AND status = 'Draft';
            """);
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@rj", SqlDbType.NVarChar, 2000, PayrollJson.Write(rates));
        P(c, "@wj", SqlDbType.NVarChar, -1, PayrollJson.Write(warnings));
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        var v = await ScalarAsync(c, ct).ConfigureAwait(false) as byte[] ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This payroll is locked and cannot be recalculated.");
        var d = Cmd("DELETE hr.pay_run_line WHERE run_id = @r;");
        P(d, "@r", SqlDbType.BigInt, runId);
        await ExecAsync(d, ct).ConfigureAwait(false);
        await InsertLinesAsync(runId, lines, ct).ConfigureAwait(false);
        return SqlValues.RowVersionToWire(v);
    }

    public async Task<(string Version, int LockSeq)> LockRunAsync(long runId, string expectedVersion, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.pay_run SET status = 'Locked', lock_seq = lock_seq + 1, locked_by = @by, locked_utc = @at
            OUTPUT INSERTED.row_ver, INSERTED.lock_seq WHERE run_id = @r AND row_ver = @v AND status = 'Draft';
            """);
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        P(c, "@v", SqlDbType.Binary, 8, SqlValues.RowVersionFromWire(expectedVersion));
        var rows = await ReadAsync(c, r => (V(r, 0), r.GetInt32(1)), ct).ConfigureAwait(false);
        SqlValues.EnsureVersionMatched(rows.Count, "The payroll run");
        var (version, seq) = rows[0];
        // F-02: freeze each line's bank details from the staff master as the checker approved them.
        var b = Cmd("""
            INSERT hr.pay_run_bank(run_id, lock_seq, staff_id, bank_name, account_no, ifsc)
            SELECT l.run_id, @seq, s.staff_id, ISNULL(s.bank_name, N''), ISNULL(s.account_no, ''), ISNULL(s.ifsc, '')
            FROM hr.pay_run_line l JOIN hr.staff s ON s.staff_id = l.staff_id WHERE l.run_id = @r;
            """);
        P(b, "@r", SqlDbType.BigInt, runId);
        P(b, "@seq", SqlDbType.Int, seq);
        await ExecAsync(b, ct).ConfigureAwait(false);
        return (version, seq);
    }

    public async Task<string> UnlockRunAsync(long runId, string expectedVersion, int lockSeq, string reason, IReadOnlyDictionary<long, long> nets, int bankFiles, int by,
        DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.pay_run SET status = 'Draft', locked_by = NULL, locked_utc = NULL OUTPUT INSERTED.row_ver WHERE run_id = @r AND row_ver = @v AND status = 'Locked';
            """);
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@v", SqlDbType.Binary, 8, SqlValues.RowVersionFromWire(expectedVersion));
        var v = await ScalarAsync(c, ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The payroll run");
        var u = Cmd("""
            INSERT hr.pay_run_unlock(run_id, lock_seq, reason, nets_json, bank_files, unlocked_by, unlocked_utc) VALUES (@r, @seq, @why, @nets, @bf, @by, @at);
            """);
        P(u, "@r", SqlDbType.BigInt, runId);
        P(u, "@seq", SqlDbType.Int, lockSeq);
        P(u, "@why", SqlDbType.NVarChar, 200, reason);
        P(u, "@nets", SqlDbType.NVarChar, -1, JsonSerializer.Serialize(nets));
        P(u, "@bf", SqlDbType.Int, bankFiles);
        P(u, "@by", SqlDbType.Int, by);
        P(u, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(u, ct).ConfigureAwait(false);
        return SqlValues.RowVersionToWire(v!);
    }

    public async Task<IReadOnlyDictionary<long, BankDetails>> BankSnapshotAsync(long runId, int lockSeq, CancellationToken ct)
    {
        var c = Cmd("SELECT staff_id, bank_name, account_no, ifsc FROM hr.pay_run_bank WHERE run_id = @r AND lock_seq = @seq;");
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@seq", SqlDbType.Int, lockSeq);
        return (await ReadAsync(c, r => (r.GetInt64(0), new BankDetails(r.GetString(1), r.GetString(2), r.GetString(3))), ct).ConfigureAwait(false))
            .ToDictionary(x => x.Item1, x => x.Item2);
    }

    public async Task<IReadOnlyList<(int IssueNo, int LockSeq, long Net, DateTime Utc)>> BankFilesAsync(long runId, CancellationToken ct)
    {
        var c = Cmd("SELECT issue_no, lock_seq, net_paise, issued_utc FROM hr.bank_file WHERE run_id = @r ORDER BY issue_no;");
        P(c, "@r", SqlDbType.BigInt, runId);
        return await ReadAsync(c, r => (r.GetInt32(0), r.GetInt32(1), r.GetInt64(2), r.GetDateTime(3)), ct).ConfigureAwait(false);
    }

    public async Task InsertBankFileAsync(long runId, int issueNo, int lockSeq, long net, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("INSERT hr.bank_file(run_id, issue_no, lock_seq, net_paise, issued_by, issued_utc) VALUES (@r, @n, @seq, @net, @by, @at);");
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@n", SqlDbType.Int, issueNo);
        P(c, "@seq", SqlDbType.Int, lockSeq);
        P(c, "@net", SqlDbType.BigInt, net);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(long StaffId, int LockSeq)>> PayslipSendsAsync(long runId, CancellationToken ct)
    {
        var c = Cmd("SELECT staff_id, lock_seq FROM hr.payslip_send WHERE run_id = @r;");
        P(c, "@r", SqlDbType.BigInt, runId);
        return await ReadAsync(c, r => (r.GetInt64(0), r.GetInt32(1)), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<long, long>>> UnlockNetsAsync(long runId, CancellationToken ct)
    {
        var c = Cmd("SELECT lock_seq, nets_json FROM hr.pay_run_unlock WHERE run_id = @r;");
        P(c, "@r", SqlDbType.BigInt, runId);
        return (await ReadAsync(c, r => (r.GetInt32(0), JsonSerializer.Deserialize<Dictionary<long, long>>(r.GetString(1)) ?? []), ct).ConfigureAwait(false))
            .ToDictionary(x => x.Item1, x => (IReadOnlyDictionary<long, long>)x.Item2);
    }

    public async Task InsertPayslipSendAsync(long runId, int lockSeq, long staffId, string to, long net, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("INSERT hr.payslip_send(run_id, lock_seq, staff_id, to_number, net_paise, sent_by, sent_utc) VALUES (@r, @seq, @s, @to, @net, @by, @at);");
        P(c, "@r", SqlDbType.BigInt, runId);
        P(c, "@seq", SqlDbType.Int, lockSeq);
        P(c, "@s", SqlDbType.BigInt, staffId);
        P(c, "@to", SqlDbType.VarChar, 12, to);
        P(c, "@net", SqlDbType.BigInt, net);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        await ExecAsync(c, ct).ConfigureAwait(false);
    }

    // ── full and final ──

    private sealed record AdjustmentJson(string Label, bool Addition, long AmountPaise);

    private static FnfRow MapFnf(SqlDataReader r) => new(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), PayMonth.Of(D(r, 3)), r.GetBoolean(4),
        r.IsDBNull(5) ? null : PayrollJson.Read<PayLine>(r.GetString(5)), r.GetInt64(6),
        PayrollJson.Read<List<AdjustmentJson>>(r.GetString(7)).Select(a => new FnfAdjustment(a.Label, a.Addition, a.AmountPaise)).ToList(),
        r.GetInt64(8), r.GetInt64(9), r.GetInt64(10), r.GetInt64(11), r.GetString(12), r.GetInt32(13), V(r, 14));

    public async Task<IReadOnlyList<FnfRow>> FnfsAsync(int propertyId, CancellationToken ct)
    {
        var c = Cmd($"SELECT {FnfColumns} FROM hr.fnf WHERE property_id = @p ORDER BY fnf_id;");
        P(c, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(c, MapFnf, ct).ConfigureAwait(false);
    }

    public async Task<FnfRow?> FnfAsync(int propertyId, long fnfId, bool lockRow, CancellationToken ct)
    {
        var c = Cmd($"SELECT {FnfColumns} FROM hr.fnf {(lockRow ? "WITH (UPDLOCK)" : "")} WHERE fnf_id = @f AND property_id = @p;");
        P(c, "@f", SqlDbType.BigInt, fnfId);
        P(c, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(c, MapFnf, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<(long FnfId, string Version)> InsertFnfAsync(NewFnf fnf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fnf);
        var f = fnf.Figures;
        var c = Cmd("""
            INSERT hr.fnf(property_id, staff_id, last_day, pay_month, salary_included, line_json, rates_json, advance_outstanding_paise, adjustments_json,
                          additions_paise, earnings_paise, recoveries_paise, net_paise, status, prepared_by, prepared_utc)
            OUTPUT INSERTED.fnf_id, INSERTED.row_ver
            VALUES (@p, @s, @ld, @m, @si, @lj, @rj, @ao, @aj, @add, @e, @rec, @n, 'Draft', @by, @at);
            """);
        P(c, "@p", SqlDbType.Int, fnf.PropertyId);
        P(c, "@s", SqlDbType.BigInt, fnf.StaffId);
        P(c, "@ld", SqlDbType.Date, f.LastDay.ToDateTime(TimeOnly.MinValue));
        P(c, "@m", SqlDbType.Date, f.Month.First.ToDateTime(TimeOnly.MinValue));
        P(c, "@si", SqlDbType.Bit, f.SalaryIncluded);
        P(c, "@lj", SqlDbType.NVarChar, 4000, f.Line is null ? null : PayrollJson.Write(f.Line));
        P(c, "@rj", SqlDbType.NVarChar, 2000, PayrollJson.Write(f.Rates));
        P(c, "@ao", SqlDbType.BigInt, f.AdvanceOutstanding);
        P(c, "@aj", SqlDbType.NVarChar, 2000, PayrollJson.Write(fnf.Adjustments.Select(a => new AdjustmentJson(a.Label, a.Addition, a.AmountPaise)).ToList()));
        P(c, "@add", SqlDbType.BigInt, f.Additions);
        P(c, "@e", SqlDbType.BigInt, f.Earnings);
        P(c, "@rec", SqlDbType.BigInt, f.Recoveries);
        P(c, "@n", SqlDbType.BigInt, f.Net);
        P(c, "@by", SqlDbType.Int, fnf.By);
        P(c, "@at", SqlDbType.DateTime2, fnf.Utc);
        return (await ReadAsync(c, r => (r.GetInt64(0), V(r, 1)), ct).ConfigureAwait(false))[0];
    }

    public async Task<string> DecideFnfAsync(long fnfId, string expectedVersion, string status, int by, DateTime utc, CancellationToken ct)
    {
        var c = Cmd("""
            UPDATE hr.fnf SET status = @st, decided_by = @by, decided_utc = @at OUTPUT INSERTED.row_ver WHERE fnf_id = @f AND row_ver = @v AND status = 'Draft';
            """);
        P(c, "@f", SqlDbType.BigInt, fnfId);
        P(c, "@st", SqlDbType.VarChar, 9, status);
        P(c, "@by", SqlDbType.Int, by);
        P(c, "@at", SqlDbType.DateTime2, utc);
        P(c, "@v", SqlDbType.Binary, 8, SqlValues.RowVersionFromWire(expectedVersion));
        var v = await ScalarAsync(c, ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The settlement");
        return SqlValues.RowVersionToWire(v!);
    }
}
