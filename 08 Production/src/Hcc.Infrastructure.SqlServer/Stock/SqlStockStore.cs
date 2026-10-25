using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Stock;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Stock;

/// <summary>Shared row mapping of stk.item (+ par and stock rows) for the write and read stores.</summary>
internal static class StockSql
{
    public const string ItemColumns = "i.item_id, i.code, i.name, i.kind, i.unit, i.cost_paise, i.min_qty, i.reorder_qty, i.std_x100, i.laundry_rate_paise, i.supplier, i.row_ver";

    public static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public static long? LongOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    public static int? Int32OrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    /// <summary>Reads the current result set of item rows (columns per <see cref="ItemColumns"/>).</summary>
    public static async Task<List<ItemRow>> ReadItemRowsAsync(SqlDataReader rd, CancellationToken ct)
    {
        var rows = new List<ItemRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new ItemRow(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetInt64(5), rd.GetInt32(6), rd.GetInt32(7),
                Int32OrNull(rd, 8), LongOrNull(rd, 9), P3Sql.StrOrNull(rd, 10), (byte[])rd[11]));
        return rows;
    }

    public static async Task<Dictionary<int, Dictionary<string, int>>> ReadPairsAsync(SqlDataReader rd, CancellationToken ct)
    {
        var map = new Dictionary<int, Dictionary<string, int>>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt32(0);
            if (!map.TryGetValue(id, out var d)) map[id] = d = new Dictionary<string, int>(StringComparer.Ordinal);
            d[rd.GetString(1)] = Convert.ToInt32(rd.GetValue(2), System.Globalization.CultureInfo.InvariantCulture);
        }

        return map;
    }

    public static StockItemRecord ToRecord(ItemRow r, Dictionary<int, Dictionary<string, int>> par, Dictionary<int, Dictionary<string, int>> stock) =>
        new(r.Id, r.Code, r.Name, r.Kind, r.Unit, r.Cost, r.Min, r.Reorder, r.Std, r.Rate, r.Supplier,
            par.TryGetValue(r.Id, out var p) ? p : new Dictionary<string, int>(StringComparer.Ordinal),
            stock.TryGetValue(r.Id, out var s) ? s : new Dictionary<string, int>(StringComparer.Ordinal), r.Version);

    internal sealed record ItemRow(int Id, string Code, string Name, string Kind, string Unit, long Cost, int Min, int Reorder, int? Std, long? Rate, string? Supplier, byte[] Version);
}

/// <summary>SQL adapter of <see cref="IStockStore"/> (schema stk). Item rows are locked with UPDLOCK before quantities change.</summary>
public sealed class SqlStockStore : IStockStore
{
    private static SqlCommandTransaction Tx(ICommandTransaction tx) => P3Sql.Sql(tx);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<StockItemRecord?> LockItemAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        List<StockSql.ItemRow> rows;
        await using (var cmd = Tx(tx).Command($"SELECT {StockSql.ItemColumns} FROM stk.item i WITH (UPDLOCK, ROWLOCK) WHERE i.property_id = @p AND i.code = @c;"))
        {
            cmd.Add("@p", SqlDbType.Int, propertyId);
            cmd.Add("@c", SqlDbType.VarChar, code, 20);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            rows = await StockSql.ReadItemRowsAsync(rd, ct).ConfigureAwait(false);
        }

        if (rows.Count == 0) return null;
        var id = rows[0].Id;
        Dictionary<int, Dictionary<string, int>> par, stock;
        await using (var cmd = Tx(tx).Command("SELECT p.item_id, t.code, p.pieces FROM stk.item_par p JOIN inv.room_type t ON t.room_type_id = p.room_type_id WHERE p.item_id = @i;"))
        {
            cmd.Add("@i", SqlDbType.Int, id);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            par = await StockSql.ReadPairsAsync(rd, ct).ConfigureAwait(false);
        }

        await using (var cmd = Tx(tx).Command("SELECT item_id, loc, qty FROM stk.stock WHERE item_id = @i;"))
        {
            cmd.Add("@i", SqlDbType.Int, id);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            stock = await StockSql.ReadPairsAsync(rd, ct).ConfigureAwait(false);
        }

