using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Pos;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.Pos;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Pos;

/// <summary>
/// MA-4 POS masters on SQL Server (migration 2840): outlets, tables and counters, menu categories, menu items with dated prices, and the
/// shared change history (mst.master_history, MA-0). The Lock* reads take UPDLOCK + HOLDLOCK on the property's rows of one list, so code /
/// name uniqueness and reorder are decided on locked data; every edit is guarded by the row's rowversion (STALE_VERSION).
/// </summary>
public sealed partial class SqlPosStore : IPosMastersStore
{
    private static string WireDate(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Date(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetFieldValue<DateOnly>(i);

    private static byte[] RowVer(SqlDataReader rd, int i) => (byte[])rd[i];

    /// <summary>Locked FROM, id column and the property filter of each list.</summary>
    private static (string From, string Id, string Where) Shape(PosMasterTable t) => t switch
    {
        PosMasterTable.Outlet => ("pos.outlet x WITH (UPDLOCK, HOLDLOCK)", "x.outlet_id", "x.property_id = @p"),
        PosMasterTable.Table => ("pos.outlet_table x WITH (UPDLOCK, HOLDLOCK) JOIN pos.outlet o ON o.outlet_id = x.outlet_id", "x.table_id", "o.property_id = @p"),
        PosMasterTable.Category => ("pos.menu_category x WITH (UPDLOCK, HOLDLOCK)", "x.category_id", "x.property_id = @p"),
        _ => ("pos.menu_item x WITH (UPDLOCK, HOLDLOCK)", "x.item_id", "x.property_id = @p"),
    };

    private static (string Table, string Id) Target(PosMasterTable t) => t switch
    {
        PosMasterTable.Outlet => ("pos.outlet", "outlet_id"),
        PosMasterTable.Table => ("pos.outlet_table", "table_id"),
        PosMasterTable.Category => ("pos.menu_category", "category_id"),
        _ => ("pos.menu_item", "item_id"),
    };

    public async Task<IReadOnlyList<PosMasterBase>> LockBaseAsync(ICommandTransaction tx, PosMasterTable table, int propertyId, CancellationToken ct = default)
    {
        var (from, id, where) = Shape(table);
        await using var cmd = Cmd(tx, $"SELECT {id}, x.code, x.name, x.sort_order, x.active, x.retired_on, x.row_ver FROM {from} WHERE {where} ORDER BY x.sort_order, {id};");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new PosMasterBase(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), Date(rd, 5), RowVer(rd, 6)), ct).ConfigureAwait(false);
    }

    // ── Outlets ──

