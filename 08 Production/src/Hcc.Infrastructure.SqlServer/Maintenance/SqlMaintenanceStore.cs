using System.Data;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Maintenance;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Maintenance;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Maintenance;

/// <summary>Row mapping shared by the write and read stores (schema <c>mnt</c>, migration 1300).</summary>
internal static class MntSql
{
    internal const string OrderColumns = """
        w.wo_id, w.kind, w.title, w.priority, w.status, w.room_id, r.room_no, w.asset_id, w.area, w.assignee_id, w.vendor_id,
        w.created_utc, w.created_by, w.started_utc, w.done_utc, w.done_by, w.verified_utc, w.verified_by,
        w.hold_reason, w.awaiting_spare_id, w.note, w.return_reason, w.cancel_reason,
        w.repair_task_id, w.plan_id, w.due_date, w.downtime_from_utc, w.downtime_to_utc, w.block_id, w.row_ver
        """;

    internal const string OrderFrom = "FROM mnt.work_order w LEFT JOIN inv.room r ON r.room_id = w.room_id";

    internal static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    internal static DateTime? UtcOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : Utc(rd, i);

    internal static long? LongOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    internal static MntOrder ReadOrder(SqlDataReader rd, IReadOnlyList<MntPart> parts) => new(
        rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3).Trim(), rd.GetString(4),
        P3Sql.IntOrNull(rd, 5), P3Sql.StrOrNull(rd, 6), P3Sql.IntOrNull(rd, 7), P3Sql.StrOrNull(rd, 8), P3Sql.IntOrNull(rd, 9), P3Sql.IntOrNull(rd, 10),
        Utc(rd, 11), rd.GetInt32(12), UtcOrNull(rd, 13), UtcOrNull(rd, 14), P3Sql.IntOrNull(rd, 15), UtcOrNull(rd, 16), P3Sql.IntOrNull(rd, 17),
        P3Sql.StrOrNull(rd, 18), P3Sql.IntOrNull(rd, 19), P3Sql.StrOrNull(rd, 20), P3Sql.StrOrNull(rd, 21), P3Sql.StrOrNull(rd, 22),
        LongOrNull(rd, 23), P3Sql.IntOrNull(rd, 24), P3Sql.DateOrNull(rd, 25), UtcOrNull(rd, 26), UtcOrNull(rd, 27), LongOrNull(rd, 28),
        parts, (byte[])rd[29]);

    internal static DateTime? D(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    internal static async Task<byte[]> VersionAsync(SqlCommand cmd, string what, CancellationToken ct) =>
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[] ?? throw new HccException(HccErrorCode.STALE_VERSION, $"{what} was changed by someone else. Reload and try again.");

    internal static MntAsset ReadAsset(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), P3Sql.IntOrNull(rd, 3), P3Sql.StrOrNull(rd, 4), P3Sql.StrOrNull(rd, 5),
        rd.GetString(6), rd.GetString(7), P3Sql.DateOrNull(rd, 8), P3Sql.IntOrNull(rd, 9), rd.GetString(10), (byte[])rd[11]);

    internal const string AssetColumns = "a.asset_id, a.name, a.kind, a.room_id, r.room_no, a.area, a.make, a.model, a.installed_on, a.vendor_id, a.status, a.row_ver";

    internal static MntPlan ReadPlan(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetInt16(3), P3Sql.Date(rd, 4), rd.GetBoolean(5), P3Sql.DateOrNull(rd, 6), (byte[])rd[7]);

    internal const string PlanColumns = "plan_id, asset_id, title, every_days, next_due, active, last_done, row_ver";

    internal static MntSpare ReadSpare(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetInt32(4), rd.GetInt64(5), (byte[])rd[6]);

    internal const string SpareColumns = "spare_id, name, unit, stock, reorder_at, cost_paise, row_ver";
}

