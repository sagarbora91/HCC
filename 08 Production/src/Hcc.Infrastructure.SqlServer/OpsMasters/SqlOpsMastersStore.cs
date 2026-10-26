using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.OpsMasters;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.OpsMasters;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.OpsMasters;

/// <summary>One column of a master table beyond the common ones: prototype field key, column, type, size.</summary>
internal sealed record OpsCol(string Key, string Column, SqlDbType Type, int Size = 0);

/// <summary>Table map of one master (MA-7, migration 2870). <c>Usage</c> = scalar SQL (alias <c>r</c>) counting the records that use the row.</summary>
internal sealed record OpsMap(string Table, string IdColumn, OpsCol[] Cols, string Usage)
{
    private static string FloorOf(string alias) =>
        $"IIF({alias}.room_no NOT LIKE '%[^0-9]%' AND LEN({alias}.room_no) >= 3, IIF(CAST({alias}.room_no AS BIGINT) / 100 < 1, 1, CAST({alias}.room_no AS BIGINT) / 100), 1)";

    public static readonly IReadOnlyDictionary<string, OpsMap> ByMaster = new Dictionary<string, OpsMap>
    {
        [OpsMasterIds.HkTaskTypes] = new("ops.hk_task_type", "task_type_id",
            [new("appliesTo", "applies_to", SqlDbType.VarChar, 80), new("standardMinutes", "standard_minutes", SqlDbType.SmallInt), new("needsInspection", "needs_inspection", SqlDbType.Bit),
             new("checklist", "checklist_json", SqlDbType.NVarChar, 1000)],
            """
            (SELECT COUNT(*) FROM ops.hk_task t WHERE t.property_id = r.property_id AND (
               (t.kind <> 'repair' AND EXISTS (SELECT 1 FROM STRING_SPLIT(r.applies_to, ',') x WHERE x.value = t.kind))
               OR (t.status = 'Inspection' AND EXISTS (SELECT 1 FROM STRING_SPLIT(r.applies_to, ',') y WHERE y.value = 'inspection'))))
            """),
        [OpsMasterIds.HkZones] = new("ops.hk_zone", "zone_id",
            [new("floors", "floor_codes", SqlDbType.VarChar, 200), new("rooms", "room_nos", SqlDbType.VarChar, 400), new("defaultAttendant", "default_attendant_id", SqlDbType.Int)],
            $"""
            (SELECT COUNT(*) FROM ops.hk_task t JOIN inv.room rm ON rm.room_id = t.room_id
              WHERE t.property_id = r.property_id AND t.status <> 'Done' AND t.kind <> 'repair'
                AND (EXISTS (SELECT 1 FROM STRING_SPLIT(r.room_nos, ',') x WHERE x.value = rm.room_no)
                  OR EXISTS (SELECT 1 FROM STRING_SPLIT(r.floor_codes, ',') f WHERE f.value = CONCAT('F', {FloorOf("rm")}))))
            """),
        [OpsMasterIds.ComplaintCategories] = new("gsv.complaint_category", "category_id",
            [new("department", "department", SqlDbType.VarChar, 12), new("slaHours", "sla_hours", SqlDbType.SmallInt)],
            "(SELECT COUNT(*) FROM ops.guest_complaint c WHERE c.property_id = r.property_id AND c.category_code = r.code)"),
        [OpsMasterIds.LostFoundCategories] = new("gsv.lostfound_category", "category_id",
            [new("department", "department", SqlDbType.VarChar, 12), new("slaHours", "sla_hours", SqlDbType.SmallInt), new("valuable", "valuable", SqlDbType.Bit)],
            "(SELECT COUNT(*) FROM ops.lost_item i WHERE i.property_id = r.property_id AND i.category_code = r.code)"),
    };

    public static OpsMap Of(OpsMasterDef def) => ByMaster[def.Master];

    /// <summary>Common columns then the master's own, all with alias r.</summary>
    public string Select => $"r.{IdColumn}, r.code, r.name, r.sort_order, r.active, r.retired_on, r.updated_utc, r.row_ver" + string.Concat(Cols.Select(c => ", r." + c.Column));

