using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Import;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Import;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Import;

internal static class ImportSql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Import SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    public static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));
}

/// <summary>
/// imp.* persistence plus the read-only snapshot of the owning modules' masters (inv.room_type/room/room_type_tariff, yld.season,
/// plat.role/app_user/user_role, pay.till/shift/cash_movement, gst.guest names + phones only — never an ID column, DEC-068, cls.business_day).
/// </summary>
public sealed class SqlImportStore(SqlConnectionFactory connections) : IImportStore
{
    private async Task<T> WithCommandAsync<T>(ICommandTransaction? tx, string sql, Func<SqlCommand, Task<T>> work, CancellationToken ct)
    {
        if (tx is not null)
        {
            await using var cmd = ImportSql.Sql(tx).Command(sql);
            return await work(cmd).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c = connections.Command(sql, conn);
        return await work(c).ConfigureAwait(false);
    }

    public async Task LockImportsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = ImportSql.Sql(tx).Command("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;");
        cmd.P("@res", SqlDbType.NVarChar, "import:" + propertyId.ToString(System.Globalization.CultureInfo.InvariantCulture), 255);
        var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
        if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another import is running. Try again in a moment.");
    }

    public Task<ImportSnapshot> SnapshotAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default)
    {
        var hold = tx is null ? string.Empty : " WITH (HOLDLOCK)";
        var sql = $"""
            SELECT business_date FROM inv.property WHERE property_id = @p;
            SELECT CASE WHEN EXISTS (SELECT 1 FROM cls.business_day{hold} WHERE property_id = @p) THEN 1 ELSE 0 END;
            SELECT room_type_id, code, name, max_occupancy, rack_rate_paise, sort_order FROM inv.room_type WHERE property_id = @p ORDER BY sort_order, code;
            SELECT room_no, room_type_id FROM inv.room WHERE property_id = @p;
            SELECT t.room_type_id, t.effective_from, t.rate_paise FROM inv.room_type_tariff t JOIN inv.room_type rt ON rt.room_type_id = t.room_type_id WHERE rt.property_id = @p;
            SELECT season_id, name, from_date, to_date, adj_bps, priority FROM yld.season WHERE property_id = @p;
            SELECT st.season_id, st.room_type_id FROM yld.season_type st JOIN yld.season s ON s.season_id = st.season_id WHERE s.property_id = @p;
            SELECT role_code, name, credential_kind FROM plat.role ORDER BY sort_order;
            SELECT user_id, login_name, display_name FROM plat.app_user;
            SELECT user_id, role_code FROM plat.user_role;
            SELECT code, name, grp, hcc_account FROM imp.ledger WHERE property_id = @p ORDER BY code;
            SELECT as_of_date FROM imp.opening_set WHERE property_id = @p;
            SELECT ledger_code, debit_paise, credit_paise FROM imp.opening_balance WHERE property_id = @p ORDER BY ledger_code;
            SELECT t.till_id, t.code, t.name,
                   (SELECT COUNT(*) FROM pay.shift s WHERE s.till_id = t.till_id),
                   (SELECT TOP (1) s.opening_float_paise FROM pay.shift s
                     WHERE s.till_id = t.till_id AND s.status = 'Closed' AND s.counted_paise = s.opening_float_paise AND s.expected_paise = s.opening_float_paise
                       AND NOT EXISTS (SELECT 1 FROM pay.cash_movement m WHERE m.shift_id = s.shift_id))
              FROM pay.till t WHERE t.property_id = @p;
            SELECT full_name, phone FROM gst.guest WHERE property_id = @p;
            """;
        return WithCommandAsync(tx, sql, async cmd =>
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
            var businessDate = ImportSql.Date(rd, 0);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            var closed = rd.GetInt32(0) == 1;
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var types = new List<SnapRoomType>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                types.Add(new SnapRoomType(rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetByte(3), rd.GetInt64(4), rd.GetInt32(5)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var rooms = new List<SnapRoom>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rooms.Add(new SnapRoom(rd.GetString(0), rd.GetInt32(1)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var tariffs = new List<SnapTariff>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) tariffs.Add(new SnapTariff(rd.GetInt32(0), ImportSql.Date(rd, 1), rd.GetInt64(2)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var seasons = new List<(int Id, string Name, DateOnly From, DateOnly To, int Adj, int Prio)>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                seasons.Add((rd.GetInt32(0), rd.GetString(1), ImportSql.Date(rd, 2), ImportSql.Date(rd, 3), rd.GetInt32(4), rd.GetInt32(5)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var seasonTypes = new List<(int Season, int Type)>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) seasonTypes.Add((rd.GetInt32(0), rd.GetInt32(1)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var roles = new List<SnapRole>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) roles.Add(new SnapRole(rd.GetString(0), rd.GetString(1), rd.GetString(2)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var users = new List<(int Id, string Login, string Name)>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) users.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var userRoles = new List<(int User, string Role)>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) userRoles.Add((rd.GetInt32(0), rd.GetString(1)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var ledgers = new List<SnapLedger>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                ledgers.Add(new SnapLedger(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            DateOnly? asOf = await rd.ReadAsync(ct).ConfigureAwait(false) ? ImportSql.Date(rd, 0) : null;
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var opening = new List<SnapOpening>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) opening.Add(new SnapOpening(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var tills = new List<SnapTill>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var shifts = rd.GetInt32(3);
                long? imported = shifts == 1 && !rd.IsDBNull(4) ? rd.GetInt64(4) : null;
                tills.Add(new SnapTill(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), shifts, imported));
            }

            await rd.NextResultAsync(ct).ConfigureAwait(false);
            var guests = new HashSet<string>(StringComparer.Ordinal);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) guests.Add(ImportPlanner.GuestKey(rd.GetString(1), rd.GetString(0)));

            return new ImportSnapshot(businessDate, closed, types, rooms, tariffs,
                seasons.Select(s => new SnapSeason(s.Id, s.Name, s.From, s.To, s.Adj, seasonTypes.Where(x => x.Season == s.Id).Select(x => x.Type).Order().ToList(), s.Prio)).ToList(),
                roles,
                users.Select(u => new SnapUser(u.Login, u.Name, userRoles.Where(r => r.User == u.Id).Select(r => r.Role).ToList())).ToList(),
                ledgers, asOf, opening, tills, guests);
        }, ct);
    }

    public async Task<long> InsertBatchAsync(ICommandTransaction tx, int propertyId, string master, string fileName, byte[] sha256, string csvText, DateOnly? asOf, int rows, int errors,
        int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = ImportSql.Sql(tx).Command("""
            INSERT imp.batch(property_id, master, file_name, file_sha256, csv_text, as_of_date, row_count, error_count, status, uploaded_by, uploaded_utc)
            OUTPUT INSERTED.batch_id VALUES (@p, @m, @f, @h, @t, @d, @r, @e, 'Uploaded', @u, @now);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.VarChar, master, 20);
        cmd.P("@f", SqlDbType.NVarChar, fileName, 200);
        cmd.P("@h", SqlDbType.Binary, sha256, 32);
        cmd.P("@t", SqlDbType.NVarChar, csvText, -1);
        cmd.P("@d", SqlDbType.Date, asOf?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@r", SqlDbType.Int, rows);
        cmd.P("@e", SqlDbType.Int, errors);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@now", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private const string BatchColumns = "batch_id, property_id, master, file_name, csv_text, as_of_date, row_count, error_count, status, uploaded_by, uploaded_utc, committed_by, committed_utc, result_json";

    private static ImportBatchRecord ReadBatch(SqlDataReader rd) =>
        new(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.IsDBNull(5) ? null : ImportSql.Date(rd, 5), rd.GetInt32(6), rd.GetInt32(7),
            rd.GetString(8), rd.GetInt32(9), ImportSql.Utc(rd, 10), rd.IsDBNull(11) ? null : rd.GetInt32(11), rd.IsDBNull(12) ? null : ImportSql.Utc(rd, 12),
            rd.IsDBNull(13) ? null : rd.GetString(13));

    public Task<ImportBatchRecord?> GetBatchAsync(ICommandTransaction? tx, int propertyId, long batchId, bool lockRow, CancellationToken ct = default) =>
        WithCommandAsync(tx, $"SELECT {BatchColumns} FROM imp.batch{(lockRow && tx is not null ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty)} WHERE batch_id = @b AND property_id = @p;", async cmd =>
        {
            cmd.P("@b", SqlDbType.BigInt, batchId);
            cmd.P("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadBatch(rd) : null;
        }, ct);

    public Task<IReadOnlyList<ImportBatchRecord>> ListBatchesAsync(int propertyId, int limit, CancellationToken ct = default) =>
        WithCommandAsync<IReadOnlyList<ImportBatchRecord>>(null,
            $"SELECT TOP (@n) batch_id, property_id, master, file_name, N'' AS csv_text, as_of_date, row_count, error_count, status, uploaded_by, uploaded_utc, committed_by, committed_utc, NULL AS result_json FROM imp.batch WHERE property_id = @p ORDER BY batch_id DESC;",
            async cmd =>
            {
                cmd.P("@n", SqlDbType.Int, limit);
                cmd.P("@p", SqlDbType.Int, propertyId);
                var list = new List<ImportBatchRecord>();
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadBatch(rd));
                return list;
            }, ct);

    public async Task MarkCommittedAsync(ICommandTransaction tx, long batchId, int principalId, DateTime utc, string resultJson, CancellationToken ct = default)
    {
        await using var cmd = ImportSql.Sql(tx).Command("UPDATE imp.batch SET status = 'Committed', committed_by = @u, committed_utc = @now, result_json = @j WHERE batch_id = @b AND status = 'Uploaded';");
        cmd.P("@b", SqlDbType.BigInt, batchId);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@now", SqlDbType.DateTime2, utc);
        cmd.P("@j", SqlDbType.NVarChar, resultJson, -1);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This import batch");
    }

    public async Task UpsertLedgerAsync(ICommandTransaction tx, int propertyId, LedgerData ledger, long batchId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        await using var cmd = ImportSql.Sql(tx).Command("""
            UPDATE imp.ledger SET name = @n, grp = @g, hcc_account = @a, batch_id = @b, changed_by = @u, changed_utc = @now WHERE property_id = @p AND code = @c;
            IF @@ROWCOUNT = 0
              INSERT imp.ledger(property_id, code, name, grp, hcc_account, batch_id, changed_by, changed_utc) VALUES (@p, @c, @n, @g, @a, @b, @u, @now);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, ledger.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, ledger.Name, 100);
        cmd.P("@g", SqlDbType.VarChar, ledger.Group, 10);
        cmd.P("@a", SqlDbType.VarChar, ledger.HccAccount, 4);
        cmd.P("@b", SqlDbType.BigInt, batchId);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@now", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ReplaceOpeningAsync(ICommandTransaction tx, int propertyId, DateOnly asOf, IReadOnlyList<OpeningData> rows, long batchId, int principalId, DateTime utc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var t = ImportSql.Sql(tx);
        await using (var cmd = t.Command("""
            DELETE imp.opening_balance WHERE property_id = @p;
            UPDATE imp.opening_set SET as_of_date = @d, batch_id = @b, debit_paise = @dr, credit_paise = @cr, committed_by = @u, committed_utc = @now WHERE property_id = @p;
            IF @@ROWCOUNT = 0
              INSERT imp.opening_set(property_id, as_of_date, batch_id, debit_paise, credit_paise, committed_by, committed_utc) VALUES (@p, @d, @b, @dr, @cr, @u, @now);
            """))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            cmd.P("@d", SqlDbType.Date, asOf.ToDateTime(TimeOnly.MinValue));
            cmd.P("@b", SqlDbType.BigInt, batchId);
            cmd.P("@dr", SqlDbType.BigInt, rows.Sum(r => r.DebitPaise));
            cmd.P("@cr", SqlDbType.BigInt, rows.Sum(r => r.CreditPaise));
            cmd.P("@u", SqlDbType.Int, principalId);
            cmd.P("@now", SqlDbType.DateTime2, utc);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var r in rows)
        {
            await using var ins = t.Command("INSERT imp.opening_balance(property_id, ledger_code, debit_paise, credit_paise) VALUES (@p, @c, @d, @k);");
            ins.P("@p", SqlDbType.Int, propertyId);
            ins.P("@c", SqlDbType.VarChar, r.LedgerCode, 20);
            ins.P("@d", SqlDbType.BigInt, r.DebitPaise);
            ins.P("@k", SqlDbType.BigInt, r.CreditPaise);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public Task<long?> OpeningBatchAsync(int propertyId, CancellationToken ct = default) =>
        WithCommandAsync<long?>(null, "SELECT batch_id FROM imp.opening_set WHERE property_id = @p;", async cmd =>
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long b ? b : null;
        }, ct);
}

/// <summary>
/// P2 inventory writer used by the import (see <see cref="IImportInventoryWriter"/>): inv.room_type insert/update, inv.room insert, and the
/// sellable count of materialised type-nights kept in step (lock order of P2: inv.type_night_count by type asc, night asc).
/// </summary>
public sealed class SqlImportInventoryWriter : IImportInventoryWriter
{
    public async Task<int> InsertRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeData type, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        await using var cmd = ImportSql.Sql(tx).Command("""
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) OUTPUT INSERTED.room_type_id VALUES (@p, @c, @o, @n, @r, @s);
            """);
        Bind(cmd, propertyId, type);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task UpdateRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeData type, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        await using var cmd = ImportSql.Sql(tx).Command("UPDATE inv.room_type SET max_occupancy = @o, name = @n, rack_rate_paise = @r, sort_order = @s WHERE property_id = @p AND code = @c;");
        Bind(cmd, propertyId, type);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This room type");
    }

    private static void Bind(SqlCommand cmd, int propertyId, RoomTypeData t)
    {
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, t.Code, 20);
        cmd.P("@o", SqlDbType.TinyInt, (byte)t.MaxOccupancy);
        cmd.P("@n", SqlDbType.NVarChar, t.Name, 60);
        cmd.P("@r", SqlDbType.BigInt, t.RackPaise);
        cmd.P("@s", SqlDbType.Int, t.SortOrder);
    }

    public async Task InsertRoomAsync(ICommandTransaction tx, int propertyId, RoomData room, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        await using var cmd = ImportSql.Sql(tx).Command("""
            IF NOT EXISTS (SELECT 1 FROM inv.room_type WHERE room_type_id = @t AND property_id = @p) THROW 52701, 'Room type not found.', 1;
            DECLARE @n TABLE(night DATE PRIMARY KEY);
            INSERT @n SELECT night FROM inv.type_night_count WITH (UPDLOCK, HOLDLOCK) WHERE room_type_id = @t;
            INSERT inv.room(property_id, room_no, room_type_id, hk_status) VALUES (@p, @r, @t, 'Ready');
            UPDATE inv.type_night_count SET sellable = sellable + 1 WHERE room_type_id = @t AND night IN (SELECT night FROM @n);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@t", SqlDbType.Int, room.TypeId);
        cmd.P("@r", SqlDbType.VarChar, room.RoomNo, 10);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// P1 access writer used by the import (see <see cref="IImportAccessWriter"/>): plat.app_user + plat.user_role, no credential row (the user
/// cannot sign in until the Owner sets the first password/PIN through Access admin SetCredential). Never assigns the Owner role.
/// </summary>
public sealed class SqlImportAccessWriter : IImportAccessWriter
{
    public async Task<(int UserId, string Version)> CreateUserAsync(ICommandTransaction tx, int propertyId, StaffData user, int assignedBy, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Roles.Contains("OWN", StringComparer.Ordinal)) throw HccException.Validation("The Owner role is not imported.");
        var t = ImportSql.Sql(tx);
        int id;
        byte[] version;
        await using (var cmd = t.Command("""
            IF EXISTS (SELECT 1 FROM plat.app_user WITH (UPDLOCK, HOLDLOCK) WHERE login_name = @l) THROW 52702, 'This sign-in name is already used.', 1;
            INSERT plat.app_user(property_id, login_name, display_name, created_utc, created_by) VALUES (@p, @l, @n, @now, @by);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            cmd.P("@l", SqlDbType.VarChar, user.Login, 40);
            cmd.P("@n", SqlDbType.NVarChar, user.DisplayName, 100);
            cmd.P("@now", SqlDbType.DateTime2, utc);
            cmd.P("@by", SqlDbType.Int, assignedBy);
            id = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var role in user.Roles)
        {
            await using var r = t.Command("INSERT plat.user_role(user_id, role_code, valid_from_utc, valid_until_utc, assigned_by, assigned_utc) VALUES (@u, @r, @now, NULL, @by, @now);");
            r.P("@u", SqlDbType.Int, id);
            r.P("@r", SqlDbType.VarChar, role, 3);
            r.P("@now", SqlDbType.DateTime2, utc);
            r.P("@by", SqlDbType.Int, assignedBy);
            await r.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var v = t.Command("SELECT row_ver FROM plat.app_user WHERE user_id = @u;"))
        {
            v.P("@u", SqlDbType.Int, id);
            version = (byte[])(await v.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        return (id, SqlValues.RowVersionToWire(version));
    }
}
