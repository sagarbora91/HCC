using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.FrontDeskMasters;
using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.FrontDeskMasters;

/// <summary>Tables of the four MA-5 lists (migration 2850) and the shared SQL (rows, usage, issued documents).</summary>
internal static class FdmSql
{
    public enum Kind { Str, NullableStr, Int, Bit, Date, JsonList }

    public sealed record Col(string Field, string Column, SqlDbType Type, int Size, Kind Kind);

    public sealed record Table(string Name, IReadOnlyList<Col> Cols, string What)
    {
        public string Select => "t.row_id, t.code, t.name, t.sort_order, t.active, t.retired_on, t.updated_utc, t.row_ver" + string.Concat(Cols.Select(c => ", t." + c.Column));
    }

    private static readonly Dictionary<string, Table> Tables = new(StringComparer.Ordinal)
    {
        [FrontDeskMasterIds.BookingSources] = new("mst.booking_source",
            [new("segment", "segment", SqlDbType.VarChar, 12, Kind.Str), new("channel", "channel", SqlDbType.NVarChar, 60, Kind.NullableStr), new("commissionBps", "commission_bps", SqlDbType.Int, 0, Kind.Int)],
            "This booking source"),
        [FrontDeskMasterIds.IdTypes] = new("mst.id_type",
            [new("accepted", "accepted", SqlDbType.Bit, 0, Kind.Bit), new("needsPhoto", "needs_photo", SqlDbType.Bit, 0, Kind.Bit), new("foreignerOnly", "foreigner_only", SqlDbType.Bit, 0, Kind.Bit)],
            "This ID type"),
        [FrontDeskMasterIds.NumberSeries] = new("bill.number_series",
            [new("document", "document", SqlDbType.Char, 3, Kind.Str), new("prefix", "prefix", SqlDbType.VarChar, 8, Kind.Str), new("startNo", "start_no", SqlDbType.Int, 0, Kind.Int),
             new("padding", "padding", SqlDbType.Int, 0, Kind.Int), new("reset", "reset_rule", SqlDbType.VarChar, 5, Kind.Str), new("activeFrom", "active_from", SqlDbType.Date, 0, Kind.Date)],
            "This numbering series"),
        [FrontDeskMasterIds.Policies] = new("inv.policy",
            [new("freeCancelHours", "free_cancel_hours", SqlDbType.Int, 0, Kind.Int), new("cancelChargeBps", "cancel_charge_bps", SqlDbType.Int, 0, Kind.Int),
             new("noShowChargeBps", "no_show_charge_bps", SqlDbType.Int, 0, Kind.Int), new("depositBps", "deposit_bps", SqlDbType.Int, 0, Kind.Int),
             new("sources", "sources", SqlDbType.NVarChar, 1000, Kind.JsonList)],
            "This policy"),
    };

