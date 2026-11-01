using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Keys;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Domain.Errors;
using Hcc.Domain.Keys;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Keys;

/// <summary>SQL adapter of the room-key port (migration 4720: ops.key_lock, key_encoder, key_job, key_card).</summary>
public sealed class SqlKeyStore(SqlConnectionFactory connections) : IKeyStore
{
    private const string JobSelect = """
        SELECT j.job_id, j.property_id, j.kind, j.event_type, j.reason, j.reservation_id, j.room_id, rm.room_no, j.payload_json, j.requested_by, COALESCE(u.display_name, N''),
               j.requested_utc, j.encoder_id, j.state, j.error_code, j.result_json, j.timeout_utc, j.finished_utc, j.warning
          FROM ops.key_job j LEFT JOIN inv.room rm ON rm.room_id = j.room_id LEFT JOIN plat.app_user u ON u.user_id = j.requested_by
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) => tx as SqlCommandTransaction ?? throw new InvalidOperationException("The key store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static DateTime? UtcN(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : Utc(rd, i);

    private static KeyJobRec Job(SqlDataReader rd) => new(rd.GetGuid(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), Str(rd, 4), rd.IsDBNull(5) ? null : rd.GetInt64(5), rd.IsDBNull(6) ? null : rd.GetInt32(6),
        Str(rd, 7), rd.GetString(8), rd.GetInt32(9), rd.GetString(10), Utc(rd, 11), Str(rd, 12), rd.GetString(13), Str(rd, 14), Str(rd, 15), Utc(rd, 16), UtcN(rd, 17), Str(rd, 18));

    private async Task<T> ReadAsync<T>(Func<SqlConnection, Task<T>> f, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn).ConfigureAwait(false);
    }

    // ── reservation / settings ──
    private const string ReservationSql = """
        SELECT r.reservation_id, r.public_no, r.status, r.arrival, r.departure, COALESCE(seg.room_id, r.room_id), rm.room_no, s.stay_id
          FROM inv.reservation r {0}
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          OUTER APPLY (SELECT TOP 1 g.room_id FROM gst.stay_segment g WHERE g.stay_id = s.stay_id ORDER BY g.seq DESC) seg
          LEFT JOIN inv.room rm ON rm.room_id = COALESCE(seg.room_id, r.room_id)
         WHERE r.property_id = @p AND r.reservation_id = @r;
        """;

    private static async Task<KeyReservation?> ReadReservationAsync(SqlCommand cmd, int propertyId, long reservationId, CancellationToken ct)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new KeyReservation(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), DateOnly.FromDateTime(rd.GetDateTime(3)), DateOnly.FromDateTime(rd.GetDateTime(4)), rd.IsDBNull(5) ? null : rd.GetInt32(5), Str(rd, 6), rd.IsDBNull(7) ? null : rd.GetInt64(7))
            : null;
    }

    public async Task<KeyReservation?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(string.Format(CultureInfo.InvariantCulture, ReservationSql, "WITH (UPDLOCK, ROWLOCK)"));
        return await ReadReservationAsync(cmd, propertyId, reservationId, ct).ConfigureAwait(false);
    }

    public Task<KeyReservation?> ReservationAsync(int propertyId, long reservationId, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command(string.Format(CultureInfo.InvariantCulture, ReservationSql, string.Empty), conn);
        return await ReadReservationAsync(cmd, propertyId, reservationId, ct).ConfigureAwait(false);
    }, ct);

    private static async Task<TimeOnly> CheckoutAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@d", SqlDbType.Date, DateTime.UtcNow.AddHours(5.5).Date);
        var json = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (json is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.String && TimeOnly.TryParseExact(doc.RootElement.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
            }
            catch (JsonException)
            {
                // fall through to the default
            }
        }

        return KeyRules.DefaultFolioCheckout;
    }

    private const string CheckoutSql = "SELECT TOP 1 value_json FROM cfg.setting_change WHERE property_id = @p AND setting_key = 'checkout' AND withdrawn_utc IS NULL AND effective_from <= @d ORDER BY effective_from DESC, change_id DESC;";

    public async Task<TimeOnly> FolioCheckoutAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(CheckoutSql);
        return await CheckoutAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public Task<TimeOnly> FolioCheckoutAsync(int propertyId, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command(CheckoutSql, conn);
        return await CheckoutAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }, ct);

    // ── locks ──
    private const string LockSelect = "SELECT l.room_id, r.room_no, l.has_lock, l.lock_no, l.door_id, l.door_name, l.note, l.row_ver FROM ops.key_lock l {0} JOIN inv.room r ON r.room_id = l.room_id WHERE l.property_id = @p {1};";

    private static KeyLockRec Lock(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.GetBoolean(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), Str(rd, 4)?.TrimEnd(), Str(rd, 5), Str(rd, 6), (byte[])rd[7]);

    public async Task<KeyLockRec?> LockOfRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default)
    {
        var t = Sql(tx);
        var sql = string.Format(CultureInfo.InvariantCulture, LockSelect, "WITH (UPDLOCK, ROWLOCK)", "AND l.room_id = @r");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using (var cmd = t.Command(sql))
            {
                P(cmd, "@p", SqlDbType.Int, propertyId);
                P(cmd, "@r", SqlDbType.Int, roomId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rd.ReadAsync(ct).ConfigureAwait(false)) return Lock(rd);
            }

            // Missing row (room added after the seed): create it from the BIS default map.
            await using var ins = t.Command("""
                INSERT ops.key_lock(property_id, room_id, door_name, has_lock, note)
                SELECT r.property_id, r.room_id, CASE WHEN r.room_no IN (SELECT value FROM STRING_SPLIT(@doors, ',')) THEN r.room_no END,
                       CASE WHEN r.room_no IN (SELECT value FROM STRING_SPLIT(@locks, ',')) THEN 1 ELSE 0 END,
                       CASE WHEN r.room_no = '106' THEN N'Owner ruling: no working lock, manual key' WHEN r.room_no NOT IN (SELECT value FROM STRING_SPLIT(@doors, ',')) THEN N'No BIS door known; the Manager can map it' END
                  FROM inv.room r WHERE r.property_id = @p AND r.room_id = @r;
                """);
            P(ins, "@p", SqlDbType.Int, propertyId);
            P(ins, "@r", SqlDbType.Int, roomId);
            P(ins, "@doors", SqlDbType.VarChar, string.Join(',', AllDoors), -1);
            P(ins, "@locks", SqlDbType.VarChar, string.Join(',', AllDoors.Where(KeyRules.DefaultHasLock)), -1);
            if (await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) return null;
        }

        return null;
    }

    private static IEnumerable<string> AllDoors => Enumerable.Range(101, 7).Concat(Enumerable.Range(201, 14)).Concat(Enumerable.Range(301, 14)).Select(r => r.ToString(CultureInfo.InvariantCulture));

    public Task<KeyLockRec?> LockOfRoomAsync(int propertyId, int roomId, CancellationToken ct = default) => ReadAsync<KeyLockRec?>(async conn =>
    {
        await using var cmd = connections.Command("SELECT r.room_id, r.room_no, l.has_lock, l.lock_no, l.door_id, l.door_name, l.note, l.row_ver FROM inv.room r LEFT JOIN ops.key_lock l ON l.room_id = r.room_id WHERE r.property_id = @p AND r.room_id = @r;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.Int, roomId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return rd.IsDBNull(2) ? DefaultLock(rd.GetInt32(0), rd.GetString(1)) : Lock(rd);
    }, ct);

    /// <summary>The BIS default for a room that has no key_lock row yet (rooms added after the migration seed): door = room number, 106 and rooms without a door have no lock.</summary>
    private static KeyLockRec DefaultLock(int roomId, string roomNo) => new(roomId, roomNo, KeyRules.DefaultHasLock(roomNo), null, null, KeyRules.BisDoorExists(roomNo) ? roomNo : null,
        roomNo == "106" ? "Owner ruling: no working lock, manual key" : KeyRules.BisDoorExists(roomNo) ? null : "No BIS door known; the Manager can map it", new byte[8]);

    public Task<IReadOnlyList<KeyLockRec>> LocksAsync(int propertyId, CancellationToken ct = default) => ReadAsync<IReadOnlyList<KeyLockRec>>(async conn =>
    {
        // Rooms added after the migration seed get their default row first, so the mapping screen always has a row (and a version) to edit.
        await using (var ins = connections.Command("""
            INSERT ops.key_lock(property_id, room_id, door_name, has_lock, note)
            SELECT r.property_id, r.room_id, CASE WHEN r.room_no IN (SELECT value FROM STRING_SPLIT(@doors, ',')) THEN r.room_no END,
                   CASE WHEN r.room_no IN (SELECT value FROM STRING_SPLIT(@locks, ',')) THEN 1 ELSE 0 END,
                   CASE WHEN r.room_no = '106' THEN N'Owner ruling: no working lock, manual key' WHEN r.room_no NOT IN (SELECT value FROM STRING_SPLIT(@doors, ',')) THEN N'No BIS door known; the Manager can map it' END
              FROM inv.room r WHERE r.property_id = @p AND NOT EXISTS (SELECT 1 FROM ops.key_lock l WHERE l.room_id = r.room_id);
            """, conn))
        {
            P(ins, "@p", SqlDbType.Int, propertyId);
            P(ins, "@doors", SqlDbType.VarChar, string.Join(',', AllDoors));
            P(ins, "@locks", SqlDbType.VarChar, string.Join(',', AllDoors.Where(KeyRules.DefaultHasLock)));
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = connections.Command(string.Format(CultureInfo.InvariantCulture, LockSelect, string.Empty, "ORDER BY r.room_no"), conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<KeyLockRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Lock(rd));
        return list;
    }, ct);

    public async Task<KeyLockRec?> SaveLockAsync(ICommandTransaction tx, int propertyId, SaveKeyLockRequest req, byte[] expected, int by, DateTime utc, CancellationToken ct = default)
    {
        await using (var cmd = Sql(tx).Command("""
            UPDATE ops.key_lock SET has_lock = @h, lock_no = @n, door_id = @i, door_name = @d, note = @t, updated_by = @u, updated_utc = @utc
             WHERE property_id = @p AND room_id = @r AND row_ver = @v;
            """))
        {
            P(cmd, "@h", SqlDbType.Bit, req.HasLock);
            P(cmd, "@n", SqlDbType.Int, req.LockNo);
            P(cmd, "@i", SqlDbType.Char, string.IsNullOrWhiteSpace(req.DoorId) ? null : req.DoorId.Trim(), 6);
            P(cmd, "@d", SqlDbType.VarChar, req.DoorName, 20);
            P(cmd, "@t", SqlDbType.NVarChar, req.Note, 200);
            P(cmd, "@u", SqlDbType.Int, by);
            P(cmd, "@utc", SqlDbType.DateTime2, utc);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@r", SqlDbType.Int, req.RoomId);
            P(cmd, "@v", SqlDbType.Binary, expected, 8);
            if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) return null;
        }

        return await LockOfRoomAsync(tx, propertyId, req.RoomId, ct).ConfigureAwait(false);
    }

    // ── encoders ──
    private static KeyEncoderRec Encoder(SqlDataReader rd) => new(rd.GetString(0), rd.GetInt32(1), rd.GetString(2), Str(rd, 3), rd.GetString(4) == "online", Str(rd, 5), UtcN(rd, 6), rd.GetBoolean(7));

    private const string EncoderSelect = "SELECT encoder_id, property_id, pc_name, adapter, status, detail, last_seen_utc, enabled FROM ops.key_encoder";

    public async Task<KeyEncoderRec?> OnlineEncoderAsync(ICommandTransaction tx, int propertyId, DateTime utcNow, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(EncoderSelect + " WHERE property_id = @p AND enabled = 1 AND status = 'online' AND last_seen_utc >= @since ORDER BY last_seen_utc DESC;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@since", SqlDbType.DateTime2, utcNow - KeyRules.HeartbeatStale);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Encoder(rd) : null;
    }

    public async Task InsertEncoderAsync(ICommandTransaction tx, int propertyId, string encoderId, string pcName, string tokenHash, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            IF EXISTS (SELECT 1 FROM ops.key_encoder WHERE encoder_id = @id) THROW 51020, 'Encoder ID already registered.', 1;
            INSERT ops.key_encoder(encoder_id, property_id, pc_name, token_hash) VALUES (@id, @p, @pc, @h);
            """);
        P(cmd, "@id", SqlDbType.VarChar, encoderId, 40);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@pc", SqlDbType.NVarChar, pcName, 80);
        P(cmd, "@h", SqlDbType.Char, tokenHash, 64);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException e) when (e.Number == 51020)
        {
            throw HccException.Validation("This encoder ID is already registered.");
        }
    }

    public Task<IReadOnlyList<KeyEncoderRec>> EncodersAsync(int propertyId, CancellationToken ct = default) => ReadAsync<IReadOnlyList<KeyEncoderRec>>(async conn =>
    {
        await using var cmd = connections.Command(EncoderSelect + " WHERE property_id = @p ORDER BY encoder_id;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<KeyEncoderRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Encoder(rd));
        return list;
    }, ct);

    public Task<KeyEncoderRec?> AuthenticateAsync(string encoderId, string tokenHash, CancellationToken ct = default) => ReadAsync<KeyEncoderRec?>(async conn =>
    {
        await using var cmd = connections.Command(EncoderSelect + " WHERE encoder_id = @id AND token_hash = @h;", conn);
        P(cmd, "@id", SqlDbType.VarChar, encoderId, 40);
        P(cmd, "@h", SqlDbType.Char, tokenHash, 64);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Encoder(rd) : null;
    }, ct);

    public Task HeartbeatAsync(string encoderId, string? adapter, bool online, string? detail, bool? x86, DateTime utc, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command("UPDATE ops.key_encoder SET last_seen_utc = @utc, status = @s, adapter = @a, detail = @d, x86 = @x WHERE encoder_id = @id;", conn);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        P(cmd, "@s", SqlDbType.VarChar, online ? "online" : "offline", 10);
        P(cmd, "@a", SqlDbType.VarChar, adapter, 40);
        P(cmd, "@d", SqlDbType.NVarChar, detail is { Length: > 300 } ? detail[..300] : detail, 300);
        P(cmd, "@x", SqlDbType.Bit, x86);
        P(cmd, "@id", SqlDbType.VarChar, encoderId, 40);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return true;
    }, ct);

    // ── jobs ──
    public async Task InsertJobAsync(ICommandTransaction tx, KeyJobRec job, string? idemKey, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT ops.key_job(job_id, property_id, kind, event_type, reason, reservation_id, room_id, payload_json, requested_by, requested_utc, state, error_code, result_json, timeout_utc, finished_utc, idem_key, warning)
            VALUES (@id, @p, @k, @e, @r, @res, @room, @pl, @by, @utc, @st, @ec, @rj, @to, @fin, @ik, @w);
            """);
        P(cmd, "@id", SqlDbType.UniqueIdentifier, job.JobId);
        P(cmd, "@p", SqlDbType.Int, job.PropertyId);
        P(cmd, "@k", SqlDbType.VarChar, job.Kind, 12);
        P(cmd, "@e", SqlDbType.VarChar, job.Event, 12);
        P(cmd, "@r", SqlDbType.VarChar, job.Reason, 12);
        P(cmd, "@res", SqlDbType.BigInt, job.ReservationId);
        P(cmd, "@room", SqlDbType.Int, job.RoomId);
        P(cmd, "@pl", SqlDbType.NVarChar, job.PayloadJson, 1000);
        P(cmd, "@by", SqlDbType.Int, job.RequestedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, job.RequestedUtc);
        P(cmd, "@st", SqlDbType.VarChar, job.State, 12);
        P(cmd, "@ec", SqlDbType.VarChar, job.ErrorCode, 20);
        P(cmd, "@rj", SqlDbType.NVarChar, job.ResultJson, 1000);
        P(cmd, "@to", SqlDbType.DateTime2, job.TimeoutUtc);
        P(cmd, "@fin", SqlDbType.DateTime2, job.FinishedUtc);
        P(cmd, "@ik", SqlDbType.VarChar, idemKey, 80);
        P(cmd, "@w", SqlDbType.NVarChar, job.Warning, 300);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertManualCardAsync(ICommandTransaction tx, int propertyId, Guid jobId, string ev, string? reason, long reservationId, int roomId, DateTime beginAt, DateTime endAt, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.key_card SET status = 'superseded' WHERE property_id = @p AND room_id = @room AND status IN ('active', 'manual');
            INSERT ops.key_card(property_id, job_id, event_type, reason, reservation_id, room_id, begin_at, end_at, issued_by, issued_utc, status)
            VALUES (@p, @j, @e, @r, @res, @room, @b, @en, @by, @utc, 'manual');
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@j", SqlDbType.UniqueIdentifier, jobId);
        P(cmd, "@e", SqlDbType.VarChar, ev, 12);
        P(cmd, "@r", SqlDbType.VarChar, reason, 12);
        P(cmd, "@res", SqlDbType.BigInt, reservationId);
        P(cmd, "@room", SqlDbType.Int, roomId);
        P(cmd, "@b", SqlDbType.DateTime2, beginAt);
        P(cmd, "@en", SqlDbType.DateTime2, endAt);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> HasActiveCardAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM ops.key_card WHERE property_id = @p AND room_id = @r AND status = 'active';");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.Int, roomId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public Task<int> ExpireAsync(int propertyId, DateTime utcNow, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command("""
            UPDATE ops.key_job SET state = 'expired', error_code = 'Expired', finished_utc = @now, result_json = N'{"code":"Expired","message":"No encoder took the job within 2 minutes."}'
             WHERE property_id = @p AND state = 'queued' AND timeout_utc <= @now;
            DECLARE @a INT = @@ROWCOUNT;
            UPDATE ops.key_job SET state = 'failed', error_code = 'UiTimeout', finished_utc = @now, result_json = N'{"code":"UiTimeout","message":"The agent did not report a result. A card may or may not have been written: read it."}'
             WHERE property_id = @p AND state IN ('picked', 'waiting_card') AND picked_utc < DATEADD(second, -120, @now);
            SELECT @a + @@ROWCOUNT;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@now", SqlDbType.DateTime2, utcNow);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }, ct);

    public Task<KeyJobRec?> ClaimNextAsync(string encoderId, int propertyId, DateTime utcNow, CancellationToken ct = default) => ReadAsync<KeyJobRec?>(async conn =>
    {
        Guid? id;
        await using (var cmd = connections.Command("""
            DECLARE @t TABLE(job_id UNIQUEIDENTIFIER);
            WITH n AS (SELECT TOP (1) * FROM ops.key_job WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE property_id = @p AND state = 'queued' AND timeout_utc > @now
                          AND NOT EXISTS (SELECT 1 FROM ops.key_job x WHERE x.encoder_id = @e AND x.state IN ('picked', 'waiting_card'))
                        ORDER BY requested_utc)
            UPDATE n SET state = 'picked', encoder_id = @e, picked_utc = @now OUTPUT INSERTED.job_id INTO @t;
            SELECT job_id FROM @t;
            """, conn))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@e", SqlDbType.VarChar, encoderId, 40);
            P(cmd, "@now", SqlDbType.DateTime2, utcNow);
            id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as Guid?;
        }

        return id is { } g ? await GetJobAsync(g, ct).ConfigureAwait(false) : null;
    }, ct);

    public Task<KeyJobRec?> GetJobAsync(Guid jobId, CancellationToken ct = default) => ReadAsync<KeyJobRec?>(async conn =>
    {
        await using var cmd = connections.Command(JobSelect + " WHERE j.job_id = @id;", conn);
        P(cmd, "@id", SqlDbType.UniqueIdentifier, jobId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Job(rd) : null;
    }, ct);

    public Task<bool> MoveStateAsync(Guid jobId, string encoderId, string from, string to, DateTime utc, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command("UPDATE ops.key_job SET state = @to WHERE job_id = @id AND encoder_id = @e AND state = @from;", conn);
        P(cmd, "@to", SqlDbType.VarChar, to, 12);
        P(cmd, "@from", SqlDbType.VarChar, from, 12);
        P(cmd, "@id", SqlDbType.UniqueIdentifier, jobId);
        P(cmd, "@e", SqlDbType.VarChar, encoderId, 40);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }, ct);

    public async Task<KeyJobRec> CompleteAsync(KeyJobRec job, string encoderId, string code, string? message, string? cardNo, string? guestSn, string? agentVersion, DateTime utcNow, CancellationToken ct = default)
    {
        var ok = code == KeyCodes.Ok;
        var result = JsonSerializer.Serialize(new { code, message = Clip(message, 300), cardNo, guestSn, agentVersion = Clip(agentVersion, 40) }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            int rows;
            await using (var cmd = connections.Command("""
                UPDATE ops.key_job SET state = @st, error_code = @ec, result_json = @rj, finished_utc = @now, encoder_id = @e
                 WHERE job_id = @id AND state IN ('picked', 'waiting_card');
                """, conn, tx))
            {
                P(cmd, "@st", SqlDbType.VarChar, ok ? KeyStates.Done : KeyStates.Failed, 12);
                P(cmd, "@ec", SqlDbType.VarChar, ok ? null : code, 20);
                P(cmd, "@rj", SqlDbType.NVarChar, result, 1000);
                P(cmd, "@now", SqlDbType.DateTime2, utcNow);
                P(cmd, "@e", SqlDbType.VarChar, encoderId, 40);
                P(cmd, "@id", SqlDbType.UniqueIdentifier, job.JobId);
                rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (rows > 0 && ok && job.RoomId is { } room)
            {
                if (job.Kind is KeyKinds.IssueGuest or KeyKinds.Extend)
                {
                    using var doc = JsonDocument.Parse(job.PayloadJson);
                    var fromAt = DateTime.Parse(doc.RootElement.GetProperty("validFrom").GetString()!, CultureInfo.InvariantCulture);
                    var toAt = DateTime.Parse(doc.RootElement.GetProperty("validTo").GetString()!, CultureInfo.InvariantCulture);
                    await using var cmd = connections.Command("""
                        DECLARE @new BIGINT;
                        INSERT ops.key_card(property_id, job_id, event_type, reason, card_no, reservation_id, room_id, guest_sn, begin_at, end_at, issued_by, issued_utc, encoder_id, status)
                        VALUES (@p, @j, @ev, @rs, @cn, @res, @room, @sn, @b, @en, @by, @now, @e, 'active');
                        SET @new = SCOPE_IDENTITY();
                        UPDATE ops.key_card SET status = CASE WHEN @lost = 1 THEN 'lost' ELSE 'superseded' END
                         WHERE property_id = @p AND room_id = @room AND status IN ('active', 'manual') AND card_id <> @new;
                        """, conn, tx);
                    P(cmd, "@p", SqlDbType.Int, job.PropertyId);
                    P(cmd, "@j", SqlDbType.UniqueIdentifier, job.JobId);
                    P(cmd, "@ev", SqlDbType.VarChar, job.Event, 12);
                    P(cmd, "@rs", SqlDbType.VarChar, job.Reason, 12);
                    P(cmd, "@cn", SqlDbType.VarChar, cardNo, 20);
                    P(cmd, "@res", SqlDbType.BigInt, job.ReservationId);
                    P(cmd, "@room", SqlDbType.Int, room);
                    P(cmd, "@sn", SqlDbType.VarChar, guestSn, 20);
                    P(cmd, "@b", SqlDbType.DateTime2, fromAt);
                    P(cmd, "@en", SqlDbType.DateTime2, toAt);
                    P(cmd, "@by", SqlDbType.Int, job.RequestedBy);
                    P(cmd, "@now", SqlDbType.DateTime2, utcNow);
                    P(cmd, "@e", SqlDbType.VarChar, encoderId, 40);
                    P(cmd, "@lost", SqlDbType.Bit, job.Reason == KeyReasons.Lost);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                else if (job.Kind == KeyKinds.Cancel)
                {
                    await using var cmd = connections.Command("UPDATE ops.key_card SET status = 'checked_out' WHERE property_id = @p AND room_id = @room AND status = 'active';", conn, tx);
                    P(cmd, "@p", SqlDbType.Int, job.PropertyId);
                    P(cmd, "@room", SqlDbType.Int, room);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            if (rows > 0)
            {
                await using var audit = connections.Command("""
                    INSERT plat.audit_event(property_id, principal_id, action, target, detail, operation) VALUES (@p, @by, 'KEY_RESULT', @t, @d, 'KeyAgent');
                    """, conn, tx);
                P(audit, "@p", SqlDbType.Int, job.PropertyId);
                P(audit, "@by", SqlDbType.Int, job.RequestedBy);
                P(audit, "@t", SqlDbType.VarChar, "key-job-" + job.JobId.ToString("N")[..12], 60);
                P(audit, "@d", SqlDbType.NVarChar, $"{job.Event} · room {job.RoomNo} · {code} · encoder {encoderId}", 1000);
                await audit.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        return (await GetJobAsync(job.JobId, ct).ConfigureAwait(false))!;
    }

    private static string? Clip(string? v, int max) => v is { } s && s.Length > max ? s[..max] : v;

    // ── reads ──
    public Task<IReadOnlyList<KeyJobRec>> JobsOfReservationAsync(int propertyId, long reservationId, int? roomId, int limit, CancellationToken ct = default) => ReadAsync<IReadOnlyList<KeyJobRec>>(async conn =>
    {
        await using var cmd = connections.Command(JobSelect.Replace("SELECT j.job_id", "SELECT TOP (@lim) j.job_id", StringComparison.Ordinal) + " WHERE j.property_id = @p AND j.reservation_id = @r ORDER BY j.requested_utc DESC;", conn);
        P(cmd, "@lim", SqlDbType.Int, limit);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        var list = new List<KeyJobRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Job(rd));
        return list;
    }, ct);

    public Task<IReadOnlyList<KeyCardRec>> ActiveCardsAsync(int propertyId, long reservationId, CancellationToken ct = default) => ReadAsync<IReadOnlyList<KeyCardRec>>(async conn =>
    {
        await using var cmd = connections.Command("""
            SELECT c.card_id, c.card_no, c.room_id, r.room_no, c.event_type, c.reason, c.begin_at, c.end_at, c.status, c.issued_by, c.issued_utc
              FROM ops.key_card c JOIN inv.room r ON r.room_id = c.room_id
             WHERE c.property_id = @p AND c.reservation_id = @r AND c.status IN ('active', 'manual') ORDER BY c.card_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        var list = new List<KeyCardRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new KeyCardRec(rd.GetInt64(0), Str(rd, 1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), Str(rd, 5), rd.GetDateTime(6), rd.GetDateTime(7), rd.GetString(8), rd.GetInt32(9), Utc(rd, 10)));
        return list;
    }, ct);

    private static KeyCountsView Counts(Dictionary<string, int> d) =>
        new(d.GetValueOrDefault(KeyEvents.Issued), d.GetValueOrDefault(KeyEvents.Reissued), d.GetValueOrDefault(KeyEvents.Extended), d.GetValueOrDefault(KeyEvents.Reactivated), d.GetValueOrDefault(KeyEvents.Cancelled), d.GetValueOrDefault(KeyEvents.Read));

    public Task<KeyCountsView> CountsAsync(int propertyId, long? reservationId, int? roomId, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        await using var cmd = connections.Command("""
            SELECT event_type, COUNT(*) FROM ops.key_job
             WHERE property_id = @p AND state = 'done' AND (@res IS NULL OR reservation_id = @res) AND (@room IS NULL OR room_id = @room) GROUP BY event_type;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@res", SqlDbType.BigInt, reservationId);
        P(cmd, "@room", SqlDbType.Int, roomId);
        var d = new Dictionary<string, int>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) d[rd.GetString(0)] = rd.GetInt32(1);
        return Counts(d);
    }, ct);

    public Task<(IReadOnlyList<KeyLogRow> Rows, KeyCountsView Totals, int Total)> LogAsync(int propertyId, KeyLogFilter f, CancellationToken ct = default) => ReadAsync(async conn =>
    {
        const string Where = """
             WHERE j.property_id = @p AND (@from IS NULL OR CAST(DATEADD(minute, 330, j.requested_utc) AS DATE) >= @from) AND (@to IS NULL OR CAST(DATEADD(minute, 330, j.requested_utc) AS DATE) <= @to)
               AND (@room IS NULL OR rm.room_no = @room) AND (@bk IS NULL OR rs.public_no = @bk OR CAST(j.reservation_id AS VARCHAR(20)) = @bk)
               AND (@user IS NULL OR j.requested_by = @user) AND (@ev IS NULL OR j.event_type = @ev)
            """;
        const string From = " FROM ops.key_job j LEFT JOIN inv.room rm ON rm.room_id = j.room_id LEFT JOIN inv.reservation rs ON rs.reservation_id = j.reservation_id LEFT JOIN plat.app_user u ON u.user_id = j.requested_by";
        void Bind(SqlCommand c)
        {
            P(c, "@p", SqlDbType.Int, propertyId);
            P(c, "@from", SqlDbType.Date, f.From?.ToDateTime(TimeOnly.MinValue));
            P(c, "@to", SqlDbType.Date, f.To?.ToDateTime(TimeOnly.MinValue));
            P(c, "@room", SqlDbType.VarChar, f.Room, 10);
            P(c, "@bk", SqlDbType.VarChar, f.Booking, 20);
            P(c, "@user", SqlDbType.Int, f.User);
            P(c, "@ev", SqlDbType.VarChar, f.Event, 12);
        }

        var rows = new List<KeyLogRow>();
        await using (var cmd = connections.Command("SELECT TOP (@lim) j.job_id, j.requested_utc, j.event_type, j.reason, rm.room_no, rs.public_no, j.reservation_id, j.requested_by, COALESCE(u.display_name, N''), j.state, j.error_code, JSON_VALUE(j.result_json, '$.cardNo')"
            + From + Where + " ORDER BY j.requested_utc DESC, j.job_id;", conn))
        {
            Bind(cmd);
            P(cmd, "@lim", SqlDbType.Int, f.Limit);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new KeyLogRow(rd.GetGuid(0).ToString(), KeyCommands.Wire(Utc(rd, 1)), rd.GetString(2), Str(rd, 3), Str(rd, 4), Str(rd, 5), rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.GetInt32(7), rd.GetString(8), rd.GetString(9), Str(rd, 10), Str(rd, 11)));
        }

        var counts = new Dictionary<string, int>();
        var total = 0;
        await using (var cmd = connections.Command("SELECT j.event_type, j.state, COUNT(*)" + From + Where + " GROUP BY j.event_type, j.state;", conn))
        {
            Bind(cmd);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var n = rd.GetInt32(2);
                total += n;
                if (rd.GetString(1) == KeyStates.Done) counts[rd.GetString(0)] = counts.GetValueOrDefault(rd.GetString(0)) + n;
            }
        }

        return ((IReadOnlyList<KeyLogRow>)rows, Counts(counts), total);
    }, ct);
}
