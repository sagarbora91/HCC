using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.WebBooking;
using Hcc.Domain.WebBooking;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.WebBooking;

/// <summary>SQL adapter of <see cref="IWebBookingStore"/> (schema web, migration 2000).</summary>
public sealed class SqlWebBookingStore(SqlConnectionFactory connections) : IWebBookingStore
{
    private const string RequestColumns = """
        r.request_id, r.property_id, r.public_no, r.room_type_id, t.code, r.arrival, r.departure, r.adults, r.children, r.guest_name, r.phone, r.email, r.notes,
        r.net_paise, r.tax_paise, r.total_paise, r.quote_nights, r.status, r.reason, r.claimed_by, r.decided_by, r.decided_utc, r.reservation_id, v.public_no,
        r.created_utc, r.row_ver
        """;

    private const string RequestFrom = """
        web.request r{0} JOIN inv.room_type t ON t.room_type_id = r.room_type_id LEFT JOIN inv.reservation v ON v.reservation_id = r.reservation_id
        """;

    private const string LinkColumns = "l.link_id, l.request_id, l.amount_paise, l.status, l.expires_utc, l.created_utc, l.paid_utc, l.paid_ref, l.receipt_id, l.row_ver";

    private static WebRequestRow ReadRequest(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetInt32(3), rd.GetString(4), rd.GetDate(5),
        rd.GetDate(6), rd.GetByte(7), rd.GetByte(8), rd.GetString(9), rd.GetString(10), rd.GetStringOrNull(11), rd.GetStringOrNull(12), rd.GetInt64(13), rd.GetInt64(14),
        rd.GetInt64(15), rd.GetString(16), rd.GetString(17), rd.GetStringOrNull(18), rd.IsDBNull(19) ? null : rd.GetInt32(19), rd.IsDBNull(20) ? null : rd.GetInt32(20),
        rd.GetUtcOrNull(21), rd.GetInt64OrNull(22), rd.GetStringOrNull(23), DateTime.SpecifyKind(rd.GetDateTime(24), DateTimeKind.Utc), (byte[])rd[25]);

    private static DepositLinkRow ReadLink(SqlDataReader rd, int o = 0) => new(rd.GetInt64(o), rd.GetInt64(o + 1), rd.GetInt64(o + 2), rd.GetString(o + 3),
        DateTime.SpecifyKind(rd.GetDateTime(o + 4), DateTimeKind.Utc), DateTime.SpecifyKind(rd.GetDateTime(o + 5), DateTimeKind.Utc), rd.GetUtcOrNull(o + 6),
        rd.GetStringOrNull(o + 7), rd.GetInt64OrNull(o + 8), (byte[])rd[o + 9]);

