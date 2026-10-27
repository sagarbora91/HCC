using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Contracts;
using Hcc.Application.PeopleLists;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.Errors;
using Hcc.Domain.PeopleLists;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.PeopleLists;

/// <summary>One list's table: id column, own columns (key, column, type, size) and the "used by" scalar (alias x = the list's row).</summary>
internal sealed record ListTable(string Table, string IdColumn, IReadOnlyList<ListField> Fields, string UsedSql)
{
    public string Select => $"x.{IdColumn}, x.code, x.name, x.sort_order, x.active, x.retired_on, x.updated_utc, x.row_ver" + string.Concat(Fields.Select(f => ", x." + f.Column));

    public static ListTable Of(PeopleList l) => l switch
    {
        PeopleList.Designations => Designations,
        PeopleList.ShiftPatterns => Shifts,
        PeopleList.LeaveTypes => Leaves,
        _ => Holidays,
    };

    public static readonly ListTable Designations = new("hr.designation", "designation_id",
        [new("dept", "dept_code", SqlDbType.VarChar, 10), new("grade", "grade", SqlDbType.NVarChar, 20), new("defaultPayRule", "default_pay_rule", SqlDbType.VarChar, 20)],
        "(SELECT COUNT(*) FROM hr.staff s WHERE s.property_id = x.property_id AND (s.designation_code = x.code OR (s.designation_code IS NULL AND s.designation = x.name)))");

    public static readonly ListTable Shifts = new("hr.shift_pattern", "shift_pattern_id",
        [new("start", "start_time", SqlDbType.Time, 0), new("end", "end_time", SqlDbType.Time, 0), new("breakMinutes", "break_minutes", SqlDbType.SmallInt, 0),
         new("night", "night", SqlDbType.Bit, 0), new("weeklyOffRule", "weekly_off_rule", SqlDbType.VarChar, 6)],
        "((SELECT COUNT(*) FROM hr.staff s WHERE s.property_id = x.property_id AND s.default_shift = x.code)"
        + " + (SELECT COUNT(*) FROM hr.roster r JOIN hr.staff s ON s.staff_id = r.staff_id JOIN inv.property p ON p.property_id = x.property_id"
        + " WHERE s.property_id = x.property_id AND r.shift_code = x.code AND r.work_date >= p.business_date))");

    public static readonly ListTable Leaves = new("hr.leave_type", "leave_type_id",
        [new("paid", "paid", SqlDbType.Bit, 0), new("yearlyQuota", "yearly_quota", SqlDbType.Decimal, 0)],
        "(SELECT COUNT(*) FROM hr.leave_request l WHERE l.property_id = x.property_id AND l.leave_type = x.code)");

    public static readonly ListTable Holidays = new("hr.holiday", "holiday_id",
        [new("kind", "kind", SqlDbType.VarChar, 10)],
        "(SELECT COUNT(*) FROM hr.pay_run r WHERE r.property_id = x.property_id AND r.status = 'Locked' AND r.pay_month = DATEFROMPARTS(YEAR(CAST(x.code AS DATE)), MONTH(CAST(x.code AS DATE)), 1))");
}

internal sealed record ListField(string Key, string Column, SqlDbType Type, int Size);