/// <summary>SQL adapter of <see cref="IMaintenanceStore"/>. Row locks: UPDLOCK; checks before inserts; versioned updates → STALE_VERSION.</summary>
public sealed class SqlMaintenanceStore : IMaintenanceStore
{
    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    // ── Work orders ──
    public async Task<MntOrder?> LockOrderAsync(ICommandTransaction tx, int propertyId, long workOrderId, CancellationToken ct = default)
    {
        var sql = P3Sql.Sql(tx);
        await using (var lockCmd = sql.Command("SELECT 1 FROM mnt.work_order WITH (UPDLOCK, ROWLOCK) WHERE wo_id = @id AND property_id = @p;"))
        {
            lockCmd.Add("@id", SqlDbType.BigInt, workOrderId);
            lockCmd.Add("@p", SqlDbType.Int, propertyId);
            if (await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is null) return null;
        }

        var parts = new List<MntPart>();
        await using (var pc = sql.Command("SELECT spare_id, qty, unit_cost_paise FROM mnt.work_order_part WHERE wo_id = @id ORDER BY spare_id, unit_cost_paise;"))
        {
            pc.Add("@id", SqlDbType.BigInt, workOrderId);
            await using var prd = await pc.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await prd.ReadAsync(ct).ConfigureAwait(false)) parts.Add(new MntPart(prd.GetInt32(0), prd.GetInt32(1), prd.GetInt64(2)));
        }

        await using var cmd = sql.Command($"SELECT {MntSql.OrderColumns} {MntSql.OrderFrom} WHERE w.wo_id = @id;");
        cmd.Add("@id", SqlDbType.BigInt, workOrderId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? MntSql.ReadOrder(rd, parts) : null;
    }

    public async Task<(long Id, byte[] Version)> InsertOrderAsync(ICommandTransaction tx, int propertyId, MntOrder draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        long id;
        byte[] version;
        await using (var cmd = P3Sql.Sql(tx).Command("""
            INSERT mnt.work_order(property_id, kind, title, priority, status, room_id, asset_id, area, assignee_id, vendor_id, created_utc, created_by,
              started_utc, note, repair_task_id, plan_id, due_date, downtime_from_utc)
            OUTPUT INSERTED.wo_id, INSERTED.row_ver
            VALUES (@p, @kind, @title, @prio, @status, @room, @asset, @area, @assignee, @vendor, @created, @by, @started, @note, @rt, @plan, @due, @down);
            """))
        {
            cmd.Add("@p", SqlDbType.Int, propertyId);
            cmd.Add("@kind", SqlDbType.VarChar, draft.Kind, 10);
            cmd.Add("@title", SqlDbType.NVarChar, draft.Title, 200);
            cmd.Add("@prio", SqlDbType.Char, draft.Priority, 2);
            cmd.Add("@status", SqlDbType.VarChar, draft.Status, 10);
            cmd.Add("@room", SqlDbType.Int, draft.RoomId);
            cmd.Add("@asset", SqlDbType.Int, draft.AssetId);
            cmd.Add("@area", SqlDbType.NVarChar, draft.Area, 60);
            cmd.Add("@assignee", SqlDbType.Int, draft.AssigneeId);
            cmd.Add("@vendor", SqlDbType.Int, draft.VendorId);
            cmd.Add("@created", SqlDbType.DateTime2, draft.CreatedUtc);
            cmd.Add("@by", SqlDbType.Int, draft.CreatedBy);
            cmd.Add("@started", SqlDbType.DateTime2, draft.StartedUtc);
            cmd.Add("@note", SqlDbType.NVarChar, draft.Note, 500);
            cmd.Add("@rt", SqlDbType.BigInt, draft.RepairTaskId);
            cmd.Add("@plan", SqlDbType.Int, draft.PlanId);
            cmd.Add("@due", SqlDbType.Date, MntSql.D(draft.DueDate));
            cmd.Add("@down", SqlDbType.DateTime2, draft.DowntimeFromUtc);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, version) = (rd.GetInt64(0), (byte[])rd[1]);
        }