    public OpsRow Read(SqlDataReader rd)
    {
        var fields = new Dictionary<string, string?>();
        for (var i = 0; i < Cols.Length; i++) fields[Cols[i].Key] = Text(rd, 8 + i);
        return new OpsRow(Convert.ToInt64(rd.GetValue(0), CultureInfo.InvariantCulture), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4),
            rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)), DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), (byte[])rd[7], fields);
    }

    private static string? Text(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetValue(i) switch
    {
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var v => Convert.ToString(v, CultureInfo.InvariantCulture),
    };

    /// <summary>Canonical text → parameter value of the column type (empty / null → NULL).</summary>
    public static object Value(OpsCol c, string? text)
    {
        if (text is null || (text.Length == 0 && c.Type is SqlDbType.Int or SqlDbType.SmallInt)) return DBNull.Value;
        return c.Type switch
        {
            SqlDbType.Bit => text == "1",
            SqlDbType.SmallInt => short.Parse(text, CultureInfo.InvariantCulture),
            SqlDbType.Int => int.Parse(text, CultureInfo.InvariantCulture),
            _ => text,
        };
    }
}

/// <summary>SQL adapter of <see cref="IOpsMastersStore"/> over the four master tables (one generic implementation driven by <see cref="OpsMap"/>).</summary>
public sealed class SqlOpsMastersStore : IOpsMastersStore
{
    public async Task<IReadOnlyList<OpsRow>> LockAllAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"SELECT {m.Select} FROM {m.Table} r WITH (UPDLOCK, HOLDLOCK) WHERE r.property_id = @p ORDER BY r.sort_order, r.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<OpsRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(m.Read(rd));
        return list;
    }

    public async Task<OpsRow?> LockOneAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, long id, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"SELECT {m.Select} FROM {m.Table} r WITH (UPDLOCK, ROWLOCK) WHERE r.property_id = @p AND r.{m.IdColumn} = @id;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@id", SqlDbType.BigInt, id);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? m.Read(rd) : null;
    }

    private static void Own(SqlCommand cmd, OpsMap m, IReadOnlyDictionary<string, string?> fields)
    {
        for (var i = 0; i < m.Cols.Length; i++)
        {
            var c = m.Cols[i];
            var p = c.Size > 0 ? cmd.Parameters.Add("@f" + i, c.Type, c.Size) : cmd.Parameters.Add("@f" + i, c.Type);
            p.Value = OpsMap.Value(c, fields.GetValueOrDefault(c.Key));
        }
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, string code, string name, int sortOrder, IReadOnlyDictionary<string, string?> fields, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"""
            INSERT {m.Table}(property_id, code, name, sort_order, active, updated_by, updated_utc{string.Concat(m.Cols.Select(c => ", " + c.Column))})
            OUTPUT INSERTED.{m.IdColumn} VALUES (@p, @c, @n, @s, 1, @u, @utc{string.Concat(m.Cols.Select((_, i) => ", @f" + i))});
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        Own(cmd, m, fields);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, string code, string name, int sortOrder, IReadOnlyDictionary<string, string?> fields, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"""
            UPDATE {m.Table} SET code = @c, name = @n, sort_order = @s, updated_by = @u, updated_utc = @utc{string.Concat(m.Cols.Select((c, i) => $", {c.Column} = @f{i}"))}
            WHERE {m.IdColumn} = @id AND row_ver = @v;
            """);
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.BigInt, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        Own(cmd, m, fields);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + def.Singular);
    }

    public async Task SetActiveAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"UPDATE {m.Table} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {m.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.BigInt, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + def.Singular);
    }

    public async Task SetSortAsync(ICommandTransaction tx, OpsMasterDef def, long id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"UPDATE {m.Table} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE {m.IdColumn} = @id;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.BigInt, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var cmd = MstSql.Sql(tx).Command($"DELETE {m.Table} WHERE {m.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@id", SqlDbType.BigInt, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This " + def.Singular);
    }

    public async Task<IReadOnlySet<string>> FloorCodesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default) =>
        await StringsAsync(tx, "SELECT code FROM mst.floor WHERE property_id = @p;", propertyId, ct).ConfigureAwait(false);

    public async Task<IReadOnlySet<string>> RoomNosAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default) =>
        await StringsAsync(tx, "SELECT room_no FROM inv.room WHERE property_id = @p;", propertyId, ct).ConfigureAwait(false);

    private static async Task<IReadOnlySet<string>> StringsAsync(ICommandTransaction tx, string sql, int propertyId, CancellationToken ct)
    {
        await using var cmd = MstSql.Sql(tx).Command(sql);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetString(0));
        return set;
    }
}

/// <summary>Read side of the four lists (own connection): rows in sort order with the number of records that use each.</summary>
public sealed class SqlOpsMastersReadStore(SqlConnectionFactory connections) : IOpsMastersReadStore
{
    public async Task<IReadOnlyList<(OpsRow Row, int Used)>> ListAsync(OpsMasterDef def, int propertyId, CancellationToken ct = default)
    {
        var m = OpsMap.Of(def);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {m.Select}, {m.Usage} FROM {m.Table} r WHERE r.property_id = @p ORDER BY r.sort_order, r.code;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<(OpsRow, int)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((m.Read(rd), rd.GetInt32(8 + m.Cols.Length)));
        return list;
    }
}

/// <summary>In-use provider of the four lists (runs inside the command transaction; the same expressions the list shows as "used").</summary>
public sealed class SqlOpsMasterUsage : IMasterUsage
{
    public IReadOnlyCollection<string> Masters { get; } = [OpsMasterIds.HkTaskTypes, OpsMasterIds.HkZones, OpsMasterIds.ComplaintCategories, OpsMasterIds.LostFoundCategories];

    public async Task<MasterUsageCount> CountAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct = default)
    {
        var m = OpsMap.ByMaster[master];
        await using var cmd = MstSql.Sql(tx).Command($"SELECT {m.Usage} FROM {m.Table} r WHERE r.property_id = @p AND r.{m.IdColumn} = @id;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@id", SqlDbType.BigInt, rowId);
        var n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0, CultureInfo.InvariantCulture);
        return n == 0 ? MasterUsageCount.None : new MasterUsageCount(n, [OpsMasterDefs.Of(master).UsedWhat]);
    }
}

/// <summary>Task-type lookup of the housekeeping commands: first active type covering the kind, else a retired one, else the built-in values.</summary>
public sealed class SqlHkTaskTypeSource : IHkTaskTypeSource
{
    public async Task<HkTypeRule> ForKindAsync(ICommandTransaction tx, int propertyId, string kind, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("""
            SELECT TOP 1 (SELECT COUNT(*) FROM OPENJSON(t.checklist_json)), t.needs_inspection, t.standard_minutes
              FROM ops.hk_task_type t
             WHERE t.property_id = @p AND EXISTS (SELECT 1 FROM STRING_SPLIT(t.applies_to, ',') x WHERE x.value = @k)
             ORDER BY t.active DESC, t.sort_order, t.code;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, kind, 10);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var fb = OpsMastersRules.Fallback(kind);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return new HkTypeRule(fb.Items, fb.NeedsInspection, fb.Minutes);
        var items = rd.GetInt32(0);
        return new HkTypeRule(items < 1 ? fb.Items : items, rd.GetBoolean(1), rd.GetInt16(2));
    }
}