    public async Task<IReadOnlyList<OutletRec>> LockOutletsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT outlet_id, code, name, kind, outlet_type, opens_at, closes_at, gst_profile, kot_station, sort_order, active, retired_on, row_ver
            FROM pos.outlet WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p ORDER BY sort_order, outlet_id;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new OutletRec(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), Str(rd, 5), Str(rd, 6), rd.GetString(7),
            Str(rd, 8), rd.GetInt32(9), rd.GetBoolean(10), Date(rd, 11), RowVer(rd, 12)), ct).ConfigureAwait(false);
    }

    private static void OutletParams(SqlCommand cmd, OutletRec r, int principalId, DateTime utc)
    {
        P(cmd, "@c", SqlDbType.VarChar, r.Code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, r.Name, 100);
        P(cmd, "@k", SqlDbType.VarChar, r.Kind, 5);
        P(cmd, "@t", SqlDbType.VarChar, r.Type, 12);
        P(cmd, "@op", SqlDbType.Char, r.Opens, 5);
        P(cmd, "@cl", SqlDbType.Char, r.Closes, 5);
        P(cmd, "@g", SqlDbType.VarChar, r.GstProfile, 9);
        P(cmd, "@st", SqlDbType.NVarChar, r.KotStation, 20);
        P(cmd, "@s", SqlDbType.Int, r.SortOrder);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
    }

    public async Task<int> InsertOutletAsync(ICommandTransaction tx, int propertyId, OutletRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT pos.outlet(property_id, code, name, kind, created_utc, outlet_type, opens_at, closes_at, gst_profile, kot_station, sort_order, active, updated_by, updated_utc)
            OUTPUT INSERTED.outlet_id VALUES (@p, @c, @n, @k, @utc, @t, @op, @cl, @g, @st, @s, 1, @u, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        OutletParams(cmd, row, principalId, utc);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task UpdateOutletAsync(ICommandTransaction tx, OutletRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            UPDATE pos.outlet SET code = @c, name = @n, kind = @k, outlet_type = @t, opens_at = @op, closes_at = @cl, gst_profile = @g, kot_station = @st, sort_order = @s,
              updated_by = @u, updated_utc = @utc
            WHERE outlet_id = @id AND row_ver = @rv;
            """);
        OutletParams(cmd, row, principalId, utc);
        P(cmd, "@id", SqlDbType.Int, row.OutletId);
        P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This outlet");
    }

    // ── Tables and counters ──

    public async Task<IReadOnlyList<TableRec>> LockTablesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT t.table_id, t.outlet_id, t.code, t.name, t.table_no, t.seats, t.section, t.is_counter, t.sort_order, t.active, t.retired_on, t.row_ver
            FROM pos.outlet_table t WITH (UPDLOCK, HOLDLOCK) JOIN pos.outlet o ON o.outlet_id = t.outlet_id
            WHERE o.property_id = @p ORDER BY t.sort_order, t.table_id;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new TableRec(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.IsDBNull(5) ? null : rd.GetByte(5),
            Str(rd, 6), rd.GetBoolean(7), rd.GetInt32(8), rd.GetBoolean(9), Date(rd, 10), RowVer(rd, 11)), ct).ConfigureAwait(false);
    }

    private static void TableParams(SqlCommand cmd, TableRec r, int principalId, DateTime utc)
    {
        P(cmd, "@o", SqlDbType.Int, r.OutletId);
        P(cmd, "@c", SqlDbType.VarChar, r.Code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, r.Name, 60);
        P(cmd, "@no", SqlDbType.VarChar, r.TableNo, 10);
        P(cmd, "@seats", SqlDbType.TinyInt, r.Seats is { } s ? (byte)s : null);
        P(cmd, "@sec", SqlDbType.NVarChar, r.Section, 40);
        P(cmd, "@ctr", SqlDbType.Bit, r.Counter);
        P(cmd, "@s", SqlDbType.Int, r.SortOrder);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
    }

    public async Task<int> InsertTableAsync(ICommandTransaction tx, TableRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT pos.outlet_table(outlet_id, table_no, code, name, seats, section, is_counter, sort_order, active, updated_by, updated_utc)
            OUTPUT INSERTED.table_id VALUES (@o, @no, @c, @n, @seats, @sec, @ctr, @s, 1, @u, @utc);
            """);
        TableParams(cmd, row, principalId, utc);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task UpdateTableAsync(ICommandTransaction tx, TableRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            UPDATE pos.outlet_table SET outlet_id = @o, table_no = @no, code = @c, name = @n, seats = @seats, section = @sec, is_counter = @ctr, sort_order = @s,
              updated_by = @u, updated_utc = @utc
            WHERE table_id = @id AND row_ver = @rv;
            """);
        TableParams(cmd, row, principalId, utc);
        P(cmd, "@id", SqlDbType.Int, row.TableId);
        P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This table");
    }

    // ── Menu categories ──

    public async Task<IReadOnlyList<CategoryRec>> LockCategoriesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT category_id, code, name, sort_order, active, retired_on, row_ver FROM pos.menu_category WITH (UPDLOCK, HOLDLOCK)
            WHERE property_id = @p ORDER BY sort_order, category_id;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new CategoryRec(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), Date(rd, 5), RowVer(rd, 6)), ct).ConfigureAwait(false);
    }

    public async Task<int> InsertCategoryAsync(ICommandTransaction tx, int propertyId, CategoryRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT pos.menu_category(property_id, code, name, sort_order, active, updated_by, updated_utc) OUTPUT INSERTED.category_id VALUES (@p, @c, @n, @s, 1, @u, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.VarChar, row.Code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, row.Name, 60);
        P(cmd, "@s", SqlDbType.Int, row.SortOrder);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task UpdateCategoryAsync(ICommandTransaction tx, CategoryRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        // The item's category text follows the category name (W2-6 readers, reports).
        await using var cmd = Cmd(tx, """
            UPDATE pos.menu_category SET code = @c, name = @n, sort_order = @s, updated_by = @u, updated_utc = @utc WHERE category_id = @id AND row_ver = @rv;
            DECLARE @rows INT = @@ROWCOUNT;
            IF @rows = 1 UPDATE pos.menu_item SET category = @n WHERE category_id = @id AND category <> @n;
            SELECT @rows;
            """);
        P(cmd, "@c", SqlDbType.VarChar, row.Code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, row.Name, 60);
        P(cmd, "@s", SqlDbType.Int, row.SortOrder);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
        P(cmd, "@id", SqlDbType.Int, row.CategoryId);
        P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched((int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!, "This menu category");
    }

    /// <summary>W2-6 / import path: the category with this name (an active one first), or a new one (code from the name, unique).</summary>
    private async Task<int> EnsureCategoryAsync(ICommandTransaction tx, int propertyId, string name, CancellationToken ct)
    {
        var n = name.Length > 60 ? name[..60] : name;
        var cats = await LockCategoriesAsync(tx, propertyId, ct).ConfigureAwait(false);
        var hit = cats.Where(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => c.Active).FirstOrDefault();
        if (hit is not null) return hit.CategoryId;
        var baseCode = PosMasterRules.CodeOf(n);
        var code = baseCode;
        for (var i = 2; cats.Any(c => c.Code == code); i++)
        {
            var suffix = "-" + i.ToString(CultureInfo.InvariantCulture);
            code = (baseCode.Length + suffix.Length > MastersRules.CodeMax ? baseCode[..(MastersRules.CodeMax - suffix.Length)] : baseCode) + suffix;
        }

        await using var cmd = Cmd(tx, "INSERT pos.menu_category(property_id, code, name, sort_order, active) OUTPUT INSERTED.category_id VALUES (@p, @c, @n, @s, 1);");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.VarChar, code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, n, 60);
        P(cmd, "@s", SqlDbType.Int, MastersRules.NextSort(cats.Select(c => c.SortOrder)));
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    // ── Menu items ──

    public async Task<IReadOnlyList<ItemRec>> LockItemsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT item_id, code, name, category_id, category, short_code, gst_class, veg, station, sort_order, active, retired_on, available, sold_out_on, row_ver
            FROM pos.menu_item WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p ORDER BY sort_order, item_id;
            SELECT mo.item_id, mo.outlet_id FROM pos.menu_item_outlet mo JOIN pos.menu_item m ON m.item_id = mo.item_id WHERE m.property_id = @p ORDER BY mo.item_id, mo.outlet_id;
            SELECT x.item_id, x.effective_from, x.price_paise FROM pos.menu_price x JOIN pos.menu_item m ON m.item_id = x.item_id WHERE m.property_id = @p ORDER BY x.item_id, x.effective_from;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var heads = new List<(int Id, string Code, string Name, int? Cat, string CatText, string? Short, string Gst, bool Veg, string? Station, int Sort, bool Active, DateOnly? Retired,
            bool Avail, DateOnly? SoldOn, byte[] Ver)>();
        var outlets = new Dictionary<int, List<int>>();
        var prices = new Dictionary<int, List<(DateOnly, long)>>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                heads.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), rd.GetString(4), Str(rd, 5), rd.GetString(6), rd.GetBoolean(7),
                    Str(rd, 8), rd.GetInt32(9), rd.GetBoolean(10), Date(rd, 11), rd.GetBoolean(12), Date(rd, 13), RowVer(rd, 14)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt32(0);
                if (!outlets.TryGetValue(id, out var l)) outlets[id] = l = [];
                l.Add(rd.GetInt32(1));
            }

            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt32(0);
                if (!prices.TryGetValue(id, out var l)) prices[id] = l = [];
                l.Add((rd.GetFieldValue<DateOnly>(1), rd.GetInt64(2)));
            }
        }

        return heads.Select(h => new ItemRec(h.Id, h.Code, h.Name, h.Cat, h.CatText, h.Short, h.Gst, h.Veg, h.Station, h.Sort, h.Active, h.Retired, h.Avail, h.SoldOn,
            outlets.TryGetValue(h.Id, out var o) ? o : [], prices.TryGetValue(h.Id, out var p) ? p : [], h.Ver)).ToList();
    }

    private static void ItemParams(SqlCommand cmd, ItemRec r, DateOnly businessDate, int principalId, DateTime utc)
    {
        P(cmd, "@c", SqlDbType.VarChar, r.Code, 20);
        P(cmd, "@n", SqlDbType.NVarChar, r.Name, 100);
        P(cmd, "@g", SqlDbType.NVarChar, r.Category, 60);
        P(cmd, "@cat", SqlDbType.Int, r.CategoryId);
        P(cmd, "@a", SqlDbType.BigInt, PosMasterRules.PriceOn(r.Prices, businessDate) ?? 0);
        P(cmd, "@v", SqlDbType.Bit, r.Veg);
        P(cmd, "@av", SqlDbType.Bit, r.Available);
        P(cmd, "@so", SqlDbType.Date, r.SoldOutOn);
        P(cmd, "@sc", SqlDbType.VarChar, r.ShortCode, 10);
        P(cmd, "@gst", SqlDbType.VarChar, r.GstClass, 7);
        P(cmd, "@st", SqlDbType.NVarChar, r.Station, 20);
        P(cmd, "@s", SqlDbType.Int, r.SortOrder);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
    }

    private async Task SetPricesAsync(ICommandTransaction tx, int itemId, IReadOnlyList<(DateOnly From, long Paise)> prices, DateOnly from, int principalId, DateTime utc, CancellationToken ct)
    {
        // Amounts in effect before the business date are unchanged (checked by the service); the rest is replaced.
        await using (var del = Cmd(tx, "DELETE pos.menu_price WHERE item_id = @i AND effective_from >= @d;"))
        {
            P(del, "@i", SqlDbType.Int, itemId);
            P(del, "@d", SqlDbType.Date, from);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (f, paise) in prices.Where(x => x.From >= from))
        {
            await using var ins = Cmd(tx, "INSERT pos.menu_price(item_id, effective_from, price_paise, created_by, created_utc) VALUES (@i, @f, @a, @u, @utc);");
            P(ins, "@i", SqlDbType.Int, itemId);
            P(ins, "@f", SqlDbType.Date, f);
            P(ins, "@a", SqlDbType.BigInt, paise);
            P(ins, "@u", SqlDbType.Int, principalId);
            Utc(ins, "@utc", utc);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<int> InsertItemAsync(ICommandTransaction tx, int propertyId, ItemRec row, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        int id;
        await using (var cmd = Cmd(tx, """
            INSERT pos.menu_item(property_id, code, name, category, category_id, price_paise, veg, available, sold_out_on, short_code, gst_class, station, sort_order, active,
              updated_by, updated_utc)
            OUTPUT INSERTED.item_id VALUES (@p, @c, @n, @g, @cat, @a, @v, @av, @so, @sc, @gst, @st, @s, 1, @u, @utc);
            """))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            ItemParams(cmd, row, businessDate, principalId, utc);
            id = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await SetItemOutletsAsync(tx, id, row.OutletIds, ct).ConfigureAwait(false);
        await SetPricesAsync(tx, id, row.Prices, DateOnly.MinValue, principalId, utc, ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateItemAsync(ICommandTransaction tx, ItemRec row, byte[] expectedVersion, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using (var cmd = Cmd(tx, """
            UPDATE pos.menu_item SET code = @c, name = @n, category = @g, category_id = @cat, price_paise = @a, veg = @v, available = @av, sold_out_on = @so, short_code = @sc,
              gst_class = @gst, station = @st, sort_order = @s, updated_by = @u, updated_utc = @utc
            WHERE item_id = @id AND row_ver = @rv;
            """))
        {
            ItemParams(cmd, row, businessDate, principalId, utc);
            P(cmd, "@id", SqlDbType.Int, row.ItemId);
            P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The menu item");
        }

        await SetItemOutletsAsync(tx, row.ItemId, row.OutletIds, ct).ConfigureAwait(false);
        await SetPricesAsync(tx, row.ItemId, row.Prices, businessDate, principalId, utc, ct).ConfigureAwait(false);
    }

    // ── Generic status, order, delete, usage ──

    public async Task SetActiveAsync(ICommandTransaction tx, PosMasterTable table, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc,
        CancellationToken ct = default)
    {
        var (t, col) = Target(table);
        await using var cmd = Cmd(tx, $"UPDATE {t} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {col} = @id AND row_ver = @rv;");
        P(cmd, "@a", SqlDbType.Bit, active);
        P(cmd, "@r", SqlDbType.Date, retiredOn);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
        P(cmd, "@id", SqlDbType.Int, id);
        P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This row");
    }

    public async Task SetSortAsync(ICommandTransaction tx, PosMasterTable table, int id, int sortOrder, CancellationToken ct = default)
    {
        var (t, col) = Target(table);
        await using var cmd = Cmd(tx, $"UPDATE {t} SET sort_order = @s WHERE {col} = @id;");
        P(cmd, "@s", SqlDbType.Int, sortOrder);
        P(cmd, "@id", SqlDbType.Int, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, PosMasterTable table, int id, byte[] expectedVersion, CancellationToken ct = default)
    {
        var (t, col) = Target(table);
        var children = table == PosMasterTable.Item ? "DELETE pos.menu_item_outlet WHERE item_id = @id; DELETE pos.menu_price WHERE item_id = @id;" : string.Empty;
        await using var cmd = Cmd(tx, $"{children} DELETE {t} WHERE {col} = @id AND row_ver = @rv; SELECT @@ROWCOUNT;");
        P(cmd, "@id", SqlDbType.Int, id);
        P(cmd, "@rv", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched((int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!, "This row");
    }

    public async Task<PosMasterUsage> UsageAsync(ICommandTransaction tx, PosMasterTable table, int id, CancellationToken ct = default)
    {
        var sql = table switch
        {
            PosMasterTable.Outlet => """
                SELECT (SELECT COUNT(*) FROM pos.check_order WHERE outlet_id = @id), (SELECT COUNT(*) FROM pos.check_order WHERE outlet_id = @id AND status = 'Open'),
                       (SELECT COUNT(*) FROM pos.menu_item_outlet WHERE outlet_id = @id), (SELECT COUNT(*) FROM pos.outlet_table WHERE outlet_id = @id);
                """,
            PosMasterTable.Table => """
                SELECT COUNT(*), ISNULL(SUM(CASE WHEN c.status = 'Open' THEN 1 ELSE 0 END), 0), 0, 0
                FROM pos.outlet_table t JOIN pos.check_order c ON c.outlet_id = t.outlet_id AND c.table_no = t.table_no WHERE t.table_id = @id;
                """,
            PosMasterTable.Category => "SELECT (SELECT COUNT(*) FROM pos.menu_item WHERE category_id = @id), 0, 0, 0;",
            _ => """
                SELECT COUNT(*), ISNULL(SUM(CASE WHEN c.status = 'Open' THEN 1 ELSE 0 END), 0), 0, 0
                FROM pos.order_line l JOIN pos.check_order c ON c.order_id = l.order_id WHERE l.item_id = @id;
                """,
        };
        await using var cmd = Cmd(tx, sql);
        P(cmd, "@id", SqlDbType.Int, id);
        var r = (await ReadAsync(cmd, rd => (rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3)), ct).ConfigureAwait(false)).Single();
        var where = new List<string>();
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        switch (table)
        {
            case PosMasterTable.Outlet:
                if (r.Item1 > 0) where.Add("Checks " + N(r.Item1));
                if (r.Item3 > 0) where.Add("Menu items " + N(r.Item3));
                if (r.Item4 > 0) where.Add("Tables " + N(r.Item4));
                return new PosMasterUsage(r.Item1 + r.Item3 + r.Item4, r.Item2, where);
            case PosMasterTable.Category:
                if (r.Item1 > 0) where.Add("Menu items " + N(r.Item1));
                return new PosMasterUsage(r.Item1, 0, where);
            case PosMasterTable.Table:
                if (r.Item1 > 0) where.Add("Checks " + N(r.Item1));
                return new PosMasterUsage(r.Item1, r.Item2, where);
            default:
                if (r.Item1 > 0) where.Add("Check lines " + N(r.Item1));
                return new PosMasterUsage(r.Item1, r.Item2, where);
        }
    }

    public async Task AddHistoryAsync(ICommandTransaction tx, int propertyId, string master, long rowId, string? code, string field, string? oldValue, string? newValue, int principalId,
        DateTime utc, CancellationToken ct = default)
    {
        static string? Clip(string? v) => v is { Length: > 200 } ? v[..200] : v;
        await using var cmd = Cmd(tx, """
            INSERT mst.master_history(property_id, master, row_id, code, field, old_value, new_value, changed_by, changed_utc) VALUES (@p, @m, @r, @c, @f, @o, @n, @u, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@m", SqlDbType.VarChar, master, 30);
        P(cmd, "@r", SqlDbType.BigInt, rowId);
        P(cmd, "@c", SqlDbType.VarChar, code, 20);
        P(cmd, "@f", SqlDbType.VarChar, field, 40);
        P(cmd, "@o", SqlDbType.NVarChar, Clip(oldValue), 200);
        P(cmd, "@n", SqlDbType.NVarChar, Clip(newValue), 200);
        P(cmd, "@u", SqlDbType.Int, principalId);
        Utc(cmd, "@utc", utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MenuCategoryView>> CategoriesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT c.category_id, c.code, c.name, c.sort_order, c.active, c.retired_on, c.row_ver, (SELECT COUNT(*) FROM pos.menu_item m WHERE m.category_id = c.category_id)
            FROM pos.menu_category c WHERE c.property_id = @p ORDER BY c.sort_order, c.category_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await ReadAsync(cmd, rd => new MenuCategoryView(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4),
            Date(rd, 5) is { } d ? WireDate(d) : null, rd.GetInt32(7), Ver(rd, 6)), ct).ConfigureAwait(false);
    }

    // ── KOT stations and the GST basis of a check (IPosStore, MA-4) ──

    public async Task<IReadOnlyDictionary<long, string?>> LineStationsAsync(ICommandTransaction tx, long orderId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT l.line_id, m.station FROM pos.order_line l JOIN pos.menu_item m ON m.item_id = l.item_id WHERE l.order_id = @o;");
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        return (await ReadAsync(cmd, rd => (rd.GetInt64(0), Str(rd, 1)), ct).ConfigureAwait(false)).ToDictionary(x => x.Item1, x => x.Item2);
    }

    public async Task<(string GstProfile, string GstClass)> CheckGstBasisAsync(int propertyId, long orderId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT ou.gst_profile, (SELECT TOP 1 ISNULL(l.gst_class, 'food') FROM pos.order_line l WHERE l.order_id = o.order_id AND l.voided = 0 ORDER BY l.line_id)
            FROM pos.check_order o JOIN pos.outlet ou ON ou.outlet_id = o.outlet_id WHERE o.order_id = @o AND o.property_id = @p;
            """, conn);
        P(cmd, "@o", SqlDbType.BigInt, orderId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var r = (await ReadAsync(cmd, rd => (rd.GetString(0), Str(rd, 1) ?? "food"), ct).ConfigureAwait(false)).SingleOrDefault();
        return r.Item1 is null ? ("hotel", "food") : r;
    }
}
