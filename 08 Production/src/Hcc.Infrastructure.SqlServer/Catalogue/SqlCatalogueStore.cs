using System.Data;
using System.Globalization;
using Hcc.Application.Catalogue;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Catalogue;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Catalogue;

/// <summary>Shared SQL of the catalogue adapters (migration 2820). Table and column names come only from <see cref="CatalogueMaster"/>.</summary>
internal static class CatSql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Catalogue SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static string Select(CatalogueMaster m, string lockHint) =>
        $"SELECT t.{m.IdColumn}, t.code, t.name, t.sort_order, t.active, t.retired_on, t.updated_utc, t.row_ver{string.Concat(m.Columns.Select(c => ", t." + c))} " +
        $"FROM {m.Table} t{lockHint} WHERE t.property_id = @p ORDER BY t.sort_order, t.code;" +
        (m.PriceTable is null ? "" : $" SELECT x.{m.IdColumn}, x.effective_from, x.amount_paise FROM {m.PriceTable} x JOIN {m.Table} t ON t.{m.IdColumn} = x.{m.IdColumn} WHERE t.property_id = @p ORDER BY x.effective_from;");

    /// <summary>Usage expression per master (alias t = the master row).</summary>
    public static string UsageSql(CatalogueMaster m) => m.Id switch
    {
        CatalogueIds.MealPlans => "(SELECT COUNT(*) FROM bill.reservation_meal_plan u WHERE u.meal_plan_id = t.meal_plan_id), (SELECT COUNT(*) FROM bill.charge_catalogue u WHERE u.meal_plan_id = t.meal_plan_id)",
        CatalogueIds.Extras => "(SELECT COUNT(*) FROM bill.charge_catalogue u WHERE u.extra_item_id = t.extra_item_id), 0",
        _ => "(SELECT COUNT(*) FROM bill.charge_catalogue u WHERE u.discount_reason_id = t.discount_reason_id), (SELECT COUNT(*) FROM bill.approval_discount_reason u WHERE u.discount_reason_id = t.discount_reason_id)",
    };

    public static (string A, string B) UsageLabels(CatalogueMaster m) => m.Id switch
    {
        CatalogueIds.MealPlans => ("Bookings", "Plan lines"),
        CatalogueIds.Extras => ("Folio lines", ""),
        _ => ("Discount lines", "Approval requests"),
    };

    public static async Task<List<CatRow>> ReadRowsAsync(SqlDataReader rd, CatalogueMaster m, CancellationToken ct)
    {
        var heads = new List<(int Id, string Code, string Name, int Sort, bool Active, DateOnly? Retired, DateTime Utc, byte[] Ver, Dictionary<string, string?> F)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var f = new Dictionary<string, string?>();
            for (var i = 0; i < m.Columns.Count; i++)
            {
                var o = 8 + i;
                f[m.Columns[i]] = rd.IsDBNull(o) ? null : CatalogueMaster.IsInt(m.Columns[i]) ? rd.GetInt32(o).ToString(CultureInfo.InvariantCulture) : rd.GetString(o);
            }

            heads.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)),
                DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), (byte[])rd[7], f));
        }

        var prices = new Dictionary<int, List<(DateOnly, long)>>();
        if (m.PriceTable is not null && await rd.NextResultAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt32(0);
                if (!prices.TryGetValue(id, out var l)) prices[id] = l = [];
                l.Add((DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetInt64(2)));
            }

        return heads.Select(h => new CatRow(h.Id, h.Code, h.Name, h.Sort, h.Active, h.Retired, h.Utc, h.Ver, h.F,
            prices.TryGetValue(h.Id, out var l) ? l : [])).ToList();
    }

    public static void Field(SqlCommand cmd, string column, string? value, int i)
    {
        if (CatalogueMaster.IsInt(column)) cmd.P("@c" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.Int, value is null ? null : int.Parse(value, CultureInfo.InvariantCulture));
        else cmd.P("@c" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.NVarChar, value ?? (column is "department" ? null : ""), 400);
    }
}

