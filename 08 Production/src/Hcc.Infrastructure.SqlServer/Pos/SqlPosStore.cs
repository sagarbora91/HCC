using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Pos;
using Hcc.Contracts;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Pos;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Pos;

/// <summary>
/// SQL Server implementation of <see cref="IPosStore"/> (migration 1200). The check row is the lock root (UPDLOCK on every change to a check,
/// its lines, KOTs and bill); KOT numbers and the bill sequence are allocated under UPDLOCK + HOLDLOCK in the settling transaction (no
/// insert-and-catch). Reads of other modules' tables (inv.room, gst.stay) are read-only.
/// </summary>
public sealed class SqlPosStore(SqlConnectionFactory connections) : IPosStore
{
    private const string OrderSelect = """
        SELECT o.order_id, o.property_id, o.outlet_id, ou.name, ou.kind, o.table_no, o.room_id, rm.room_no, o.stay_id, o.guest, o.covers, o.business_date,
               o.status, o.pending_void_id, o.rev, o.row_ver
        FROM pos.check_order o {0}
        JOIN pos.outlet ou ON ou.outlet_id = o.outlet_id
        LEFT JOIN inv.room rm ON rm.room_id = o.room_id
        """;

    private const string BillColumns = """
        bill_id, order_id, bill_no, mode, tender, reference, net_paise, tax_bps, tax_paise, total_paise, business_date, till_id, shift_id, folio_id, charge_id, status
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The POS store needs a SQL command transaction.");

    private static SqlCommand Cmd(ICommandTransaction tx, string sql) => Sql(tx).Command(sql);

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static void Utc(SqlCommand cmd, string name, DateTime? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.DateTime2);
        p.Scale = 3;
        p.Value = value is { } v ? (v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v) : DBNull.Value;
    }

    private static async Task<List<T>> ReadAsync<T>(SqlCommand cmd, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(rd));
        return list;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateTime? Dt(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static string Ver(SqlDataReader rd, int i) => SqlValues.RowVersionToWire((byte[])rd[i]);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    // ── setup ──

    public async Task<bool> OutletCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM pos.outlet WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.VarChar, code, 20);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<OutletRow> InsertOutletAsync(ICommandTransaction tx, int propertyId, string code, string name, string kind, IReadOnlyList<string> tables, DateTime utc, CancellationToken ct)
    {
        int id;
        string version;
        await using (var cmd = Cmd(tx, "INSERT pos.outlet(property_id, code, name, kind, created_utc) OUTPUT INSERTED.outlet_id, INSERTED.row_ver VALUES (@p, @c, @n, @k, @u);"))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@c", SqlDbType.VarChar, code, 20);
            P(cmd, "@n", SqlDbType.NVarChar, name, 100);
            P(cmd, "@k", SqlDbType.VarChar, kind, 5);
            Utc(cmd, "@u", utc);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, version) = (rd.GetInt32(0), Ver(rd, 1));
        }

        foreach (var t in tables)
        {
            await using var cmd = Cmd(tx, "INSERT pos.outlet_table(outlet_id, table_no) VALUES (@o, @t);");
            P(cmd, "@o", SqlDbType.Int, id);
            P(cmd, "@t", SqlDbType.VarChar, t, 10);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return new OutletRow(id, propertyId, code, name, kind, version);
    }

    public async Task<OutletRow?> OutletAsync(ICommandTransaction tx, int propertyId, int outletId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT outlet_id, property_id, code, name, kind, row_ver FROM pos.outlet WHERE outlet_id = @o AND property_id = @p;");
        P(cmd, "@o", SqlDbType.Int, outletId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(cmd, rd => new OutletRow(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), Ver(rd, 5)), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<bool> OutletHasTableAsync(ICommandTransaction tx, int outletId, string table, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM pos.outlet_table WHERE outlet_id = @o AND table_no = @t;");
        P(cmd, "@o", SqlDbType.Int, outletId);
        P(cmd, "@t", SqlDbType.VarChar, table, 10);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<MenuItemRow?> MenuItemAsync(ICommandTransaction tx, int propertyId, int itemId, bool lockRow, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT item_id, property_id, code, name, category, price_paise, veg, available, row_ver FROM pos.menu_item {(lockRow ? "WITH (UPDLOCK)" : string.Empty)} WHERE item_id = @i AND property_id = @p;");
        P(cmd, "@i", SqlDbType.Int, itemId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var row = (await ReadAsync(cmd, rd => (rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), SqlValues.ReadPaise(rd, 5), rd.GetBoolean(6), rd.GetBoolean(7), Ver(rd, 8)), ct).ConfigureAwait(false)).Cast<(int, int, string, string, string, Paise, bool, bool, string)?>().SingleOrDefault();
        if (row is not { } r) return null;
        await using var oc = Cmd(tx, "SELECT outlet_id FROM pos.menu_item_outlet WHERE item_id = @i ORDER BY outlet_id;");
        P(oc, "@i", SqlDbType.Int, itemId);
        var outlets = await ReadAsync(oc, rd => rd.GetInt32(0), ct).ConfigureAwait(false);
        return new MenuItemRow(r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7, r.Item8, outlets, r.Item9);
    }

    public async Task<bool> MenuCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, int? exceptItemId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM pos.menu_item WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c AND (@x IS NULL OR item_id <> @x);");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.VarChar, code, 20);
        P(cmd, "@x", SqlDbType.Int, exceptItemId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<int> CountOutletsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> outletIds, CancellationToken ct)
    {
        var n = 0;
        foreach (var id in outletIds)
        {
            await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM pos.outlet WHERE outlet_id = @o AND property_id = @p;");
            P(cmd, "@o", SqlDbType.Int, id);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            n += (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        return n;
    }

    private static async Task SetItemOutletsAsync(ICommandTransaction tx, int itemId, IReadOnlyList<int> outletIds, CancellationToken ct)
    {
        await using (var del = Cmd(tx, "DELETE pos.menu_item_outlet WHERE item_id = @i;"))
        {
            P(del, "@i", SqlDbType.Int, itemId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var o in outletIds)
        {
            await using var ins = Cmd(tx, "INSERT pos.menu_item_outlet(item_id, outlet_id) VALUES (@i, @o);");
            P(ins, "@i", SqlDbType.Int, itemId);
            P(ins, "@o", SqlDbType.Int, o);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<MenuItemRow> InsertMenuItemAsync(ICommandTransaction tx, int propertyId, string code, string name, string category, Paise price, bool veg,
        IReadOnlyList<int> outletIds, CancellationToken ct)
    {
        int id;
        string version;
        await using (var cmd = Cmd(tx, "INSERT pos.menu_item(property_id, code, name, category, price_paise, veg) OUTPUT INSERTED.item_id, INSERTED.row_ver VALUES (@p, @c, @n, @g, @a, @v);"))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@c", SqlDbType.VarChar, code, 20);
            P(cmd, "@n", SqlDbType.NVarChar, name, 100);
            P(cmd, "@g", SqlDbType.NVarChar, category, 60);
            P(cmd, "@a", SqlDbType.BigInt, price.Value);
            P(cmd, "@v", SqlDbType.Bit, veg);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, version) = (rd.GetInt32(0), Ver(rd, 1));
        }

        await SetItemOutletsAsync(tx, id, outletIds, ct).ConfigureAwait(false);
        return new MenuItemRow(id, propertyId, code, name, category, price, veg, true, outletIds, version);
    }

    public async Task<MenuItemRow> UpdateMenuItemAsync(ICommandTransaction tx, int itemId, byte[] expectedVersion, string code, string name, string category, Paise price,
        bool veg, IReadOnlyList<int> outletIds, CancellationToken ct)
    {
        string version;
        int property;
        bool available;
        await using (var cmd = Cmd(tx, """
            UPDATE pos.menu_item SET code = @c, name = @n, category = @g, price_paise = @a, veg = @v
            OUTPUT INSERTED.row_ver, INSERTED.property_id, INSERTED.available
            WHERE item_id = @i AND row_ver = @rv;
            """))
        {
            P(cmd, "@c", SqlDbType.VarChar, code, 20);
            P(cmd, "@n", SqlDbType.NVarChar, name, 100);
            P(cmd, "@g", SqlDbType.NVarChar, category, 60);
            P(cmd, "@a", SqlDbType.BigInt, price.Value);
            P(cmd, "@v", SqlDbType.Bit, veg);
            P(cmd, "@i", SqlDbType.Int, itemId);
            P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) SqlValues.EnsureVersionMatched(0, "The menu item");
            (version, property, available) = (Ver(rd, 0), rd.GetInt32(1), rd.GetBoolean(2));
        }

        await SetItemOutletsAsync(tx, itemId, outletIds, ct).ConfigureAwait(false);
        return new MenuItemRow(itemId, property, code, name, category, price, veg, available, outletIds, version);
    }

    public async Task<string> SetAvailabilityAsync(ICommandTransaction tx, int itemId, bool available, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pos.menu_item SET available = @a OUTPUT INSERTED.row_ver WHERE item_id = @i;");
        P(cmd, "@a", SqlDbType.Bit, available);
        P(cmd, "@i", SqlDbType.Int, itemId);
        return SqlValues.RowVersionToWire((byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    // ── in-house guests ──

    private const string StaySelect = """
        SELECT s.stay_id, s.folio_id, rm.room_id, rm.room_no, rs.guest_name, s.status
        FROM gst.stay s
        JOIN inv.reservation rs ON rs.reservation_id = s.reservation_id
        OUTER APPLY (SELECT TOP 1 sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC) cur
        JOIN inv.room rm ON rm.room_id = cur.room_id
        """;

    private static StayRoomRow ReadStay(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetString(5));

    public async Task<StayRoomRow?> InHouseByRoomAsync(ICommandTransaction tx, int propertyId, string roomNo, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, StaySelect + " WHERE s.property_id = @p AND s.status = 'InHouse' AND rm.room_no = @r;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.VarChar, roomNo, 10);
        return (await ReadAsync(cmd, ReadStay, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<StayRoomRow?> StayByIdAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, StaySelect + " WHERE s.property_id = @p AND s.stay_id = @s;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        return (await ReadAsync(cmd, ReadStay, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    // ── checks ──

    public async Task<long> InsertOrderAsync(ICommandTransaction tx, int propertyId, int outletId, string? table, int? roomId, long? stayId, string? guest, int covers,
        DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT pos.check_order(property_id, outlet_id, table_no, room_id, stay_id, guest, covers, business_date, opened_utc, opened_by, status)
            OUTPUT INSERTED.order_id VALUES (@p, @o, @t, @r, @s, @g, @c, @d, @u, @by, 'Open');
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@o", SqlDbType.Int, outletId);
        P(cmd, "@t", SqlDbType.VarChar, table, 10);
        P(cmd, "@r", SqlDbType.Int, roomId);
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        P(cmd, "@g", SqlDbType.NVarChar, guest, 60);
        P(cmd, "@c", SqlDbType.TinyInt, (byte)covers);
        P(cmd, "@d", SqlDbType.Date, businessDate);
        Utc(cmd, "@u", utc);
        P(cmd, "@by", SqlDbType.Int, principalId);
        try
        {
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        catch (SqlException ex) when (SqlErrorTranslator.IsDuplicateKey(ex))
        {
            throw new HccException(HccErrorCode.NOT_AVAILABLE, "That table or room already has an open check.");
        }
    }

    private static OrderRow ReadOrder(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), Str(rd, 5),
        rd.IsDBNull(6) ? null : rd.GetInt32(6), Str(rd, 7), rd.IsDBNull(8) ? null : rd.GetInt64(8), Str(rd, 9), rd.GetByte(10), rd.GetFieldValue<DateOnly>(11),
        rd.GetString(12), rd.IsDBNull(13) ? null : rd.GetInt64(13), rd.GetInt32(14), Ver(rd, 15));

    public async Task<OrderRow?> LockOrderAsync(ICommandTransaction tx, int propertyId, long orderId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, string.Format(System.Globalization.CultureInfo.InvariantCulture, OrderSelect, "WITH (UPDLOCK)") + " WHERE o.order_id = @o AND o.property_id = @p;");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(cmd, ReadOrder, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<bool> OpenCheckExistsAsync(ICommandTransaction tx, int outletId, string? table, int? roomId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT COUNT(*) FROM pos.check_order WITH (UPDLOCK, HOLDLOCK)
            WHERE outlet_id = @o AND status = 'Open' AND ((@t IS NOT NULL AND table_no = @t) OR (@r IS NOT NULL AND room_id = @r));
            """);
        P(cmd, "@o", SqlDbType.Int, outletId);
        P(cmd, "@t", SqlDbType.VarChar, table, 10);
        P(cmd, "@r", SqlDbType.Int, roomId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<string> TouchOrderAsync(ICommandTransaction tx, long orderId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pos.check_order SET rev = rev + 1 OUTPUT INSERTED.row_ver WHERE order_id = @o;");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        return SqlValues.RowVersionToWire((byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    public async Task SetOrderStatusAsync(ICommandTransaction tx, long orderId, string status, string? voidReason, int? voidedBy, DateTime? voidedUtc, long? stayId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE pos.check_order SET status = @s, void_reason = @vr, voided_by = @vb, voided_utc = @vu, stay_id = COALESCE(@st, stay_id) WHERE order_id = @o;
            """);
        P(cmd, "@s", SqlDbType.VarChar, status, 7);
        P(cmd, "@vr", SqlDbType.NVarChar, voidReason, 200);
        P(cmd, "@vb", SqlDbType.Int, voidedBy);
        Utc(cmd, "@vu", voidedUtc);
        P(cmd, "@st", SqlDbType.BigInt, stayId);
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetPendingVoidAsync(ICommandTransaction tx, long orderId, long? voidId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pos.check_order SET pending_void_id = @v WHERE order_id = @o;");
        P(cmd, "@v", SqlDbType.BigInt, voidId);
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LineRow>> LinesAsync(ICommandTransaction tx, long orderId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT line_id, order_id, item_id, name, price_paise, qty, kot_id, voided, void_reason FROM pos.order_line WHERE order_id = @o ORDER BY line_id;");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        return await ReadAsync(cmd, rd => new LineRow(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), SqlValues.ReadPaise(rd, 4), rd.GetInt16(5),
            rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.GetBoolean(7), Str(rd, 8)), ct).ConfigureAwait(false);
    }

    public async Task<long> InsertLineAsync(ICommandTransaction tx, long orderId, MenuItemRow item, int qty, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "INSERT pos.order_line(order_id, item_id, name, price_paise, qty) OUTPUT INSERTED.line_id VALUES (@o, @i, @n, @p, @q);");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        P(cmd, "@i", SqlDbType.Int, item.ItemId);
        P(cmd, "@n", SqlDbType.NVarChar, item.Name, 100);
        P(cmd, "@p", SqlDbType.BigInt, item.Price.Value);
        P(cmd, "@q", SqlDbType.SmallInt, (short)qty);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task SetLineQtyAsync(ICommandTransaction tx, long lineId, int qty, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pos.order_line SET qty = @q WHERE line_id = @l;");
        P(cmd, "@q", SqlDbType.SmallInt, (short)qty);
        P(cmd, "@l", SqlDbType.BigInt, lineId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteLineAsync(ICommandTransaction tx, long lineId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "DELETE pos.order_line WHERE line_id = @l AND kot_id IS NULL;");
        P(cmd, "@l", SqlDbType.BigInt, lineId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task VoidLinesAsync(ICommandTransaction tx, IReadOnlyList<long> lineIds, string reason, int by, DateOnly date, long? voidRequestId, CancellationToken ct)
    {
        foreach (var id in lineIds)
        {
            await using var cmd = Cmd(tx, "UPDATE pos.order_line SET voided = 1, void_reason = @r, void_by = @b, void_date = @d, void_request_id = @v WHERE line_id = @l;");
            P(cmd, "@r", SqlDbType.NVarChar, reason, 200);
            P(cmd, "@b", SqlDbType.Int, by);
            P(cmd, "@d", SqlDbType.Date, date);
            P(cmd, "@v", SqlDbType.BigInt, voidRequestId);
            P(cmd, "@l", SqlDbType.BigInt, id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    // ── kitchen ──

    public async Task<(long KotId, int KotNo)> InsertKotAsync(ICommandTransaction tx, OrderRow order, DateOnly date, int principalId, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @n INT = (SELECT ISNULL(MAX(kot_no), 0) + 1 FROM pos.kot WITH (UPDLOCK, HOLDLOCK) WHERE outlet_id = @o AND business_date = @d);
            INSERT pos.kot(order_id, outlet_id, business_date, kot_no, status, created_utc, created_by)
            OUTPUT INSERTED.kot_id, INSERTED.kot_no VALUES (@ord, @o, @d, @n, 'Preparing', @u, @by);
            """);
        P(cmd, "@o", SqlDbType.Int, order.OutletId);
        P(cmd, "@d", SqlDbType.Date, date);
        P(cmd, "@ord", SqlDbType.BigInt, order.OrderId);
        Utc(cmd, "@u", utc);
        P(cmd, "@by", SqlDbType.Int, principalId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), rd.GetInt32(1));
    }

    public async Task AssignKotAsync(ICommandTransaction tx, long kotId, IReadOnlyList<long> lineIds, CancellationToken ct)
    {
        foreach (var id in lineIds)
        {
            await using var cmd = Cmd(tx, "UPDATE pos.order_line SET kot_id = @k WHERE line_id = @l AND kot_id IS NULL;");
            P(cmd, "@k", SqlDbType.BigInt, kotId);
            P(cmd, "@l", SqlDbType.BigInt, id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<KotRow?> LockKotAsync(ICommandTransaction tx, long orderId, long kotId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT kot_id, order_id, outlet_id, kot_no, status, created_utc, ready_utc, served_utc FROM pos.kot WITH (UPDLOCK) WHERE kot_id = @k AND order_id = @o;");
        P(cmd, "@k", SqlDbType.BigInt, kotId);
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        return (await ReadAsync(cmd, rd => new KotRow(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetString(4), Dt(rd, 5)!.Value, Dt(rd, 6), Dt(rd, 7)), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task UpdateKotStatusAsync(ICommandTransaction tx, long kotId, string status, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE pos.kot SET status = @s,
              ready_utc = CASE WHEN @s = 'Ready' THEN @u ELSE ready_utc END,
              served_utc = CASE WHEN @s = 'Served' THEN @u ELSE served_utc END
            WHERE kot_id = @k;
            """);
        P(cmd, "@s", SqlDbType.VarChar, status, 9);
        Utc(cmd, "@u", utc);
        P(cmd, "@k", SqlDbType.BigInt, kotId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── bills ──

    public async Task<int> NextBillSequenceAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            IF NOT EXISTS (SELECT 1 FROM pos.counter WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p) INSERT pos.counter(property_id, bill_seq) VALUES (@p, 0);
            UPDATE pos.counter SET bill_seq = bill_seq + 1 OUTPUT INSERTED.bill_seq WHERE property_id = @p;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertBillAsync(ICommandTransaction tx, NewBill b, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(b);
        await using var cmd = Cmd(tx, """
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, reference, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by,
                            till_id, shift_id, cash_movement_id, stay_id, folio_id, charge_id, cgst_paise, sgst_paise)
            OUTPUT INSERTED.bill_id
            VALUES (@o, @p, @no, @m, @t, @ref, @n, @bps, @tax, @tot, @d, @u, @by, @till, @shift, @mv, @stay, @f, @c, @cgst, @sgst);
            """);
        P(cmd, "@o", SqlDbType.BigInt, b.OrderId);
        P(cmd, "@p", SqlDbType.Int, b.PropertyId);
        P(cmd, "@no", SqlDbType.VarChar, b.BillNo, 20);
        P(cmd, "@m", SqlDbType.VarChar, b.Mode, 6);
        P(cmd, "@t", SqlDbType.VarChar, b.Tender, 8);
        P(cmd, "@ref", SqlDbType.NVarChar, b.Reference, 120);
        P(cmd, "@n", SqlDbType.BigInt, b.Net.Value);
        P(cmd, "@bps", SqlDbType.Int, b.TaxBps);
        P(cmd, "@tax", SqlDbType.BigInt, b.Tax.Value);
        P(cmd, "@tot", SqlDbType.BigInt, b.Total.Value);
        P(cmd, "@d", SqlDbType.Date, b.BusinessDate);
        Utc(cmd, "@u", b.SettledUtc);
        P(cmd, "@by", SqlDbType.Int, b.SettledBy);
        P(cmd, "@till", SqlDbType.Int, b.TillId);
        P(cmd, "@shift", SqlDbType.BigInt, b.ShiftId);
        P(cmd, "@mv", SqlDbType.BigInt, b.CashMovementId);
        P(cmd, "@stay", SqlDbType.BigInt, b.StayId);
        P(cmd, "@f", SqlDbType.BigInt, b.FolioId);
        P(cmd, "@c", SqlDbType.BigInt, b.ChargeId);
        P(cmd, "@cgst", SqlDbType.BigInt, b.Cgst.Value);
        P(cmd, "@sgst", SqlDbType.BigInt, b.Sgst.Value);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static BillRow ReadBill(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.GetString(3), Str(rd, 4), Str(rd, 5), SqlValues.ReadPaise(rd, 6),
        rd.GetInt32(7), SqlValues.ReadPaise(rd, 8), SqlValues.ReadPaise(rd, 9), rd.GetFieldValue<DateOnly>(10), rd.IsDBNull(11) ? null : rd.GetInt32(11),
        rd.IsDBNull(12) ? null : rd.GetInt64(12), rd.IsDBNull(13) ? null : rd.GetInt64(13), rd.IsDBNull(14) ? null : rd.GetInt64(14), rd.GetString(15));

    public async Task<BillRow?> BillForOrderAsync(ICommandTransaction tx, long orderId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {BillColumns} FROM pos.bill WHERE order_id = @o;");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        return (await ReadAsync(cmd, ReadBill, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task MarkBillVoidAsync(ICommandTransaction tx, long billId, string reason, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE pos.bill SET status = 'Void', void_reason = @r, voided_by = @b, voided_utc = @u WHERE bill_id = @i;");
        P(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        P(cmd, "@b", SqlDbType.Int, by);
        Utc(cmd, "@u", utc);
        P(cmd, "@i", SqlDbType.BigInt, billId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertReceiptAsync(ICommandTransaction tx, NewReceipt r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Cmd(tx, """
            INSERT pos.receipt(bill_id, order_id, outlet_id, kind, tender, amount_paise, net_paise, tax_paise, tax_bps, reference, business_date, principal_id, recorded_utc)
            OUTPUT INSERTED.receipt_id VALUES (@b, @o, @ou, @k, @t, @a, @n, @tx, @bps, @ref, @d, @p, @u);
            """);
        P(cmd, "@b", SqlDbType.BigInt, r.BillId);
        P(cmd, "@o", SqlDbType.BigInt, r.OrderId);
        P(cmd, "@ou", SqlDbType.Int, r.OutletId);
        P(cmd, "@k", SqlDbType.VarChar, r.Kind, 4);
        P(cmd, "@t", SqlDbType.VarChar, r.Tender, 8);
        P(cmd, "@a", SqlDbType.BigInt, r.Amount.Value);
        P(cmd, "@n", SqlDbType.BigInt, r.Net.Value);
        P(cmd, "@tx", SqlDbType.BigInt, r.Tax.Value);
        P(cmd, "@bps", SqlDbType.Int, r.TaxBps);
        P(cmd, "@ref", SqlDbType.NVarChar, r.Reference, 120);
        P(cmd, "@d", SqlDbType.Date, r.BusinessDate);
        P(cmd, "@p", SqlDbType.Int, r.PrincipalId);
        Utc(cmd, "@u", r.RecordedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    // ── void requests ──

    private const string VoidColumns = "void_id, property_id, order_id, scope, line_id, amount_paise, reason, status, order_rev, requested_by, expires_utc, row_ver";

    private static VoidRequestRow ReadVoid(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetInt64(4),
        SqlValues.ReadPaise(rd, 5), rd.GetString(6), rd.GetString(7), rd.GetInt32(8), rd.GetInt32(9), Dt(rd, 10)!.Value, Ver(rd, 11));

    public async Task<(long VoidId, string Version)> InsertVoidRequestAsync(ICommandTransaction tx, NewVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Cmd(tx, """
            INSERT pos.void_request(property_id, order_id, scope, line_id, amount_paise, reason, status, order_rev, requested_by, requested_utc, expires_utc)
            OUTPUT INSERTED.void_id, INSERTED.row_ver VALUES (@p, @o, @s, @l, @a, @r, 'Pending', @rev, @by, @ru, @eu);
            """);
        P(cmd, "@p", SqlDbType.Int, r.PropertyId);
        P(cmd, "@o", SqlDbType.BigInt, r.OrderId);
        P(cmd, "@s", SqlDbType.VarChar, r.Scope, 4);
        P(cmd, "@l", SqlDbType.BigInt, r.LineId);
        P(cmd, "@a", SqlDbType.BigInt, r.Amount.Value);
        P(cmd, "@r", SqlDbType.NVarChar, r.Reason, 200);
        P(cmd, "@rev", SqlDbType.Int, r.OrderRev);
        P(cmd, "@by", SqlDbType.Int, r.RequestedBy);
        Utc(cmd, "@ru", r.RequestedUtc);
        Utc(cmd, "@eu", r.ExpiresUtc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), Ver(rd, 1));
    }

    public async Task<VoidRequestRow?> PeekVoidRequestAsync(ICommandTransaction tx, int propertyId, long voidId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {VoidColumns} FROM pos.void_request WHERE void_id = @v AND property_id = @p;");
        P(cmd, "@v", SqlDbType.BigInt, voidId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return (await ReadAsync(cmd, ReadVoid, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<VoidRequestRow?> LockVoidRequestAsync(ICommandTransaction tx, long voidId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT {VoidColumns} FROM pos.void_request WITH (UPDLOCK) WHERE void_id = @v;");
        P(cmd, "@v", SqlDbType.BigInt, voidId);
        return (await ReadAsync(cmd, ReadVoid, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<string> DecideVoidRequestAsync(ICommandTransaction tx, long voidId, string expectedVersion, string status, int decidedBy, DateTime utc,
        string? decisionReason, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE pos.void_request SET status = @s, decided_by = @b, decided_utc = @u, decision_reason = @r
            OUTPUT INSERTED.row_ver WHERE void_id = @v AND row_ver = @rv;
            """);
        P(cmd, "@s", SqlDbType.VarChar, status, 8);
        P(cmd, "@b", SqlDbType.Int, decidedBy);
        Utc(cmd, "@u", utc);
        P(cmd, "@r", SqlDbType.NVarChar, decisionReason, 200);
        P(cmd, "@v", SqlDbType.BigInt, voidId);
        P(cmd, "@rv", SqlDbType.Binary, SqlValues.RowVersionFromWire(expectedVersion), 8);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "The void request");
        return SqlValues.RowVersionToWire(v!);
    }

    // ── queries ──

    public async Task<DateOnly?> PropertyBusinessDateAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT business_date FROM inv.property WHERE property_id = @p;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
    }

    public async Task<CheckView?> ReadCheckAsync(int propertyId, long orderId, int taxBps, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        OrderRow? o;
        await using (var c = connections.Command(string.Format(System.Globalization.CultureInfo.InvariantCulture, OrderSelect, string.Empty) + " WHERE o.order_id = @o AND o.property_id = @p;", conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            P(c, "@p", SqlDbType.Int, propertyId);
            o = (await ReadAsync(c, ReadOrder, ct).ConfigureAwait(false)).SingleOrDefault();
        }

        if (o is null) return null;
        List<KotView> kots;
        await using (var c = connections.Command("SELECT kot_id, kot_no, status, created_utc, ready_utc, served_utc, created_by FROM pos.kot WHERE order_id = @o ORDER BY kot_id;", conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            kots = await ReadAsync(c, rd => new KotView(rd.GetInt64(0), rd.GetInt32(1), PosRules.KotLabel(o.OutletName, rd.GetInt32(1)), rd.GetString(2), Dt(rd, 3)!.Value, Dt(rd, 4), Dt(rd, 5), rd.GetInt32(6)), ct).ConfigureAwait(false);
        }

        List<LineRow> lines;
        await using (var c = connections.Command("SELECT line_id, order_id, item_id, name, price_paise, qty, kot_id, voided, void_reason FROM pos.order_line WHERE order_id = @o ORDER BY line_id;", conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            lines = await ReadAsync(c, rd => new LineRow(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), SqlValues.ReadPaise(rd, 4), rd.GetInt16(5),
                rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.GetBoolean(7), Str(rd, 8)), ct).ConfigureAwait(false);
        }

        BillRow? bill;
        await using (var c = connections.Command($"SELECT {BillColumns} FROM pos.bill WHERE order_id = @o;", conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            bill = (await ReadAsync(c, ReadBill, ct).ConfigureAwait(false)).SingleOrDefault();
        }

        DateTime? settledUtc = null;
        int? settledBy = null;
        long? billReservation = null;
        await using (var c = connections.Command("""
            SELECT b.settled_utc, b.settled_by, st.reservation_id
            FROM pos.bill b LEFT JOIN gst.stay st ON st.stay_id = b.stay_id WHERE b.order_id = @o;
            """, conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            var s = (await ReadAsync(c, rd => (At: Dt(rd, 0), By: rd.GetInt32(1), Res: rd.IsDBNull(2) ? (long?)null : rd.GetInt64(2)), ct).ConfigureAwait(false)).SingleOrDefault();
            if (s.At is not null) { settledUtc = s.At; settledBy = s.By; billReservation = s.Res; }
        }

        (DateTime? OpenedUtc, int OpenedBy, string? Reason, DateTime? VoidedUtc, long? Res) head;
        await using (var c = connections.Command("""
            SELECT o.opened_utc, o.opened_by, o.void_reason, o.voided_utc, st.reservation_id
            FROM pos.check_order o LEFT JOIN gst.stay st ON st.stay_id = o.stay_id WHERE o.order_id = @o;
            """, conn, tx))
        {
            P(c, "@o", SqlDbType.BigInt, orderId);
            head = (await ReadAsync(c, rd => (Dt(rd, 0), rd.GetInt32(1), Str(rd, 2), Dt(rd, 3), rd.IsDBNull(4) ? (long?)null : rd.GetInt64(4)), ct).ConfigureAwait(false)).Single();
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        var kotStatus = kots.ToDictionary(k => k.KotId, k => k.Status);
        var views = lines.Select(l => new LineView(l.LineId, l.ItemId, l.Name, l.Price.Value, l.Qty, new PosLineAmount(l.Price, l.Qty).Value.Value, l.KotId,
            l.Voided ? "Void" : l.KotId is null ? "New" : kotStatus[l.KotId.Value], l.Voided, l.VoidReason)).ToList();
        var active = lines.Where(l => !l.Voided).ToList();
        var items = active.Sum(l => l.Qty);
        var t = bill is not null && o.Status == "Settled"
            ? new PosTotals(bill.Net, bill.TaxBps, bill.Tax, bill.Total, items)
            : PosRules.Totals(active.Select(l => new PosLineAmount(l.Price, l.Qty)), taxBps);
        var spot = o.TableNo is not null ? "Table " + o.TableNo : "Room " + o.RoomNo;
        var billView = bill is null ? null : new BillView(bill.BillId, bill.BillNo, bill.Mode, bill.Tender, bill.Reference, bill.Net.Value, bill.TaxBps, bill.Tax.Value,
            bill.Total.Value, bill.BusinessDate, bill.Status, settledUtc, settledBy, billReservation);
        return new CheckView(o.OrderId, o.OutletId, o.OutletName, spot, o.Guest, o.Covers, o.BusinessDate, o.Status, o.Version, o.PendingVoidId, views, kots,
            t.Net.Value, t.TaxBps, t.Tax.Value, t.Total.Value, items, billView, head.OpenedUtc, head.OpenedBy, head.Reason, head.VoidedUtc, head.Res);
    }

    public async Task<IReadOnlyList<OpenCheckSummary>> OpenChecksAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT o.order_id, ou.name, o.table_no, rm.room_no, o.guest, o.business_date, o.pending_void_id,
                   ISNULL(SUM(CASE WHEN l.voided = 0 THEN l.qty END), 0),
                   ISNULL(SUM(CASE WHEN l.voided = 0 THEN l.qty * l.price_paise END), 0),
                   MAX(CASE WHEN l.voided = 0 AND l.kot_id IS NULL THEN 1 ELSE 0 END)
            FROM pos.check_order o
            JOIN pos.outlet ou ON ou.outlet_id = o.outlet_id
            LEFT JOIN inv.room rm ON rm.room_id = o.room_id
            LEFT JOIN pos.order_line l ON l.order_id = o.order_id
            WHERE o.property_id = @p AND o.status = 'Open'
            GROUP BY o.order_id, ou.name, o.table_no, rm.room_no, o.guest, o.business_date, o.pending_void_id
            ORDER BY o.order_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new OpenCheckSummary(rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? "Room " + rd.GetString(3) : "Table " + rd.GetString(2), Str(rd, 4),
            rd.GetFieldValue<DateOnly>(5), (int)rd.GetInt32(7), rd.GetInt64(8), rd.GetInt32(9) == 1, rd.IsDBNull(6) ? null : rd.GetInt64(6)), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutletView>> OutletsAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT ou.outlet_id, ou.code, ou.name, ou.kind, t.table_no
            FROM pos.outlet ou LEFT JOIN pos.outlet_table t ON t.outlet_id = ou.outlet_id
            WHERE ou.property_id = @p ORDER BY ou.outlet_id, t.table_no;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = await ReadAsync(cmd, rd => (Id: rd.GetInt32(0), Code: rd.GetString(1), Name: rd.GetString(2), Kind: rd.GetString(3), Table: Str(rd, 4)), ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.Id).Select(g => new OutletView(g.Key, g.First().Code, g.First().Name, g.First().Kind, g.Where(r => r.Table is not null).Select(r => r.Table!).ToList())).ToList();
    }

    public async Task<IReadOnlyList<MenuItemView>> MenuAsync(int propertyId, int? outletId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT m.item_id, m.code, m.name, m.category, m.price_paise, m.veg, m.available, m.row_ver, mo.outlet_id
            FROM pos.menu_item m JOIN pos.menu_item_outlet mo ON mo.item_id = m.item_id
            WHERE m.property_id = @p AND (@o IS NULL OR m.item_id IN (SELECT item_id FROM pos.menu_item_outlet WHERE outlet_id = @o))
            ORDER BY m.category, m.name, m.item_id, mo.outlet_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@o", SqlDbType.Int, outletId);
        var rows = await ReadAsync(cmd, rd => (Id: rd.GetInt32(0), Code: rd.GetString(1), Name: rd.GetString(2), Cat: rd.GetString(3), Price: rd.GetInt64(4), Veg: rd.GetBoolean(5),
            Avail: rd.GetBoolean(6), Ver: Ver(rd, 7), Outlet: rd.GetInt32(8)), ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.Id).Select(g => new MenuItemView(g.Key, g.First().Code, g.First().Name, g.First().Cat, g.First().Price, g.First().Veg, g.First().Avail,
            g.Select(r => r.Outlet).ToList(), g.First().Ver)).ToList();
    }

    public async Task<IReadOnlyList<KitchenItem>> KitchenAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT k.kot_id, k.kot_no, k.status, k.created_utc, o.order_id, ou.name, o.table_no, rm.room_no,
                   l.line_id, l.item_id, l.name, l.price_paise, l.qty
            FROM pos.kot k
            JOIN pos.check_order o ON o.order_id = k.order_id
            JOIN pos.outlet ou ON ou.outlet_id = k.outlet_id
            JOIN inv.property pr ON pr.property_id = o.property_id
            LEFT JOIN inv.room rm ON rm.room_id = o.room_id
            JOIN pos.order_line l ON l.kot_id = k.kot_id AND l.voided = 0
            WHERE o.property_id = @p AND o.status <> 'Void' AND (k.status <> 'Served' OR k.business_date = pr.business_date)
            ORDER BY k.created_utc DESC, k.kot_no DESC, l.line_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = await ReadAsync(cmd, rd => (Kot: rd.GetInt64(0), No: rd.GetInt32(1), Status: rd.GetString(2), At: Dt(rd, 3)!.Value, Order: rd.GetInt64(4), Outlet: rd.GetString(5),
            Table: Str(rd, 6), Room: Str(rd, 7), Line: rd.GetInt64(8), Item: rd.GetInt32(9), Name: rd.GetString(10), Price: rd.GetInt64(11), Qty: (int)rd.GetInt16(12)), ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.Kot).Select(g =>
        {
            var f = g.First();
            return new KitchenItem(f.Order, f.Kot, f.No, PosRules.KotLabel(f.Outlet, f.No), f.Table is not null ? "Table " + f.Table : "Room " + f.Room, f.Status, f.At,
                g.Select(r => new LineView(r.Line, r.Item, r.Name, r.Price, r.Qty, r.Price * r.Qty, r.Kot, r.Status, false, null)).ToList());
        }).ToList();
    }

    public async Task<DaySummaryView> DaySummaryAsync(int propertyId, DateOnly date, int taxBps, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        List<(int Outlet, string Name, int Checks, int Covers, long Net, long Tax, long Total)> outlets;
        await using (var c = connections.Command("""
            SELECT ou.outlet_id, ou.name, COUNT(b.bill_id), ISNULL(SUM(o.covers), 0), ISNULL(SUM(b.net_paise), 0), ISNULL(SUM(b.tax_paise), 0), ISNULL(SUM(b.total_paise), 0)
            FROM pos.outlet ou
            LEFT JOIN pos.check_order o ON o.outlet_id = ou.outlet_id
            LEFT JOIN pos.bill b ON b.order_id = o.order_id AND b.status = 'Settled' AND b.business_date = @d
            WHERE ou.property_id = @p GROUP BY ou.outlet_id, ou.name ORDER BY ou.outlet_id;
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            // covers only count for settled checks: recomputed below to avoid summing covers of unsettled joins.
            outlets = await ReadAsync(c, rd => (rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), 0, rd.GetInt64(4), rd.GetInt64(5), rd.GetInt64(6)), ct).ConfigureAwait(false);
        }

        int covers;
        await using (var c = connections.Command("""
            SELECT ISNULL(SUM(o.covers), 0) FROM pos.bill b JOIN pos.check_order o ON o.order_id = b.order_id
            WHERE b.property_id = @p AND b.status = 'Settled' AND b.business_date = @d;
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            covers = Convert.ToInt32(await c.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        long cash = 0, upi = 0, card = 0, room = 0;
        await using (var c = connections.Command("""
            SELECT ISNULL(tender, 'Room'), SUM(total_paise) FROM pos.bill WHERE property_id = @p AND status = 'Settled' AND business_date = @d GROUP BY ISNULL(tender, 'Room');
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            foreach (var (mode, total) in await ReadAsync(c, rd => (rd.GetString(0), rd.GetInt64(1)), ct).ConfigureAwait(false))
            {
                switch (mode)
                {
                    case "Cash": cash = total; break;
                    case "UPI": upi = total; break;
                    case "Card": card = total; break;
                    default: room = total; break;
                }
            }
        }

        long outletCash;
        await using (var c = connections.Command("""
            SELECT ISNULL(SUM(r.amount_paise), 0) FROM pos.receipt r JOIN pos.outlet ou ON ou.outlet_id = r.outlet_id
            WHERE ou.property_id = @p AND r.business_date = @d AND r.tender = 'Cash';
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            outletCash = Convert.ToInt64(await c.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        int voidCount;
        long voidValue;
        await using (var c = connections.Command("""
            SELECT COUNT(*), ISNULL(SUM(l.qty * l.price_paise), 0) FROM pos.order_line l JOIN pos.check_order o ON o.order_id = l.order_id
            WHERE o.property_id = @p AND l.voided = 1 AND l.void_date = @d;
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (voidCount, voidValue) = (rd.GetInt32(0), rd.GetInt64(1));
        }

        var openByOutlet = new Dictionary<int, int>();
        var openValue = Paise.Zero;
        var openCount = 0;
        await using (var c = connections.Command("""
            SELECT o.outlet_id, ISNULL(SUM(CASE WHEN l.voided = 0 THEN l.qty * l.price_paise END), 0)
            FROM pos.check_order o LEFT JOIN pos.order_line l ON l.order_id = o.order_id
            WHERE o.property_id = @p AND o.status = 'Open' GROUP BY o.order_id, o.outlet_id;
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            foreach (var (outlet, net) in await ReadAsync(c, rd => (rd.GetInt32(0), rd.GetInt64(1)), ct).ConfigureAwait(false))
            {
                openCount++;
                openByOutlet[outlet] = openByOutlet.GetValueOrDefault(outlet) + 1;
                openValue += new Paise(net) + MoneyMath.TaxOf(new Paise(net), taxBps);
            }
        }

        List<TopItem> top;
        await using (var c = connections.Command("""
            SELECT TOP 5 MIN(l.name), SUM(l.qty), SUM(l.qty * l.price_paise)
            FROM pos.bill b JOIN pos.order_line l ON l.order_id = b.order_id AND l.voided = 0
            WHERE b.property_id = @p AND b.status = 'Settled' AND b.business_date = @d
            GROUP BY l.item_id ORDER BY SUM(l.qty) DESC, SUM(l.qty * l.price_paise) DESC, l.item_id;
            """, conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@d", SqlDbType.Date, date);
            top = await ReadAsync(c, rd => new TopItem(rd.GetString(0), rd.GetInt32(1), rd.GetInt64(2)), ct).ConfigureAwait(false);
        }

        int pending;
        await using (var c = connections.Command("SELECT COUNT(*) FROM pos.void_request WHERE property_id = @p AND status = 'Pending';", conn))
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            pending = (int)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        return new DaySummaryView(date, outlets.Sum(o => o.Checks), covers, outlets.Sum(o => o.Net), outlets.Sum(o => o.Tax), outlets.Sum(o => o.Total), cash, upi, card, room,
            outletCash, voidCount, voidValue, openCount, openValue.Value,
            outlets.Select(o => new DayOutletSummary(o.Outlet, o.Name, o.Checks, o.Net, o.Tax, o.Total, openByOutlet.GetValueOrDefault(o.Outlet))).ToList(), top, pending);
    }

    public async Task<PosCloseBlockers> BlockersAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT (SELECT COUNT(*) FROM pos.check_order WHERE property_id = @p AND status = 'Open'),
                   (SELECT COUNT(*) FROM pos.void_request WHERE property_id = @p AND status = 'Pending');
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new PosCloseBlockers(rd.GetInt32(0), rd.GetInt32(1));
    }

    public async Task<IReadOnlyList<long>> ClosedCheckIdsAsync(int propertyId, DateOnly date, int take, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@n) o.order_id
            FROM pos.check_order o LEFT JOIN pos.bill b ON b.order_id = o.order_id
            WHERE o.property_id = @p AND o.status <> 'Open' AND (b.business_date = @d OR o.business_date = @d)
            ORDER BY o.order_id DESC;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@n", SqlDbType.Int, take);
        P(cmd, "@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        return await ReadAsync(cmd, rd => rd.GetInt64(0), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VoidRequestView>> PendingVoidsAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT v.void_id, v.order_id, o.table_no, rm.room_no, v.scope, v.line_id, v.amount_paise, v.reason, v.requested_by, v.expires_utc, o.row_ver
            FROM pos.void_request v
            JOIN pos.check_order o ON o.order_id = v.order_id
            LEFT JOIN inv.room rm ON rm.room_id = o.room_id
            WHERE v.property_id = @p AND v.status = 'Pending'
            ORDER BY v.void_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new VoidRequestView(rd.GetInt64(0), rd.GetInt64(1), rd.IsDBNull(2) ? "Room " + rd.GetString(3) : "Table " + rd.GetString(2), rd.GetString(4),
            rd.IsDBNull(5) ? null : rd.GetInt64(5), rd.GetInt64(6), rd.GetString(7), rd.GetInt32(8), Dt(rd, 9)!.Value, Ver(rd, 10)), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PosStayView>> InHouseStaysAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.stay_id, s.folio_id, rm.room_id, rm.room_no, rs.guest_name, s.reservation_id
            FROM gst.stay s
            JOIN inv.reservation rs ON rs.reservation_id = s.reservation_id
            OUTER APPLY (SELECT TOP 1 sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC) cur
            JOIN inv.room rm ON rm.room_id = cur.room_id
            WHERE s.property_id = @p AND s.status = 'InHouse' ORDER BY rm.room_no;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new PosStayView(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(5), rd.GetString(3), rd.GetString(4)), ct).ConfigureAwait(false);
    }

    public async Task<PosTillView> TillAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (1) t.till_id, t.code, s.shift_id
            FROM pay.till t LEFT JOIN pay.shift s ON s.till_id = t.till_id AND s.status = 'Open'
            WHERE t.property_id = @p
            ORDER BY CASE WHEN s.shift_id IS NULL THEN 1 ELSE 0 END, t.till_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = await ReadAsync(cmd, rd => (Id: rd.GetInt32(0), Code: rd.GetString(1), Shift: rd.IsDBNull(2) ? (long?)null : rd.GetInt64(2)), ct).ConfigureAwait(false);
        return rows.Count == 0 ? new PosTillView(null, null, null, false) : new PosTillView(rows[0].Id, rows[0].Code, rows[0].Shift, rows[0].Shift is not null);
    }
}
