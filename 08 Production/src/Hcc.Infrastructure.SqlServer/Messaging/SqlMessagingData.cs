using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Messaging;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Messaging;

/// <summary>
/// SQL adapter of <see cref="IMessagingData"/> bound to one connection + transaction (a command's, or the trigger consumer's).
/// Tables: msg.* (migration 1600) plus read-only joins on inv.reservation / gst.guest / gst.stay / bill.* / pay.receipt.
/// The guest ID columns (id_type, id_last4) are never selected here (DEC-068).
/// </summary>
public sealed class SqlMessagingData(SqlConnection connection, SqlTransaction transaction, SqlConnectionFactory factory) : IMessagingData
{
    private SqlCommand Cmd(string sql) => factory.Command(sql, connection, transaction);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    public async Task EnsureDefaultsAsync(int propertyId, DateTime utc, CancellationToken ct = default)
    {
        // Lazy per-property seed of the six templates and six rules; locked check-then-insert (rule 8), idempotent.
        await using var cmd = Cmd("""
            IF NOT EXISTS (SELECT 1 FROM msg.template WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p)
              INSERT msg.template(property_id, code, name, subject, body, channel)
              SELECT @p, JSON_VALUE(j.value, '$.code'), JSON_VALUE(j.value, '$.name'), JSON_VALUE(j.value, '$.subject'), JSON_VALUE(j.value, '$.body'), JSON_VALUE(j.value, '$.channel')
              FROM OPENJSON(@tpl) j;
            IF NOT EXISTS (SELECT 1 FROM msg.trigger_rule WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p)
              INSERT msg.trigger_rule(property_id, rule_id, event, template_code, channel, enabled)
              SELECT @p, JSON_VALUE(j.value, '$.id'), JSON_VALUE(j.value, '$.event'), JSON_VALUE(j.value, '$.template'), JSON_VALUE(j.value, '$.channel'), CAST(JSON_VALUE(j.value, '$.enabled') AS BIT)
              FROM OPENJSON(@rules) j;
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@tpl", SqlDbType.NVarChar, System.Text.Json.JsonSerializer.Serialize(MessagingRules.DefaultTemplates.Select(t => new { code = t.Code, name = t.Name, subject = t.Subject, body = t.Body, channel = t.Channel })), -1);
        cmd.Add("@rules", SqlDbType.NVarChar, System.Text.Json.JsonSerializer.Serialize(MessagingRules.DefaultRules.Select(r => new { id = r.RuleId, @event = r.Event, template = r.TemplateCode, channel = r.Channel, enabled = r.Enabled ? "1" : "0" })), -1);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _ = utc;
    }

    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<MsgBooking?> LoadBookingAsync(int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("""
            SELECT r.reservation_id, r.public_no, r.status, r.guest_name, r.arrival, r.departure, p.name,
                   g.guest_id, g.full_name, g.phone, g.email,
                   COALESCE(
                     (SELECT TOP 1 rm.room_no FROM gst.stay s JOIN gst.stay_segment sg ON sg.stay_id = s.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
                      WHERE s.reservation_id = r.reservation_id ORDER BY sg.seq DESC),
                     (SELECT TOP 1 rm.room_no FROM inv.room_night n JOIN inv.room rm ON rm.room_id = n.room_id WHERE n.reservation_id = r.reservation_id ORDER BY n.night)) AS room_no
            FROM inv.reservation r
            JOIN inv.property p ON p.property_id = r.property_id
            LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
            LEFT JOIN gst.guest g ON g.guest_id = reg.guest_id
            WHERE r.reservation_id = @r AND r.property_id = @p;
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var guestId = rd.IsDBNull(7) ? (long?)null : rd.GetInt64(7);
        return new MsgBooking(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), guestId, guestId is null ? rd.GetString(3) : rd.GetString(8),
            new MsgContact(P3Sql.StrOrNull(rd, 9), P3Sql.StrOrNull(rd, 10)), P3Sql.StrOrNull(rd, 11), P3Sql.Date(rd, 4), P3Sql.Date(rd, 5), rd.GetString(6));
    }

