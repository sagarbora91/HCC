using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Stays;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Stays;
using Hcc.Domain.Dates;
using Hcc.Domain.Housekeeping;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Infrastructure.SqlServer.Sql;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Housekeeping;

/// <summary>SQL adapter of <see cref="IHousekeepingStore"/> (ops.hk_task, ops.room_block, inv.room readiness).</summary>
public sealed class SqlHousekeepingStore : IHousekeepingStore
{
    internal const string TaskColumns = """
        t.task_id, t.room_id, r.room_no, t.kind, t.status, t.assignee_id, t.checklist_mask, t.note, t.reject_reason,
        t.business_date, t.started_utc, t.completed_utc, t.row_ver
        """;

    internal static HousekeepingTaskView ReadTaskView(SqlDataReader rd)
    {
        var mask = (int)rd.GetByte(6);
        return new HousekeepingTaskView(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), P3Sql.IntOrNull(rd, 5),
            Enumerable.Range(0, HkTaskMachine.ChecklistItems).Select(i => (mask & (1 << i)) != 0).ToList(),
            P3Sql.StrOrNull(rd, 7), P3Sql.StrOrNull(rd, 8), StayDates.ToWire(P3Sql.Date(rd, 9)),
            Utc(rd, 10), Utc(rd, 11), VersionToken.ToWire((byte[])rd[12]));
    }

    private static string? Utc(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<HkTaskRecord?> LockTaskAsync(ICommandTransaction tx, int propertyId, long taskId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT t.task_id, t.property_id, t.room_id, r.room_no, t.kind, t.status, t.checklist_mask, t.assignee_id, t.reject_reason, t.row_ver
            FROM ops.hk_task t WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = t.room_id
            WHERE t.task_id = @t AND t.property_id = @p;
            """);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new HkTaskRecord(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetByte(6),
            P3Sql.IntOrNull(rd, 7), P3Sql.StrOrNull(rd, 8), (byte[])rd[9]);
    }

    public async Task UpdateTaskAsync(ICommandTransaction tx, long taskId, byte[] expectedVersion, HkTransition t, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE ops.hk_task SET status = @s, checklist_mask = @m, assignee_id = @a, reject_reason = @rr,
              started_utc = CASE WHEN @start = 1 THEN @utc ELSE started_utc END,
              completed_utc = CASE WHEN @done = 1 THEN @utc WHEN @clear = 1 THEN NULL ELSE completed_utc END,
              inspected_utc = CASE WHEN @insp = 1 THEN @utc ELSE inspected_utc END,
              inspected_by = CASE WHEN @insp = 1 THEN @u ELSE inspected_by END
            WHERE task_id = @t AND row_ver = @v;
            """);
        cmd.Add("@s", SqlDbType.VarChar, t.Task.Status, 12);
        cmd.Add("@m", SqlDbType.TinyInt, (byte)t.Task.ChecklistMask);
        cmd.Add("@a", SqlDbType.Int, t.Task.AssigneeId);
        cmd.Add("@rr", SqlDbType.NVarChar, t.Task.RejectReason, 500);
        cmd.Add("@start", SqlDbType.Bit, t.SetStarted);
        cmd.Add("@done", SqlDbType.Bit, t.SetCompleted);
        cmd.Add("@clear", SqlDbType.Bit, t.ClearCompleted);
        cmd.Add("@insp", SqlDbType.Bit, t.SetInspected);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This task");
    }

    public async Task<HousekeepingTaskView> TaskViewAsync(ICommandTransaction tx, long taskId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {TaskColumns} FROM ops.hk_task t JOIN inv.room r ON r.room_id = t.room_id WHERE t.task_id = @t;");
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return ReadTaskView(rd);
    }

    public async Task<HkRoomRecord?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT room_id, property_id, room_no, hk_status FROM inv.room WITH (UPDLOCK, ROWLOCK) WHERE room_id = @r AND property_id = @p;");
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new HkRoomRecord(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3)) : null;
    }

    public async Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE inv.room SET hk_status = @hk WHERE room_id = @r;");
        cmd.Add("@hk", SqlDbType.VarChar, hk, 12);
        cmd.Add("@r", SqlDbType.Int, roomId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HasOpenTaskAssignedAsync(ICommandTransaction tx, int roomId, int principalId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM ops.hk_task WHERE room_id = @r AND assignee_id = @u AND status <> 'Done';");
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@u", SqlDbType.Int, principalId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<bool> HasOpenRepairAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT COUNT(*) FROM ops.hk_task WITH (UPDLOCK, HOLDLOCK) WHERE room_id = @r AND kind = 'repair' AND status <> 'Done';");
        cmd.Add("@r", SqlDbType.Int, roomId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<long> InsertTaskAsync(ICommandTransaction tx, int propertyId, int roomId, string kind, string? note, DateOnly businessDate, DateTime utc, CancellationToken ct = default)
    {
        var sql = P3Sql.Sql(tx);
        return await InsertTaskAsync(sql.Command(InsertTaskSql), propertyId, roomId, kind, note, null, null, businessDate, utc, ct).ConfigureAwait(false);
    }

    internal const string InsertTaskSql = """
        INSERT ops.hk_task(property_id, room_id, kind, status, note, stay_id, causal_key, business_date, created_utc)
        OUTPUT INSERTED.task_id VALUES (@p, @r, @k, 'ToDo', @n, @s, @ck, @bd, @utc);
        """;

    internal static async Task<long> InsertTaskAsync(SqlCommand command, int propertyId, int roomId, string kind, string? note, long? stayId, string? causalKey, DateOnly businessDate, DateTime utc, CancellationToken ct)
    {
        await using var cmd = command;
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@k", SqlDbType.VarChar, kind, 10);
        cmd.Add("@n", SqlDbType.NVarChar, note, 500);
        cmd.Add("@s", SqlDbType.BigInt, stayId);
        cmd.Add("@ck", SqlDbType.VarChar, causalKey, 80);
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<RoomBlockRecord?> LockActiveBlockAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command($"SELECT {SqlRoomBlockReader.BlockColumns} FROM ops.room_block WITH (UPDLOCK, HOLDLOCK) WHERE room_id = @r AND released_utc IS NULL;");
        cmd.Add("@r", SqlDbType.Int, roomId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? SqlRoomBlockReader.ReadBlock(rd, 0) : null;
    }

    public async Task<int> AllocationsOverlappingAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly? to, DateTime utcNow, CancellationToken ct = default)
    {
        // Same holder rule as the prototype's hkHoldsInventory: Confirmed, InHouse, or Held whose hold has not lapsed.
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT COUNT(DISTINCT n.reservation_id)
            FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
            WHERE n.room_id = @room AND n.night >= @from AND (@to IS NULL OR n.night < @to)
              AND (r.status IN ('Confirmed','InHouse') OR (r.status = 'Held' AND (r.hold_until_utc IS NULL OR r.hold_until_utc > @now)));
            """);
        cmd.Add("@room", SqlDbType.Int, roomId);
        cmd.Add("@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@to", SqlDbType.Date, to?.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@now", SqlDbType.DateTime2, utcNow);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<(long BlockId, byte[] Version)> InsertBlockAsync(ICommandTransaction tx, int propertyId, int roomId, DateOnly from, DateOnly? to, string reason, string source, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc)
            OUTPUT INSERTED.block_id, INSERTED.row_ver VALUES (@p, @r, @f, @t, @reason, @src, @u, @utc);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@t", SqlDbType.Date, to?.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@reason", SqlDbType.NVarChar, reason, 200);
        cmd.Add("@src", SqlDbType.VarChar, source, 12);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), (byte[])rd[1]);
    }

    public async Task ReleaseBlockAsync(ICommandTransaction tx, long blockId, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE ops.room_block SET released_utc = @utc, released_by = @u WHERE block_id = @b AND row_ver = @v AND released_utc IS NULL;");
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@b", SqlDbType.BigInt, blockId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This block");
    }
}

/// <summary>
/// Reads ops.room_block (housekeeping's block record; the board and check-in). The inventory effect of a block lives in
/// P2's inv.room_block, written through <see cref="Application.Inventory.IRoomBlockPort"/> in the same command.
/// </summary>
public sealed class SqlRoomBlockReader(SqlConnectionFactory connections)
{
    internal const string BlockColumns = "block_id, room_id, from_date, to_date, reason, source, row_ver";

    internal const string BlockedDuringSql = """
        SELECT COUNT(*) FROM ops.room_block
        WHERE room_id = @r AND released_utc IS NULL AND (to_date IS NULL OR @from < to_date) AND (from_date IS NULL OR @to > from_date);
        """;

    internal static RoomBlockRecord ReadBlock(SqlDataReader rd, int o) =>
        new(rd.GetInt64(o), rd.GetInt32(o + 1), P3Sql.DateOrNull(rd, o + 2), P3Sql.DateOrNull(rd, o + 3), rd.GetString(o + 4), rd.GetString(o + 5), (byte[])rd[o + 6]);

    internal static async Task<bool> BlockedDuringAsync(SqlCommand cmd, int roomId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<bool> IsBlockedDuringAsync(int roomId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(BlockedDuringSql, conn);
        return await BlockedDuringAsync(cmd, roomId, from, to, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> BlockedRoomsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT room_id FROM ops.room_block
            WHERE property_id = @p AND released_utc IS NULL AND (to_date IS NULL OR @from < to_date) AND (from_date IS NULL OR @to > from_date)
            ORDER BY room_id;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        var list = new List<int>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetInt32(0));
        return list;
    }
}

/// <summary>Read side of housekeeping: board rows (no money columns are ever selected) and task lists.</summary>
public sealed class SqlHousekeepingReadStore(SqlConnectionFactory connections) : IHousekeepingReadStore
{
    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(P3Sql.BusinessDateSql, conn);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BoardRoomData>> BoardAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT r.room_id, r.room_no, t.code, r.hk_status, r.row_ver,
                   b.block_id, b.room_id, b.from_date, b.to_date, b.reason, b.source, b.row_ver,
                   cur.guest_name, cur.arrival, cur.departure,
                   nxt.guest_name, nxt.arrival, nxt.departure
            FROM inv.room r
            JOIN inv.room_type t ON t.room_type_id = r.room_type_id
            LEFT JOIN ops.room_block b ON b.room_id = r.room_id AND b.released_utc IS NULL
            OUTER APPLY (SELECT TOP (1) res.guest_name, s.arrival, s.departure
                         FROM gst.stay_segment seg JOIN gst.stay s ON s.stay_id = seg.stay_id JOIN inv.reservation res ON res.reservation_id = s.reservation_id
                         WHERE seg.room_id = r.room_id AND seg.to_date IS NULL) cur
            OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival, res.departure
                         FROM inv.reservation res
                         WHERE res.status = 'Confirmed' AND EXISTS (SELECT 1 FROM inv.room_night n WHERE n.reservation_id = res.reservation_id AND n.room_id = r.room_id)
                         ORDER BY res.arrival, res.reservation_id) nxt
            WHERE r.property_id = @p
            ORDER BY r.room_no;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var list = new List<BoardRoomData>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var block = rd.IsDBNull(5) ? null : SqlRoomBlockReader.ReadBlock(rd, 5);
            BoardGuest? Guest(int o) => rd.IsDBNull(o) ? null : new BoardGuest(rd.GetString(o), StayDates.ToWire(P3Sql.Date(rd, o + 1)), StayDates.ToWire(P3Sql.Date(rd, o + 2)));
            list.Add(new BoardRoomData(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), (byte[])rd[4], block, Guest(12), Guest(15)));
        }

        return list;
    }

    public async Task<IReadOnlyList<HousekeepingTaskView>> OpenTasksAsync(int propertyId, int? assigneeId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {SqlHousekeepingStore.TaskColumns}
            FROM ops.hk_task t JOIN inv.room r ON r.room_id = t.room_id
            WHERE t.property_id = @p AND t.status <> 'Done' AND (@a IS NULL OR t.assignee_id = @a)
            ORDER BY CASE t.status WHEN 'InProgress' THEN 0 WHEN 'ToDo' THEN 1 ELSE 2 END, r.room_no, t.task_id;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@a", SqlDbType.Int, assigneeId);
        var list = new List<HousekeepingTaskView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(SqlHousekeepingStore.ReadTaskView(rd));
        return list;
    }

    public async Task<string?> TaskKindAsync(int propertyId, long taskId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT kind FROM ops.hk_task WHERE task_id = @t AND property_id = @p;", conn);
        cmd.Add("@t", SqlDbType.BigInt, taskId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }
}

/// <summary>
/// Assigned-scope ("A") resolver for P1's policy, one instance per kind: <c>task</c> → the principal is the task's assignee;
/// <c>room</c> → the principal has an open (not Done) task on the room.
/// </summary>
public sealed class SqlHousekeepingScopeResolver(SqlConnectionFactory connections, string kind) : IAccessScopeResolver
{
    public string Kind { get; } = kind is AccessScopeKinds.Task or AccessScopeKinds.Room ? kind : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Housekeeping resolves task and room scopes.");

    public static IEnumerable<IAccessScopeResolver> All(SqlConnectionFactory connections) =>
        [new SqlHousekeepingScopeResolver(connections, AccessScopeKinds.Task), new SqlHousekeepingScopeResolver(connections, AccessScopeKinds.Room)];

    public async Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind != Kind) return false;
        var sql = scope.Kind switch
        {
            AccessScopeKinds.Task => "SELECT COUNT(*) FROM ops.hk_task WHERE task_id = @id AND property_id = @p AND assignee_id = @u;",
            AccessScopeKinds.Room => "SELECT COUNT(*) FROM ops.hk_task WHERE room_id = @id AND property_id = @p AND assignee_id = @u AND status <> 'Done';",
            _ => null,
        };
        if (sql is null) return false;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(sql, conn);
        cmd.Add("@id", SqlDbType.BigInt, scope.TargetId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@u", SqlDbType.Int, principalId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }
}

/// <summary>
/// Housekeeping's outbox consumer: <c>StayDeparted</c> → exactly one checkout-clean task per stay (causal key
/// <c>hk:checkout:&lt;stayId&gt;</c>). Delivery dedup is the frozen <see cref="OutboxConsumer"/> (consumer_dedup, spike T09);
/// the causal-key check under lock plus the filtered unique index is the second line of defence.
/// </summary>
public sealed class HousekeepingTaskProjector(OutboxConsumer outbox)
{
    public const string ConsumerName = "housekeeping-tasks";

    public static string CausalKey(long stayId) => "hk:checkout:" + stayId.ToString(CultureInfo.InvariantCulture);

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) =>
        outbox.ConsumeAsync(ConsumerName, [StaysEvents.StayDeparted], ApplyAsync, batchSize, ct);

    public static async Task ApplyAsync(Application.Commands.OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var e = JsonSerializer.Deserialize<StayDepartedEvent>(evt.PayloadJson, CanonicalJson.SerializerOptions)
            ?? throw new InvalidOperationException($"Outbox {evt.OutboxId}: empty StayDeparted payload.");
        var key = CausalKey(e.StayId);
        await using (var chk = new SqlCommand("SELECT COUNT(*) FROM ops.hk_task WITH (UPDLOCK, HOLDLOCK) WHERE causal_key = @ck;", connection, transaction))
        {
            chk.Add("@ck", SqlDbType.VarChar, key, 80);
            if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return;
        }

        await SqlHousekeepingStore.InsertTaskAsync(new SqlCommand(SqlHousekeepingStore.InsertTaskSql, connection, transaction), e.PropertyId, e.RoomId,
            HkTaskKind.Checkout, null, e.StayId, key, StayDates.Parse(e.BusinessDate), DateTime.SpecifyKind(evt.CreatedUtc, DateTimeKind.Utc), ct).ConfigureAwait(false);
    }
}
