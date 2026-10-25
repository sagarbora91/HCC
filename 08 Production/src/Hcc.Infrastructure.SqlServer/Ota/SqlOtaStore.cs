using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Ota;
using Hcc.Contracts;
using Hcc.Contracts.Ota;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Ota;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Ota;

/// <summary>SQL adapter for <see cref="IOtaStore"/> (schema ota). Reads inv.* only to show current reservation facts; never writes it.</summary>
public sealed class SqlOtaStore(SqlConnectionFactory connections) : IOtaStore
{
    private const string MsgCols = "m.message_id, m.property_id, m.channel_id, m.external_id, m.kind, m.channel_ref, m.guest_name, m.phone, m.email, m.channel_room_code, " +
        "m.arrival, m.departure, m.adults, m.children, m.amount_paise, m.received_utc, m.status, m.reservation_id, m.room_id, m.reason, m.decided_by, m.row_ver";

    private async Task<T> RunAsync<T>(ICommandTransaction? tx, Func<SqlScope, Task<T>> work, CancellationToken ct)
    {
        if (tx is not null) return await work(SqlScope.Of(tx, connections)).ConfigureAwait(false);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await work(new SqlScope(conn, null, connections)).ConfigureAwait(false);
    }

    private static MessageRecord ReadMessage(SqlDataReader r) => new(r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        r.GetStringOrNull(7), r.GetStringOrNull(8), r.GetString(9), r.GetDate(10), r.GetDate(11), r.GetByte(12), r.GetByte(13), r.GetInt64(14), r.GetUtcOrNull(15), r.GetString(16),
        r.GetInt64OrNull(17), r.IsDBNull(18) ? null : r.GetInt32(18), r.GetStringOrNull(19), r.IsDBNull(20) ? null : r.GetInt32(20), (byte[])r["row_ver"]);