    public async Task<MsgFolioFigures> FolioFiguresAsync(long reservationId, long? receiptId, CancellationToken ct = default)
    {
        var lines = new List<FolioLine>();
        var receipts = new List<FolioReceipt>();
        long? receiptAmount = null;
        await using (var cmd = Cmd("""
            SELECT c.amount_paise, c.tax_bps FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = @r;
            SELECT rc.receipt_id, rc.kind, rc.amount_paise FROM pay.receipt rc JOIN bill.folio f ON f.folio_id = rc.folio_id WHERE f.reservation_id = @r;
            """))
        {
            cmd.Add("@r", SqlDbType.BigInt, reservationId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) lines.Add(new FolioLine(Paise.FromWire(rd.GetInt64(0)), rd.GetInt32(1)));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                receipts.Add(new FolioReceipt(FolioTotals.ParseKind(rd.GetString(1)), Paise.FromWire(rd.GetInt64(2))));
                if (receiptId is { } id && rd.GetInt64(0) == id) receiptAmount = rd.GetInt64(2);
            }
        }

        var t = FolioTotals.Compute(lines, receipts);
        return new MsgFolioFigures(t.Total.Value, t.Balance.Value, receiptAmount);
    }

    public async Task<long?> ReservationOfFolioAsync(long folioId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT reservation_id FROM bill.folio WHERE folio_id = @f;");
        cmd.Add("@f", SqlDbType.BigInt, folioId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long r ? r : null;
    }

    public async Task<int?> PropertyOfReservationAsync(long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT property_id FROM inv.reservation WHERE reservation_id = @r;");
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int p ? p : null;
    }

    public async Task<MsgConsent> GetConsentAsync(int propertyId, long? guestId, CancellationToken ct = default) =>
        guestId is { } g ? (await GetStoredConsentAsync(propertyId, g, ct).ConfigureAwait(false))?.Consent ?? MsgConsent.Default : MsgConsent.Default;

    public async Task<IReadOnlyList<MsgRuleDef>> EnabledRulesForEventAsync(int propertyId, string evt, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT rule_id, event, template_code, channel, enabled FROM msg.trigger_rule WHERE property_id = @p AND event = @e AND enabled = 1 ORDER BY rule_id;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@e", SqlDbType.VarChar, evt, 20);
        var list = new List<MsgRuleDef>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new MsgRuleDef(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4)));
        return list;
    }

    private const string TemplateSql = "SELECT code, name, channel, subject, body, row_ver FROM msg.template ";

    private static StoredTemplate ReadTemplate(SqlDataReader rd) => new(new MsgTemplateDef(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4)), (byte[])rd[5]);

    public async Task<StoredTemplate?> GetTemplateAsync(int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(TemplateSql + "WHERE property_id = @p AND code = @c;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@c", SqlDbType.VarChar, code, 30);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadTemplate(rd) : null;
    }

    public async Task<StoredTemplate?> LockTemplateAsync(int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(TemplateSql + "WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND code = @c;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@c", SqlDbType.VarChar, code, 30);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadTemplate(rd) : null;
    }

    public async Task<IReadOnlyList<StoredTemplate>> TemplatesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(TemplateSql + "WHERE property_id = @p ORDER BY code;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var list = new List<StoredTemplate>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadTemplate(rd));
        return list;
    }

    private const string RuleSql = "SELECT rule_id, event, template_code, channel, enabled, row_ver FROM msg.trigger_rule ";

    private static StoredRule ReadRule(SqlDataReader rd) => new(new MsgRuleDef(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4)), (byte[])rd[5]);

    public async Task<StoredRule?> LockRuleAsync(int propertyId, string ruleId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(RuleSql + "WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND rule_id = @r;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@r", SqlDbType.VarChar, ruleId, 8);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRule(rd) : null;
    }

    public async Task<IReadOnlyList<StoredRule>> RulesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(RuleSql + "WHERE property_id = @p ORDER BY rule_id;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        var list = new List<StoredRule>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRule(rd));
        return list;
    }

    public async Task UpdateTemplateAsync(int propertyId, string code, byte[] expected, string subject, string body, string channel, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Cmd("UPDATE msg.template SET subject = @s, body = @b, channel = @ch, updated_utc = @u, updated_by = @by WHERE property_id = @p AND code = @c AND row_ver = @v;");
        cmd.Add("@s", SqlDbType.NVarChar, subject, 120);
        cmd.Add("@b", SqlDbType.NVarChar, body, 600);
        cmd.Add("@ch", SqlDbType.VarChar, channel, 8);
        cmd.Add("@u", SqlDbType.DateTime2, utc);
        cmd.Add("@by", SqlDbType.Int, principalId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@c", SqlDbType.VarChar, code, 30);
        cmd.Add("@v", SqlDbType.Binary, expected, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This template");
    }

    public async Task UpdateRuleAsync(int propertyId, string ruleId, byte[] expected, bool enabled, string channel, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Cmd("UPDATE msg.trigger_rule SET enabled = @e, channel = @ch, updated_utc = @u, updated_by = @by WHERE property_id = @p AND rule_id = @r AND row_ver = @v;");
        cmd.Add("@e", SqlDbType.Bit, enabled);
        cmd.Add("@ch", SqlDbType.VarChar, channel, 8);
        cmd.Add("@u", SqlDbType.DateTime2, utc);
        cmd.Add("@by", SqlDbType.Int, principalId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@r", SqlDbType.VarChar, ruleId, 8);
        cmd.Add("@v", SqlDbType.Binary, expected, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This rule");
    }

    public async Task<long> NextMessageIdAsync(CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT NEXT VALUE FOR msg.message_seq;");
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<bool> InsertMessageAsync(NewMessage m, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.TriggerKey is not null)
        {
            await using var chk = Cmd("SELECT COUNT(*) FROM msg.message WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND trigger_key = @k;");
            chk.Add("@p", SqlDbType.Int, m.PropertyId);
            chk.Add("@k", SqlDbType.VarChar, m.TriggerKey, 60);
            if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return false;
        }

        await using var cmd = Cmd("""
            INSERT msg.message(message_id, property_id, reservation_id, guest_id, template_code, channel, recipient_masked, subject, body, queued_utc, outcome,
                               blocked_reason, rule_id, trigger_key, retry_of, actor, created_utc)
            VALUES (@id, @p, @r, @g, @t, @ch, @rm, @s, @b, @q, @o, @br, @ru, @k, @ro, @a, @c);
            """);
        cmd.Add("@id", SqlDbType.BigInt, m.MessageId);
        cmd.Add("@p", SqlDbType.Int, m.PropertyId);
        cmd.Add("@r", SqlDbType.BigInt, m.ReservationId);
        cmd.Add("@g", SqlDbType.BigInt, m.GuestId);
        cmd.Add("@t", SqlDbType.VarChar, m.TemplateCode, 30);
        cmd.Add("@ch", SqlDbType.VarChar, m.Channel, 8);
        cmd.Add("@rm", SqlDbType.NVarChar, m.RecipientMasked, 120);
        cmd.Add("@s", SqlDbType.NVarChar, m.Subject, 200);
        cmd.Add("@b", SqlDbType.NVarChar, m.Body, 1000);
        cmd.Add("@q", SqlDbType.DateTime2, m.QueuedUtc);
        cmd.Add("@o", SqlDbType.VarChar, m.Outcome, 9);
        cmd.Add("@br", SqlDbType.NVarChar, m.BlockedReason, 200);
        cmd.Add("@ru", SqlDbType.VarChar, m.RuleId, 8);
        cmd.Add("@k", SqlDbType.VarChar, m.TriggerKey, 60);
        cmd.Add("@ro", SqlDbType.BigInt, m.RetryOf);
        cmd.Add("@a", SqlDbType.Int, m.Actor);
        cmd.Add("@c", SqlDbType.DateTime2, m.CreatedUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<SweepCandidate>> SweepCandidatesAsync(int propertyId, DateOnly businessDate, CancellationToken ct = default)
    {
        // R2 (arrival today or tomorrow, still Confirmed) and R3 (in house), only for enabled rules and events not yet queued.
        await using var cmd = Cmd("""
            SELECT 'pre-arrival', r.reservation_id FROM inv.reservation r
            WHERE r.property_id = @p AND r.status = 'Confirmed' AND r.arrival IN (@bd, DATEADD(DAY, 1, @bd))
              AND EXISTS (SELECT 1 FROM msg.trigger_rule x WHERE x.property_id = @p AND x.event = 'pre-arrival' AND x.enabled = 1)
              AND NOT EXISTS (SELECT 1 FROM msg.message m JOIN msg.trigger_rule x ON x.property_id = m.property_id AND x.event = 'pre-arrival' AND x.enabled = 1
                              WHERE m.property_id = @p AND m.trigger_key = x.rule_id + ':res:' + CAST(r.reservation_id AS VARCHAR(20)))
            UNION ALL
            SELECT 'checked-in', r.reservation_id FROM inv.reservation r JOIN gst.stay s ON s.reservation_id = r.reservation_id
            WHERE r.property_id = @p AND s.status = 'InHouse'
              AND EXISTS (SELECT 1 FROM msg.trigger_rule x WHERE x.property_id = @p AND x.event = 'checked-in' AND x.enabled = 1)
              AND NOT EXISTS (SELECT 1 FROM msg.message m JOIN msg.trigger_rule x ON x.property_id = m.property_id AND x.event = 'checked-in' AND x.enabled = 1
                              WHERE m.property_id = @p AND m.trigger_key = x.rule_id + ':res:' + CAST(r.reservation_id AS VARCHAR(20)))
            ORDER BY 2;
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        var list = new List<SweepCandidate>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new SweepCandidate(rd.GetString(0), rd.GetInt64(1)));
        return list;
    }

    private const string MessageCols = """
        m.message_id, m.reservation_id, r.public_no, m.guest_id, COALESCE(g.full_name, r.guest_name), m.template_code, m.channel, m.recipient_masked, m.subject, m.body,
               m.queued_utc, m.outcome, m.boost_minutes, m.blocked_reason, m.rule_id, m.retry_of, m.retried_by, m.row_ver
        """;

    private const string MessageFrom = """
        JOIN inv.reservation r ON r.reservation_id = m.reservation_id
        LEFT JOIN gst.guest g ON g.guest_id = m.guest_id
        """;

    private static StoredMessage ReadMessage(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt64(3), rd.GetString(4),
        P3Sql.StrOrNull(rd, 5), rd.GetString(6), rd.GetString(7), P3Sql.StrOrNull(rd, 8), rd.GetString(9), Utc(rd, 10), rd.GetString(11), rd.GetInt32(12),
        P3Sql.StrOrNull(rd, 13), P3Sql.StrOrNull(rd, 14), rd.IsDBNull(15) ? null : rd.GetInt64(15), rd.IsDBNull(16) ? null : rd.GetInt64(16), (byte[])rd[17]);

    public async Task<StoredMessage?> LockMessageAsync(int propertyId, long messageId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT " + MessageCols + " FROM msg.message m WITH (UPDLOCK, ROWLOCK) " + MessageFrom + " WHERE m.property_id = @p AND m.message_id = @id;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@id", SqlDbType.BigInt, messageId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadMessage(rd) : null;
    }

    public async Task<IReadOnlyList<StoredMessage>> MessagesAsync(int propertyId, long? onlyId, long? guestId, int limit, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT TOP (@n) " + MessageCols + " FROM msg.message m " + MessageFrom + " WHERE m.property_id = @p AND (@id IS NULL OR m.message_id = @id) AND (@g IS NULL OR m.guest_id = @g) ORDER BY m.message_id DESC;");
        cmd.Add("@n", SqlDbType.Int, limit);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@id", SqlDbType.BigInt, onlyId);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        var list = new List<StoredMessage>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadMessage(rd));
        return list;
    }

    public async Task AddBoostAsync(long messageId, int minutes, CancellationToken ct = default)
    {
        await using var cmd = Cmd("UPDATE msg.message SET boost_minutes = boost_minutes + @m WHERE message_id = @id;");
        cmd.Add("@m", SqlDbType.Int, minutes);
        cmd.Add("@id", SqlDbType.BigInt, messageId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task BlockMessageAsync(long messageId, string reason, CancellationToken ct = default)
    {
        await using var cmd = Cmd("UPDATE msg.message SET blocked_reason = @r WHERE message_id = @id AND blocked_reason IS NULL;");
        cmd.Add("@r", SqlDbType.NVarChar, reason, 200);
        cmd.Add("@id", SqlDbType.BigInt, messageId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetRetriedByAsync(long messageId, long retriedBy, CancellationToken ct = default)
    {
        await using var cmd = Cmd("UPDATE msg.message SET retried_by = @r WHERE message_id = @id AND retried_by IS NULL;");
        cmd.Add("@r", SqlDbType.BigInt, retriedBy);
        cmd.Add("@id", SqlDbType.BigInt, messageId);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This message was already retried.");
    }

    public async Task<bool> GuestExistsAsync(int propertyId, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT COUNT(*) FROM gst.guest WHERE guest_id = @g AND property_id = @p;");
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<StoredConsent?> GetStoredConsentAsync(int propertyId, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Cmd("SELECT whatsapp, sms, email, opt_out, updated_utc, row_ver FROM msg.consent WHERE property_id = @p AND guest_id = @g;");
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new StoredConsent(new MsgConsent(rd.GetBoolean(0), rd.GetBoolean(1), rd.GetBoolean(2), rd.GetBoolean(3)), Utc(rd, 4), (byte[])rd[5]) : null;
    }

    public async Task UpsertConsentAsync(int propertyId, long guestId, MsgConsent consent, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consent);
        await using var cmd = Cmd("""
            IF EXISTS (SELECT 1 FROM msg.consent WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND guest_id = @g)
              UPDATE msg.consent SET whatsapp = @w, sms = @s, email = @e, opt_out = @o, updated_utc = @u, updated_by = @by WHERE property_id = @p AND guest_id = @g;
            ELSE
              INSERT msg.consent(property_id, guest_id, whatsapp, sms, email, opt_out, updated_utc, updated_by) VALUES (@p, @g, @w, @s, @e, @o, @u, @by);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@g", SqlDbType.BigInt, guestId);
        cmd.Add("@w", SqlDbType.Bit, consent.WhatsApp);
        cmd.Add("@s", SqlDbType.Bit, consent.Sms);
        cmd.Add("@e", SqlDbType.Bit, consent.Email);
        cmd.Add("@o", SqlDbType.Bit, consent.OptOut);
        cmd.Add("@u", SqlDbType.DateTime2, utc);
        cmd.Add("@by", SqlDbType.Int, principalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Binds <see cref="IMessagingData"/> to a command transaction, or to a short read transaction for queries.</summary>
public sealed class SqlMessagingDataFactory(SqlConnectionFactory connections) : IMessagingDataFactory
{
    public IMessagingData For(ICommandTransaction tx)
    {
        var sql = tx as SqlCommandTransaction ?? throw new InvalidOperationException("Messaging needs the SQL command transaction.");
        return new SqlMessagingData(sql.Connection, sql.Transaction, connections);
    }

    public async Task<T> ReadAsync<T>(Func<IMessagingData, CancellationToken, Task<T>> read, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var result = await read(new SqlMessagingData(conn, tx, connections), ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }
}
