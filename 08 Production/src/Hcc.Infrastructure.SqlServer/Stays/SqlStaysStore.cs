using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Stays;

/// <summary>Shared SQL helpers for the P3 adapters.</summary>
internal static class P3Sql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("P3 SQL stores need the SQL command transaction.");

    public static void Add(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    public static DateOnly? DateOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));

    public static string? StrOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    public static int? IntOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    public static async Task<DateOnly> BusinessDateAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public const string BusinessDateSql = "SELECT business_date FROM inv.property WHERE property_id = @p;";
}

/// <summary>SQL adapter of <see cref="IStaysStore"/> (0002 inventory tables + 0300 gst tables). Row locks: UPDLOCK; checks before inserts.</summary>
public sealed class SqlStaysStore : IStaysStore
{
    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<ReservationRecord?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT r.reservation_id, r.property_id, r.status, r.arrival, r.departure, r.guest_name, r.row_ver
            FROM inv.reservation r WITH (UPDLOCK, ROWLOCK) WHERE r.reservation_id = @id AND r.property_id = @p;
            SELECT MIN(n.room_id), MAX(n.room_id), COUNT(*)
            FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
            WHERE n.reservation_id = @id AND n.night >= r.arrival AND n.night < r.departure;
            """);
        cmd.Add("@id", SqlDbType.BigInt, reservationId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var (id, prop, status, arrival, departure, name, ver) = (rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), P3Sql.Date(rd, 3), P3Sql.Date(rd, 4), rd.GetString(5), (byte[])rd[6]);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        int? min = P3Sql.IntOrNull(rd, 0), max = P3Sql.IntOrNull(rd, 1);
        var count = rd.GetInt32(2);
        var nights = departure.DayNumber - arrival.DayNumber;
        return new ReservationRecord(id, prop, status, arrival, departure, name, ver, min == max ? min : null, min is not null && min == max && count == nights);
    }

    public async Task<long?> RegisteredGuestAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT guest_id FROM gst.registration WHERE reservation_id = @id;");
        cmd.Add("@id", SqlDbType.BigInt, reservationId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long g ? g : null;
    }

    public async Task<GuestRecord?> LockGuestAsync(ICommandTransaction tx, int propertyId, long guestId, CancellationToken ct = default)
    {
        var sql = P3Sql.Sql(tx);
        return await SqlStaysReadStore.ReadGuestAsync(sql.Command(SqlStaysReadStore.GuestSql("WITH (UPDLOCK, ROWLOCK)")), propertyId, guestId, ct).ConfigureAwait(false);
    }

    public async Task<long> InsertGuestAsync(ICommandTransaction tx, int propertyId, GuestDraft clean, bool verified, string origin, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clean);
        var sql = P3Sql.Sql(tx);
        await using var cmd = sql.Command("""
            INSERT gst.guest(property_id, full_name, phone, email, address, nationality, id_type, id_last4, company, gstin, preferences, notes, verified, origin, created_utc)
            OUTPUT INSERTED.guest_id
            VALUES (@p, @name, @phone, @email, @address, @nat, @idt, @id4, @company, @gstin, @pref, @notes, @verified, @origin, @utc);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        AddGuestFields(cmd, clean, verified);
        cmd.Add("@origin", SqlDbType.VarChar, origin, 10);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await WritePartyAsync(sql, id, clean.Party ?? [], ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateGuestAsync(ICommandTransaction tx, long guestId, byte[] expectedVersion, GuestDraft clean, bool verified, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clean);
        var sql = P3Sql.Sql(tx);
        await using var cmd = sql.Command("""
            UPDATE gst.guest SET full_name = @name, phone = @phone, email = @email, address = @address, nationality = @nat, id_type = @idt,
              id_last4 = @id4, company = @company, gstin = @gstin, preferences = @pref, notes = @notes, verified = @verified,
              origin = 'manual'
            WHERE guest_id = @g AND row_ver = @v;
            """);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        AddGuestFields(cmd, clean, verified);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This guest profile");
        await using (var del = sql.Command("DELETE gst.guest_party WHERE guest_id = @g;"))
        {
            del.Add("@g", SqlDbType.BigInt, guestId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await WritePartyAsync(sql, guestId, clean.Party ?? [], ct).ConfigureAwait(false);
    }

    public async Task<byte[]> GuestVersionAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT row_ver FROM gst.guest WHERE guest_id = @g;");
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task UpsertRegistrationAsync(ICommandTransaction tx, long reservationId, long guestId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        // Locked check-then-write (contracts §4 rule 8).
        await using var cmd = P3Sql.Sql(tx).Command("""
            IF EXISTS (SELECT 1 FROM gst.registration WITH (UPDLOCK, HOLDLOCK) WHERE reservation_id = @r)
              UPDATE gst.registration SET guest_id = @g, registered_utc = @utc, registered_by = @u WHERE reservation_id = @r;
            ELSE
              INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES (@r, @g, @utc, @u);
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<RoomRecord?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT r.room_id, r.property_id, r.room_no, r.hk_status, r.row_ver,
                   (SELECT TOP (1) s.stay_id FROM gst.stay_segment s WITH (UPDLOCK, HOLDLOCK) WHERE s.room_id = r.room_id AND s.to_date IS NULL)
            FROM inv.room r WITH (UPDLOCK, ROWLOCK) WHERE r.room_id = @r AND r.property_id = @p;
            """);
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new RoomRecord(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.IsDBNull(5) ? null : rd.GetInt64(5), (byte[])rd[4]);
    }

    public async Task<bool> RoomBlockedDuringAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(SqlRoomBlockReader.BlockedDuringSql);
        return await SqlRoomBlockReader.BlockedDuringAsync(cmd, roomId, from, to, ct).ConfigureAwait(false);
    }

    public async Task SetReservationStatusAsync(ICommandTransaction tx, long reservationId, byte[] expectedVersion, string status, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE inv.reservation SET status = @s WHERE reservation_id = @id AND row_ver = @v;");
        cmd.Add("@s", SqlDbType.VarChar, status, 12);
        cmd.Add("@id", SqlDbType.BigInt, reservationId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This reservation");
    }

    public async Task<(long StayId, byte[] Version)> InsertStayAsync(ICommandTransaction tx, ReservationRecord r, long guestId, long folioId, int roomId, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = P3Sql.Sql(tx).Command("""
            DECLARE @out TABLE(stay_id BIGINT, row_ver BINARY(8));
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
            OUTPUT INSERTED.stay_id, INSERTED.row_ver INTO @out
            VALUES (@p, @res, @g, @f, 'InHouse', @a, @d, @bd, @utc, @u);
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date) SELECT stay_id, 1, @room, @bd FROM @out;
            SELECT stay_id, row_ver FROM @out;
            """);
        cmd.Add("@p", SqlDbType.Int, r.PropertyId);
        cmd.Add("@res", SqlDbType.BigInt, r.ReservationId);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@f", SqlDbType.BigInt, folioId);
        cmd.Add("@a", SqlDbType.Date, r.Arrival.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@d", SqlDbType.Date, r.Departure.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@room", SqlDbType.Int, roomId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), (byte[])rd[1]);
    }

    public async Task<StayRecord?> LockStayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT s.stay_id, s.property_id, s.reservation_id, s.folio_id, s.status, s.arrival, s.departure, seg.room_id, s.row_ver
            FROM gst.stay s WITH (UPDLOCK, ROWLOCK)
            CROSS APPLY (SELECT TOP (1) room_id FROM gst.stay_segment WHERE stay_id = s.stay_id ORDER BY seq DESC) seg
            WHERE s.stay_id = @s AND s.property_id = @p;
            """);
        cmd.Add("@s", SqlDbType.BigInt, stayId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new StayRecord(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetString(4), P3Sql.Date(rd, 5), P3Sql.Date(rd, 6), rd.GetInt32(7), (byte[])rd[8]);
    }

    public async Task<byte[]> CheckOutStayAsync(ICommandTransaction tx, long stayId, byte[] expectedVersion, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            DECLARE @out TABLE(row_ver BINARY(8));
            UPDATE gst.stay SET status = 'CheckedOut', checked_out_date = @bd, checked_out_utc = @utc, checked_out_by = @u
            OUTPUT INSERTED.row_ver INTO @out
            WHERE stay_id = @s AND row_ver = @v AND status = 'InHouse';
            UPDATE gst.stay_segment SET to_date = @bd WHERE stay_id = @s AND to_date IS NULL AND EXISTS (SELECT 1 FROM @out);
            SELECT row_ver FROM @out;
            """);
        cmd.Add("@s", SqlDbType.BigInt, stayId);
        cmd.Add("@v", SqlDbType.Binary, expectedVersion, 8);
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        cmd.Add("@u", SqlDbType.Int, principalId);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        SqlValues.EnsureVersionMatched(v is null ? 0 : 1, "This stay");
        return (byte[])v!;
    }

    public async Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE inv.room SET hk_status = @hk WHERE room_id = @r;");
        cmd.Add("@hk", SqlDbType.VarChar, hk, 12);
        cmd.Add("@r", SqlDbType.Int, roomId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddGuestFields(SqlCommand cmd, GuestDraft d, bool verified)
    {
        cmd.Add("@name", SqlDbType.NVarChar, d.Name, 200);
        cmd.Add("@phone", SqlDbType.VarChar, d.Phone, 20);
        cmd.Add("@email", SqlDbType.NVarChar, d.Email, 200);
        cmd.Add("@address", SqlDbType.NVarChar, d.Address, 400);
        cmd.Add("@nat", SqlDbType.NVarChar, d.Nationality ?? "Indian", 60);
        cmd.Add("@idt", SqlDbType.NVarChar, d.IdType, 40);
        cmd.Add("@id4", SqlDbType.VarChar, d.IdLast4, 4);
        cmd.Add("@company", SqlDbType.NVarChar, d.Company, 200);
        cmd.Add("@gstin", SqlDbType.VarChar, d.Gstin, 15);
        cmd.Add("@pref", SqlDbType.NVarChar, d.Preferences, 500);
        cmd.Add("@notes", SqlDbType.NVarChar, d.Notes, 1000);
        cmd.Add("@verified", SqlDbType.Bit, verified);
    }

    private static async Task WritePartyAsync(SqlCommandTransaction sql, long guestId, IReadOnlyList<GuestPartyMember> party, CancellationToken ct)
    {
        for (var i = 0; i < party.Count; i++)
        {
            await using var cmd = sql.Command("INSERT gst.guest_party(guest_id, seq, name, age_band, relation) VALUES (@g, @s, @n, @a, @r);");
            cmd.Add("@g", SqlDbType.BigInt, guestId);
            cmd.Add("@s", SqlDbType.TinyInt, (byte)(i + 1));
            cmd.Add("@n", SqlDbType.NVarChar, party[i].Name, 200);
            cmd.Add("@a", SqlDbType.VarChar, party[i].AgeBand, 10);
            cmd.Add("@r", SqlDbType.NVarChar, party[i].Relation, 60);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Read side: guest profile with party (own connection).</summary>
public sealed class SqlStaysReadStore(SqlConnectionFactory connections) : IStaysReadStore
{
    internal static string GuestSql(string hint) => $"""
        SELECT guest_id, property_id, full_name, phone, email, address, nationality, id_type, id_last4, company, gstin, preferences, notes, verified, origin, row_ver,
               CASE WHEN EXISTS (SELECT 1 FROM gst.guest_id_secret s WHERE s.guest_id = gst.guest.guest_id) THEN 1 ELSE 0 END -- DEC-068: on file? (never the number)
        FROM gst.guest {hint} WHERE guest_id = @g AND property_id = @p;
        SELECT name, age_band, relation FROM gst.guest_party WHERE guest_id = @g ORDER BY seq;
        """;

    public async Task<GuestRecord?> GetGuestAsync(int propertyId, long guestId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await ReadGuestAsync(connections.Command(GuestSql(string.Empty), conn), propertyId, guestId, ct).ConfigureAwait(false);
    }

    internal static async Task<GuestRecord?> ReadGuestAsync(SqlCommand command, int propertyId, long guestId, CancellationToken ct)
    {
        await using var cmd = command;
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var id = rd.GetInt64(0);
        var prop = rd.GetInt32(1);
        var name = rd.GetString(2);
        var phone = rd.GetString(3);
        var email = P3Sql.StrOrNull(rd, 4);
        var address = P3Sql.StrOrNull(rd, 5);
        var nat = rd.GetString(6);
        var idt = P3Sql.StrOrNull(rd, 7);
        var id4 = P3Sql.StrOrNull(rd, 8);
        var company = P3Sql.StrOrNull(rd, 9);
        var gstin = P3Sql.StrOrNull(rd, 10);
        var pref = P3Sql.StrOrNull(rd, 11);
        var notes = P3Sql.StrOrNull(rd, 12);
        var verified = rd.GetBoolean(13);
        var origin = rd.GetString(14);
        var ver = (byte[])rd[15];
        var idOnFile = rd.GetInt32(16) == 1;
        var party = new List<GuestPartyMember>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) party.Add(new GuestPartyMember(rd.GetString(0), rd.GetString(1), P3Sql.StrOrNull(rd, 2)));
        return new GuestRecord(id, prop, new GuestDraft(name, phone, email, address, nat, idt, id4, company, gstin, pref, notes, party), verified, origin, ver, idOnFile);
    }
}