        await ReplacePartsAsync(tx, id, draft.Parts, ct).ConfigureAwait(false);
        return (id, version);
    }

    private static async Task ReplacePartsAsync(ICommandTransaction tx, long id, IReadOnlyList<MntPart> parts, CancellationToken ct)
    {
        await using (var del = P3Sql.Sql(tx).Command("DELETE mnt.work_order_part WHERE wo_id = @id;"))
        {
            del.Add("@id", SqlDbType.BigInt, id);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var p in parts)
        {
            await using var ins = P3Sql.Sql(tx).Command("INSERT mnt.work_order_part(wo_id, spare_id, unit_cost_paise, qty) VALUES (@id, @s, @c, @q);");
            ins.Add("@id", SqlDbType.BigInt, id);
            ins.Add("@s", SqlDbType.Int, p.SpareId);
            ins.Add("@c", SqlDbType.BigInt, p.UnitCostPaise);
            ins.Add("@q", SqlDbType.Int, p.Qty);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<byte[]> UpdateOrderAsync(ICommandTransaction tx, MntOrder order, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        byte[] version;
        await using (var cmd = P3Sql.Sql(tx).Command("""
            UPDATE mnt.work_order SET title = @title, priority = @prio, status = @status, assignee_id = @assignee, vendor_id = @vendor,
              started_utc = @started, done_utc = @done, done_by = @doneBy, verified_utc = @ver, verified_by = @verBy,
              hold_reason = @hold, awaiting_spare_id = @spare, note = @note, return_reason = @ret, cancel_reason = @cancel,
              downtime_from_utc = @df, downtime_to_utc = @dt, block_id = @block
            OUTPUT INSERTED.row_ver
            WHERE wo_id = @id AND row_ver = @v;
            """))
        {
            cmd.Add("@title", SqlDbType.NVarChar, order.Title, 200);
            cmd.Add("@prio", SqlDbType.Char, order.Priority, 2);
            cmd.Add("@status", SqlDbType.VarChar, order.Status, 10);
            cmd.Add("@assignee", SqlDbType.Int, order.AssigneeId);
            cmd.Add("@vendor", SqlDbType.Int, order.VendorId);
            cmd.Add("@started", SqlDbType.DateTime2, order.StartedUtc);
            cmd.Add("@done", SqlDbType.DateTime2, order.DoneUtc);
            cmd.Add("@doneBy", SqlDbType.Int, order.DoneBy);
            cmd.Add("@ver", SqlDbType.DateTime2, order.VerifiedUtc);
            cmd.Add("@verBy", SqlDbType.Int, order.VerifiedBy);
            cmd.Add("@hold", SqlDbType.NVarChar, order.HoldReason, 200);
            cmd.Add("@spare", SqlDbType.Int, order.AwaitingSpareId);
            cmd.Add("@note", SqlDbType.NVarChar, order.Note, 500);
            cmd.Add("@ret", SqlDbType.NVarChar, order.ReturnReason, 500);
            cmd.Add("@cancel", SqlDbType.NVarChar, order.CancelReason, 200);
            cmd.Add("@df", SqlDbType.DateTime2, order.DowntimeFromUtc);
            cmd.Add("@dt", SqlDbType.DateTime2, order.DowntimeToUtc);
            cmd.Add("@block", SqlDbType.BigInt, order.BlockId);
            cmd.Add("@id", SqlDbType.BigInt, order.Id);
            cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
            version = await MntSql.VersionAsync(cmd, "This work order", ct).ConfigureAwait(false);
        }

        await ReplacePartsAsync(tx, order.Id, order.Parts, ct).ConfigureAwait(false);
        return version;
    }

    public async Task AddHistoryAsync(ICommandTransaction tx, long workOrderId, DateTime utc, int by, string action, string? note, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("INSERT mnt.work_order_history(wo_id, at_utc, by_user, action, note) VALUES (@id, @at, @by, @a, @n);");
        cmd.Add("@id", SqlDbType.BigInt, workOrderId);
        cmd.Add("@at", SqlDbType.DateTime2, utc);
        cmd.Add("@by", SqlDbType.Int, by);
        cmd.Add("@a", SqlDbType.VarChar, action, 40);
        cmd.Add("@n", SqlDbType.NVarChar, note is { Length: > 500 } ? note[..500] : note, 500);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> OrderExistsForRepairAsync(ICommandTransaction tx, long repairTaskId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM mnt.work_order WITH (UPDLOCK, HOLDLOCK) WHERE repair_task_id = @t;");
        cmd.Add("@t", SqlDbType.BigInt, repairTaskId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<bool> OccurrenceExistsAsync(ICommandTransaction tx, int planId, DateOnly due, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM mnt.work_order WITH (UPDLOCK, HOLDLOCK) WHERE plan_id = @p AND due_date = @d;");
        cmd.Add("@p", SqlDbType.Int, planId);
        cmd.Add("@d", SqlDbType.Date, MntSql.D(due));
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<bool> OtherActiveOrderOnAssetAsync(ICommandTransaction tx, int assetId, long exceptWorkOrderId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM mnt.work_order WHERE asset_id = @a AND wo_id <> @w AND status IN ('Open','InProgress','OnHold');");
        cmd.Add("@a", SqlDbType.Int, assetId);
        cmd.Add("@w", SqlDbType.BigInt, exceptWorkOrderId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    // ── Spares ──
    public async Task<MntSpare?> LockSpareAsync(ICommandTransaction tx, int propertyId, int spareId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {MntSql.SpareColumns} FROM mnt.spare WITH (UPDLOCK, ROWLOCK) WHERE spare_id = @id AND property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, spareId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? MntSql.ReadSpare(rd) : null;
    }

    public async Task<byte[]> SetSpareStockAsync(ICommandTransaction tx, int spareId, int stock, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE mnt.spare SET stock = @s OUTPUT INSERTED.row_ver WHERE spare_id = @id;");
        cmd.Add("@s", SqlDbType.Int, stock);
        cmd.Add("@id", SqlDbType.Int, spareId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    // ── Assets ──
    public async Task<MntAsset?> LockAssetAsync(ICommandTransaction tx, int propertyId, int assetId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {MntSql.AssetColumns} FROM mnt.asset a WITH (UPDLOCK, ROWLOCK) LEFT JOIN inv.room r ON r.room_id = a.room_id WHERE a.asset_id = @id AND a.property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, assetId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? MntSql.ReadAsset(rd) : null;
    }

    public async Task<(int Id, byte[] Version)> InsertAssetAsync(ICommandTransaction tx, int propertyId, MntAsset draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT mnt.asset(property_id, name, kind, room_id, area, make, model, installed_on, vendor_id, status)
            OUTPUT INSERTED.asset_id, INSERTED.row_ver VALUES (@p, @n, @k, @r, @a, @mk, @md, @i, @v, @s);
            """);
        AssetParams(cmd, draft);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), (byte[])rd[1]);
    }

    private static void AssetParams(SqlCommand cmd, MntAsset a)
    {
        cmd.Add("@n", SqlDbType.NVarChar, a.Name, 120);
        cmd.Add("@k", SqlDbType.VarChar, a.Kind, 12);
        cmd.Add("@r", SqlDbType.Int, a.RoomId);
        cmd.Add("@a", SqlDbType.NVarChar, a.Area, 60);
        cmd.Add("@mk", SqlDbType.NVarChar, a.Make, 60);
        cmd.Add("@md", SqlDbType.NVarChar, a.Model, 60);
        cmd.Add("@i", SqlDbType.Date, MntSql.D(a.InstalledOn));
        cmd.Add("@v", SqlDbType.Int, a.VendorId);
        cmd.Add("@s", SqlDbType.VarChar, a.Status, 8);
    }

    public async Task<byte[]> UpdateAssetAsync(ICommandTransaction tx, MntAsset asset, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE mnt.asset SET name = @n, kind = @k, room_id = @r, area = @a, make = @mk, model = @md, installed_on = @i, vendor_id = @v, status = @s
            OUTPUT INSERTED.row_ver WHERE asset_id = @id AND row_ver = @ver;
            """);
        AssetParams(cmd, asset);
        cmd.Add("@id", SqlDbType.Int, asset.Id);
        cmd.Add("@ver", SqlDbType.Binary, expectedVersion, 8);
        return await MntSql.VersionAsync(cmd, "This asset", ct).ConfigureAwait(false);
    }

    public async Task SetAssetStatusAsync(ICommandTransaction tx, int assetId, string status, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE mnt.asset SET status = @s WHERE asset_id = @id;");
        cmd.Add("@s", SqlDbType.VarChar, status, 8);
        cmd.Add("@id", SqlDbType.Int, assetId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Vendors ──
    public async Task<bool> VendorExistsAsync(ICommandTransaction tx, int propertyId, int vendorId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM mnt.vendor WHERE vendor_id = @id AND property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, vendorId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<MntVendor?> LockVendorAsync(ICommandTransaction tx, int propertyId, int vendorId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT vendor_id, name, trade, phone, row_ver FROM mnt.vendor WITH (UPDLOCK, ROWLOCK) WHERE vendor_id = @id AND property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, vendorId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new MntVendor(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), (byte[])rd[4]) : null;
    }

    public async Task<(int Id, byte[] Version)> InsertVendorAsync(ICommandTransaction tx, int propertyId, MntVendor draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var cmd = P3Sql.Sql(tx).Command("INSERT mnt.vendor(property_id, name, trade, phone) OUTPUT INSERTED.vendor_id, INSERTED.row_ver VALUES (@p, @n, @t, @ph);");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@n", SqlDbType.NVarChar, draft.Name, 120);
        cmd.Add("@t", SqlDbType.NVarChar, draft.Trade, 60);
        cmd.Add("@ph", SqlDbType.VarChar, draft.Phone, 30);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), (byte[])rd[1]);
    }

    public async Task<byte[]> UpdateVendorAsync(ICommandTransaction tx, MntVendor vendor, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE mnt.vendor SET name = @n, trade = @t, phone = @ph OUTPUT INSERTED.row_ver WHERE vendor_id = @id AND row_ver = @v;");
        cmd.Add("@n", SqlDbType.NVarChar, vendor.Name, 120);
        cmd.Add("@t", SqlDbType.NVarChar, vendor.Trade, 60);
        cmd.Add("@ph", SqlDbType.VarChar, vendor.Phone, 30);
        cmd.Add("@id", SqlDbType.Int, vendor.Id);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        return await MntSql.VersionAsync(cmd, "This vendor", ct).ConfigureAwait(false);
    }

    // ── AMC ──
    public async Task<MntAmc?> LockAmcAsync(ICommandTransaction tx, int propertyId, int amcId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT amc_id, vendor_id, scope, from_date, to_date, visits, value_paise, row_ver FROM mnt.amc WITH (UPDLOCK, ROWLOCK) WHERE amc_id = @id AND property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, amcId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new MntAmc(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), P3Sql.Date(rd, 3), P3Sql.Date(rd, 4), rd.GetByte(5), rd.GetInt64(6), (byte[])rd[7]) : null;
    }

    public async Task<(int Id, byte[] Version)> InsertAmcAsync(ICommandTransaction tx, int propertyId, MntAmc draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT mnt.amc(property_id, vendor_id, scope, from_date, to_date, visits, value_paise)
            OUTPUT INSERTED.amc_id, INSERTED.row_ver VALUES (@p, @v, @s, @f, @t, @vis, @val);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        AmcParams(cmd, draft);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), (byte[])rd[1]);
    }

    private static void AmcParams(SqlCommand cmd, MntAmc a)
    {
        cmd.Add("@v", SqlDbType.Int, a.VendorId);
        cmd.Add("@s", SqlDbType.NVarChar, a.Scope, 200);
        cmd.Add("@f", SqlDbType.Date, MntSql.D(a.From));
        cmd.Add("@t", SqlDbType.Date, MntSql.D(a.To));
        cmd.Add("@vis", SqlDbType.TinyInt, (byte)a.Visits);
        cmd.Add("@val", SqlDbType.BigInt, a.ValuePaise);
    }

    public async Task<byte[]> UpdateAmcAsync(ICommandTransaction tx, MntAmc amc, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(amc);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE mnt.amc SET vendor_id = @v, scope = @s, from_date = @f, to_date = @t, visits = @vis, value_paise = @val
            OUTPUT INSERTED.row_ver WHERE amc_id = @id AND row_ver = @ver;
            """);
        AmcParams(cmd, amc);
        cmd.Add("@id", SqlDbType.Int, amc.Id);
        cmd.Add("@ver", SqlDbType.Binary, expectedVersion, 8);
        return await MntSql.VersionAsync(cmd, "This AMC", ct).ConfigureAwait(false);
    }

    // ── PM plans ──
    public async Task<MntPlan?> LockPlanAsync(ICommandTransaction tx, int propertyId, int planId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {MntSql.PlanColumns} FROM mnt.pm_plan WITH (UPDLOCK, ROWLOCK) WHERE plan_id = @id AND property_id = @p;");
        cmd.Add("@id", SqlDbType.Int, planId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? MntSql.ReadPlan(rd) : null;
    }

    public async Task<IReadOnlyList<MntPlan>> LockActivePlansAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {MntSql.PlanColumns} FROM mnt.pm_plan WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND active = 1 ORDER BY plan_id;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var list = new List<MntPlan>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(MntSql.ReadPlan(rd));
        return list;
    }

    public async Task<(int Id, byte[] Version)> InsertPlanAsync(ICommandTransaction tx, int propertyId, MntPlan draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT mnt.pm_plan(property_id, asset_id, title, every_days, next_due, active)
            OUTPUT INSERTED.plan_id, INSERTED.row_ver VALUES (@p, @a, @t, @e, @n, @act);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        PlanParams(cmd, draft);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt32(0), (byte[])rd[1]);
    }

    private static void PlanParams(SqlCommand cmd, MntPlan p)
    {
        cmd.Add("@a", SqlDbType.Int, p.AssetId);
        cmd.Add("@t", SqlDbType.NVarChar, p.Title, 200);
        cmd.Add("@e", SqlDbType.SmallInt, (short)p.EveryDays);
        cmd.Add("@n", SqlDbType.Date, MntSql.D(p.NextDue));
        cmd.Add("@act", SqlDbType.Bit, p.Active);
    }

    public async Task<byte[]> UpdatePlanAsync(ICommandTransaction tx, MntPlan plan, byte[] expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE mnt.pm_plan SET asset_id = @a, title = @t, every_days = @e, next_due = @n, active = @act
            OUTPUT INSERTED.row_ver WHERE plan_id = @id AND row_ver = @v;
            """);
        PlanParams(cmd, plan);
        cmd.Add("@id", SqlDbType.Int, plan.Id);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        return await MntSql.VersionAsync(cmd, "This plan", ct).ConfigureAwait(false);
    }

    public async Task SetPlanLastDoneAsync(ICommandTransaction tx, int planId, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE mnt.pm_plan SET last_done = @d WHERE plan_id = @id;");
        cmd.Add("@d", SqlDbType.Date, MntSql.D(date));
        cmd.Add("@id", SqlDbType.Int, planId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Housekeeping repair tasks (ops.hk_task kind repair) ──
    public async Task<RepairTaskRecord?> LockRepairTaskAsync(ICommandTransaction tx, int propertyId, long taskId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT t.task_id, t.room_id, r.room_no, t.status, t.assignee_id, t.note, t.created_utc, t.started_utc
            FROM ops.hk_task t WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = t.room_id
            WHERE t.task_id = @t AND t.property_id = @p AND t.kind = 'repair';
            """);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new RepairTaskRecord(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), P3Sql.IntOrNull(rd, 4), P3Sql.StrOrNull(rd, 5), MntSql.Utc(rd, 6), MntSql.UtcOrNull(rd, 7)) : null;
    }

    public async Task SetRepairAssigneeAsync(ICommandTransaction tx, long taskId, int assigneeId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE ops.hk_task SET assignee_id = @a WHERE task_id = @t;");
        cmd.Add("@a", SqlDbType.Int, assigneeId);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetRepairStartedAsync(ICommandTransaction tx, long taskId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE ops.hk_task SET status = 'InProgress', started_utc = @u WHERE task_id = @t AND status = 'ToDo';");
        cmd.Add("@u", SqlDbType.DateTime2, utc);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetRepairDoneAsync(ICommandTransaction tx, long taskId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE ops.hk_task SET status = 'Done', started_utc = ISNULL(started_utc, @u), completed_utc = @u WHERE task_id = @t AND status <> 'Done';");
        cmd.Add("@u", SqlDbType.DateTime2, utc);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Staff (interim: role MNT, enabled) ──
    private const string TechnicianFrom = """
        FROM plat.app_user u JOIN plat.user_role ur ON ur.user_id = u.user_id AND ur.role_code = 'MNT'
        WHERE u.property_id = @p AND u.enabled = 1
        """;

    public async Task<bool> IsTechnicianAsync(ICommandTransaction tx, int propertyId, int userId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT COUNT(*) {TechnicianFrom} AND u.user_id = @u;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@u", SqlDbType.Int, userId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<int?> FirstTechnicianAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT TOP (1) u.user_id {TechnicianFrom} ORDER BY u.user_id;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as int?;
    }
}

/// <summary>Read side of Maintenance. Selects money columns (part cost, AMC value, spare cost); the application layer strips them for readers without finance rights.</summary>
public sealed class SqlMaintenanceReadStore(SqlConnectionFactory connections) : IMaintenanceReadStore
{
    public async Task<MntSnapshot> LoadAsync(int propertyId, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        DateOnly bd;
        await using (var c = connections.Command(P3Sql.BusinessDateSql, conn)) bd = await P3Sql.BusinessDateAsync(c, propertyId, ct).ConfigureAwait(false);

        var parts = new Dictionary<long, List<MntPart>>();
        await using (var c = connections.Command("SELECT p.wo_id, p.spare_id, p.qty, p.unit_cost_paise FROM mnt.work_order_part p JOIN mnt.work_order w ON w.wo_id = p.wo_id WHERE w.property_id = @p;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!parts.TryGetValue(rd.GetInt64(0), out var l)) parts[rd.GetInt64(0)] = l = [];
                l.Add(new MntPart(rd.GetInt32(1), rd.GetInt32(2), rd.GetInt64(3)));
            }
        }

        var orders = new List<MntOrder>();
        await using (var c = connections.Command($"SELECT {MntSql.OrderColumns} {MntSql.OrderFrom} WHERE w.property_id = @p ORDER BY w.wo_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                orders.Add(MntSql.ReadOrder(rd, parts.TryGetValue(rd.GetInt64(0), out var l) ? l : []));
        }

        var assets = new List<MntAsset>();
        await using (var c = connections.Command($"SELECT {MntSql.AssetColumns} FROM mnt.asset a LEFT JOIN inv.room r ON r.room_id = a.room_id WHERE a.property_id = @p ORDER BY a.asset_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) assets.Add(MntSql.ReadAsset(rd));
        }

        var vendors = new List<MntVendor>();
        await using (var c = connections.Command("SELECT vendor_id, name, trade, phone, row_ver FROM mnt.vendor WHERE property_id = @p ORDER BY vendor_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) vendors.Add(new MntVendor(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), (byte[])rd[4]));
        }

        var spares = new List<MntSpare>();
        await using (var c = connections.Command($"SELECT {MntSql.SpareColumns} FROM mnt.spare WHERE property_id = @p ORDER BY spare_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) spares.Add(MntSql.ReadSpare(rd));
        }

        var plans = new List<MntPlan>();
        await using (var c = connections.Command($"SELECT {MntSql.PlanColumns} FROM mnt.pm_plan WHERE property_id = @p ORDER BY plan_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) plans.Add(MntSql.ReadPlan(rd));
        }

        var amcs = new List<MntAmc>();
        await using (var c = connections.Command("SELECT amc_id, vendor_id, scope, from_date, to_date, visits, value_paise, row_ver FROM mnt.amc WHERE property_id = @p ORDER BY amc_id;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                amcs.Add(new MntAmc(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), P3Sql.Date(rd, 3), P3Sql.Date(rd, 4), rd.GetByte(5), rd.GetInt64(6), (byte[])rd[7]));
        }

        var blocked = new HashSet<int>();
        await using (var c = connections.Command("SELECT room_id FROM ops.room_block WHERE property_id = @p AND released_utc IS NULL;", conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) blocked.Add(rd.GetInt32(0));
        }

        int pending;
        await using (var c = connections.Command("""
            SELECT COUNT(*) FROM ops.hk_task t WHERE t.property_id = @p AND t.kind = 'repair' AND t.status <> 'Done'
              AND NOT EXISTS (SELECT 1 FROM mnt.work_order w WHERE w.repair_task_id = t.task_id);
            """, conn))
        {
            c.Add("@p", SqlDbType.Int, propertyId);
            pending = (int)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        return new MntSnapshot(bd, orders, assets, vendors, spares, plans, amcs, blocked, pending);
    }

    public async Task<IReadOnlyList<MntHistoryRow>> HistoryAsync(long workOrderId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c = connections.Command("SELECT at_utc, by_user, action, note FROM mnt.work_order_history WHERE wo_id = @id ORDER BY history_id;", conn);
        c.Add("@id", SqlDbType.BigInt, workOrderId);
        var list = new List<MntHistoryRow>();
        await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new MntHistoryRow(MntSql.Utc(rd, 0), rd.GetInt32(1), rd.GetString(2), P3Sql.StrOrNull(rd, 3)));
        return list;
    }

    public async Task<ISet<int>> OwnOpenTaskRoomsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c = connections.Command("SELECT DISTINCT room_id FROM ops.hk_task WHERE property_id = @p AND assignee_id = @u AND status <> 'Done';", conn);
        c.Add("@p", SqlDbType.Int, propertyId);
        c.Add("@u", SqlDbType.Int, principalId);
        var set = new HashSet<int>();
        await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetInt32(0));
        return set;
    }
}

/// <summary>Assigned scope "workOrder" (RBAC scope A): the work order is assigned to the principal (own technician work orders).</summary>
public sealed class SqlMaintenanceScopeResolver(SqlConnectionFactory connections) : IAccessScopeResolver
{
    public string Kind => AccessScopeKinds.WorkOrder;

    public async Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind != Kind) return false;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT COUNT(*) FROM mnt.work_order WHERE wo_id = @id AND property_id = @p AND assignee_id = @u;", conn);
        cmd.Add("@id", SqlDbType.BigInt, scope.TargetId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@u", SqlDbType.Int, principalId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }
}