    private async Task<T> ReadAsync<T>(string sql, Action<SqlParameterCollection> bind, Func<SqlDataReader, Task<T>> read, ICommandTransaction? tx, CancellationToken ct)
    {
        if (tx is not null)
        {
            await using var q = SqlScope.Of(tx, connections).Command(sql);
            bind(q.Parameters);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await read(rd).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(sql, conn);
        bind(cmd.Parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await read(reader).ConfigureAwait(false);
    }

    private async Task<WebRequestRow?> OneRequestAsync(string where, Action<SqlParameterCollection> bind, bool forUpdate, ICommandTransaction? tx, CancellationToken ct)
    {
        var from = string.Format(System.Globalization.CultureInfo.InvariantCulture, RequestFrom, forUpdate ? " WITH (UPDLOCK, ROWLOCK)" : "");
        return await ReadAsync($"SELECT {RequestColumns} FROM {from} WHERE {where};", bind,
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(rd) : null, tx, ct).ConfigureAwait(false);
    }

    public async Task<(int PropertyId, string Name)?> PublicPropertyAsync(CancellationToken ct = default)
        => await ReadAsync("SELECT TOP (1) property_id, name FROM inv.property ORDER BY property_id;", _ => { },
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? ((int, string)?)(rd.GetInt32(0), rd.GetString(1)) : null, null, ct).ConfigureAwait(false);

    public async Task<int> PendingForPhoneAsync(ICommandTransaction tx, int propertyId, string phone, CancellationToken ct = default)
        => await ReadAsync("SELECT COUNT(*) FROM web.request WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND phone = @ph AND status IN ('Pending','Confirming');",
            p => { p.Int("@p", propertyId); p.Text("@ph", phone, 20); },
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? rd.GetInt32(0) : 0, tx, ct).ConfigureAwait(false);

    public async Task<WebRequestRow> InsertRequestAsync(ICommandTransaction tx, NewWebRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        long id;
        await using (var ins = SqlScope.Of(tx, connections).Command("""
            DECLARE @no VARCHAR(20) = CONCAT('WEB-', RIGHT(CONCAT('0000', CAST(NEXT VALUE FOR web.seq_request_no AS VARCHAR(10))), 4));
            INSERT web.request(property_id, public_no, room_type_id, arrival, departure, adults, children, guest_name, phone, email, notes,
              net_paise, tax_paise, total_paise, quote_nights, created_utc)
            OUTPUT INSERTED.request_id
            VALUES (@p, @no, @t, @a, @d, @ad, @ch, @g, @ph, @em, @nt, @net, @tax, @net + @tax, @q, @now);
            """))
        {
            ins.Parameters.Int("@p", r.PropertyId);
            ins.Parameters.Int("@t", r.RoomTypeId);
            ins.Parameters.Date("@a", r.Arrival);
            ins.Parameters.Date("@d", r.Departure);
            ins.Parameters.Add("@ad", SqlDbType.TinyInt).Value = (byte)r.Adults;
            ins.Parameters.Add("@ch", SqlDbType.TinyInt).Value = (byte)r.Children;
            ins.Parameters.Text("@g", r.Guest, 200);
            ins.Parameters.Text("@ph", r.Phone, 20);
            ins.Parameters.Text("@em", r.Email, 200);
            ins.Parameters.Text("@nt", r.Notes, 200);
            ins.Parameters.Big("@net", r.NetPaise);
            ins.Parameters.Big("@tax", r.TaxPaise);
            ins.Parameters.Add("@q", SqlDbType.NVarChar, -1).Value = r.QuoteNightsJson;
            ins.Parameters.Utc("@now", r.NowUtc);
            id = Convert.ToInt64(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        return (await OneRequestAsync("r.request_id = @i", p => p.Big("@i", id), false, tx, ct).ConfigureAwait(false))!;
    }

    public Task<WebRequestRow?> GetForUpdateAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default)
        => OneRequestAsync("r.request_id = @i AND r.property_id = @p", p => { p.Big("@i", requestId); p.Int("@p", propertyId); }, true, tx, ct);

    public async Task SetStateAsync(ICommandTransaction tx, long requestId, string status, string? reason, int? claimedBy, int? decidedBy, DateTime? decidedUtc,
        long? reservationId, CancellationToken ct = default)
    {
        await using var cmd = SqlScope.Of(tx, connections).Command("""
            UPDATE web.request SET status = @s, reason = @r, claimed_by = @c, decided_by = @db, decided_utc = @du, reservation_id = @res WHERE request_id = @i;
            """);
        cmd.Parameters.Big("@i", requestId);
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = status;
        cmd.Parameters.Text("@r", reason, 200);
        cmd.Parameters.Int("@c", claimedBy);
        cmd.Parameters.Int("@db", decidedBy);
        cmd.Parameters.Utc("@du", decidedUtc);
        cmd.Parameters.Big("@res", reservationId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebRequestRow>> ListAsync(int propertyId, string? status, int limit, CancellationToken ct = default)
    {
        var from = string.Format(System.Globalization.CultureInfo.InvariantCulture, RequestFrom, "");
        return await ReadAsync($"SELECT TOP (@n) {RequestColumns} FROM {from} WHERE r.property_id = @p AND (@s IS NULL OR r.status = @s) ORDER BY r.created_utc DESC, r.request_id DESC;",
            p => { p.Int("@n", limit); p.Int("@p", propertyId); p.Add("@s", SqlDbType.VarChar, 10).Value = (object?)status ?? DBNull.Value; },
            async rd =>
            {
                var list = new List<WebRequestRow>();
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRequest(rd));
                return (IReadOnlyList<WebRequestRow>)list;
            }, null, ct).ConfigureAwait(false);
    }

    public async Task<int> PendingCountAsync(int propertyId, CancellationToken ct = default)
        => await ReadAsync("SELECT COUNT(*) FROM web.request WHERE property_id = @p AND status IN ('Pending','Confirming');", p => p.Int("@p", propertyId),
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? rd.GetInt32(0) : 0, null, ct).ConfigureAwait(false);

    public Task<WebRequestRow?> GetAsync(int propertyId, long requestId, CancellationToken ct = default)
        => OneRequestAsync("r.request_id = @i AND r.property_id = @p", p => { p.Big("@i", requestId); p.Int("@p", propertyId); }, false, null, ct);

    public async Task<long?> FindReservationIdAsync(int propertyId, string publicNo, CancellationToken ct = default)
        => await ReadAsync("SELECT TOP (1) reservation_id FROM inv.reservation WHERE property_id = @p AND source = 'Website' AND status IN ('Held','Confirmed','InHouse') AND notes LIKE @n ORDER BY reservation_id;",
            p => { p.Int("@p", propertyId); p.Text("@n", "Web request " + publicNo + "%", 40); },
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? (long?)rd.GetInt64(0) : null, null, ct).ConfigureAwait(false);

    public Task<WebRequestRow?> GetByNoAsync(int propertyId, string publicNo, CancellationToken ct = default)
        => OneRequestAsync("r.public_no = @n AND r.property_id = @p", p => { p.Text("@n", publicNo, 20); p.Int("@p", propertyId); }, false, null, ct);

    public async Task<DepositLinkRow> InsertLinkAsync(ICommandTransaction tx, long requestId, byte[] tokenHash, long amountPaise, DateTime expiresUtc, int createdBy,
        DateTime nowUtc, CancellationToken ct = default)
    {
        await using var cmd = SqlScope.Of(tx, connections).Command($"""
            INSERT web.deposit_link(request_id, token_hash, amount_paise, expires_utc, created_by, created_utc)
            OUTPUT {LinkColumns.Replace("l.", "INSERTED.", StringComparison.Ordinal)}
            VALUES (@r, @h, @a, @e, @u, @n);
            """);
        cmd.Parameters.Big("@r", requestId);
        cmd.Parameters.Add("@h", SqlDbType.Binary, 32).Value = tokenHash;
        cmd.Parameters.Big("@a", amountPaise);
        cmd.Parameters.Utc("@e", expiresUtc);
        cmd.Parameters.Int("@u", createdBy);
        cmd.Parameters.Utc("@n", nowUtc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return ReadLink(rd);
    }

    private Task<(DepositLinkRow Link, long RequestId)?> FindLinkCoreAsync(ICommandTransaction? tx, int propertyId, byte[] hash, string hint, CancellationToken ct)
        => ReadAsync($"""
            SELECT {LinkColumns} FROM web.deposit_link l{hint} JOIN web.request r ON r.request_id = l.request_id
             WHERE l.token_hash = @h AND r.property_id = @p;
            """, p => { p.Add("@h", SqlDbType.Binary, 32).Value = hash; p.Int("@p", propertyId); },
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? ((DepositLinkRow Link, long RequestId)?)(ReadLink(rd), rd.GetInt64(1)) : null, tx, ct);

    public Task<(DepositLinkRow Link, long RequestId)?> FindLinkForUpdateAsync(ICommandTransaction tx, int propertyId, byte[] tokenHash, CancellationToken ct = default)
        => FindLinkCoreAsync(tx, propertyId, tokenHash, " WITH (UPDLOCK, ROWLOCK)", ct);

    public Task<(DepositLinkRow Link, long RequestId)?> FindLinkAsync(int propertyId, byte[] tokenHash, CancellationToken ct = default)
        => FindLinkCoreAsync(null, propertyId, tokenHash, "", ct);

    public async Task<DepositLinkRow?> OpenLinkForUpdateAsync(ICommandTransaction tx, long requestId, CancellationToken ct = default)
        => await ReadAsync($"SELECT {LinkColumns} FROM web.deposit_link l WITH (UPDLOCK, ROWLOCK) WHERE l.request_id = @r AND l.status = 'Open';", p => p.Big("@r", requestId),
            async rd => await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadLink(rd) : null, tx, ct).ConfigureAwait(false);

    public async Task SetLinkStateAsync(ICommandTransaction tx, long linkId, string status, DateTime? paidUtc, int? paidBy, string? paidRef, long? receiptId,
        CancellationToken ct = default)
    {
        await using var cmd = SqlScope.Of(tx, connections).Command("""
            UPDATE web.deposit_link SET status = @s, paid_utc = @pu, paid_by = @pb, paid_ref = @pr, receipt_id = @rc WHERE link_id = @i;
            """);
        cmd.Parameters.Big("@i", linkId);
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 10).Value = status;
        cmd.Parameters.Utc("@pu", paidUtc);
        cmd.Parameters.Int("@pb", paidBy);
        cmd.Parameters.Text("@pr", paidRef, 80);
        cmd.Parameters.Big("@rc", receiptId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DepositLinkRow>> LinksAsync(long requestId, CancellationToken ct = default)
        => await ReadAsync($"SELECT {LinkColumns} FROM web.deposit_link l WHERE l.request_id = @r ORDER BY l.link_id;", p => p.Big("@r", requestId),
            async rd =>
            {
                var list = new List<DepositLinkRow>();
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadLink(rd));
                return (IReadOnlyList<DepositLinkRow>)list;
            }, null, ct).ConfigureAwait(false);
}