internal static class PlSql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("People lists SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal)
        {
            p.Precision = 9;
            p.Scale = 2;
        }

        p.Value = value ?? DBNull.Value;
    }

    /// <summary>The wire value of a field (string / int / bool / null) as the column stores it.</summary>
    public static object? ToDb(ListField f, object? v) => v switch
    {
        null => null,
        string s when f.Type == SqlDbType.Time => TimeSpan.ParseExact(s, "hh\\:mm", CultureInfo.InvariantCulture),
        string s => s,
        int i when f.Type == SqlDbType.TinyInt => (byte)i,
        int i when f.Type == SqlDbType.SmallInt => (short)i,
        int i when f.Type == SqlDbType.Decimal => (decimal)i,
        _ => v,
    };

    public static void FieldParams(SqlCommand cmd, ListTable t, IReadOnlyDictionary<string, object?> fields)
    {
        foreach (var f in t.Fields)
        {
            var v = ToDb(f, fields.TryGetValue(f.Key, out var raw) ? raw : null);
            cmd.P("@f_" + f.Key, f.Type, v, f.Type is SqlDbType.VarChar or SqlDbType.NVarChar ? f.Size : 0);
        }
    }

    public static ListRec Read(SqlDataReader rd, ListTable t)
    {
        var fields = new Dictionary<string, object?>();
        for (var i = 0; i < t.Fields.Count; i++)
        {
            var f = t.Fields[i];
            var o = 8 + i;
            fields[f.Key] = rd.IsDBNull(o) ? null : f.Type switch
            {
                SqlDbType.Time => PeopleListsRules.MinutesText(TimeOnly.FromTimeSpan((TimeSpan)rd.GetValue(o))),
                SqlDbType.TinyInt => (int)rd.GetByte(o),
                SqlDbType.SmallInt => (int)rd.GetInt16(o),
                SqlDbType.Bit => rd.GetBoolean(o),
                SqlDbType.Decimal => rd.GetDecimal(o),
                _ => rd.GetString(o),
            };
        }

        return new ListRec(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)),
            DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), (byte[])rd[7], fields);
    }

    /// <summary>First-use seed of one property (rows only when the property has never been seeded): shift patterns, leave types, designations from staff titles.</summary>
    public static async Task SeedAsync(Func<string, SqlCommand> make, int propertyId, CancellationToken ct)
    {
        await using (var chk = make("SELECT COUNT(*) FROM hr.people_list_seed WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;"))
        {
            chk.P("@p", SqlDbType.Int, propertyId);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0) return;
        }

        var cl = 12m;
        var sl = 7m;
        var el = 15m;
        await using (var pol = make("SELECT cl_days, sl_days, el_days FROM hr.leave_policy WHERE property_id = @p;"))
        {
            pol.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await pol.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                cl = rd.GetDecimal(0);
                sl = rd.GetDecimal(1);
                el = rd.GetDecimal(2);
            }
        }

        var staff = new List<(string Title, string Dept, bool Daily, string? Structure)>();
        await using (var q = make("SELECT designation, dept_code, pay_type, structure FROM hr.staff WHERE property_id = @p ORDER BY staff_id;"))
        {
            q.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) staff.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2) == "daily", rd.IsDBNull(3) ? null : rd.GetString(3)));
        }

        await InsertSeedAsync(make, PeopleList.ShiftPatterns, propertyId, PeopleListsRules.SeedShiftPatterns(), ct).ConfigureAwait(false);
        await InsertSeedAsync(make, PeopleList.LeaveTypes, propertyId, PeopleListsRules.SeedLeaveTypes(cl, sl, el), ct).ConfigureAwait(false);
        await InsertSeedAsync(make, PeopleList.Designations, propertyId, PeopleListsRules.SeedDesignations(staff), ct).ConfigureAwait(false);
        await using var mark = make("INSERT hr.people_list_seed(property_id, seeded_utc) VALUES (@p, SYSUTCDATETIME());");
        mark.P("@p", SqlDbType.Int, propertyId);
        await mark.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertSeedAsync(Func<string, SqlCommand> make, PeopleList list, int propertyId, IReadOnlyList<PeopleListsRules.SeedRow> rows, CancellationToken ct)
    {
        var t = ListTable.Of(list);
        foreach (var r in rows)
        {
            await using var cmd = make($"""
                INSERT {t.Table}(property_id, code, name, sort_order, active, updated_by, updated_utc{string.Concat(t.Fields.Select(f => ", " + f.Column))})
                VALUES (@p, @c, @n, @s, 1, 0, SYSUTCDATETIME(){string.Concat(t.Fields.Select(f => ", @f_" + f.Key))});
                """);
            cmd.P("@p", SqlDbType.Int, propertyId);
            cmd.P("@c", SqlDbType.VarChar, r.Code, 20);
            cmd.P("@n", SqlDbType.NVarChar, r.Name, 60);
            cmd.P("@s", SqlDbType.Int, r.SortOrder);
            FieldParams(cmd, t, r.Fields);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>SQL adapter of <see cref="IPeopleListsStore"/> (hr.designation, hr.shift_pattern, hr.leave_type, hr.holiday; migration 2860).</summary>
public sealed class SqlPeopleListsStore : IPeopleListsStore
{
    public Task EnsureSeededAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default) => PlSql.SeedAsync(PlSql.Sql(tx).Command, propertyId, ct);

    public async Task<IReadOnlyList<ListRec>> LockRowsAsync(ICommandTransaction tx, PeopleList list, int propertyId, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"SELECT {t.Select} FROM {t.Table} x WITH (UPDLOCK, HOLDLOCK) WHERE x.property_id = @p ORDER BY x.sort_order, x.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        var rows = new List<ListRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add(PlSql.Read(rd, t));
        return rows;
    }

    public async Task<int> InsertAsync(ICommandTransaction tx, PeopleList list, int propertyId, CleanListRow row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"""
            INSERT {t.Table}(property_id, code, name, sort_order, active, updated_by, updated_utc{string.Concat(t.Fields.Select(f => ", " + f.Column))})
            OUTPUT INSERTED.{t.IdColumn}
            VALUES (@p, @c, @n, @s, 1, @u, @utc{string.Concat(t.Fields.Select(f => ", @f_" + f.Key))});
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, row.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, row.Name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        PlSql.FieldParams(cmd, t, row.Fields);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, CleanListRow row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"""
            UPDATE {t.Table} SET code = @c, name = @n, sort_order = @s, updated_by = @u, updated_utc = @utc{string.Concat(t.Fields.Select(f => $", {f.Column} = @f_{f.Key}"))}
             WHERE {t.IdColumn} = @id AND row_ver = @v;
            """);
        cmd.P("@c", SqlDbType.VarChar, row.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, row.Name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        PlSql.FieldParams(cmd, t, row.Fields);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + PeopleListsRules.Singular(list));
    }

    public async Task SetActiveAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"UPDATE {t.Table} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {t.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + PeopleListsRules.Singular(list));
    }

    public async Task SetSortAsync(ICommandTransaction tx, PeopleList list, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"UPDATE {t.Table} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE {t.IdColumn} = @id;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"DELETE {t.Table} WHERE {t.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + PeopleListsRules.Singular(list));
    }

    public async Task<bool> DepartmentExistsAsync(ICommandTransaction tx, int propertyId, string dept, CancellationToken ct = default)
    {
        await using var cmd = PlSql.Sql(tx).Command("SELECT COUNT(*) FROM hr.department WHERE property_id = @p AND dept_code = @d;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@d", SqlDbType.VarChar, dept, 10);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<IReadOnlyList<string>> ActiveStaffOnShiftAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = PlSql.Sql(tx).Command("SELECT name FROM hr.staff WHERE property_id = @p AND active = 1 AND default_shift = @c ORDER BY staff_id;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, code, 3);
        var names = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) names.Add(rd.GetString(0));
        return names;
    }

    public async Task MirrorLeavePolicyAsync(ICommandTransaction tx, int propertyId, string code, decimal quota, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var column = code switch { "CL" => "cl_days", "SL" => "sl_days", "EL" => "el_days", _ => throw new ArgumentException("Only CL, SL and EL mirror the leave policy.", nameof(code)) };
        await using var cmd = PlSql.Sql(tx).Command($"""
            UPDATE hr.leave_policy SET {column} = @q, updated_by = @u, updated_utc = @utc WHERE property_id = @p;
            IF @@ROWCOUNT = 0 INSERT hr.leave_policy(property_id, cl_days, sl_days, el_days, default_cap, updated_by, updated_utc)
              VALUES (@p, IIF('{column}' = 'cl_days', @q, 12), IIF('{column}' = 'sl_days', @q, 7), IIF('{column}' = 'el_days', @q, 15), 3, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@q", SqlDbType.Decimal, quota);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = PlSql.Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<bool> PayMonthLockedAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = PlSql.Sql(tx).Command("SELECT COUNT(*) FROM hr.pay_run WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND pay_month = @m AND status = 'Locked';");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.Date, new DateTime(date.Year, date.Month, 1));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }
}

/// <summary>Read side of the People lists (own connection).</summary>
public sealed class SqlPeopleListsReadStore(SqlConnectionFactory connections) : IPeopleListsReadStore
{
    public async Task EnsureSeededAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var chk = connections.Command("SELECT COUNT(*) FROM hr.people_list_seed WHERE property_id = @p;", conn);
        chk.P("@p", SqlDbType.Int, propertyId);
        if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0) return;
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await PlSql.SeedAsync(sql => connections.Command(sql, conn, tx), propertyId, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(ListRec Rec, int Used)>> RowsAsync(PeopleList list, int propertyId, CancellationToken ct = default)
    {
        var t = ListTable.Of(list);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {t.Select}, {t.UsedSql} FROM {t.Table} x WHERE x.property_id = @p ORDER BY x.sort_order, x.code;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var rows = new List<(ListRec, int)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((PlSql.Read(rd, t), rd.GetInt32(8 + t.Fields.Count)));
        return rows;
    }
}

/// <summary>"In use" provider of the four People lists: how many records use a row (the same counts the read side shows).</summary>
public sealed class SqlPeopleListsUsage : IMasterUsage
{
    public IReadOnlyCollection<string> Masters { get; } = PeopleListIds.All.ToList();

    public async Task<MasterUsageCount> CountAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct = default)
    {
        var list = master switch
        {
            PeopleListIds.Designations => PeopleList.Designations,
            PeopleListIds.ShiftPatterns => PeopleList.ShiftPatterns,
            PeopleListIds.LeaveTypes => PeopleList.LeaveTypes,
            PeopleListIds.Holidays => PeopleList.Holidays,
            _ => throw new ArgumentException("Not a People list: " + master, nameof(master)),
        };
        var t = ListTable.Of(list);
        await using var cmd = PlSql.Sql(tx).Command($"SELECT {t.UsedSql} FROM {t.Table} x WHERE x.property_id = @p AND x.{t.IdColumn} = @id;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@id", SqlDbType.Int, (int)rowId);
        var n = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is { } o ? Convert.ToInt32(o, CultureInfo.InvariantCulture) : 0;
        var where = list switch
        {
            PeopleList.Designations => "Staff",
            PeopleList.ShiftPatterns => "Staff and roster",
            PeopleList.LeaveTypes => "Leave requests",
            _ => "Locked pay-runs",
        };
        return n == 0 ? MasterUsageCount.None : new MasterUsageCount(n, [where]);
    }
}
