using Hcc.Contracts;
using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.GuestServices;
using Hcc.Contracts.GuestServices;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.GuestServices;

/// <summary>SQL adapter of <see cref="IFeedbackStore"/> (ops.guest_feedback / ops.feedback_followup, migrations 4393-4394).</summary>
public sealed class SqlFeedbackStore(SqlConnectionFactory connections) : IFeedbackStore
{
    private static async Task AuditAsync(SqlConnection c, SqlTransaction t, int p, int by, string action, string target, string detail, CancellationToken ct)
    {
        await using var a = new SqlCommand("INSERT plat.audit_event(property_id, principal_id, action, target, detail) VALUES (@p, @by, @a, @t, @d);", c, t);
        a.Add("@p", SqlDbType.Int, p); a.Add("@by", SqlDbType.Int, by); a.Add("@a", SqlDbType.VarChar, action, 60); a.Add("@t", SqlDbType.VarChar, target, 60); a.Add("@d", SqlDbType.NVarChar, detail, 1000);
        await a.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<FeedbackStay?> StayAsync(int propertyId, long stayId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.stay_id, seg.room_id, r.room_no, g.full_name, s.status
              FROM gst.stay s JOIN gst.guest g ON g.guest_id = s.guest_id
              JOIN gst.stay_segment seg ON seg.stay_id = s.stay_id AND seg.seq = (SELECT MAX(x.seq) FROM gst.stay_segment x WHERE x.stay_id = s.stay_id)
              JOIN inv.room r ON r.room_id = seg.room_id
             WHERE s.property_id = @p AND s.stay_id = @s;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@s", SqlDbType.BigInt, stayId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new FeedbackStay(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4)) : null;
    }

    public async Task<FeedbackResult> InsertAsync(int propertyId, int principalId, FeedbackStay stay, int stars, string? comment, FeedbackComplaint? complaint, DateTime utc, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var dup = new SqlCommand("SELECT 1 FROM ops.guest_feedback WITH (UPDLOCK, HOLDLOCK) WHERE stay_id = @s;", conn, tx))
        {
            dup.Add("@s", SqlDbType.BigInt, stay.StayId);
            if (await dup.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Feedback for this stay is already recorded.");
        }
        long? complaintId = null;
        if (complaint is not null)
        {
            await using var c = new SqlCommand("""
                INSERT ops.guest_complaint(property_id, stay_id, room_id, guest_name, category, severity, description, owner_id, status, created_utc, created_by)
                OUTPUT INSERTED.complaint_id VALUES (@p, @s, @r, @g, @cat, @sev, @d, @by, 'Open', @t, @by);
                """, conn, tx);
            c.Add("@p", SqlDbType.Int, propertyId); c.Add("@s", SqlDbType.BigInt, stay.StayId); c.Add("@r", SqlDbType.Int, stay.RoomId); c.Add("@g", SqlDbType.NVarChar, stay.GuestName, 200);
            c.Add("@cat", SqlDbType.NVarChar, complaint.Category, 60); c.Add("@sev", SqlDbType.VarChar, complaint.Severity, 8); c.Add("@d", SqlDbType.NVarChar, complaint.Description, 400);
            c.Add("@by", SqlDbType.Int, principalId); c.Add("@t", SqlDbType.DateTime2, utc);
            complaintId = (long)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        long id;
        await using (var f = new SqlCommand("""
            INSERT ops.guest_feedback(property_id, stay_id, stars, comment, complaint_id, recorded_utc, recorded_by)
            OUTPUT INSERTED.feedback_id VALUES (@p, @s, @st, @c, @cid, @t, @by);
            """, conn, tx))
        {
            f.Add("@p", SqlDbType.Int, propertyId); f.Add("@s", SqlDbType.BigInt, stay.StayId); f.Add("@st", SqlDbType.TinyInt, (byte)stars); f.Add("@c", SqlDbType.NVarChar, comment, 400);
            f.Add("@cid", SqlDbType.BigInt, complaintId); f.Add("@t", SqlDbType.DateTime2, utc); f.Add("@by", SqlDbType.Int, principalId);
            id = (long)(await f.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        await AuditAsync(conn, tx, propertyId, principalId, "FEEDBACK_RECORD", $"stay:{stay.StayId}", $"Feedback {stars} of 5{(complaintId is { } x ? $"; complaint {x} opened" : string.Empty)}.", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new FeedbackResult(id, complaintId);
    }

    public async Task<IReadOnlyList<FeedbackView>> ListAsync(int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT f.feedback_id, f.stay_id, r.room_no, g.full_name, f.stars, f.comment, f.complaint_id, f.recorded_utc, f.recorded_by
              FROM ops.guest_feedback f JOIN gst.stay s ON s.stay_id = f.stay_id JOIN gst.guest g ON g.guest_id = s.guest_id
              JOIN gst.stay_segment seg ON seg.stay_id = s.stay_id AND seg.seq = (SELECT MAX(x.seq) FROM gst.stay_segment x WHERE x.stay_id = s.stay_id)
              JOIN inv.room r ON r.room_id = seg.room_id
             WHERE f.property_id = @p AND (@from IS NULL OR f.recorded_utc >= @from) AND (@to IS NULL OR f.recorded_utc < @to)
             ORDER BY f.feedback_id DESC;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        // Hotel-local dates (IST, +05:30): [from 00:00, to+1 00:00).
        cmd.Add("@from", SqlDbType.DateTime2, from is { } a ? a.ToDateTime(TimeOnly.MinValue).AddMinutes(-330) : null);
        cmd.Add("@to", SqlDbType.DateTime2, to is { } b ? b.AddDays(1).ToDateTime(TimeOnly.MinValue).AddMinutes(-330) : null);
        var list = new List<FeedbackView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new FeedbackView(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.GetString(3), rd.GetByte(4), rd.IsDBNull(5) ? null : rd.GetString(5), rd.IsDBNull(6) ? null : rd.GetInt64(6),
                DateTime.SpecifyKind(rd.GetDateTime(7), DateTimeKind.Utc), rd.GetInt32(8)));
        return list;
    }

    public async Task<IReadOnlyList<FeedbackFollowUpView>> FollowUpsAsync(int propertyId, bool openOnly, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT followup_id, source, ref_id, since_date, status, created_utc, done_utc FROM ops.feedback_followup
             WHERE property_id = @p AND (@open = 0 OR status = 'open') ORDER BY followup_id DESC;
            """, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@open", SqlDbType.Bit, openOnly);
        var list = new List<FeedbackFollowUpView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new FeedbackFollowUpView(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), DateOnly.FromDateTime(rd.GetDateTime(3)), rd.GetString(4),
                DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc), rd.IsDBNull(6) ? null : DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc)));
        return list;
    }

    public async Task<bool> ResolveFollowUpAsync(int propertyId, int principalId, long followUpId, DateTime utc, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE ops.feedback_followup SET status = 'done', done_utc = @t, done_by = @by WHERE property_id = @p AND followup_id = @id AND status = 'open';", conn, tx);
        cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@id", SqlDbType.BigInt, followUpId); cmd.Add("@t", SqlDbType.DateTime2, utc); cmd.Add("@by", SqlDbType.Int, principalId);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) return false;
        await AuditAsync(conn, tx, propertyId, principalId, "FEEDBACK_FOLLOWUP_DONE", $"followup:{followUpId}", "Owner follow-up call done.", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The day-close sweep (set-based, idempotent): one open follow-up per complaint / stored lost item unresolved 7+ days; resolved ones close.</summary>
    public const string SweepSql = """
        INSERT ops.feedback_followup(property_id, source, ref_id, since_date)
        SELECT @p, 'complaint', c.complaint_id, CAST(c.created_utc AS DATE) FROM ops.guest_complaint c
         WHERE c.property_id = @p AND c.status IN ('Open','Investigating') AND DATEDIFF(DAY, CAST(c.created_utc AS DATE), @d) >= 7
           AND NOT EXISTS (SELECT 1 FROM ops.feedback_followup f WHERE f.property_id = @p AND f.source = 'complaint' AND f.ref_id = c.complaint_id AND f.status = 'open');
        INSERT ops.feedback_followup(property_id, source, ref_id, since_date)
        SELECT @p, 'lostfound', i.item_id, i.found_date FROM ops.lost_item i
         WHERE i.property_id = @p AND i.status = 'Stored' AND DATEDIFF(DAY, i.found_date, @d) >= 7
           AND NOT EXISTS (SELECT 1 FROM ops.feedback_followup f WHERE f.property_id = @p AND f.source = 'lostfound' AND f.ref_id = i.item_id AND f.status = 'open');
        UPDATE f SET status = 'done', done_utc = SYSUTCDATETIME() FROM ops.feedback_followup f
         WHERE f.property_id = @p AND f.status = 'open' AND (
               (f.source = 'complaint' AND NOT EXISTS (SELECT 1 FROM ops.guest_complaint c WHERE c.complaint_id = f.ref_id AND c.status IN ('Open','Investigating')))
            OR (f.source = 'lostfound' AND NOT EXISTS (SELECT 1 FROM ops.lost_item i WHERE i.item_id = f.ref_id AND i.status = 'Stored')));
        """;

    public static async Task SweepAsync(SqlConnection connection, SqlTransaction transaction, int propertyId, DateOnly businessDate, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SweepSql, connection, transaction);
        cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@d", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Runs the follow-up sweep when a business day closes (consumes <c>BusinessDayClosed</c>; exactly-once effect per consumer, and the sweep is idempotent anyway).</summary>
public sealed class FeedbackFollowUpProjector(OutboxConsumer outbox)
{
    public const string ConsumerName = "feedback-followups";

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, ["BusinessDayClosed"], ApplyAsync, batchSize, ct);

    private sealed record Closed(int PropertyId, string BusinessDate);

    public static async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var e = JsonSerializer.Deserialize<Closed>(evt.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Outbox {evt.OutboxId}: empty BusinessDayClosed payload.");
        await SqlFeedbackStore.SweepAsync(connection, transaction, e.PropertyId, StayDates.Parse(e.BusinessDate, "businessDate"), ct).ConfigureAwait(false);
    }
}