/// <summary>Category lookup of the guest-services commands (an active row by code or name) and the report's category names.</summary>
public sealed class SqlGsCategorySource(SqlConnectionFactory connections) : IGsCategorySource
{
    public Task<GsCategory?> ComplaintAsync(ICommandTransaction tx, int propertyId, string? codeOrName, CancellationToken ct = default) =>
        FindAsync(tx, "gsv.complaint_category", "CAST(0 AS BIT)", propertyId, codeOrName, ct);

    public Task<GsCategory?> LostFoundAsync(ICommandTransaction tx, int propertyId, string? codeOrName, CancellationToken ct = default) =>
        FindAsync(tx, "gsv.lostfound_category", "valuable", propertyId, codeOrName, ct);

    private static async Task<GsCategory?> FindAsync(ICommandTransaction tx, string table, string valuableExpr, int propertyId, string? input, CancellationToken ct)
    {
        var key = (input ?? string.Empty).Trim();
        if (key.Length == 0) return null;
        await using var cmd = MstSql.Sql(tx).Command($"""
            SELECT TOP 1 code, name, department, sla_hours, {valuableExpr} FROM {table}
             WHERE property_id = @p AND active = 1 AND (code = @i OR name = @i) ORDER BY CASE WHEN code = @i THEN 0 ELSE 1 END;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@i", SqlDbType.NVarChar, key, 60);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new GsCategory(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetInt16(3), rd.GetBoolean(4)) : null;
    }

    public async Task<IReadOnlyList<string>> ComplaintNamesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT name FROM gsv.complaint_category WHERE property_id = @p ORDER BY sort_order, code;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }
}

/// <summary>
/// Seed of the lists for a new property (first-time setup runs it in its transaction; migration 2871 holds the identical statement for
/// installs that already have properties): task types with the 3-item checklist, today's complaint and found-item categories. Idempotent.
/// </summary>
public static class OpsMastersSeed
{
    public const string Sql = """
        INSERT ops.hk_task_type(property_id, code, name, sort_order, active, applies_to, standard_minutes, needs_inspection, checklist_json, updated_by, updated_utc)
        SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.applies_to, v.minutes, v.needs_inspection, v.checklist, 0, SYSUTCDATETIME()
          FROM inv.property pr CROSS JOIN (VALUES
            ('DEP',  N'Departure clean',  10, 'checkout,move', 40, 1, N'["Linen & bathroom","Surfaces & amenities","Final room check"]'),
            ('STAY', N'Stayover service', 20, 'stayover',      20, 0, N'["Linen & bathroom","Surfaces & amenities","Final room check"]'),
            ('DEEP', N'Deep clean',       30, 'deep',          90, 1, N'["Mattress, linen and curtains","Bathroom descale and grout","Furniture, vents and fittings","Final room check"]'),
            ('INSP', N'Inspection',       40, 'inspection',    10, 0, N'["Bathroom and linen","Surfaces and amenities","Fittings and lights"]')
          ) v(code, name, sort_order, applies_to, minutes, needs_inspection, checklist)
         WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM ops.hk_task_type t WHERE t.property_id = pr.property_id);

        INSERT gsv.complaint_category(property_id, code, name, sort_order, active, department, sla_hours, updated_by, updated_utc)
        SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.department, v.sla, 0, SYSUTCDATETIME()
          FROM inv.property pr CROSS JOIN (VALUES
            ('CLEAN',   N'Room cleanliness', 10, 'Housekeeping', 4),
            ('MAINT',   N'Maintenance',      20, 'Maintenance',  8),
            ('NOISE',   N'Noise',            30, 'Front desk',   2),
            ('STAFF',   N'Staff behaviour',  40, 'Front desk',   24),
            ('FNB',     N'Food & beverage',  50, 'Front desk',   8),
            ('BILLING', N'Billing',          60, 'Front desk',   24),
            ('AMEN',    N'Amenities',        70, 'Housekeeping', 8),
            ('OTHER',   N'Other',            80, 'Front desk',   24)
          ) v(code, name, sort_order, department, sla)
         WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM gsv.complaint_category t WHERE t.property_id = pr.property_id);

        INSERT gsv.lostfound_category(property_id, code, name, sort_order, active, department, sla_hours, valuable, updated_by, updated_utc)
        SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.department, v.sla, v.valuable, 0, SYSUTCDATETIME()
          FROM inv.property pr CROSS JOIN (VALUES
            ('JEWEL', N'Jewellery',     10, 'Front desk',   4,  1),
            ('ELEC',  N'Electronics',   20, 'Front desk',   4,  1),
            ('CASH',  N'Cash / wallet', 30, 'Front desk',   4,  1),
            ('DOCS',  N'Documents',     40, 'Front desk',   4,  1),
            ('CLOTH', N'Clothing',      50, 'Housekeeping', 24, 0),
            ('CARE',  N'Personal care', 60, 'Housekeeping', 24, 0),
            ('OTHER', N'Other',         70, 'Housekeeping', 48, 0)
          ) v(code, name, sort_order, department, sla, valuable)
         WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM gsv.lostfound_category t WHERE t.property_id = pr.property_id);
        """;
}