/// <summary>SQL adapter of <see cref="ICatalogueStore"/> (command transaction; master rows locked UPDLOCK + HOLDLOCK per property).</summary>
public sealed class SqlCatalogueStore : ICatalogueStore
{
    public async Task<IReadOnlyList<CatRow>> LockRowsAsync(ICommandTransaction tx, CatalogueMaster master, int propertyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var cmd = CatSql.Sql(tx).Command(CatSql.Select(master, " WITH (UPDLOCK, HOLDLOCK)"));
        cmd.P("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await CatSql.ReadRowsAsync(rd, master, ct).ConfigureAwait(false);
    }

    public async Task<int> InsertAsync(ICommandTransaction tx, CatalogueMaster master, int propertyId, CatRowInput row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(row);
        var cols = string.Concat(master.Columns.Select(c => ", " + c));
        var vals = string.Concat(master.Columns.Select((_, i) => ", @c" + i.ToString(CultureInfo.InvariantCulture)));
        await using var cmd = CatSql.Sql(tx).Command($"INSERT {master.Table}(property_id, code, name, sort_order, active, updated_by, updated_utc{cols}) OUTPUT INSERTED.{master.IdColumn} VALUES (@p, @code, @name, @s, 1, @u, @utc{vals});");
        cmd.P("@p", SqlDbType.Int, propertyId);
        Common(cmd, row, sortOrder, principalId, utc, master);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await WritePricesAsync(tx, master, id, row, ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, CatRowInput row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(row);
        var sets = string.Concat(master.Columns.Select((c, i) => $", {c} = @c{i.ToString(CultureInfo.InvariantCulture)}"));
        await using (var cmd = CatSql.Sql(tx).Command($"UPDATE {master.Table} SET code = @code, name = @name, sort_order = @s, updated_by = @u, updated_utc = @utc{sets} WHERE {master.IdColumn} = @id AND row_ver = @v;"))
        {
            Common(cmd, row, sortOrder, principalId, utc, master);
            cmd.P("@id", SqlDbType.Int, id);
            cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), $"This {master.Singular}");
        }

        if (master.PriceTable is null) return;
        await using (var del = CatSql.Sql(tx).Command($"DELETE {master.PriceTable} WHERE {master.IdColumn} = @id;"))
        {
            del.P("@id", SqlDbType.Int, id);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await WritePricesAsync(tx, master, id, row, ct).ConfigureAwait(false);
    }

    private static void Common(SqlCommand cmd, CatRowInput row, int sortOrder, int principalId, DateTime utc, CatalogueMaster master)
    {
        cmd.P("@code", SqlDbType.VarChar, row.Code, 20);
        cmd.P("@name", SqlDbType.NVarChar, row.Name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        for (var i = 0; i < master.Columns.Count; i++) CatSql.Field(cmd, master.Columns[i], row.Fields.TryGetValue(master.Columns[i], out var v) ? v : null, i);
    }

    private static async Task WritePricesAsync(ICommandTransaction tx, CatalogueMaster master, int id, CatRowInput row, CancellationToken ct)
    {
        if (master.PriceTable is null) return;
        foreach (var (from, amount) in row.Prices)
        {
            await using var cmd = CatSql.Sql(tx).Command($"INSERT {master.PriceTable}({master.IdColumn}, effective_from, amount_paise) VALUES (@id, @f, @a);");
            cmd.P("@id", SqlDbType.Int, id);
            cmd.P("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
            cmd.P("@a", SqlDbType.BigInt, amount);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task SetActiveAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var cmd = CatSql.Sql(tx).Command($"UPDATE {master.Table} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {master.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), $"This {master.Singular}");
    }

    public async Task SetSortAsync(ICommandTransaction tx, CatalogueMaster master, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var cmd = CatSql.Sql(tx).Command($"UPDATE {master.Table} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE {master.IdColumn} = @id;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, CatalogueMaster master, int id, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var cmd = CatSql.Sql(tx).Command($"DELETE {master.Table} WHERE {master.IdColumn} = @id AND row_ver = @v;");
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), $"This {master.Singular}");
    }

    public async Task<(int Count, IReadOnlyList<string> Where)> UsageAsync(ICommandTransaction tx, CatalogueMaster master, int id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var cmd = CatSql.Sql(tx).Command($"SELECT {CatSql.UsageSql(master)} FROM {master.Table} t WHERE t.{master.IdColumn} = @id;");
        cmd.P("@id", SqlDbType.Int, id);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return (0, []);
        var (la, lb) = CatSql.UsageLabels(master);
        int a = rd.GetInt32(0), b = rd.GetInt32(1);
        var where = new List<string>();
        if (a > 0) where.Add($"{la} {a.ToString(CultureInfo.InvariantCulture)}");
        if (b > 0) where.Add($"{lb} {b.ToString(CultureInfo.InvariantCulture)}");
        return (a + b, where);
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<IReadOnlyList<string>> RoleCodesAsync(ICommandTransaction tx, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("SELECT role_code FROM plat.user_role WHERE user_id = @u AND valid_from_utc <= @t AND (valid_until_utc IS NULL OR valid_until_utc > @t);");
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@t", SqlDbType.DateTime2, utc);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }

    public async Task<PlanReservation?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("""
            SELECT r.reservation_id, r.status, rt.code, rm.meal_plan_id, m.code
              FROM inv.reservation r WITH (UPDLOCK, ROWLOCK)
              LEFT JOIN inv.room rr ON rr.room_id = r.room_id
              LEFT JOIN inv.room_type rt ON rt.room_type_id = COALESCE(rr.room_type_id, r.room_type_id)
              LEFT JOIN bill.reservation_meal_plan rm ON rm.reservation_id = r.reservation_id
              LEFT JOIN bill.meal_plan m ON m.meal_plan_id = rm.meal_plan_id
             WHERE r.property_id = @p AND r.reservation_id = @r;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@r", SqlDbType.BigInt, reservationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new PlanReservation(rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), rd.IsDBNull(4) ? null : rd.GetString(4));
    }

    public async Task SetReservationPlanAsync(ICommandTransaction tx, long reservationId, int? mealPlanId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("""
            IF @m IS NULL DELETE bill.reservation_meal_plan WHERE reservation_id = @r;
            ELSE
            BEGIN
              UPDATE bill.reservation_meal_plan SET meal_plan_id = @m, updated_by = @u, updated_utc = @t WHERE reservation_id = @r;
              IF @@ROWCOUNT = 0 INSERT bill.reservation_meal_plan(reservation_id, meal_plan_id, updated_by, updated_utc) VALUES (@r, @m, @u, @t);
            END
            """);
        cmd.P("@r", SqlDbType.BigInt, reservationId);
        cmd.P("@m", SqlDbType.Int, mealPlanId);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@t", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task LinkChargeAsync(ICommandTransaction tx, long chargeId, int? extraItemId, int? mealPlanId, int? discountReasonId, int? quantity, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("INSERT bill.charge_catalogue(charge_id, extra_item_id, meal_plan_id, discount_reason_id, quantity) VALUES (@c, @e, @m, @d, @q);");
        cmd.P("@c", SqlDbType.BigInt, chargeId);
        cmd.P("@e", SqlDbType.Int, extraItemId);
        cmd.P("@m", SqlDbType.Int, mealPlanId);
        cmd.P("@d", SqlDbType.Int, discountReasonId);
        cmd.P("@q", SqlDbType.Int, quantity);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task LinkApprovalAsync(ICommandTransaction tx, long approvalId, int discountReasonId, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("INSERT bill.approval_discount_reason(approval_id, discount_reason_id) VALUES (@a, @d);");
        cmd.P("@a", SqlDbType.BigInt, approvalId);
        cmd.P("@d", SqlDbType.Int, discountReasonId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanNightCandidate>> PlanNightCandidatesAsync(ICommandTransaction tx, int propertyId, DateOnly serviceDate, CancellationToken ct = default)
    {
        // A night posted before the plan was chosen keeps no plan line (prototype: the plan applies from the next room night posted).
        await using var cmd = CatSql.Sql(tx).Command("""
            SELECT f.folio_id, r.reservation_id, m.meal_plan_id, m.code, m.gst_treatment, c.amount_paise, r.adults + r.children,
                   (SELECT TOP 1 pp.amount_paise FROM bill.meal_plan_price pp WHERE pp.meal_plan_id = m.meal_plan_id AND pp.effective_from <= @d ORDER BY pp.effective_from DESC)
              FROM bill.folio f WITH (UPDLOCK, HOLDLOCK)
              JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              JOIN bill.reservation_meal_plan rm ON rm.reservation_id = r.reservation_id
              JOIN bill.meal_plan m ON m.meal_plan_id = rm.meal_plan_id
              JOIN bill.charge c ON c.folio_id = f.folio_id AND c.kind = 'room' AND c.service_date = @d AND c.reverses_charge_id IS NULL
             WHERE r.property_id = @p AND r.status = 'InHouse' AND c.posted_utc >= rm.updated_utc
               AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id)
               AND NOT EXISTS (SELECT 1 FROM bill.meal_plan_night n WITH (UPDLOCK, HOLDLOCK) WHERE n.folio_id = f.folio_id AND n.service_date = @d)
             ORDER BY f.folio_id;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@d", SqlDbType.Date, serviceDate.ToDateTime(TimeOnly.MinValue));
        var list = new List<PlanNightCandidate>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new PlanNightCandidate(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetInt64(5), Math.Max(1, Convert.ToInt32(rd.GetValue(6), CultureInfo.InvariantCulture)),
                rd.IsDBNull(7) ? null : rd.GetInt64(7)));
        return list;
    }

    public async Task<bool> InsertPlanNightAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, long chargeId, CancellationToken ct = default)
    {
        await using var cmd = CatSql.Sql(tx).Command("""
            IF EXISTS (SELECT 1 FROM bill.meal_plan_night WITH (UPDLOCK, HOLDLOCK) WHERE folio_id = @f AND service_date = @d) SELECT 0;
            ELSE BEGIN INSERT bill.meal_plan_night(folio_id, service_date, charge_id) VALUES (@f, @d, @c); SELECT 1; END
            """);
        cmd.P("@f", SqlDbType.BigInt, folioId);
        cmd.P("@d", SqlDbType.Date, serviceDate.ToDateTime(TimeOnly.MinValue));
        cmd.P("@c", SqlDbType.BigInt, chargeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
    }
}

/// <summary>Read side of the catalogue (own connection).</summary>
public sealed class SqlCatalogueReadStore(SqlConnectionFactory connections) : ICatalogueReadStore
{
    public async Task<IReadOnlyList<(CatRow Row, int Usage)>> RowsAsync(CatalogueMaster master, int propertyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        List<CatRow> rows;
        await using (var cmd = connections.Command(CatSql.Select(master, ""), conn))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            rows = await CatSql.ReadRowsAsync(rd, master, ct).ConfigureAwait(false);
        }

        var usage = new Dictionary<int, int>();
        await using (var cmd = connections.Command($"SELECT t.{master.IdColumn}, {CatSql.UsageSql(master)} FROM {master.Table} t WHERE t.property_id = @p;", conn))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) usage[rd.GetInt32(0)] = rd.GetInt32(1) + rd.GetInt32(2);
        }

        return rows.Select(r => (r, usage.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<CatalogueLinks> LinksAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT r.reservation_id, m.code
              FROM bill.reservation_meal_plan rm JOIN inv.reservation r ON r.reservation_id = rm.reservation_id JOIN bill.meal_plan m ON m.meal_plan_id = rm.meal_plan_id
             WHERE r.property_id = @p AND r.departure >= @f AND r.arrival <= @t ORDER BY r.reservation_id;
            SELECT c.charge_id, cc.extra_item_id, m.code, d.code
              FROM bill.charge_catalogue cc JOIN bill.charge c ON c.charge_id = cc.charge_id JOIN bill.folio f ON f.folio_id = c.folio_id
              JOIN inv.reservation r ON r.reservation_id = f.reservation_id
              LEFT JOIN bill.meal_plan m ON m.meal_plan_id = cc.meal_plan_id LEFT JOIN bill.discount_reason d ON d.discount_reason_id = cc.discount_reason_id
             WHERE r.property_id = @p AND c.service_date BETWEEN @f AND @t
            UNION ALL
            SELECT c.charge_id, NULL, NULL, d.code
              FROM bill.charge c JOIN bill.approval_discount_reason ar ON ar.approval_id = c.approval_id JOIN bill.discount_reason d ON d.discount_reason_id = ar.discount_reason_id
              JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
             WHERE r.property_id = @p AND c.kind = 'discount' AND c.service_date BETWEEN @f AND @t
               AND NOT EXISTS (SELECT 1 FROM bill.charge_catalogue x WHERE x.charge_id = c.charge_id)
             ORDER BY 1;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        cmd.P("@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        var plans = new List<ReservationPlanLink>();
        var charges = new List<ChargeCatalogueLink>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) plans.Add(new ReservationPlanLink(rd.GetInt64(0), rd.GetString(1)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            charges.Add(new ChargeCatalogueLink(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetInt32(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3)));
        return new CatalogueLinks(plans, charges);
    }

    public async Task<DateOnly?> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT business_date FROM inv.property WHERE property_id = @p;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
    }
}