        return StockSql.ToRecord(rows[0], par, stock);
    }

    public async Task AdjustAsync(ICommandTransaction tx, int itemId, string loc, int delta, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("UPDATE stk.stock SET qty = qty + @d WHERE item_id = @i AND loc = @l;");
        cmd.Add("@d", SqlDbType.Int, delta);
        cmd.Add("@i", SqlDbType.Int, itemId);
        cmd.Add("@l", SqlDbType.VarChar, loc, 8);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) throw new HccException(Contracts.HccErrorCode.NOT_FOUND, "Stock location not found.");
    }

    public async Task SetCostAsync(ICommandTransaction tx, int itemId, long costPaise, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("UPDATE stk.item SET cost_paise = @c WHERE item_id = @i;");
        cmd.Add("@c", SqlDbType.BigInt, costPaise);
        cmd.Add("@i", SqlDbType.Int, itemId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertMoveAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, string kind, int itemId, int qty,
        string? from, string? to, string? reference, string? reason, int actorId, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            INSERT stk.move(property_id, business_date, at_utc, kind, item_id, qty, from_loc, to_loc, ref, reason, actor_id)
            OUTPUT INSERTED.move_id VALUES (@p, @bd, @utc, @k, @i, @q, @f, @t, @r, @reason, @u);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@bd", SqlDbType.Date, StockSql.D(businessDate));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@k", SqlDbType.VarChar, kind, 14);
        cmd.Add("@i", SqlDbType.Int, itemId);
        cmd.Add("@q", SqlDbType.Int, qty);
        cmd.Add("@f", SqlDbType.NVarChar, from, 120);
        cmd.Add("@t", SqlDbType.NVarChar, to, 40);
        cmd.Add("@r", SqlDbType.VarChar, reference, 40);
        cmd.Add("@reason", SqlDbType.NVarChar, reason, 200);
        cmd.Add("@u", SqlDbType.Int, actorId);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<(long GrnId, string Ref)> InsertGrnAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, string supplier, string? reference, int actorId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            DECLARE @id BIGINT;
            INSERT stk.grn(property_id, business_date, supplier, ref, created_by, created_utc) VALUES (@p, @bd, @s, ISNULL(@r, N''), @u, @utc);
            SET @id = SCOPE_IDENTITY();
            UPDATE stk.grn SET ref = N'GRN-' + CAST(@id AS NVARCHAR(20)) WHERE grn_id = @id AND ref = N'';
            SELECT @id, ref FROM stk.grn WHERE grn_id = @id;
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@bd", SqlDbType.Date, StockSql.D(businessDate));
        cmd.Add("@s", SqlDbType.NVarChar, supplier, 120);
        cmd.Add("@r", SqlDbType.NVarChar, reference, 60);
        cmd.Add("@u", SqlDbType.Int, actorId);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), rd.GetString(1));
    }

    public async Task InsertGrnLineAsync(ICommandTransaction tx, long grnId, int itemId, int qty, long costPaise, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("INSERT stk.grn_line(grn_id, item_id, qty, cost_paise) VALUES (@g, @i, @q, @c);");
        cmd.Add("@g", SqlDbType.BigInt, grnId);
        cmd.Add("@i", SqlDbType.Int, itemId);
        cmd.Add("@q", SqlDbType.Int, qty);
        cmd.Add("@c", SqlDbType.BigInt, costPaise);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertLossAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, int itemId, string loc, int qty, string reason, string? note,
        int actorId, long valuePaise, string? reference, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            INSERT stk.loss(property_id, business_date, item_id, loc, qty, reason, note, actor_id, value_paise, ref, created_utc)
            OUTPUT INSERTED.loss_id VALUES (@p, @bd, @i, @l, @q, @r, @n, @u, @v, @ref, @utc);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@bd", SqlDbType.Date, StockSql.D(businessDate));
        cmd.Add("@i", SqlDbType.Int, itemId);
        cmd.Add("@l", SqlDbType.VarChar, loc, 8);
        cmd.Add("@q", SqlDbType.Int, qty);
        cmd.Add("@r", SqlDbType.NVarChar, reason, 100);
        cmd.Add("@n", SqlDbType.NVarChar, note, 400);
        cmd.Add("@u", SqlDbType.Int, actorId);
        cmd.Add("@v", SqlDbType.BigInt, valuePaise);
        cmd.Add("@ref", SqlDbType.VarChar, reference, 40);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<StockSettingsRecord> SettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("SELECT par_sets, vendor_name, turnaround_days FROM stk.setting WHERE property_id = @p;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new StockSettingsRecord(rd.GetByte(0), P3Sql.StrOrNull(rd, 1), rd.GetByte(2)) : StockSettingsRecord.Default;
    }

    public async Task UpsertSettingsAsync(ICommandTransaction tx, int propertyId, StockSettingsRecord settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var cmd = Tx(tx).Command("""
            UPDATE stk.setting WITH (UPDLOCK, HOLDLOCK) SET par_sets = @s, vendor_name = @v, turnaround_days = @t WHERE property_id = @p;
            IF @@ROWCOUNT = 0 INSERT stk.setting(property_id, par_sets, vendor_name, turnaround_days) VALUES (@p, @s, @v, @t);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@s", SqlDbType.TinyInt, (byte)settings.ParSets);
        cmd.Add("@v", SqlDbType.NVarChar, settings.VendorName, 120);
        cmd.Add("@t", SqlDbType.TinyInt, (byte)settings.TurnaroundDays);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(long BatchId, byte[] Version)> InsertBatchAsync(ICommandTransaction tx, int propertyId, string vendor, DateOnly sentDate, DateTime utc, int actorId, string? note, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, actor_id, note)
            OUTPUT INSERTED.batch_id, INSERTED.row_ver VALUES (@p, @v, @d, @utc, 'Out', @u, @n);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@v", SqlDbType.NVarChar, vendor, 120);
        cmd.Add("@d", SqlDbType.Date, StockSql.D(sentDate));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@u", SqlDbType.Int, actorId);
        cmd.Add("@n", SqlDbType.NVarChar, note, 400);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), (byte[])rd[1]);
    }

    public async Task InsertBatchLineAsync(ICommandTransaction tx, long batchId, int itemId, int sent, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("INSERT stk.laundry_line(batch_id, item_id, sent) VALUES (@b, @i, @s);");
        cmd.Add("@b", SqlDbType.BigInt, batchId);
        cmd.Add("@i", SqlDbType.Int, itemId);
        cmd.Add("@s", SqlDbType.Int, sent);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<LaundryBatchRecord?> LockBatchAsync(ICommandTransaction tx, int propertyId, long batchId, CancellationToken ct = default)
    {
        (long Id, string Vendor, DateOnly Sent, string Status, DateOnly? Returned, string? Note, long? Charge, byte[] Ver)? head;
        await using (var cmd = Tx(tx).Command("""
            SELECT batch_id, vendor, sent_date, status, returned_date, note, charge_paise, row_ver
            FROM stk.laundry_batch WITH (UPDLOCK, ROWLOCK) WHERE batch_id = @b AND property_id = @p;
            """))
        {
            cmd.Add("@b", SqlDbType.BigInt, batchId);
            cmd.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            head = await rd.ReadAsync(ct).ConfigureAwait(false)
                ? (rd.GetInt64(0), rd.GetString(1), P3Sql.Date(rd, 2), rd.GetString(3), P3Sql.DateOrNull(rd, 4), P3Sql.StrOrNull(rd, 5), StockSql.LongOrNull(rd, 6), (byte[])rd[7])
                : null;
        }

        if (head is not { } h) return null;
        var lines = new List<LaundryLineRecord>();
        await using (var cmd = Tx(tx).Command("""
            SELECT l.item_id, i.code, i.name, l.sent, l.received, l.damaged FROM stk.laundry_line l JOIN stk.item i ON i.item_id = l.item_id
            WHERE l.batch_id = @b ORDER BY i.code;
            """))
        {
            cmd.Add("@b", SqlDbType.BigInt, batchId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                lines.Add(new LaundryLineRecord(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), StockSql.Int32OrNull(rd, 4), StockSql.Int32OrNull(rd, 5)));
        }

        return new LaundryBatchRecord(h.Id, h.Vendor, h.Sent, h.Status, h.Returned, h.Note, h.Charge, lines, h.Ver);
    }

    public async Task SetBatchLineReturnAsync(ICommandTransaction tx, long batchId, int itemId, int received, int damaged, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("UPDATE stk.laundry_line SET received = @r, damaged = @d WHERE batch_id = @b AND item_id = @i;");
        cmd.Add("@r", SqlDbType.Int, received);
        cmd.Add("@d", SqlDbType.Int, damaged);
        cmd.Add("@b", SqlDbType.BigInt, batchId);
        cmd.Add("@i", SqlDbType.Int, itemId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteBatchAsync(ICommandTransaction tx, long batchId, byte[] expectedVersion, string status, DateOnly returned, int receivedBy, string? note, long charge, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            UPDATE stk.laundry_batch SET status = @s, returned_date = @d, received_by = @u, note = @n, charge_paise = @c WHERE batch_id = @b AND row_ver = @v;
            """);
        cmd.Add("@s", SqlDbType.VarChar, status, 14);
        cmd.Add("@d", SqlDbType.Date, StockSql.D(returned));
        cmd.Add("@u", SqlDbType.Int, receivedBy);
        cmd.Add("@n", SqlDbType.NVarChar, note, 400);
        cmd.Add("@c", SqlDbType.BigInt, charge);
        cmd.Add("@b", SqlDbType.BigInt, batchId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This laundry batch");
    }

    public async Task<IReadOnlyList<(int ItemId, string Code, string Name, int Qty)>> LevelsAtAsync(ICommandTransaction tx, int propertyId, string loc, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("""
            SELECT i.item_id, i.code, i.name, ISNULL(s.qty, 0) FROM stk.item i LEFT JOIN stk.stock s ON s.item_id = i.item_id AND s.loc = @l
            WHERE i.property_id = @p ORDER BY i.code;
            """);
        cmd.Add("@l", SqlDbType.VarChar, loc, 8);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var list = new List<(int, string, string, int)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3)));
        return list;
    }

    public async Task<bool> HasPendingCountAsync(ICommandTransaction tx, int propertyId, string loc, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("SELECT COUNT(*) FROM stk.count WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND loc = @l AND status = 'Submitted';");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@l", SqlDbType.VarChar, loc, 8);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<(long CountId, byte[] Version)> InsertCountAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, string loc, string status, int actorId, string? note,
        IReadOnlyList<(int ItemId, int System, int Counted)> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        long id;
        byte[] version;
        await using (var cmd = Tx(tx).Command("""
            INSERT stk.count(property_id, business_date, at_utc, loc, status, actor_id, note)
            OUTPUT INSERTED.count_id, INSERTED.row_ver VALUES (@p, @bd, @utc, @l, @s, @u, @n);
            """))
        {
            cmd.Add("@p", SqlDbType.Int, propertyId);
            cmd.Add("@bd", SqlDbType.Date, StockSql.D(businessDate));
            cmd.Add("@utc", SqlDbType.DateTime2, utc);
            cmd.Add("@l", SqlDbType.VarChar, loc, 8);
            cmd.Add("@s", SqlDbType.VarChar, status, 10);
            cmd.Add("@u", SqlDbType.Int, actorId);
            cmd.Add("@n", SqlDbType.NVarChar, note, 400);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, version) = (rd.GetInt64(0), (byte[])rd[1]);
        }

        foreach (var (item, system, counted) in lines)
        {
            await using var cmd = Tx(tx).Command("INSERT stk.count_line(count_id, item_id, system_qty, counted_qty) VALUES (@c, @i, @s, @n);");
            cmd.Add("@c", SqlDbType.BigInt, id);
            cmd.Add("@i", SqlDbType.Int, item);
            cmd.Add("@s", SqlDbType.Int, system);
            cmd.Add("@n", SqlDbType.Int, counted);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return (id, version);
    }

    public async Task<CountRecord?> LockCountAsync(ICommandTransaction tx, int propertyId, long countId, CancellationToken ct = default)
    {
        (long Id, DateOnly Bd, string Loc, string Status, int Actor, int? Reviewer, string? Reason, string? Note, byte[] Ver)? head;
        await using (var cmd = Tx(tx).Command("""
            SELECT count_id, business_date, loc, status, actor_id, reviewer_id, reason, note, row_ver
            FROM stk.count WITH (UPDLOCK, ROWLOCK) WHERE count_id = @c AND property_id = @p;
            """))
        {
            cmd.Add("@c", SqlDbType.BigInt, countId);
            cmd.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            head = await rd.ReadAsync(ct).ConfigureAwait(false)
                ? (rd.GetInt64(0), P3Sql.Date(rd, 1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), P3Sql.IntOrNull(rd, 5), P3Sql.StrOrNull(rd, 6), P3Sql.StrOrNull(rd, 7), (byte[])rd[8])
                : null;
        }

        if (head is not { } h) return null;
        var lines = new List<CountLineRecord>();
        await using (var cmd = Tx(tx).Command("""
            SELECT l.item_id, i.code, i.name, l.system_qty, l.counted_qty FROM stk.count_line l JOIN stk.item i ON i.item_id = l.item_id
            WHERE l.count_id = @c ORDER BY i.code;
            """))
        {
            cmd.Add("@c", SqlDbType.BigInt, countId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) lines.Add(new CountLineRecord(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetInt32(4)));
        }

        return new CountRecord(h.Id, h.Bd, h.Loc, h.Status, h.Actor, h.Reviewer, h.Reason, h.Note, lines, h.Ver);
    }

    public async Task DecideCountAsync(ICommandTransaction tx, long countId, byte[] expectedVersion, string status, int reviewerId, string reason, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("UPDATE stk.count SET status = @s, reviewer_id = @u, reason = @r WHERE count_id = @c AND row_ver = @v;");
        cmd.Add("@s", SqlDbType.VarChar, status, 10);
        cmd.Add("@u", SqlDbType.Int, reviewerId);
        cmd.Add("@r", SqlDbType.NVarChar, reason, 400);
        cmd.Add("@c", SqlDbType.BigInt, countId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This count");
    }

    public async Task<IReadOnlyDictionary<string, int>> RoomTypeIdsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Tx(tx).Command("SELECT code, room_type_id FROM inv.room_type WHERE property_id = @p;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0)] = rd.GetInt32(1);
        return map;
    }

    public async Task<(bool Created, int ItemId, byte[] Version)> InsertItemAsync(ICommandTransaction tx, int propertyId, string code, string name, string kind, string unit,
        long costPaise, int minQty, int reorderQty, int? stdX100, long? laundryRatePaise, string? supplier, DateTime utc, CancellationToken ct = default)
    {
        await using (var check = Tx(tx).Command("SELECT COUNT(*) FROM stk.item WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c;"))
        {
            check.Add("@p", SqlDbType.Int, propertyId);
            check.Add("@c", SqlDbType.VarChar, code, 20);
            if ((int)(await check.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return (false, 0, []);
        }

        int id;
        byte[] version;
        await using (var cmd = Tx(tx).Command("""
            INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, std_x100, laundry_rate_paise, supplier, created_utc)
            OUTPUT INSERTED.item_id, INSERTED.row_ver VALUES (@p, @c, @n, @k, @u, @cost, @min, @re, @std, @lr, @s, @utc);
            """))
        {
            cmd.Add("@p", SqlDbType.Int, propertyId);
            cmd.Add("@c", SqlDbType.VarChar, code, 20);
            cmd.Add("@n", SqlDbType.NVarChar, name, 120);
            cmd.Add("@k", SqlDbType.VarChar, kind, 10);
            cmd.Add("@u", SqlDbType.VarChar, unit, 20);
            cmd.Add("@cost", SqlDbType.BigInt, costPaise);
            cmd.Add("@min", SqlDbType.Int, minQty);
            cmd.Add("@re", SqlDbType.Int, reorderQty);
            cmd.Add("@std", SqlDbType.Int, stdX100);
            cmd.Add("@lr", SqlDbType.BigInt, laundryRatePaise);
            cmd.Add("@s", SqlDbType.NVarChar, supplier, 120);
            cmd.Add("@utc", SqlDbType.DateTime2, utc);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, version) = (rd.GetInt32(0), (byte[])rd[1]);
        }

        await using (var cmd = Tx(tx).Command("""
            INSERT stk.stock(item_id, loc, qty) VALUES (@i, 'store', 0), (@i, 'floor1', 0), (@i, 'floor2', 0), (@i, 'floor3', 0), (@i, 'rooms', 0), (@i, 'soiled', 0), (@i, 'laundry', 0);
            """))
        {
            cmd.Add("@i", SqlDbType.Int, id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return (true, id, version);
    }

    public async Task<byte[]> UpdateItemAsync(ICommandTransaction tx, int itemId, byte[] expectedVersion, int minQty, int reorderQty, int? stdX100,
        IReadOnlyDictionary<int, int>? par, CancellationToken ct = default)
    {
        byte[] version;
        await using (var cmd = Tx(tx).Command("""
            UPDATE stk.item SET min_qty = @min, reorder_qty = @re, std_x100 = @std OUTPUT INSERTED.row_ver WHERE item_id = @i AND row_ver = @v;
            """))
        {
            cmd.Add("@min", SqlDbType.Int, minQty);
            cmd.Add("@re", SqlDbType.Int, reorderQty);
            cmd.Add("@std", SqlDbType.Int, stdX100);
            cmd.Add("@i", SqlDbType.Int, itemId);
            cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(Contracts.HccErrorCode.STALE_VERSION, "This item was changed by someone else. Reload and try again.");
            version = (byte[])rd[0];
        }

        if (par is null) return version;
        await using (var del = Tx(tx).Command("DELETE stk.item_par WHERE item_id = @i;"))
        {
            del.Add("@i", SqlDbType.Int, itemId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (roomType, pieces) in par)
        {
            await using var ins = Tx(tx).Command("INSERT stk.item_par(item_id, room_type_id, pieces) VALUES (@i, @t, @p);");
            ins.Add("@i", SqlDbType.Int, itemId);
            ins.Add("@t", SqlDbType.Int, roomType);
            ins.Add("@p", SqlDbType.TinyInt, (byte)pieces);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return version;
    }
}

/// <summary>SQL read side of Linen &amp; stock.</summary>
public sealed class SqlStockReadStore(SqlConnectionFactory connections) : IStockReadStore
{
    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(P3Sql.BusinessDateSql, conn);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StockItemRecord>> ItemsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {StockSql.ItemColumns} FROM stk.item i WHERE i.property_id = @p ORDER BY i.code;
            SELECT p.item_id, t.code, p.pieces FROM stk.item_par p JOIN stk.item i ON i.item_id = p.item_id JOIN inv.room_type t ON t.room_type_id = p.room_type_id WHERE i.property_id = @p;
            SELECT s.item_id, s.loc, s.qty FROM stk.stock s JOIN stk.item i ON i.item_id = s.item_id WHERE i.property_id = @p;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = await StockSql.ReadItemRowsAsync(rd, ct).ConfigureAwait(false);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var par = await StockSql.ReadPairsAsync(rd, ct).ConfigureAwait(false);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var stock = await StockSql.ReadPairsAsync(rd, ct).ConfigureAwait(false);
        return rows.Select(r => StockSql.ToRecord(r, par, stock)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> RoomsByTypeAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT t.code, COUNT(*) FROM inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE r.property_id = @p GROUP BY t.code;", conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0)] = rd.GetInt32(1);
        return map;
    }

    public async Task<StockSettingsRecord> SettingsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT par_sets, vendor_name, turnaround_days FROM stk.setting WHERE property_id = @p;", conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new StockSettingsRecord(rd.GetByte(0), P3Sql.StrOrNull(rd, 1), rd.GetByte(2)) : StockSettingsRecord.Default;
    }

    public async Task<IReadOnlyList<LaundryBatchRecord>> BatchesAsync(int propertyId, string? status, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT batch_id, vendor, sent_date, status, returned_date, note, charge_paise, row_ver FROM stk.laundry_batch
            WHERE property_id = @p AND (@s IS NULL OR status = @s) ORDER BY batch_id DESC;
            SELECT l.batch_id, l.item_id, i.code, i.name, l.sent, l.received, l.damaged
            FROM stk.laundry_line l JOIN stk.laundry_batch b ON b.batch_id = l.batch_id JOIN stk.item i ON i.item_id = l.item_id
            WHERE b.property_id = @p AND (@s IS NULL OR b.status = @s) ORDER BY l.batch_id, i.code;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@s", SqlDbType.VarChar, status, 14);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var heads = new List<(long Id, string Vendor, DateOnly Sent, string Status, DateOnly? Ret, string? Note, long? Charge, byte[] Ver)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), rd.GetString(1), P3Sql.Date(rd, 2), rd.GetString(3), P3Sql.DateOrNull(rd, 4), P3Sql.StrOrNull(rd, 5), StockSql.LongOrNull(rd, 6), (byte[])rd[7]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var lines = new Dictionary<long, List<LaundryLineRecord>>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            if (!lines.TryGetValue(id, out var list)) lines[id] = list = [];
            list.Add(new LaundryLineRecord(rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), StockSql.Int32OrNull(rd, 5), StockSql.Int32OrNull(rd, 6)));
        }

        return heads.Select(h => new LaundryBatchRecord(h.Id, h.Vendor, h.Sent, h.Status, h.Ret, h.Note, h.Charge, lines.TryGetValue(h.Id, out var l) ? l : [], h.Ver)).ToList();
    }

    public async Task<int> PendingCountsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT COUNT(*) FROM stk.count WHERE property_id = @p AND status = 'Submitted';", conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<CountRecord>> CountsAsync(int propertyId, string? status, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT count_id, business_date, loc, status, actor_id, reviewer_id, reason, note, row_ver FROM stk.count
            WHERE property_id = @p AND (@s IS NULL OR status = @s) ORDER BY count_id DESC;
            SELECT l.count_id, l.item_id, i.code, i.name, l.system_qty, l.counted_qty
            FROM stk.count_line l JOIN stk.count c ON c.count_id = l.count_id JOIN stk.item i ON i.item_id = l.item_id
            WHERE c.property_id = @p AND (@s IS NULL OR c.status = @s) ORDER BY l.count_id, i.code;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@s", SqlDbType.VarChar, status, 10);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var heads = new List<(long Id, DateOnly Bd, string Loc, string Status, int Actor, int? Rev, string? Reason, string? Note, byte[] Ver)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), P3Sql.Date(rd, 1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), P3Sql.IntOrNull(rd, 5), P3Sql.StrOrNull(rd, 6), P3Sql.StrOrNull(rd, 7), (byte[])rd[8]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var lines = new Dictionary<long, List<CountLineRecord>>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            if (!lines.TryGetValue(id, out var list)) lines[id] = list = [];
            list.Add(new CountLineRecord(rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetInt32(5)));
        }

        return heads.Select(h => new CountRecord(h.Id, h.Bd, h.Loc, h.Status, h.Actor, h.Rev, h.Reason, h.Note, lines.TryGetValue(h.Id, out var l) ? l : [], h.Ver)).ToList();
    }

    public async Task<IReadOnlyList<LossRecord>> LossesAsync(int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT l.loss_id, l.business_date, i.code, i.name, l.loc, l.qty, l.reason, l.note, l.actor_id, l.value_paise, l.ref
            FROM stk.loss l JOIN stk.item i ON i.item_id = l.item_id
            WHERE l.property_id = @p AND (@f IS NULL OR l.business_date >= @f) AND (@t IS NULL OR l.business_date <= @t)
            ORDER BY l.business_date DESC, l.loss_id DESC;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@f", SqlDbType.Date, from is { } f ? StockSql.D(f) : null);
        cmd.Add("@t", SqlDbType.Date, to is { } t ? StockSql.D(t) : null);
        var list = new List<LossRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new LossRecord(rd.GetInt64(0), P3Sql.Date(rd, 1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetString(6), P3Sql.StrOrNull(rd, 7), rd.GetInt32(8), rd.GetInt64(9), P3Sql.StrOrNull(rd, 10)));
        return list;
    }

    public async Task<IReadOnlyList<MoveRecord>> MovesAsync(int propertyId, string? itemCode, DateOnly? from, DateOnly? to, int limit, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) m.move_id, m.business_date, m.at_utc, m.kind, i.code, m.qty, m.from_loc, m.to_loc, m.ref, m.reason, m.actor_id
            FROM stk.move m JOIN stk.item i ON i.item_id = m.item_id
            WHERE m.property_id = @p AND (@c IS NULL OR i.code = @c) AND (@f IS NULL OR m.business_date >= @f) AND (@t IS NULL OR m.business_date <= @t)
            ORDER BY m.move_id DESC;
            """, conn);
        cmd.Add("@n", SqlDbType.Int, limit);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@c", SqlDbType.VarChar, itemCode, 20);
        cmd.Add("@f", SqlDbType.Date, from is { } f ? StockSql.D(f) : null);
        cmd.Add("@t", SqlDbType.Date, to is { } t ? StockSql.D(t) : null);
        var list = new List<MoveRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new MoveRecord(rd.GetInt64(0), P3Sql.Date(rd, 1), rd.GetDateTime(2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5),
                P3Sql.StrOrNull(rd, 6), P3Sql.StrOrNull(rd, 7), P3Sql.StrOrNull(rd, 8), P3Sql.StrOrNull(rd, 9), rd.GetInt32(10)));
        return list;
    }

    public async Task<IReadOnlyList<OccupancyDay>> OccupancyAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT n.night, COUNT(*) FROM inv.room_night n JOIN inv.room r ON r.room_id = n.room_id JOIN inv.reservation s ON s.reservation_id = n.reservation_id
            WHERE r.property_id = @p AND n.night BETWEEN @f AND @t AND s.status IN ('InHouse', 'Departed') GROUP BY n.night;
            SELECT night, rooms FROM stk.occupancy_history WHERE property_id = @p AND night BETWEEN @f AND @t;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@f", SqlDbType.Date, StockSql.D(from));
        cmd.Add("@t", SqlDbType.Date, StockSql.D(to));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var live = new Dictionary<DateOnly, int>();
        var hist = new Dictionary<DateOnly, int>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) live[P3Sql.Date(rd, 0)] = rd.GetInt32(1);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) hist[P3Sql.Date(rd, 0)] = rd.GetInt16(1);
        var days = new List<OccupancyDay>();
        for (var d = from; d <= to; d = d.AddDays(1)) days.Add(new OccupancyDay(d, live.GetValueOrDefault(d), hist.GetValueOrDefault(d)));
        return days;
    }

    public async Task<IReadOnlyList<ConsumptionUsed>> ConsumptionAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT i.code, SUM(m.qty) FROM stk.move m JOIN stk.item i ON i.item_id = m.item_id
            WHERE m.property_id = @p AND m.business_date BETWEEN @f AND @t AND m.kind = CASE WHEN i.kind = 'linen' THEN 'room-change' ELSE 'use' END
            GROUP BY i.code;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@f", SqlDbType.Date, StockSql.D(from));
        cmd.Add("@t", SqlDbType.Date, StockSql.D(to));
        var list = new List<ConsumptionUsed>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new ConsumptionUsed(rd.GetString(0), rd.GetInt32(1)));
        return list;
    }
}