    // ───────────── channels ─────────────
    public Task<IReadOnlyList<ChannelRecord>> ListChannelsAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default)
        => RunAsync<IReadOnlyList<ChannelRecord>>(tx, async s =>
        {
            var heads = new List<(int Id, string Code, string Name, string Status, int Com, int Mk, DateTime? Sync, string Note, DateTime? Dirty, byte[] Ver)>();
            await using (var q = s.Command("SELECT channel_id, code, name, status, commission_bps, markup_bps, last_sync_utc, status_note, ari_dirty_utc, row_ver FROM ota.channel WHERE property_id = @p ORDER BY name, channel_id;"))
            {
                q.Parameters.Int("@p", propertyId);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    heads.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetInt32(5), rd.GetUtcOrNull(6), rd.GetString(7), rd.GetUtcOrNull(8), (byte[])rd["row_ver"]));
            }

            var maps = new Dictionary<int, List<ChannelMap>>();
            await using (var q = s.Command("""
                SELECT m.channel_id, m.room_type_id, t.code, m.channel_code, m.stop_sell
                FROM ota.channel_room_map m JOIN ota.channel c ON c.channel_id = m.channel_id JOIN inv.room_type t ON t.room_type_id = m.room_type_id
                WHERE c.property_id = @p ORDER BY t.sort_order, t.room_type_id;
                """))
            {
                q.Parameters.Int("@p", propertyId);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (!maps.TryGetValue(rd.GetInt32(0), out var list)) maps[rd.GetInt32(0)] = list = [];
                    list.Add(new ChannelMap(rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4)));
                }
            }

            return heads.Select(h => new ChannelRecord(h.Id, propertyId, h.Code, h.Name, h.Status, h.Com, h.Mk, h.Sync, h.Note, h.Dirty, h.Ver,
                maps.TryGetValue(h.Id, out var m) ? m : [])).ToList();
        }, ct);

    public async Task<ChannelRecord?> GetChannelAsync(int propertyId, int channelId, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default)
    {
        if (forUpdate && tx is not null)
        {
            var s = SqlScope.Of(tx, connections);
            await using var lk = s.Command("SELECT 1 FROM ota.channel WITH (UPDLOCK, ROWLOCK) WHERE channel_id = @c AND property_id = @p;");
            lk.Parameters.Int("@c", channelId);
            lk.Parameters.Int("@p", propertyId);
            await lk.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        return (await ListChannelsAsync(propertyId, tx, ct).ConfigureAwait(false)).FirstOrDefault(c => c.ChannelId == channelId);
    }

    public Task<int> InsertChannelAsync(ICommandTransaction tx, int propertyId, string code, string name, int commissionBps, int markupBps,
        IReadOnlyList<(int RoomTypeId, string ChannelCode)> mapping, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            int id;
            await using (var q = s.Command("""
                INSERT ota.channel(property_id, code, name, commission_bps, markup_bps) OUTPUT INSERTED.channel_id VALUES (@p, @code, @name, @com, @mk);
                """))
            {
                q.Parameters.Int("@p", propertyId);
                q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = code;
                q.Parameters.Text("@name", name, 80);
                q.Parameters.Int("@com", commissionBps);
                q.Parameters.Int("@mk", markupBps);
                try
                {
                    id = (int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                }
                catch (SqlException ex) when (SqlErrorTranslator.IsDuplicateKey(ex))
                {
                    throw HccException.Validation($"Channel code {code} is already used.");
                }
            }

            await WriteMapAsync(s, id, mapping, new Dictionary<int, bool>(), ct).ConfigureAwait(false);
            return id;
        }, ct);

    private static async Task WriteMapAsync(SqlScope s, int channelId, IReadOnlyList<(int RoomTypeId, string ChannelCode)> mapping, Dictionary<int, bool> stop, CancellationToken ct)
    {
        foreach (var (type, code) in mapping)
        {
            await using var q = s.Command("INSERT ota.channel_room_map(channel_id, room_type_id, channel_code, stop_sell) VALUES (@c, @t, @code, @stop);");
            q.Parameters.Int("@c", channelId);
            q.Parameters.Int("@t", type);
            q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = code;
            q.Parameters.Add("@stop", SqlDbType.Bit).Value = stop.TryGetValue(type, out var v) && v;
            try
            {
                await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqlException ex) when (SqlErrorTranslator.IsDuplicateKey(ex))
            {
                throw HccException.Validation($"Channel code {code} is used twice.");
            }
        }
    }

    public Task UpdateChannelAsync(ICommandTransaction tx, int propertyId, int channelId, byte[] expected, string name, int commissionBps, int markupBps,
        IReadOnlyList<(int RoomTypeId, string ChannelCode)> mapping, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            int rows;
            await using (var q = s.Command("UPDATE ota.channel SET name = @name, commission_bps = @com, markup_bps = @mk, ari_dirty_utc = CASE WHEN status = 'Connected' THEN ISNULL(ari_dirty_utc, SYSUTCDATETIME()) ELSE ari_dirty_utc END WHERE channel_id = @c AND property_id = @p AND row_ver = @v;"))
            {
                q.Parameters.Text("@name", name, 80);
                q.Parameters.Int("@com", commissionBps);
                q.Parameters.Int("@mk", markupBps);
                q.Parameters.Int("@c", channelId);
                q.Parameters.Int("@p", propertyId);
                q.Parameters.Version("@v", expected);
                rows = await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            SqlValues.EnsureVersionMatched(rows, "This channel");
            var stop = new Dictionary<int, bool>();
            await using (var q = s.Command("SELECT room_type_id, stop_sell FROM ota.channel_room_map WHERE channel_id = @c;"))
            {
                q.Parameters.Int("@c", channelId);
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) stop[rd.GetInt32(0)] = rd.GetBoolean(1);
            }

            await using (var q = s.Command("DELETE ota.channel_room_map WHERE channel_id = @c;"))
            {
                q.Parameters.Int("@c", channelId);
                await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await WriteMapAsync(s, channelId, mapping, stop, ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task SetStatusAsync(ICommandTransaction tx, int channelId, string status, string note, DateTime? lastSyncUtc, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("UPDATE ota.channel SET status = @st, status_note = @n, last_sync_utc = ISNULL(@sync, last_sync_utc) WHERE channel_id = @c;");
            q.Parameters.Add("@st", SqlDbType.VarChar, 12).Value = status;
            q.Parameters.Text("@n", note, 200);
            q.Parameters.Utc("@sync", lastSyncUtc);
            q.Parameters.Int("@c", channelId);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task<int> SetStopSellAsync(ICommandTransaction tx, int propertyId, int? channelId, int roomTypeId, bool on, DateTime nowUtc, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("""
                UPDATE m SET stop_sell = @on FROM ota.channel_room_map m JOIN ota.channel c ON c.channel_id = m.channel_id
                WHERE c.property_id = @p AND m.room_type_id = @t AND (@c IS NULL OR m.channel_id = @c) AND m.stop_sell <> @on;
                DECLARE @n INT = @@ROWCOUNT;
                UPDATE c SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) FROM ota.channel c
                WHERE c.property_id = @p AND c.status = 'Connected' AND (@c IS NULL OR c.channel_id = @c)
                  AND EXISTS (SELECT 1 FROM ota.channel_room_map m WHERE m.channel_id = c.channel_id AND m.room_type_id = @t);
                SELECT @n;
                """);
            q.Parameters.Add("@on", SqlDbType.Bit).Value = on;
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Int("@t", roomTypeId);
            q.Parameters.Int("@c", channelId);
            q.Parameters.Utc("@now", nowUtc);
            return (int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }, ct);

    public Task MarkPushedAsync(ICommandTransaction tx, int channelId, DateTime nowUtc, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("UPDATE ota.channel SET last_sync_utc = @now, ari_dirty_utc = NULL WHERE channel_id = @c;");
            q.Parameters.Utc("@now", nowUtc);
            q.Parameters.Int("@c", channelId);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task<IReadOnlyList<(int Id, string Code, int MaxOccupancy)>> ListRoomTypesAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default)
        => RunAsync<IReadOnlyList<(int, string, int)>>(tx, async s =>
        {
            var list = new List<(int, string, int)>();
            await using var q = s.Command("SELECT room_type_id, code, max_occupancy FROM inv.room_type WHERE property_id = @p ORDER BY sort_order, room_type_id;");
            q.Parameters.Int("@p", propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((rd.GetInt32(0), rd.GetString(1), rd.GetByte(2)));
            return list;
        }, ct);

    // ───────────── messages ─────────────
    public Task<(MessageRecord Message, bool Created)> InsertMessageAsync(ICommandTransaction tx, MessageDraft d, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using (var q = s.Command($"SELECT {MsgCols} FROM ota.message m WITH (UPDLOCK, HOLDLOCK) WHERE m.channel_id = @c AND (m.external_id = @e OR (@kind = 'New' AND m.kind = 'New' AND m.channel_ref = @ref)) ORDER BY m.message_id;"))
            {
                q.Parameters.Int("@c", d.ChannelId);
                q.Parameters.Add("@e", SqlDbType.VarChar, 40).Value = d.ExternalId;
                q.Parameters.Add("@kind", SqlDbType.VarChar, 6).Value = d.Kind;
                q.Parameters.Add("@ref", SqlDbType.VarChar, 30).Value = d.ChannelRef;
                await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rd.ReadAsync(ct).ConfigureAwait(false)) return (ReadMessage(rd), false);
            }

            long id;
            await using (var q = s.Command("""
                INSERT ota.message(property_id, channel_id, external_id, kind, channel_ref, guest_name, phone, email, channel_room_code, arrival, departure, adults, children, amount_paise, received_utc, status)
                OUTPUT INSERTED.message_id
                VALUES (@p, @c, @e, @kind, @ref, @guest, @phone, @email, @code, @a, @d, @ad, @ch, @amt, @recv, @st);
                """))
            {
                q.Parameters.Int("@p", d.PropertyId);
                q.Parameters.Int("@c", d.ChannelId);
                q.Parameters.Add("@e", SqlDbType.VarChar, 40).Value = d.ExternalId;
                q.Parameters.Add("@kind", SqlDbType.VarChar, 6).Value = d.Kind;
                q.Parameters.Add("@ref", SqlDbType.VarChar, 30).Value = d.ChannelRef;
                q.Parameters.Text("@guest", d.Guest, 200);
                q.Parameters.Add("@phone", SqlDbType.VarChar, 30).Value = (object?)d.Phone ?? DBNull.Value;
                q.Parameters.Text("@email", d.Email, 200);
                q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = d.ChannelRoomCode;
                q.Parameters.Date("@a", d.Arrival);
                q.Parameters.Date("@d", d.Departure);
                q.Parameters.Int("@ad", d.Adults);
                q.Parameters.Int("@ch", d.Children);
                q.Parameters.Big("@amt", d.AmountPaise);
                q.Parameters.Utc("@recv", d.ReceivedUtc);
                q.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = d.Status;
                id = (long)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            await using var r2 = s.Command($"SELECT {MsgCols} FROM ota.message m WHERE m.message_id = @id;");
            r2.Parameters.Big("@id", id);
            await using var rd2 = await r2.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd2.ReadAsync(ct).ConfigureAwait(false);
            return (ReadMessage(rd2), true);
        }, ct);

    private Task<MessageRecord?> OneMessageAsync(ICommandTransaction? tx, string where, Action<SqlParameterCollection> bind, bool forUpdate, CancellationToken ct)
        => RunAsync<MessageRecord?>(tx, async s =>
        {
            var hint = forUpdate && tx is not null ? " WITH (UPDLOCK, ROWLOCK)" : "";
            await using var q = s.Command($"SELECT TOP 1 {MsgCols} FROM ota.message m{hint} WHERE {where} ORDER BY m.message_id;");
            bind(q.Parameters);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadMessage(rd) : null;
        }, ct);

    public Task<MessageRecord?> GetMessageAsync(int propertyId, long messageId, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default)
        => OneMessageAsync(tx, "m.message_id = @id AND m.property_id = @p", p => { p.Big("@id", messageId); p.Int("@p", propertyId); }, forUpdate, ct);

    public Task<MessageRecord?> FindNewAsync(int propertyId, int channelId, string channelRef, ICommandTransaction? tx = null, bool forUpdate = false, CancellationToken ct = default)
        => OneMessageAsync(tx, "m.property_id = @p AND m.channel_id = @c AND m.kind = 'New' AND m.channel_ref = @ref",
            p => { p.Int("@p", propertyId); p.Int("@c", channelId); p.Add("@ref", SqlDbType.VarChar, 30).Value = channelRef; }, forUpdate, ct);

    public Task<IReadOnlyList<MessageRecord>> ListMessagesAsync(int propertyId, string? status, int limit, CancellationToken ct = default)
        => RunAsync<IReadOnlyList<MessageRecord>>(null, async s =>
        {
            var list = new List<MessageRecord>();
            await using var q = s.Command($"SELECT TOP (@n) {MsgCols} FROM ota.message m WHERE m.property_id = @p AND m.status <> 'Queued' AND (@st IS NULL OR m.status = @st) ORDER BY m.message_id DESC;");
            q.Parameters.Int("@n", limit);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = (object?)status ?? DBNull.Value;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadMessage(rd));
            return list;
        }, ct);

    public Task<MessageRecord?> NextQueuedAsync(ICommandTransaction tx, int propertyId, int? channelId, CancellationToken ct = default)
        => RunAsync<MessageRecord?>(tx, async s =>
        {
            await using var q = s.Command($"""
                SELECT TOP 1 {MsgCols} FROM ota.message m WITH (UPDLOCK, READPAST, ROWLOCK) JOIN ota.channel c ON c.channel_id = m.channel_id
                WHERE m.property_id = @p AND m.status = 'Queued' AND c.status = 'Connected' AND (@c IS NULL OR m.channel_id = @c) ORDER BY m.message_id;
                """);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Int("@c", channelId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadMessage(rd) : null;
        }, ct);

    public Task ActivateAsync(ICommandTransaction tx, long messageId, DateTime nowUtc, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("UPDATE ota.message SET status = 'Pending', received_utc = @now WHERE message_id = @id AND status = 'Queued';");
            q.Parameters.Utc("@now", nowUtc);
            q.Parameters.Big("@id", messageId);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task SetMessageStateAsync(ICommandTransaction tx, long messageId, string status, string? reason, int? decidedBy, DateTime? decidedUtc, long? reservationId,
        int? roomId, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("UPDATE ota.message SET status = @st, reason = @r, decided_by = @u, decided_utc = @d, reservation_id = ISNULL(@res, reservation_id), room_id = ISNULL(@room, room_id) WHERE message_id = @id;");
            q.Parameters.Add("@st", SqlDbType.VarChar, 10).Value = status;
            q.Parameters.Text("@r", reason, 200);
            q.Parameters.Int("@u", decidedBy);
            q.Parameters.Utc("@d", decidedUtc);
            q.Parameters.Big("@res", reservationId);
            q.Parameters.Int("@room", roomId);
            q.Parameters.Big("@id", messageId);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task UpdateStayAsync(ICommandTransaction tx, long messageId, DateOnly arrival, DateOnly departure, int adults, int children, long? amountPaise, int? roomId,
        CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("UPDATE ota.message SET arrival = @a, departure = @d, adults = @ad, children = @ch, amount_paise = ISNULL(@amt, amount_paise), room_id = ISNULL(@room, room_id) WHERE message_id = @id;");
            q.Parameters.Date("@a", arrival);
            q.Parameters.Date("@d", departure);
            q.Parameters.Int("@ad", adults);
            q.Parameters.Int("@ch", children);
            q.Parameters.Big("@amt", amountPaise);
            q.Parameters.Int("@room", roomId);
            q.Parameters.Big("@id", messageId);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    // ───────────── commission ledger ─────────────
    public Task AddLedgerAsync(ICommandTransaction tx, LedgerEntry e, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("""
                INSERT ota.commission_entry(property_id, channel_id, message_id, reservation_id, kind, base_paise, commission_bps, commission_paise, note, created_utc, created_by)
                VALUES (@p, @c, @m, @r, @k, @b, @bps, @com, @n, @now, @u);
                """);
            q.Parameters.Int("@p", e.PropertyId);
            q.Parameters.Int("@c", e.ChannelId);
            q.Parameters.Big("@m", e.MessageId);
            q.Parameters.Big("@r", e.ReservationId);
            q.Parameters.Add("@k", SqlDbType.VarChar, 10).Value = e.Kind;
            q.Parameters.Big("@b", e.BasePaise);
            q.Parameters.Int("@bps", e.CommissionBps);
            q.Parameters.Big("@com", e.CommissionPaise);
            q.Parameters.Text("@n", e.Note, 200);
            q.Parameters.Utc("@now", e.CreatedUtc);
            q.Parameters.Int("@u", e.CreatedBy);
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task<LedgerTotals> LedgerTotalsAsync(ICommandTransaction tx, long messageId, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            await using var q = s.Command("SELECT ISNULL(SUM(base_paise), 0), ISNULL(SUM(commission_paise), 0), (SELECT TOP 1 commission_bps FROM ota.commission_entry WHERE message_id = @m ORDER BY entry_id) FROM ota.commission_entry WHERE message_id = @m;");
            q.Parameters.Big("@m", messageId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            return new LedgerTotals(rd.GetInt64(0), rd.GetInt64(1), rd.IsDBNull(2) ? null : rd.GetInt32(2));
        }, ct);

    // ───────────── push ─────────────
    public Task<long> InsertPushAsync(ICommandTransaction tx, PushDraft d, CancellationToken ct = default)
        => RunAsync(tx, async s =>
        {
            long id;
            await using (var q = s.Command("""
                INSERT ota.push(property_id, channel_id, requested_utc, requested_by, row_count, closed_count, summary, status)
                OUTPUT INSERTED.push_id VALUES (@p, @c, @now, @u, @rows, @closed, @sum, 'Queued');
                """))
            {
                q.Parameters.Int("@p", d.PropertyId);
                q.Parameters.Int("@c", d.ChannelId);
                q.Parameters.Utc("@now", d.RequestedUtc);
                q.Parameters.Int("@u", d.RequestedBy);
                q.Parameters.Int("@rows", d.Rows.Count);
                q.Parameters.Int("@closed", d.Rows.Count(x => x.Closed));
                q.Parameters.Text("@sum", d.Summary, 300);
                id = (long)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            foreach (var r in d.Rows)
            {
                await using var q = s.Command("INSERT ota.push_row(push_id, room_type_id, night, channel_code, avail, rate_paise, closed, why) VALUES (@id, @t, @n, @code, @av, @rate, @cl, @why);");
                q.Parameters.Big("@id", id);
                q.Parameters.Int("@t", r.RoomTypeId);
                q.Parameters.Date("@n", r.Night);
                q.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = r.ChannelCode;
                q.Parameters.Int("@av", r.Avail);
                q.Parameters.Big("@rate", r.RatePaise);
                q.Parameters.Add("@cl", SqlDbType.Bit).Value = r.Closed;
                q.Parameters.Add("@why", SqlDbType.VarChar, 12).Value = (object?)r.Why ?? DBNull.Value;
                await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return id;
        }, ct);

    public Task<IReadOnlyList<PushLogView>> ListPushesAsync(int propertyId, int? channelId, int limit, CancellationToken ct = default)
        => RunAsync<IReadOnlyList<PushLogView>>(null, async s =>
        {
            var list = new List<PushLogView>();
            await using var q = s.Command("""
                SELECT TOP (@n) p.push_id, p.channel_id, c.name, p.requested_utc, p.row_count, p.closed_count, p.summary, p.status, p.delivered_utc, p.ack_ref
                FROM ota.push p JOIN ota.channel c ON c.channel_id = p.channel_id
                WHERE p.property_id = @p AND (@c IS NULL OR p.channel_id = @c) ORDER BY p.push_id DESC;
                """);
            q.Parameters.Int("@n", limit);
            q.Parameters.Int("@p", propertyId);
            q.Parameters.Int("@c", channelId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new PushLogView(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), OtaQueries.Iso(DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc)), rd.GetInt32(4), rd.GetInt32(5),
                    rd.GetString(6), rd.GetString(7), rd.GetUtcOrNull(8) is { } dl ? OtaQueries.Iso(dl) : null, rd.GetStringOrNull(9)));
            return list;
        }, ct);

    // ───────────── reports ─────────────
    private const string BookingSql = """
        SELECT n.message_id, n.reservation_id, r.public_no, n.channel_id, c.name, n.channel_ref, n.guest_name, r.arrival, r.departure, r.status,
               n.arrival, n.departure, n.amount_paise,
               ISNULL((SELECT SUM(rate_paise) FROM inv.reservation_night x WHERE x.reservation_id = r.reservation_id), 0),
               ISNULL((SELECT TOP 1 commission_bps FROM ota.commission_entry e WHERE e.message_id = n.message_id ORDER BY e.entry_id), c.commission_bps),
               ISNULL((SELECT SUM(commission_paise) FROM ota.commission_entry e WHERE e.message_id = n.message_id), 0)
        FROM ota.message n
        JOIN inv.reservation r ON r.reservation_id = n.reservation_id
        JOIN ota.channel c ON c.channel_id = n.channel_id
        WHERE n.property_id = @p AND n.kind = 'New' AND n.status = 'Accepted' AND r.arrival BETWEEN @from AND @to AND (@ch IS NULL OR n.channel_id = @ch)
        ORDER BY c.name, r.arrival, n.message_id;
        """;

    private sealed record Booking(long MessageId, long ReservationId, string PublicNo, int ChannelId, string Channel, string Ref, string Guest, DateOnly Arrival, DateOnly Departure,
        string Status, DateOnly ChArrival, DateOnly ChDeparture, long ChannelAmount, long Base, int Bps, long Ledger);

    private async Task<List<Booking>> BookingsAsync(int propertyId, DateOnly from, DateOnly to, int? channelId, CancellationToken ct)
    {
        var list = new List<Booking>();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command(BookingSql, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Date("@from", from);
        q.Parameters.Date("@to", to);
        q.Parameters.Int("@ch", channelId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new Booking(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.GetInt32(3), rd.GetString(4), rd.GetString(5), rd.GetString(6), rd.GetDate(7), rd.GetDate(8), rd.GetString(9),
                rd.GetDate(10), rd.GetDate(11), rd.GetInt64(12), rd.GetInt64(13), rd.GetInt32(14), rd.GetInt64(15)));
        return list;
    }

    public async Task<IReadOnlyList<CommissionRowView>> CommissionRowsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => (await BookingsAsync(propertyId, from, to, null, ct).ConfigureAwait(false)).Where(b => OtaRules.EarnsCommission(b.Status)).Select(b =>
        {
            var com = OtaRules.Commission(b.Base, b.Bps);
            return new CommissionRowView(b.MessageId, b.ReservationId, b.PublicNo, b.ChannelId, b.Ref, b.Guest, StayDates.ToWire(b.Arrival), StayDates.ToWire(b.Departure), b.Status,
                StayDates.Nights(b.Arrival, b.Departure), b.Base, b.Bps, com, b.Base - com);
        }).ToList();

    public async Task<IReadOnlyList<ReconciliationRowView>> ReconciliationRowsAsync(int propertyId, DateOnly from, DateOnly to, int? channelId, CancellationToken ct = default)
        => (await BookingsAsync(propertyId, from, to, channelId, ct).ConfigureAwait(false)).Select(b =>
        {
            var expected = OtaRules.EarnsCommission(b.Status) ? OtaRules.Commission(b.Base, b.Bps) : 0;
            var flags = new List<string>();
            if (!OtaRules.EarnsCommission(b.Status) && b.Ledger != 0) flags.Add($"Reservation is {b.Status}: commission was accrued but the channel never cancelled it");
            else if (b.Ledger != expected) flags.Add("Commission ledger differs from the booking as it stands now");
            if (b.ChArrival != b.Arrival || b.ChDeparture != b.Departure) flags.Add("Stay dates differ from the channel's");
            if (b.ChannelAmount > 0 && b.ChannelAmount < b.Base) flags.Add("Channel amount is below the hotel's room revenue");
            return new ReconciliationRowView(b.MessageId, b.ReservationId, b.PublicNo, b.Channel, b.Ref, b.Status, StayDates.ToWire(b.ChArrival), StayDates.ToWire(b.ChDeparture),
                StayDates.ToWire(b.Arrival), StayDates.ToWire(b.Departure), b.ChannelAmount, b.Base, b.ChannelAmount - b.Base, b.Ledger, expected, b.Ledger - expected, flags);
        }).ToList();
}