    public static Table Of(string master) => Tables.TryGetValue(master, out var t) ? t : throw HccException.Validation("Unknown list.");

    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Front-desk master stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static FdmRecord Read(SqlDataReader rd, Table t)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < t.Cols.Count; i++)
        {
            var c = t.Cols[i];
            var o = 8 + i;
            fields[c.Field] = rd.IsDBNull(o) ? null : c.Kind switch
            {
                Kind.Int => rd.GetInt32(o),
                Kind.Bit => rd.GetBoolean(o),
                Kind.Date => DateOnly.FromDateTime(rd.GetDateTime(o)),
                Kind.JsonList => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(rd.GetString(o)) ?? []),
                _ => rd.GetString(o).TrimEnd(),
            };
        }

        return new FdmRecord(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)),
            fields, DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), (byte[])rd[7]);
    }

    public static void Fields(SqlCommand cmd, Table t, IReadOnlyDictionary<string, object?> fields)
    {
        for (var i = 0; i < t.Cols.Count; i++)
        {
            var c = t.Cols[i];
            var v = fields.GetValueOrDefault(c.Field);
            object? value = v switch
            {
                null => null,
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                IEnumerable<string> l when c.Kind == Kind.JsonList => JsonSerializer.Serialize(l.ToList()),
                _ => v,
            };
            if (c.Kind == Kind.JsonList && value is null) value = "[]";
            cmd.P("@f" + i.ToString(CultureInfo.InvariantCulture), c.Type, value, c.Size);
        }
    }

    public static async Task<List<FdmRecord>> ListAsync(SqlCommand cmd, Table t, CancellationToken ct)
    {
        var list = new List<FdmRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(rd, t));
        return list;
    }

    /// <summary>Issued documents of a kind between two dates (INV / BOS invoices by invoice date, CRN credit notes by note date).</summary>
    public static async Task<DocumentActivity> DocumentsAsync(Func<string, SqlCommand> command, int propertyId, string document, DateOnly? from, DateOnly? toExclusive, CancellationToken ct)
    {
        var (table, date, filter) = document switch
        {
            "INV" or "BOS" => ("ivc.invoice", "invoice_date", " AND doc_type = @t"),
            "CRN" => ("ivc.credit_note", "note_date", string.Empty),
            _ => (null, null, null),
        };
        if (table is null) return DocumentActivity.None;
        await using var cmd = command($"""
            SELECT COUNT(*), MAX({date}) FROM {table} WHERE property_id = @p{filter} AND (@from IS NULL OR {date} >= @from) AND (@to IS NULL OR {date} < @to);
            SELECT DISTINCT fy_code FROM {table} WHERE property_id = @p{filter} AND (@from IS NULL OR {date} >= @from) AND (@to IS NULL OR {date} < @to);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@t", SqlDbType.Char, document, 3);
        cmd.P("@from", SqlDbType.Date, from?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@to", SqlDbType.Date, toExclusive?.ToDateTime(TimeOnly.MinValue));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var count = rd.GetInt32(0);
        DateOnly? last = rd.IsDBNull(1) ? null : DateOnly.FromDateTime(rd.GetDateTime(1));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var fys = new List<string>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) fys.Add(rd.GetString(0));
        return new DocumentActivity(count, last, fys);
    }

    public static async Task<DateOnly> BusinessDateAsync(Func<string, SqlCommand> command, int propertyId, CancellationToken ct)
    {
        await using var cmd = command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    /// <summary>Records using a row: reservations with the source (its name, its OTA channel text, its code) + policies naming it; guest
    /// profiles with the ID type; documents issued under a series (+1 while it is the series in force); the default policy counts 1.</summary>
    public static async Task<int> UsageAsync(Func<string, SqlCommand> command, int propertyId, string master, FdmRecord row, IReadOnlyList<FdmRecord> list, CancellationToken ct)
    {
        switch (master)
        {
            case FrontDeskMasterIds.BookingSources:
            {
                await using var cmd = command("""
                    SELECT (SELECT COUNT(*) FROM inv.reservation WHERE property_id = @p AND (source = @name OR source = @code OR (@chan IS NOT NULL AND source = CONCAT(N'OTA · ', @chan))))
                         + (SELECT COUNT(*) FROM inv.policy WHERE property_id = @p AND sources LIKE @like);
                    """);
                cmd.P("@p", SqlDbType.Int, propertyId);
                cmd.P("@name", SqlDbType.NVarChar, row.Name, 60);
                cmd.P("@code", SqlDbType.NVarChar, row.Code, 20);
                cmd.P("@chan", SqlDbType.NVarChar, row.Get<string>("channel"), 60);
                cmd.P("@like", SqlDbType.NVarChar, "%\"" + row.Code + "\"%", 40);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            case FrontDeskMasterIds.IdTypes:
            {
                await using var cmd = command("SELECT COUNT(*) FROM gst.guest WHERE property_id = @p AND id_type = @name;");
                cmd.P("@p", SqlDbType.Int, propertyId);
                cmd.P("@name", SqlDbType.NVarChar, row.Name, 60);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            case FrontDeskMasterIds.NumberSeries:
            {
                var doc = row.Get<string>("document");
                var from = row.Get<DateOnly>("activeFrom");
                DateOnly? next = list.Where(r => r.RowId != row.RowId && r.Get<string>("document") == doc && r.Get<DateOnly>("activeFrom") > from)
                    .Select(r => (DateOnly?)r.Get<DateOnly>("activeFrom")).Min();
                var issued = (await DocumentsAsync(command, propertyId, doc, from, next, ct).ConfigureAwait(false)).Count;
                var today = await BusinessDateAsync(command, propertyId, ct).ConfigureAwait(false);
                return issued + (NumberSeriesSource.InForce(list, doc, today)?.RowId == row.RowId ? 1 : 0);
            }
            default:
                return row.Active && (row.Get<IReadOnlyList<string>>("sources") ?? []).Count == 0 ? 1 : 0;
        }
    }
}

/// <summary>SQL adapter of <see cref="IFrontDeskMastersStore"/> (inside the command transaction).</summary>
public sealed class SqlFrontDeskMastersStore : IFrontDeskMastersStore
{
    private static Func<string, SqlCommand> Cmd(ICommandTransaction tx) => FdmSql.Sql(tx).Command;

    public async Task<IReadOnlyList<FdmRecord>> LockAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default)
    {
        var t = FdmSql.Of(master);
        await using var cmd = Cmd(tx)($"SELECT {t.Select} FROM {t.Name} t WITH (UPDLOCK, HOLDLOCK) WHERE t.property_id = @p ORDER BY t.sort_order, t.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await FdmSql.ListAsync(cmd, t, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FdmRecord>> ReadAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default)
    {
        var t = FdmSql.Of(master);
        await using var cmd = Cmd(tx)($"SELECT {t.Select} FROM {t.Name} t WHERE t.property_id = @p ORDER BY t.sort_order, t.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await FdmSql.ListAsync(cmd, t, ct).ConfigureAwait(false);
    }

    public async Task<int> InsertAsync(ICommandTransaction tx, int propertyId, string master, string code, string name, int sortOrder, IReadOnlyDictionary<string, object?> fields,
        int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var t = FdmSql.Of(master);
        var cols = string.Concat(t.Cols.Select(c => ", " + c.Column));
        var vals = string.Concat(t.Cols.Select((_, i) => ", @f" + i.ToString(CultureInfo.InvariantCulture)));
        await using var cmd = Cmd(tx)($"INSERT {t.Name}(property_id, code, name, sort_order, active, updated_by, updated_utc{cols}) OUTPUT INSERTED.row_id VALUES (@p, @c, @n, @s, 1, @u, @utc{vals});");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        FdmSql.Fields(cmd, t, fields);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, string code, string name, int sortOrder, IReadOnlyDictionary<string, object?> fields,
        int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var t = FdmSql.Of(master);
        var sets = string.Concat(t.Cols.Select((c, i) => $", {c.Column} = @f{i.ToString(CultureInfo.InvariantCulture)}"));
        await using var cmd = Cmd(tx)($"UPDATE {t.Name} SET code = @c, name = @n, sort_order = @s, updated_by = @u, updated_utc = @utc{sets} WHERE row_id = @r AND row_ver = @v;");
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@r", SqlDbType.Int, rowId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        FdmSql.Fields(cmd, t, fields);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), t.What);
    }

    public async Task SetActiveAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = FdmSql.Of(master);
        await using var cmd = Cmd(tx)($"UPDATE {t.Name} SET active = @a, retired_on = @ret, updated_by = @u, updated_utc = @utc WHERE row_id = @r AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@ret", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@r", SqlDbType.Int, rowId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), t.What);
    }

    public async Task SetSortAsync(ICommandTransaction tx, string master, int rowId, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = FdmSql.Of(master);
        await using var cmd = Cmd(tx)($"UPDATE {t.Name} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE row_id = @r;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@r", SqlDbType.Int, rowId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, CancellationToken ct = default)
    {
        var t = FdmSql.Of(master);
        await using var cmd = Cmd(tx)($"DELETE {t.Name} WHERE row_id = @r AND row_ver = @v;");
        cmd.P("@r", SqlDbType.Int, rowId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), t.What);
    }

    public Task<int> UsageAsync(ICommandTransaction tx, int propertyId, string master, FdmRecord row, IReadOnlyList<FdmRecord> list, CancellationToken ct = default) =>
        FdmSql.UsageAsync(Cmd(tx), propertyId, master, row, list, ct);

    public Task<DocumentActivity> DocumentsAsync(ICommandTransaction tx, int propertyId, string document, DateOnly? from = null, DateOnly? toExclusive = null, CancellationToken ct = default) =>
        FdmSql.DocumentsAsync(Cmd(tx), propertyId, document, from, toExclusive, ct);
}

/// <summary>Read side of the four lists (own connection per call).</summary>
public sealed class SqlFrontDeskMastersReadStore(SqlConnectionFactory connections) : IFrontDeskMastersReadStore
{
    private async Task<T> WithAsync<T>(Func<Func<string, SqlCommand>, Task<T>> body, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await body(sql => connections.Command(sql, conn)).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FdmRecord>> ListAsync(int propertyId, string master, CancellationToken ct = default) => WithAsync<IReadOnlyList<FdmRecord>>(async cmdOf =>
    {
        var t = FdmSql.Of(master);
        await using var cmd = cmdOf($"SELECT {t.Select} FROM {t.Name} t WHERE t.property_id = @p ORDER BY t.sort_order, t.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await FdmSql.ListAsync(cmd, t, ct).ConfigureAwait(false);
    }, ct);

    public Task<int> UsageAsync(int propertyId, string master, FdmRecord row, IReadOnlyList<FdmRecord> list, CancellationToken ct = default) =>
        WithAsync(cmdOf => FdmSql.UsageAsync(cmdOf, propertyId, master, row, list, ct), ct);

    public Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default) => WithAsync(cmdOf => FdmSql.BusinessDateAsync(cmdOf, propertyId, ct), ct);

    public Task<(string Prefix, int LastSeq)?> FySeriesAsync(int propertyId, string fyCode, string document, CancellationToken ct = default) => WithAsync<(string, int)?>(async cmdOf =>
    {
        await using var cmd = cmdOf("SELECT prefix, last_seq FROM ivc.series WHERE property_id = @p AND fy_code = @fy AND doc_type = @t;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@fy", SqlDbType.Char, fyCode, 4);
        cmd.P("@t", SqlDbType.Char, document, 3);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetString(0), rd.GetInt32(1)) : null;
    }, ct);
}
