// F12 MEAS · f12-int-cap: SQL adapter of every measurement capture port (schema meas, migrations 4810 / 4811 / 4812 / 4815 / 4820 / 4825 / 4826 / 4830).
// Writes run inside the command runner's transaction (P3Sql.Sql(tx)); inserts are idempotent on the tables' (property_id, client_id) keys.
// The anonymous pulse uses its own connection and transaction and writes no audit row, no idempotency row and no timestamp (review H4, 4860).
using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Meas;
using Hcc.Contracts.Meas;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Meas;

public sealed class SqlMeasCaptureStore(SqlConnectionFactory connections)
    : IMeasCaptureCommon, IMeasDeskStore, IMeasGuestStore, IMeasPeopleStore, IMeasHkStore, IMeasRevenueStore, IMeasCaptureReads
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static SqlCommand Cmd(ICommandTransaction tx, string sql)
    {
        var c = P3Sql.Sql(tx).Command(sql);
        c.Add("@p", SqlDbType.Int, tx.Context.PropertyId);
        return c;
    }

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);


    private static string W(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Inv);

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetValue(i).ToString()?.TrimEnd();

    private static string? UtcOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : Utc(rd.GetDateTime(i));

    private static string? DateOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : W(DateOnly.FromDateTime(rd.GetDateTime(i)));

    private static string Num(SqlDataReader rd, int i) => Convert.ToInt64(rd.GetValue(i), Inv).ToString(Inv);

    private static int Int(SqlDataReader rd, int i) => Convert.ToInt32(rd.GetValue(i), Inv);

    private static async Task<(long Id, bool Existed)> IdFlagAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (Convert.ToInt64(rd.GetValue(0), Inv), rd.GetBoolean(1));
    }

    private static async Task<bool> ExistsAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using (cmd) return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null and not DBNull;
    }

    // ── common ──

    public async Task<IReadOnlyList<string>> RoleCodesAsync(int propertyId, int principalId, DateTime utcNow, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT DISTINCT r.role_code FROM plat.v_user_role_effective r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.user_id = @u AND u.property_id = @p AND u.enabled = 1 AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now);
            """, conn);
        cmd.Add("@u", SqlDbType.Int, principalId); cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@now", SqlDbType.DateTime2, utcNow);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0).Trim());
        return list;
    }

    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(P3Sql.BusinessDateSql, conn);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    // ── desk ──

    public async Task<(long Id, bool Existed)> InsertTurnawayAsync(ICommandTransaction tx, string clientId, MeasTurnawayRow row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            DECLARE @id BIGINT = (SELECT turnaway_id FROM meas.turnaway WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.turnaway(property_id, client_id, reason, arrival, nights, room_type, rooms, quoted_rate_paise, channel, lead_days, note, business_date, recorded_by)
              VALUES (@p, @c, @r, @a, @n, @t, @rooms, @q, @ch, @lead, @note, @bd, @by);
              SELECT CAST(SCOPE_IDENTITY() AS BIGINT), CAST(0 AS BIT);
            END
            """);
        cmd.Add("@c", SqlDbType.NVarChar, clientId, 64); cmd.Add("@r", SqlDbType.VarChar, row.Reason, 12); cmd.Add("@a", SqlDbType.Date, D(row.Arrival));
        cmd.Add("@n", SqlDbType.TinyInt, (byte)row.Nights); cmd.Add("@t", SqlDbType.NVarChar, row.RoomType, 40); cmd.Add("@rooms", SqlDbType.TinyInt, (byte)row.Rooms);
        cmd.Add("@q", SqlDbType.BigInt, row.QuotedRatePaise); cmd.Add("@ch", SqlDbType.NVarChar, row.Channel, 30); cmd.Add("@lead", SqlDbType.SmallInt, (short)Math.Min(row.LeadDays, short.MaxValue));
        cmd.Add("@note", SqlDbType.NVarChar, row.Note, 120); cmd.Add("@bd", SqlDbType.Date, D(row.BusinessDate)); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return await IdFlagAsync(cmd, ct).ConfigureAwait(false);
    }

    public Task<bool> ReservationExistsAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM inv.reservation WHERE property_id = @p AND reservation_id = @r;");
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        return ExistsAsync(cmd, ct);
    }

    public async Task<string?> ReservationRoomTypeAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT COALESCE(art.code, brt.code) FROM inv.reservation r
              LEFT JOIN inv.room rm ON rm.room_id = r.room_id LEFT JOIN inv.room_type art ON art.room_type_id = rm.room_type_id
              LEFT JOIN inv.room_type brt ON brt.room_type_id = r.room_type_id
             WHERE r.property_id = @p AND r.reservation_id = @r;
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is string s ? s.Trim() : null;
    }

    public Task<bool> RoomTypeExistsAsync(ICommandTransaction tx, string code, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM inv.room_type WHERE property_id = @p AND code = @c;");
        cmd.Add("@c", SqlDbType.VarChar, code, 20);
        return ExistsAsync(cmd, ct);
    }

    public async Task<(long Id, bool Existed)> InsertWalkedAsync(ICommandTransaction tx, string clientId, long reservationId, DateOnly night, string reason, long? costPaise, int approvedBy, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @id BIGINT = (SELECT walked_id FROM meas.walked WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.walked(property_id, client_id, reservation_id, night, reason, cost_paise, approved_by, recorded_by) VALUES (@p, @c, @res, @n, @r, @cost, @ap, @by);
              SELECT CAST(SCOPE_IDENTITY() AS BIGINT), CAST(0 AS BIT);
            END
            """);
        cmd.Add("@c", SqlDbType.NVarChar, clientId, 64); cmd.Add("@res", SqlDbType.BigInt, reservationId); cmd.Add("@n", SqlDbType.Date, D(night));
        cmd.Add("@r", SqlDbType.NVarChar, reason, 200); cmd.Add("@cost", SqlDbType.BigInt, costPaise); cmd.Add("@ap", SqlDbType.Int, approvedBy); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return await IdFlagAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<MeasAllowanceReasonView?> AllowanceReasonAsync(ICommandTransaction tx, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT code, label, active FROM meas.allowance_reason WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c;");
        cmd.Add("@c", SqlDbType.VarChar, code, 24);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new MeasAllowanceReasonView(rd.GetString(0), rd.GetString(1), rd.GetBoolean(2)) : null;
    }

    public async Task UpsertAllowanceReasonAsync(ICommandTransaction tx, string code, string label, bool active, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            MERGE meas.allowance_reason AS t USING (SELECT @p AS property_id, @c AS code) AS s ON t.property_id = s.property_id AND t.code = s.code
            WHEN MATCHED THEN UPDATE SET label = @l, active = @a, changed_utc = SYSUTCDATETIME(), changed_by = @by
            WHEN NOT MATCHED THEN INSERT (property_id, code, label, active, changed_by) VALUES (@p, @c, @l, @a, @by);
            """);
        cmd.Add("@c", SqlDbType.VarChar, code, 24); cmd.Add("@l", SqlDbType.NVarChar, label, 60); cmd.Add("@a", SqlDbType.Bit, active); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<MeasBookingExtraView?> BookingExtraAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT booked_type, upgrade, upgrade_approver FROM meas.booking_extra WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND reservation_id = @r;");
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new MeasBookingExtraView(reservationId.ToString(Inv), S(rd, 0), S(rd, 1), S(rd, 2)) : null;
    }

    public async Task UpsertBookingExtraAsync(ICommandTransaction tx, long reservationId, string? bookedType, string? upgrade, int? approver, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            MERGE meas.booking_extra AS t USING (SELECT @p AS property_id, @r AS reservation_id) AS s ON t.property_id = s.property_id AND t.reservation_id = s.reservation_id
            WHEN MATCHED THEN UPDATE SET booked_type = @bt, upgrade = @u, upgrade_approver = @ap, changed_utc = SYSUTCDATETIME(), changed_by = @by
            WHEN NOT MATCHED THEN INSERT (property_id, reservation_id, booked_type, upgrade, upgrade_approver, changed_by) VALUES (@p, @r, @bt, @u, @ap, @by);
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId); cmd.Add("@bt", SqlDbType.NVarChar, bookedType, 40); cmd.Add("@u", SqlDbType.VarChar, upgrade, 4);
        cmd.Add("@ap", SqlDbType.Int, approver); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> InsertBookingExtraLogAsync(ICommandTransaction tx, string clientId, long reservationId, string? item, bool waived, string? waiveReason, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            IF NOT EXISTS (SELECT 1 FROM meas.booking_extra_log WHERE property_id = @p AND client_id = @c)
              INSERT meas.booking_extra_log(property_id, client_id, reservation_id, item, waived, waive_reason, recorded_by) VALUES (@p, @c, @r, @i, @w, @wr, @by);
            SELECT @@ROWCOUNT;
            """);
        cmd.Add("@c", SqlDbType.NVarChar, clientId, 64); cmd.Add("@r", SqlDbType.BigInt, reservationId); cmd.Add("@i", SqlDbType.VarChar, item, 4);
        cmd.Add("@w", SqlDbType.Bit, waived); cmd.Add("@wr", SqlDbType.NVarChar, waiveReason is { Length: > 80 } x ? x[..80] : waiveReason, 80); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Inv) > 0;
    }

    public async Task<MeasPreferenceRow?> PreferenceAsync(ICommandTransaction tx, string customerKey, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT floor, room_kind, meal, pillow, bill_to_company, note, do_not_disturb, blacklist_reason, blacklist_approved_by, blacklist_utc, loyalty_tag
              FROM meas.customer_preference WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND customer_key = @k;
            """);
        cmd.Add("@k", SqlDbType.NVarChar, customerKey, 80);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new MeasPreferenceRow(S(rd, 0), S(rd, 1), S(rd, 2), S(rd, 3), S(rd, 4), S(rd, 5), rd.GetBoolean(6), S(rd, 7), rd.IsDBNull(8) ? null : rd.GetInt32(8),
            rd.IsDBNull(9) ? null : DateTime.SpecifyKind(rd.GetDateTime(9), DateTimeKind.Utc), S(rd, 10));
    }

    public async Task UpsertPreferenceAsync(ICommandTransaction tx, string customerKey, MeasPreferenceRow row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            MERGE meas.customer_preference AS t USING (SELECT @p AS property_id, @k AS customer_key) AS s ON t.property_id = s.property_id AND t.customer_key = s.customer_key
            WHEN MATCHED THEN UPDATE SET floor = @f, room_kind = @rk, meal = @m, pillow = @pi, bill_to_company = @b, note = @n, do_not_disturb = @d,
              blacklist_reason = @br, blacklist_approved_by = @ba, blacklist_utc = @bu, loyalty_tag = @lt, changed_utc = SYSUTCDATETIME(), changed_by = @by
            WHEN NOT MATCHED THEN INSERT (property_id, customer_key, floor, room_kind, meal, pillow, bill_to_company, note, do_not_disturb, blacklist_reason, blacklist_approved_by, blacklist_utc, loyalty_tag, changed_by)
              VALUES (@p, @k, @f, @rk, @m, @pi, @b, @n, @d, @br, @ba, @bu, @lt, @by);
            """);
        cmd.Add("@k", SqlDbType.NVarChar, customerKey, 80); cmd.Add("@f", SqlDbType.NVarChar, row.Floor, 80); cmd.Add("@rk", SqlDbType.NVarChar, row.RoomKind, 80);
        cmd.Add("@m", SqlDbType.NVarChar, row.Meal, 80); cmd.Add("@pi", SqlDbType.NVarChar, row.Pillow, 80); cmd.Add("@b", SqlDbType.NVarChar, row.BillToCompany, 80);
        cmd.Add("@n", SqlDbType.NVarChar, row.Note, 80); cmd.Add("@d", SqlDbType.Bit, row.DoNotDisturb); cmd.Add("@br", SqlDbType.NVarChar, row.BlacklistReason, 200);
        cmd.Add("@ba", SqlDbType.Int, row.BlacklistApprovedBy); cmd.Add("@bu", SqlDbType.DateTime2, row.BlacklistUtc); cmd.Add("@lt", SqlDbType.NVarChar, row.LoyaltyTag, 40);
        cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> InsertPreferenceLogAsync(ICommandTransaction tx, string clientId, string customerKey, string action, string? detail, int? approvedBy, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            IF NOT EXISTS (SELECT 1 FROM meas.customer_preference_log WHERE property_id = @p AND client_id = @c)
              INSERT meas.customer_preference_log(property_id, client_id, customer_key, action, detail, approved_by, recorded_by) VALUES (@p, @c, @k, @a, @d, @ap, @by);
            SELECT @@ROWCOUNT;
            """);
        cmd.Add("@c", SqlDbType.NVarChar, clientId, 64); cmd.Add("@k", SqlDbType.NVarChar, customerKey, 80); cmd.Add("@a", SqlDbType.VarChar, action, 20);
        cmd.Add("@d", SqlDbType.NVarChar, detail is { Length: > 200 } x ? x[..200] : detail, 200); cmd.Add("@ap", SqlDbType.Int, approvedBy); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Inv) > 0;
    }

    public async Task<IReadOnlyList<string>> ApproverRolesAsync(ICommandTransaction tx, int userId, DateTime utcNow, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT DISTINCT r.role_code FROM plat.v_user_role_effective r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.user_id = @u AND u.property_id = @p AND u.enabled = 1 AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now);
            """);
        cmd.Add("@u", SqlDbType.Int, userId); cmd.Add("@now", SqlDbType.DateTime2, utcNow);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0).Trim());
        return list;
    }

    // ── guest ──

    public Task<bool> SurveyExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM meas.survey WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c;");
        cmd.Add("@c", SqlDbType.VarChar, clientId, 60);
        return ExistsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<DateTime>> QrSurveyTimesAsync(ICommandTransaction tx, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT at_utc FROM meas.survey WHERE property_id = @p AND via = 'qr' AND at_utc >= @s;");
        cmd.Add("@s", SqlDbType.DateTime2, sinceUtc);
        var list = new List<DateTime>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(DateTime.SpecifyKind(rd.GetDateTime(0), DateTimeKind.Utc));
        return list;
    }

    public async Task<(bool Found, string? RoomType, string? Channel)> SurveyBookingAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT COALESCE(art.code, brt.code), r.source FROM inv.reservation r
              LEFT JOIN inv.room rm ON rm.room_id = r.room_id LEFT JOIN inv.room_type art ON art.room_type_id = rm.room_type_id
              LEFT JOIN inv.room_type brt ON brt.room_type_id = r.room_type_id
             WHERE r.property_id = @p AND r.reservation_id = @r;
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (true, S(rd, 0), S(rd, 1)) : (false, null, null);
    }

    public async Task InsertSurveyAsync(ICommandTransaction tx, MeasSurveyRow row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT meas.survey(property_id, client_id, at_utc, via, reservation_id, room_type, channel, overall, cleanliness, staff, value_score, breakfast, recommend, comment, consent, entered_by)
            VALUES (@p, @c, @at, @via, @r, @rt, @ch, @o, @cl, @st, @v, @b, @rec, @cm, 1, @by);
            """);
        cmd.Add("@c", SqlDbType.VarChar, row.ClientId, 60); cmd.Add("@at", SqlDbType.DateTime2, row.AtUtc); cmd.Add("@via", SqlDbType.VarChar, row.Via, 5);
        cmd.Add("@r", SqlDbType.BigInt, row.ReservationId); cmd.Add("@rt", SqlDbType.VarChar, row.RoomType, 40); cmd.Add("@ch", SqlDbType.VarChar, row.Channel, 40);
        cmd.Add("@o", SqlDbType.TinyInt, (byte)row.Overall); cmd.Add("@cl", SqlDbType.TinyInt, (byte)row.Cleanliness); cmd.Add("@st", SqlDbType.TinyInt, (byte)row.Staff);
        cmd.Add("@v", SqlDbType.TinyInt, (byte)row.Value); cmd.Add("@b", SqlDbType.TinyInt, row.Breakfast is { } b ? (byte)b : null); cmd.Add("@rec", SqlDbType.TinyInt, (byte)row.Recommend);
        cmd.Add("@cm", SqlDbType.NVarChar, row.Comment, 500); cmd.Add("@by", SqlDbType.Int, row.EnteredBy);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> UpsertReviewAsync(ICommandTransaction tx, MeasReviewView row, int by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            DECLARE @had BIT = CASE WHEN EXISTS (SELECT 1 FROM meas.review_month WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND month = @m AND platform = @pl) THEN 1 ELSE 0 END;
            MERGE meas.review_month AS t USING (SELECT @p AS property_id, @m AS month, @pl AS platform) AS s ON t.property_id = s.property_id AND t.month = s.month AND t.platform = s.platform
            WHEN MATCHED THEN UPDATE SET review_count = @n, avg_score = @a, score_12m = @s12, responded = @r, themes = @th, updated_by = @by, updated_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (property_id, month, platform, review_count, avg_score, score_12m, responded, themes, updated_by) VALUES (@p, @m, @pl, @n, @a, @s12, @r, @th, @by);
            SELECT @had;
            """);
        cmd.Add("@m", SqlDbType.Char, row.Month, 7); cmd.Add("@pl", SqlDbType.VarChar, row.Platform, 12); cmd.Add("@n", SqlDbType.Int, row.Count);
        var a = cmd.Parameters.Add("@a", SqlDbType.Decimal); a.Precision = 3; a.Scale = 2; a.Value = row.AvgScore;
        var s12 = cmd.Parameters.Add("@s12", SqlDbType.Decimal); s12.Precision = 3; s12.Scale = 2; s12.Value = (object?)row.Score12m ?? DBNull.Value;
        cmd.Add("@r", SqlDbType.Int, row.Responded); cmd.Add("@th", SqlDbType.NVarChar, row.Themes, 200); cmd.Add("@by", SqlDbType.Int, by);
        return (bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<MeasRootCauseView>> RootCausesAsync(ICommandTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT code, label, active FROM meas.root_cause WHERE property_id = @p ORDER BY code;");
        var list = new List<MeasRootCauseView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new MeasRootCauseView(rd.GetString(0), rd.GetString(1), rd.GetBoolean(2)));
        return list;
    }

    public async Task<IReadOnlyList<string>> UsedRootCausesAsync(ICommandTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT DISTINCT code FROM meas.root_cause_set WHERE property_id = @p;");
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }

    public async Task ReplaceRootCausesAsync(ICommandTransaction tx, IReadOnlyList<MeasRootCauseView> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var r in rows)
        {
            await using var m = Cmd(tx, """
                MERGE meas.root_cause AS t USING (SELECT @p AS property_id, @c AS code) AS s ON t.property_id = s.property_id AND t.code = s.code
                WHEN MATCHED THEN UPDATE SET label = @l, active = @a
                WHEN NOT MATCHED THEN INSERT (property_id, code, label, active) VALUES (@p, @c, @l, @a);
                """);
            m.Add("@c", SqlDbType.VarChar, r.Code, 12); m.Add("@l", SqlDbType.NVarChar, r.Label, 40); m.Add("@a", SqlDbType.Bit, r.Active);
            await m.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var names = rows.Select((_, i) => "@k" + i.ToString(Inv)).ToList();
        await using var del = Cmd(tx, $"DELETE meas.root_cause WHERE property_id = @p AND code NOT IN ({string.Join(", ", names)}) AND code NOT IN (SELECT code FROM meas.root_cause_set WHERE property_id = @p);");
        for (var i = 0; i < rows.Count; i++) del.Add(names[i], SqlDbType.VarChar, rows[i].Code, 12);
        await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<bool> ComplaintExistsAsync(ICommandTransaction tx, long complaintId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM ops.guest_complaint WHERE property_id = @p AND complaint_id = @c;");
        cmd.Add("@c", SqlDbType.BigInt, complaintId);
        return ExistsAsync(cmd, ct);
    }

    public async Task<string?> RootCauseOfAsync(ICommandTransaction tx, string complaintId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT code FROM meas.root_cause_set WHERE property_id = @p AND complaint_id = @c;");
        cmd.Add("@c", SqlDbType.VarChar, complaintId, 40);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    public async Task SetRootCauseAsync(ICommandTransaction tx, string complaintId, string? code, int by, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, code is null
            ? "DELETE meas.root_cause_set WHERE property_id = @p AND complaint_id = @c;"
            : """
              MERGE meas.root_cause_set AS t USING (SELECT @p AS property_id, @c AS complaint_id) AS s ON t.property_id = s.property_id AND t.complaint_id = s.complaint_id
              WHEN MATCHED THEN UPDATE SET code = @code, set_by = @by, set_utc = SYSUTCDATETIME()
              WHEN NOT MATCHED THEN INSERT (property_id, complaint_id, code, set_by) VALUES (@p, @c, @code, @by);
              """);
        cmd.Add("@c", SqlDbType.VarChar, complaintId, 40); cmd.Add("@code", SqlDbType.VarChar, code, 12); cmd.Add("@by", SqlDbType.Int, by);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetLoyaltyTagAsync(ICommandTransaction tx, string customerKey, string? tag, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            MERGE meas.customer_preference AS t USING (SELECT @p AS property_id, @k AS customer_key) AS s ON t.property_id = s.property_id AND t.customer_key = s.customer_key
            WHEN MATCHED THEN UPDATE SET loyalty_tag = @t, changed_utc = SYSUTCDATETIME(), changed_by = @by
            WHEN NOT MATCHED AND @t IS NOT NULL THEN INSERT (property_id, customer_key, loyalty_tag, changed_by) VALUES (@p, @k, @t, @by);
            """);
        cmd.Add("@k", SqlDbType.NVarChar, customerKey, 80); cmd.Add("@t", SqlDbType.NVarChar, tag, 40); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── people ──

    public async Task<DateOnly?> StaffJoinedAsync(ICommandTransaction tx, long staffId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT joined FROM hr.staff WHERE property_id = @p AND staff_id = @s;");
        cmd.Add("@s", SqlDbType.BigInt, staffId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
    }

    public async Task<long?> TrainingStaffAsync(ICommandTransaction tx, long trainingId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT staff_id FROM meas.training WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND training_id = @t;");
        cmd.Add("@t", SqlDbType.BigInt, trainingId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long s ? s : null;
    }

    private static void TrainingParams(SqlCommand cmd, MeasTrainingView row)
    {
        cmd.Add("@s", SqlDbType.BigInt, long.Parse(row.Staff, Inv)); cmd.Add("@co", SqlDbType.NVarChar, row.Course, 80);
        cmd.Add("@d", SqlDbType.Date, D(DateOnly.ParseExact(row.Date, "yyyy-MM-dd", Inv)));
        var h = cmd.Parameters.Add("@h", SqlDbType.Decimal); h.Precision = 6; h.Scale = 2; h.Value = row.Hours;
        cmd.Add("@cn", SqlDbType.NVarChar, row.CertificateNo, 40);
        cmd.Add("@v", SqlDbType.Date, row.ValidTo is { } v ? D(DateOnly.ParseExact(v, "yyyy-MM-dd", Inv)) : null);
        cmd.Add("@e", SqlDbType.NVarChar, row.EvidenceRef, 120); cmd.Add("@m", SqlDbType.Bit, row.Mandatory);
    }

    public async Task<(long Id, bool Existed)> InsertTrainingAsync(ICommandTransaction tx, Guid clientId, MeasTrainingView row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            DECLARE @id BIGINT = (SELECT training_id FROM meas.training WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.training(property_id, client_id, staff_id, course, trained_on, hours, certificate_no, valid_to, evidence_ref, mandatory, created_by)
              VALUES (@p, @c, @s, @co, @d, @h, @cn, @v, @e, @m, @by);
              SET @id = CAST(SCOPE_IDENTITY() AS BIGINT);
              INSERT meas.training_audit(training_id, property_id, action, actor_id, detail) VALUES (@id, @p, 'created', @by, @co);
              SELECT @id, CAST(0 AS BIT);
            END
            """);
        cmd.Add("@c", SqlDbType.UniqueIdentifier, clientId); TrainingParams(cmd, row); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return await IdFlagAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task UpdateTrainingAsync(ICommandTransaction tx, long trainingId, MeasTrainingView row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            UPDATE meas.training SET staff_id = @s, course = @co, trained_on = @d, hours = @h, certificate_no = @cn, valid_to = @v, evidence_ref = @e, mandatory = @m,
                   updated_by = @by, updated_utc = SYSUTCDATETIME()
             WHERE property_id = @p AND training_id = @t;
            INSERT meas.training_audit(training_id, property_id, action, actor_id, detail) VALUES (@t, @p, 'edited', @by, @co);
            """);
        cmd.Add("@t", SqlDbType.BigInt, trainingId); TrainingParams(cmd, row); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SetExitAsync(ICommandTransaction tx, long staffId, string? reason, DateOnly? exitDate, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, reason is null
            ? "DELETE meas.staff_exit WHERE property_id = @p AND staff_id = @s; SELECT @@ROWCOUNT;"
            : """
              MERGE meas.staff_exit AS t USING (SELECT @p AS property_id, @s AS staff_id) AS s ON t.property_id = s.property_id AND t.staff_id = s.staff_id
              WHEN MATCHED THEN UPDATE SET reason = @r, exit_date = @d, set_by = @by, set_utc = SYSUTCDATETIME()
              WHEN NOT MATCHED THEN INSERT (property_id, staff_id, reason, exit_date, set_by) VALUES (@p, @s, @r, @d, @by);
              SELECT @@ROWCOUNT;
              """);
        cmd.Add("@s", SqlDbType.BigInt, staffId); cmd.Add("@r", SqlDbType.NVarChar, reason, 30); cmd.Add("@d", SqlDbType.Date, exitDate is { } d ? D(d) : null);
        cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Inv) > 0;
    }

    public async Task<bool> DepartmentExistsAsync(int propertyId, string deptCode, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT 1 FROM hr.department WHERE property_id = @p AND dept_code = @d;", conn);
        cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@d", SqlDbType.VarChar, deptCode, 10);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<(long Id, bool Existed)> AddPulseAsync(int propertyId, Guid clientId, MeasPulseRow row, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        (long Id, bool Existed) result;
        await using (var cmd = new SqlCommand("""
            DECLARE @id BIGINT = (SELECT pulse_id FROM meas.pulse WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.pulse(property_id, client_id, quarter_key, dept_code, workload, respect, tools, pay_fairness, recommend)
              VALUES (@p, @c, @q, @d, @w, @r, @t, @pf, @rec);
              SET @id = CAST(SCOPE_IDENTITY() AS BIGINT);
              -- Anonymous (review H4, migration 4860): no timestamp and no audit row (an audit time could be matched to the signed-in request).
              SELECT @id, CAST(0 AS BIT);
            END
            """, conn, tx))
        {
            cmd.Add("@p", SqlDbType.Int, propertyId); cmd.Add("@c", SqlDbType.UniqueIdentifier, clientId); cmd.Add("@q", SqlDbType.Char, row.QuarterKey, 10);
            cmd.Add("@d", SqlDbType.VarChar, row.DeptCode, 10); cmd.Add("@w", SqlDbType.TinyInt, (byte)row.Workload); cmd.Add("@r", SqlDbType.TinyInt, (byte)row.Respect);
            cmd.Add("@t", SqlDbType.TinyInt, (byte)row.Tools); cmd.Add("@pf", SqlDbType.TinyInt, (byte)row.PayFairness); cmd.Add("@rec", SqlDbType.TinyInt, (byte)row.Recommend);
            _ = utc; // kept in the port signature; never stored (H4)
            result = await IdFlagAsync(cmd, ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    // ── housekeeping / engineering ──

    public async Task<int?> RoomIdAsync(ICommandTransaction tx, string roomNo, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = @r;");
        cmd.Add("@r", SqlDbType.VarChar, roomNo, 10);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id ? id : null;
    }

    public Task<bool> WorkOrderExistsAsync(ICommandTransaction tx, long workOrderId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM mnt.work_order WHERE property_id = @p AND wo_id = @w;");
        cmd.Add("@w", SqlDbType.BigInt, workOrderId);
        return ExistsAsync(cmd, ct);
    }

    public Task<bool> OfflineExistsAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM meas.room_offline WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND room_id = @r;");
        cmd.Add("@r", SqlDbType.Int, roomId);
        return ExistsAsync(cmd, ct);
    }

    public async Task SetOfflineAsync(ICommandTransaction tx, int roomId, string clientId, string reason, DateOnly? expectedReturn, string? workOrder, string? note, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            MERGE meas.room_offline AS t USING (SELECT @p AS property_id, @r AS room_id) AS s ON t.property_id = s.property_id AND t.room_id = s.room_id
            WHEN MATCHED THEN UPDATE SET reason = @re, expected_return = @x, work_order_no = @w, note = @n, set_by = @by, set_utc = SYSUTCDATETIME(), client_id = NULL
            WHEN NOT MATCHED THEN INSERT (property_id, room_id, reason, expected_return, work_order_no, note, set_by, client_id) VALUES (@p, @r, @re, @x, @w, @n, @by, @c);
            """);
        cmd.Add("@r", SqlDbType.Int, roomId); cmd.Add("@re", SqlDbType.VarChar, reason, 12); cmd.Add("@x", SqlDbType.Date, expectedReturn is { } d ? D(d) : null);
        cmd.Add("@w", SqlDbType.VarChar, workOrder, 30); cmd.Add("@n", SqlDbType.NVarChar, note, 200); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId); cmd.Add("@c", SqlDbType.VarChar, clientId, 80);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearOfflineAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "DELETE meas.room_offline WHERE property_id = @p AND room_id = @r;");
        cmd.Add("@r", SqlDbType.Int, roomId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MeasMeterView>> MetersAsync(ICommandTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT meter_id, name, kind, unit, active FROM meas.meter WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p ORDER BY meter_id;");
        var list = new List<MeasMeterView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new MeasMeterView(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4)));
        return list;
    }

    public Task<bool> MeterHasReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT TOP (1) 1 FROM meas.meter_reading WHERE property_id = @p AND meter_id = @m;");
        cmd.Add("@m", SqlDbType.VarChar, meterId, 30);
        return ExistsAsync(cmd, ct);
    }

    public async Task UpsertMeterAsync(ICommandTransaction tx, MeasMeterView meter, string clientId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meter);
        await using var cmd = Cmd(tx, """
            MERGE meas.meter AS t USING (SELECT @p AS property_id, @id AS meter_id) AS s ON t.property_id = s.property_id AND t.meter_id = s.meter_id
            WHEN MATCHED THEN UPDATE SET name = @n, kind = @k, unit = @u, active = @a
            WHEN NOT MATCHED THEN INSERT (property_id, meter_id, name, kind, unit, active, client_id) VALUES (@p, @id, @n, @k, @u, @a, @c);
            """);
        cmd.Add("@id", SqlDbType.VarChar, meter.Id, 30); cmd.Add("@n", SqlDbType.NVarChar, meter.Name, 60); cmd.Add("@k", SqlDbType.VarChar, meter.Kind, 12);
        cmd.Add("@u", SqlDbType.VarChar, meter.Unit, 16); cmd.Add("@a", SqlDbType.Bit, meter.Active); cmd.Add("@c", SqlDbType.VarChar, clientId, 80);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(DateOnly Date, decimal Value)>> ReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT reading_date, value FROM meas.meter_reading WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND meter_id = @m ORDER BY reading_date, reading_id;");
        cmd.Add("@m", SqlDbType.VarChar, meterId, 30);
        var list = new List<(DateOnly, decimal)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetDecimal(1)));
        return list;
    }

    public async Task<(long Id, bool Existed)> InsertReadingAsync(ICommandTransaction tx, string clientId, string meterId, DateOnly date, decimal value, string? photoRef, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @id BIGINT = (SELECT reading_id FROM meas.meter_reading WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.meter_reading(property_id, meter_id, reading_date, value, entered_by, photo_ref, client_id) VALUES (@p, @m, @d, @v, @by, @ph, @c);
              SELECT CAST(SCOPE_IDENTITY() AS BIGINT), CAST(0 AS BIT);
            END
            """);
        cmd.Add("@c", SqlDbType.VarChar, clientId, 80); cmd.Add("@m", SqlDbType.VarChar, meterId, 30); cmd.Add("@d", SqlDbType.Date, D(date));
        var v = cmd.Parameters.Add("@v", SqlDbType.Decimal); v.Precision = 14; v.Scale = 2; v.Value = value;
        cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId); cmd.Add("@ph", SqlDbType.NVarChar, photoRef, 200);
        return await IdFlagAsync(cmd, ct).ConfigureAwait(false);
    }

    public Task<bool> AssetExistsAsync(ICommandTransaction tx, int assetId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM mnt.asset WHERE property_id = @p AND asset_id = @a;");
        cmd.Add("@a", SqlDbType.Int, assetId);
        return ExistsAsync(cmd, ct);
    }

    public async Task UpsertAssetLifeAsync(ICommandTransaction tx, string assetId, int? lifeMonths, string? grade, long? costPaise, string clientId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            MERGE meas.asset_life AS t USING (SELECT @p AS property_id, @a AS asset_id) AS s ON t.property_id = s.property_id AND t.asset_id = s.asset_id
            WHEN MATCHED THEN UPDATE SET expected_life_months = COALESCE(@l, t.expected_life_months), condition_grade = COALESCE(@g, t.condition_grade),
              replacement_cost_paise = COALESCE(@c, t.replacement_cost_paise), updated_by = @by, updated_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (property_id, asset_id, expected_life_months, condition_grade, replacement_cost_paise, updated_by, client_id) VALUES (@p, @a, @l, @g, @c, @by, @cid);
            """);
        cmd.Add("@a", SqlDbType.VarChar, assetId, 30); cmd.Add("@l", SqlDbType.Int, lifeMonths); cmd.Add("@g", SqlDbType.Char, grade, 1); cmd.Add("@c", SqlDbType.BigInt, costPaise);
        cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId); cmd.Add("@cid", SqlDbType.VarChar, clientId, 80);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(long Id, bool Existed)> InsertDiscardAsync(ICommandTransaction tx, string clientId, string item, int qty, string reason, DateOnly date, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @id BIGINT = (SELECT discard_id FROM meas.discard WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c);
            IF @id IS NOT NULL SELECT @id, CAST(1 AS BIT);
            ELSE BEGIN
              INSERT meas.discard(property_id, item, qty, reason, discard_date, entered_by, client_id) VALUES (@p, @i, @q, @r, @d, @by, @c);
              SELECT CAST(SCOPE_IDENTITY() AS BIGINT), CAST(0 AS BIT);
            END
            """);
        cmd.Add("@c", SqlDbType.VarChar, clientId, 80); cmd.Add("@i", SqlDbType.NVarChar, item, 60); cmd.Add("@q", SqlDbType.Int, qty); cmd.Add("@r", SqlDbType.NVarChar, reason, 60);
        cmd.Add("@d", SqlDbType.Date, D(date)); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        return await IdFlagAsync(cmd, ct).ConfigureAwait(false);
    }

    // ── revenue ──

    public async Task<(MeasEventView Event, bool Deleted)?> EventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT event_id, name, from_date, to_date, kind, impact_pct, note, deleted FROM meas.event WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND event_id = @e;");
        cmd.Add("@e", SqlDbType.VarChar, eventId, 40);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (new MeasEventView(rd.GetString(0), rd.GetString(1), DateOrNull(rd, 2)!, DateOrNull(rd, 3)!, rd.GetString(4), rd.GetInt32(5), S(rd, 6)), rd.GetBoolean(7));
    }

    public async Task UpsertEventAsync(ICommandTransaction tx, MeasEventView e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Cmd(tx, """
            MERGE meas.event AS t USING (SELECT @p AS property_id, @e AS event_id) AS s ON t.property_id = s.property_id AND t.event_id = s.event_id
            WHEN MATCHED THEN UPDATE SET name = @n, from_date = @f, to_date = @t, kind = @k, impact_pct = @i, note = @no, deleted = 0, updated_by = @by, updated_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (property_id, event_id, name, from_date, to_date, kind, impact_pct, note, updated_by) VALUES (@p, @e, @n, @f, @t, @k, @i, @no, @by);
            """);
        cmd.Add("@e", SqlDbType.VarChar, e.Id, 40); cmd.Add("@n", SqlDbType.NVarChar, e.Name, 80);
        cmd.Add("@f", SqlDbType.Date, D(DateOnly.ParseExact(e.From, "yyyy-MM-dd", Inv))); cmd.Add("@t", SqlDbType.Date, D(DateOnly.ParseExact(e.To, "yyyy-MM-dd", Inv)));
        cmd.Add("@k", SqlDbType.VarChar, e.Kind, 16); cmd.Add("@i", SqlDbType.Int, e.ImpactPct); cmd.Add("@no", SqlDbType.NVarChar, e.Note, 200); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteEventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "UPDATE meas.event SET deleted = 1, updated_by = @by, updated_utc = SYSUTCDATETIME() WHERE property_id = @p AND event_id = @e;");
        cmd.Add("@e", SqlDbType.VarChar, eventId, 40); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> SnapshotOtbAsync(ICommandTransaction tx, DateOnly takenOn, int nights, CancellationToken ct = default)
    {
        // Rooms on the books for each night as known at the end of the snapshot day (hotel day ends 00:00 IST = 18:30 UTC the day before):
        // reservations created by then and not cancelled / no-show / expired; revenue = the agreed nightly rate (paise, before tax).
        // Back-fill caveat: a reservation cancelled after the snapshot day is not counted (inv.reservation keeps no cancel time besides status_utc).
        await using var cmd = Cmd(tx, """
            DELETE meas.otb_snapshot WHERE property_id = @p AND taken_on = @d;
            INSERT meas.otb_snapshot(property_id, taken_on, night, rooms, revenue_paise)
            SELECT @p, @d, n.night, COUNT(DISTINCT n.reservation_id), SUM(n.rate_paise)
              FROM inv.reservation_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
             WHERE r.property_id = @p AND n.night >= @d AND n.night < DATEADD(day, @n, @d)
               AND r.status IN ('Held', 'Confirmed', 'InHouse', 'Departed') AND r.created_utc < @cut
             GROUP BY n.night;
            SELECT @@ROWCOUNT;
            """);
        cmd.Add("@d", SqlDbType.Date, D(takenOn)); cmd.Add("@n", SqlDbType.Int, nights); cmd.Add("@cut", SqlDbType.DateTime2, D(takenOn).AddDays(1).AddMinutes(-330));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Inv);
    }

    public async Task PruneOtbAsync(ICommandTransaction tx, int keep, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            DELETE meas.otb_snapshot WHERE property_id = @p
               AND taken_on NOT IN (SELECT DISTINCT TOP (@k) taken_on FROM meas.otb_snapshot WHERE property_id = @p ORDER BY taken_on DESC);
            """);
        cmd.Add("@k", SqlDbType.Int, keep);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MeasCompetitorView>> CompetitorsAsync(ICommandTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT competitor_id, name, active FROM meas.competitor WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p ORDER BY name;");
        var list = new List<MeasCompetitorView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new MeasCompetitorView(rd.GetString(0), rd.GetString(1), rd.GetBoolean(2)));
        return list;
    }

    public async Task UpsertCompetitorAsync(ICommandTransaction tx, MeasCompetitorView c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Cmd(tx, """
            MERGE meas.competitor AS t USING (SELECT @p AS property_id, @id AS competitor_id) AS s ON t.property_id = s.property_id AND t.competitor_id = s.competitor_id
            WHEN MATCHED THEN UPDATE SET name = @n, active = @a, updated_by = @by, updated_utc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (property_id, competitor_id, name, active, updated_by) VALUES (@p, @id, @n, @a, @by);
            """);
        cmd.Add("@id", SqlDbType.VarChar, c.Id, 40); cmd.Add("@n", SqlDbType.NVarChar, c.Name, 80); cmd.Add("@a", SqlDbType.Bit, c.Active); cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<bool> RateShopExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default)
    {
        var cmd = Cmd(tx, "SELECT 1 FROM meas.rate_shop WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND client_id = @c;");
        cmd.Add("@c", SqlDbType.VarChar, clientId, 60);
        return ExistsAsync(cmd, ct);
    }

    public async Task<int?> RoomTypeIdAsync(ICommandTransaction tx, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = @c;");
        cmd.Add("@c", SqlDbType.VarChar, code, 20);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id ? id : null;
    }

    public async Task InsertRateShopAsync(ICommandTransaction tx, string clientId, DateOnly shopDate, DateOnly stayDate, string competitorId, int roomTypeId, long ratePaise, bool withBreakfast, string source, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            INSERT meas.rate_shop(property_id, client_id, shop_date, stay_date, competitor_id, room_type_id, rate_paise, with_breakfast, source, entered_by)
            VALUES (@p, @c, @sd, @st, @co, @rt, @r, @b, @s, @by);
            """);
        cmd.Add("@c", SqlDbType.VarChar, clientId, 60); cmd.Add("@sd", SqlDbType.Date, D(shopDate)); cmd.Add("@st", SqlDbType.Date, D(stayDate)); cmd.Add("@co", SqlDbType.VarChar, competitorId, 40);
        cmd.Add("@rt", SqlDbType.Int, roomTypeId); cmd.Add("@r", SqlDbType.BigInt, ratePaise); cmd.Add("@b", SqlDbType.Bit, withBreakfast); cmd.Add("@s", SqlDbType.VarChar, source, 40);
        cmd.Add("@by", SqlDbType.Int, tx.Context.PrincipalId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── read ──

    private const string ReadSql = """
        SELECT turnaway_id, recorded_utc, recorded_by, reason, arrival, nights, room_type, rooms, quoted_rate_paise, channel, lead_days, note FROM meas.turnaway WHERE property_id = @p ORDER BY turnaway_id;
        SELECT walked_id, recorded_utc, recorded_by, reservation_id, night, reason, cost_paise, approved_by FROM meas.walked WHERE property_id = @p ORDER BY walked_id;
        SELECT code, label, active FROM meas.allowance_reason WHERE property_id = @p ORDER BY code;
        SELECT reservation_id, booked_type, upgrade, upgrade_approver FROM meas.booking_extra WHERE property_id = @p;
        SELECT customer_key, floor, room_kind, meal, pillow, bill_to_company, do_not_disturb, note, blacklist_reason, blacklist_approved_by, blacklist_utc, loyalty_tag FROM meas.customer_preference WHERE property_id = @p;
        SELECT task_no, started_utc, finished_utc, inspected_utc, inspect_result, reject_reason, attendant_id FROM meas.task_times WHERE property_id = @p;
        SELECT r.room_no, o.reason, o.expected_return, o.work_order_no, o.note FROM meas.room_offline o JOIN inv.room r ON r.room_id = o.room_id WHERE o.property_id = @p;
        SELECT meter_id, name, kind, unit, active FROM meas.meter WHERE property_id = @p ORDER BY meter_id;
        SELECT reading_id, meter_id, reading_date, value, entered_by, photo_ref FROM meas.meter_reading WHERE property_id = @p ORDER BY reading_date, reading_id;
        SELECT asset_id, expected_life_months, condition_grade, replacement_cost_paise FROM meas.asset_life WHERE property_id = @p;
        SELECT discard_id, item, qty, reason, discard_date, entered_by FROM meas.discard WHERE property_id = @p ORDER BY discard_id;
        SELECT client_id, at_utc, reservation_id, room_type, channel, overall, cleanliness, staff, value_score, breakfast, recommend, comment, via, entered_by FROM meas.survey WHERE property_id = @p ORDER BY at_utc, survey_id;
        SELECT month, platform, review_count, avg_score, score_12m, responded, themes FROM meas.review_month WHERE property_id = @p ORDER BY month, platform;
        SELECT code, label, active FROM meas.root_cause WHERE property_id = @p ORDER BY code;
        SELECT complaint_id, code FROM meas.root_cause_set WHERE property_id = @p;
        SELECT training_id, staff_id, course, trained_on, hours, certificate_no, valid_to, evidence_ref, mandatory FROM meas.training WHERE property_id = @p ORDER BY trained_on, training_id;
        SELECT staff_id, reason, exit_date FROM meas.staff_exit WHERE property_id = @p;
        SELECT pulse_id, quarter_key, dept_code, workload, respect, tools, pay_fairness, recommend FROM meas.pulse WHERE property_id = @p ORDER BY pulse_id;
        SELECT event_id, name, from_date, to_date, kind, impact_pct, note FROM meas.event WHERE property_id = @p AND deleted = 0 ORDER BY from_date, event_id;
        SELECT taken_on, night, rooms, revenue_paise FROM meas.otb_snapshot WHERE property_id = @p ORDER BY taken_on, night;
        SELECT competitor_id, name, active FROM meas.competitor WHERE property_id = @p ORDER BY name;
        SELECT s.client_id, s.shop_date, s.stay_date, s.competitor_id, rt.code, s.rate_paise, s.with_breakfast, s.source, s.entered_by
          FROM meas.rate_shop s JOIN inv.room_type rt ON rt.room_type_id = s.room_type_id WHERE s.property_id = @p ORDER BY s.created_utc, s.client_id;
        SELECT night, kind, refused, reshaped FROM meas.restriction_counter WHERE property_id = @p ORDER BY night, kind;
        """;

    private static async Task<List<T>> RowsAsync<T>(SqlDataReader rd, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var list = new List<T>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(map(rd));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        return list;
    }

    public async Task<MeasCapturesView> CapturesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(ReadSql, conn);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var turnaway = await RowsAsync(rd, r => new MeasTurnawayView(Num(r, 0), Utc(r.GetDateTime(1)), Num(r, 2), r.GetString(3), DateOrNull(r, 4)!, Int(r, 5), r.GetString(6), Int(r, 7),
            r.IsDBNull(8) ? null : r.GetInt64(8), r.GetString(9), Int(r, 10), S(r, 11)), ct).ConfigureAwait(false);
        var walked = await RowsAsync(rd, r => new MeasWalkedView(Num(r, 0), Utc(r.GetDateTime(1)), Num(r, 2), Num(r, 3), DateOrNull(r, 4)!, r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6), Num(r, 7)), ct).ConfigureAwait(false);
        var allowance = await RowsAsync(rd, r => new MeasAllowanceReasonView(r.GetString(0), r.GetString(1), r.GetBoolean(2)), ct).ConfigureAwait(false);
        var extra = await RowsAsync(rd, r => new MeasBookingExtraView(Num(r, 0), S(r, 1), S(r, 2), S(r, 3)), ct).ConfigureAwait(false);
        var pref = await RowsAsync(rd, r => new MeasPreferenceView(r.GetString(0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), r.GetBoolean(6), S(r, 7),
            r.IsDBNull(8) ? null : new MeasBlacklistView(r.GetString(8), r.IsDBNull(9) ? string.Empty : Num(r, 9), UtcOrNull(r, 10) ?? string.Empty), S(r, 11)), ct).ConfigureAwait(false);
        var tasks = await RowsAsync(rd, r => new MeasTaskTimesView(r.GetString(0), UtcOrNull(r, 1), UtcOrNull(r, 2), UtcOrNull(r, 3), S(r, 4), S(r, 5), S(r, 6)), ct).ConfigureAwait(false);
        var offline = await RowsAsync(rd, r => new MeasOfflineView(r.GetString(0), r.GetString(1), DateOrNull(r, 2), S(r, 3), S(r, 4)), ct).ConfigureAwait(false);
        var meters = await RowsAsync(rd, r => new MeasMeterView(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4)), ct).ConfigureAwait(false);
        var readings = await RowsAsync(rd, r => new MeasReadingView(Num(r, 0), r.GetString(1), DateOrNull(r, 2)!, r.GetDecimal(3), Num(r, 4), S(r, 5)), ct).ConfigureAwait(false);
        var assets = await RowsAsync(rd, r => new MeasAssetLifeView(r.GetString(0), r.IsDBNull(1) ? null : r.GetInt32(1), S(r, 2), r.IsDBNull(3) ? null : r.GetInt64(3)), ct).ConfigureAwait(false);
        var discards = await RowsAsync(rd, r => new MeasDiscardView(Num(r, 0), r.GetString(1), r.GetInt32(2), r.GetString(3), DateOrNull(r, 4)!, Num(r, 5)), ct).ConfigureAwait(false);
        var surveys = await RowsAsync(rd, r => new MeasSurveyView(r.GetString(0), Utc(r.GetDateTime(1)), r.IsDBNull(2) ? null : Num(r, 2), S(r, 3), S(r, 4), Int(r, 5), Int(r, 6), Int(r, 7), Int(r, 8),
            r.IsDBNull(9) ? null : Int(r, 9), Int(r, 10), S(r, 11), true, r.GetString(12), r.IsDBNull(13) ? null : Num(r, 13)), ct).ConfigureAwait(false);
        var reviews = await RowsAsync(rd, r => new MeasReviewView($"{r.GetString(0)}-{r.GetString(1)}", r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetDecimal(4), r.GetInt32(5), S(r, 6)), ct).ConfigureAwait(false);
        var causes = await RowsAsync(rd, r => new MeasRootCauseView(r.GetString(0), r.GetString(1), r.GetBoolean(2)), ct).ConfigureAwait(false);
        var causeSet = await RowsAsync(rd, r => new MeasRootCauseSetView(r.GetString(0), r.GetString(1)), ct).ConfigureAwait(false);
        var training = await RowsAsync(rd, r => new MeasTrainingView(Num(r, 0), Num(r, 1), r.GetString(2), DateOrNull(r, 3)!, r.GetDecimal(4), S(r, 5), DateOrNull(r, 6), S(r, 7), r.GetBoolean(8)), ct).ConfigureAwait(false);
        var exits = await RowsAsync(rd, r => new MeasExitView(Num(r, 0), r.GetString(1), DateOrNull(r, 2)!), ct).ConfigureAwait(false);
        var pulse = await RowsAsync(rd, r => new MeasPulseView(Num(r, 0), r.GetString(1).Trim(), r.GetString(2), Int(r, 3), Int(r, 4), Int(r, 5), Int(r, 6), Int(r, 7)), ct).ConfigureAwait(false);
        var events = await RowsAsync(rd, r => new MeasEventView(r.GetString(0), r.GetString(1), DateOrNull(r, 2)!, DateOrNull(r, 3)!, r.GetString(4), r.GetInt32(5), S(r, 6)), ct).ConfigureAwait(false);
        var otb = await RowsAsync(rd, r => { var t = DateOrNull(r, 0)!; var n = DateOrNull(r, 1)!; return new MeasOtbView($"OTB-{t}-{n}", t, n, r.GetInt32(2), r.GetInt64(3)); }, ct).ConfigureAwait(false);
        var comps = await RowsAsync(rd, r => new MeasCompetitorView(r.GetString(0), r.GetString(1), r.GetBoolean(2)), ct).ConfigureAwait(false);
        var shops = await RowsAsync(rd, r => new MeasRateShopView("RS-" + r.GetString(0), DateOrNull(r, 1)!, DateOrNull(r, 2)!, r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetBoolean(6), r.GetString(7), Num(r, 8)), ct).ConfigureAwait(false);
        var restr = await RowsAsync(rd, r => new MeasRestrictionView(DateOrNull(r, 0)!, r.GetString(1), r.GetInt32(2), r.GetInt32(3)), ct).ConfigureAwait(false);
        return new MeasCapturesView(turnaway, walked, allowance, extra, pref, tasks, offline, meters, readings, assets, discards, surveys, reviews, causes, causeSet, training, exits, pulse,
            events, otb, comps, shops, restr);
    }
}
