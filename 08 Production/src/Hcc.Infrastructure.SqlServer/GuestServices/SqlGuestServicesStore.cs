using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.GuestServices;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestServices;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.GuestServices;

/// <summary>SQL adapter of <see cref="IGuestServicesStore"/> and <see cref="IGuestServicesReadStore"/> (ops.guest_* / ops.lost_item, migration 1500).</summary>
public sealed class SqlGuestServicesStore(SqlConnectionFactory connections) : IGuestServicesStore, IGuestServicesReadStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Guest-services SQL store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static int? I(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    private static long? L(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);

    private static DateTime? T(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static DateTime U(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static DateOnly? D(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));

    private static string StatusToDb(string status) => status;

    // ── Property and settings ──

    private const string PropertySql = "SELECT business_date, time_zone FROM inv.property WHERE property_id = @p;";

    private static async Task<PropertyClock> ReadPropertyAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        return new PropertyClock(DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetString(1));
    }

    public async Task<PropertyClock> PropertyAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(PropertySql);
        return await ReadPropertyAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<PropertyClock> PropertyAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(PropertySql, conn);
        return await ReadPropertyAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    private const string SettingsSql = "SELECT sla_json, retention_days, valuable_retention_days, row_ver FROM ops.guest_service_setting {0} WHERE property_id = @p;";

    private static async Task<(GuestServiceSettings, byte[]?)> ReadSettingsAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return (GuestServiceSettings.Default, null);
        var sla = JsonSerializer.Deserialize<Dictionary<string, int>>(rd.GetString(0));
        return (GuestServiceSettings.Merge(sla, rd.GetInt16(1), rd.GetInt16(2)), (byte[])rd[3]);
    }

    public async Task<(GuestServiceSettings Settings, byte[]? Version)> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(string.Format(System.Globalization.CultureInfo.InvariantCulture, SettingsSql, "WITH (UPDLOCK, HOLDLOCK)"));
        return await ReadSettingsAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<(GuestServiceSettings Settings, byte[]? Version)> SettingsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(string.Format(System.Globalization.CultureInfo.InvariantCulture, SettingsSql, string.Empty), conn);
        return await ReadSettingsAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> SaveSettingsAsync(ICommandTransaction tx, int propertyId, GuestServiceSettings settings, byte[]? expected, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings.Sla);
        if (expected is null)
        {
            await using var ins = Sql(tx).Command("""
                INSERT ops.guest_service_setting(property_id, sla_json, retention_days, valuable_retention_days, updated_by, updated_utc)
                VALUES (@p, @j, @r, @v, @u, @t);
                SELECT row_ver FROM ops.guest_service_setting WHERE property_id = @p;
                """);
            P(ins, "@p", SqlDbType.Int, propertyId);
            P(ins, "@j", SqlDbType.NVarChar, json, 600);
            P(ins, "@r", SqlDbType.SmallInt, (short)settings.RetentionDays);
            P(ins, "@v", SqlDbType.SmallInt, (short)settings.ValuableRetentionDays);
            P(ins, "@u", SqlDbType.Int, principalId);
            P(ins, "@t", SqlDbType.DateTime2, utc);
            return (byte[])(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await using var cmd = Sql(tx).Command("""
            UPDATE ops.guest_service_setting SET sla_json = @j, retention_days = @r, valuable_retention_days = @v, updated_by = @u, updated_utc = @t
            WHERE property_id = @p AND row_ver = @e;
            SELECT @@ROWCOUNT AS n, (SELECT row_ver FROM ops.guest_service_setting WHERE property_id = @p) AS v;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@j", SqlDbType.NVarChar, json, 600);
        P(cmd, "@r", SqlDbType.SmallInt, (short)settings.RetentionDays);
        P(cmd, "@v", SqlDbType.SmallInt, (short)settings.ValuableRetentionDays);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@t", SqlDbType.DateTime2, utc);
        P(cmd, "@e", SqlDbType.Binary, expected, 8);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        SqlValues.EnsureVersionMatched(rd.GetInt32(0), "The guest-service settings");
        return (byte[])rd[1];
    }

    // ── Stays and rooms ──

    public async Task<StayInfo?> StayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT s.stay_id, seg.room_id, r.room_no, g.full_name, s.status, s.folio_id
            FROM gst.stay s
            JOIN gst.guest g ON g.guest_id = s.guest_id
            JOIN gst.stay_segment seg ON seg.stay_id = s.stay_id AND seg.seq = (SELECT MAX(x.seq) FROM gst.stay_segment x WHERE x.stay_id = s.stay_id)
            JOIN inv.room r ON r.room_id = seg.room_id
            WHERE s.stay_id = @s AND s.property_id = @p;
            """);
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new StayInfo(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetInt64(5)) : null;
    }

    public async Task<long?> LastStayInRoomAsync(ICommandTransaction tx, int roomId, DateOnly today, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT TOP (1) s.stay_id FROM gst.stay_segment seg JOIN gst.stay s ON s.stay_id = seg.stay_id
            WHERE seg.room_id = @r AND s.arrival <= @d ORDER BY s.departure DESC, s.stay_id DESC;
            """);
        P(cmd, "@r", SqlDbType.Int, roomId);
        P(cmd, "@d", SqlDbType.Date, today.ToDateTime(TimeOnly.MinValue));
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is long l ? l : null;
    }

    public async Task<(int RoomId, string RoomNo)?> RoomByNoAsync(ICommandTransaction tx, int propertyId, string roomNo, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT room_id, room_no FROM inv.room WHERE property_id = @p AND room_no = @n;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@n", SqlDbType.VarChar, roomNo, 10);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetInt32(0), rd.GetString(1)) : null;
    }

    // ── Requests ──

    private const string RequestColumns = """
        q.request_id, q.property_id, q.stay_id, q.room_id, r.room_no, q.guest_name, q.request_type, q.detail, q.dept, q.assignee_id, q.priority, q.status,
        q.created_utc, q.due_utc, q.scheduled_utc, q.started_utc, q.done_utc, q.guest_note, q.cancel_reason, q.created_by, q.row_ver
        """;

    private static RequestRecord ReadRequest(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetInt32(3), rd.GetString(4), rd.GetString(5), rd.GetString(6),
        rd.GetString(7), rd.GetString(8), I(rd, 9), rd.GetString(10), rd.GetString(11), U(rd, 12), U(rd, 13), T(rd, 14), T(rd, 15), T(rd, 16), S(rd, 17), S(rd, 18), rd.GetInt32(19), (byte[])rd[20]);

    public async Task<long> InsertRequestAsync(ICommandTransaction tx, NewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var cmd = Sql(tx).Command("""
            INSERT ops.guest_request(property_id, stay_id, room_id, guest_name, request_type, detail, dept, assignee_id, priority, status, created_utc, due_utc, scheduled_utc, created_by)
            OUTPUT INSERTED.request_id
            VALUES (@p, @s, @room, @g, @t, @d, @dept, @a, @prio, @st, @c, @due, @sch, @by);
            """);
        P(cmd, "@p", SqlDbType.Int, request.PropertyId);
        P(cmd, "@s", SqlDbType.BigInt, request.Stay.StayId);
        P(cmd, "@room", SqlDbType.Int, request.Stay.RoomId);
        P(cmd, "@g", SqlDbType.NVarChar, request.Stay.GuestName, 200);
        P(cmd, "@t", SqlDbType.VarChar, request.Type, 30);
        P(cmd, "@d", SqlDbType.NVarChar, request.Detail, 160);
        P(cmd, "@dept", SqlDbType.VarChar, request.Dept, 12);
        P(cmd, "@a", SqlDbType.Int, request.AssigneeId);
        P(cmd, "@prio", SqlDbType.VarChar, request.Priority, 6);
        P(cmd, "@st", SqlDbType.VarChar, StatusToDb(request.Status), 12);
        P(cmd, "@c", SqlDbType.DateTime2, request.CreatedUtc);
        P(cmd, "@due", SqlDbType.DateTime2, request.DueUtc);
        P(cmd, "@sch", SqlDbType.DateTime2, request.ScheduledUtc);
        P(cmd, "@by", SqlDbType.Int, request.CreatedBy);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<RequestRecord?> LockRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM ops.guest_request q WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = q.room_id WHERE q.request_id = @id AND q.property_id = @p;");
        P(cmd, "@id", SqlDbType.BigInt, requestId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(rd) : null;
    }

    public async Task<byte[]> UpdateRequestAsync(ICommandTransaction tx, RequestRecord updated, byte[] expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.guest_request SET dept = @dept, assignee_id = @a, status = @st, started_utc = @sta, done_utc = @done, guest_note = @note, cancel_reason = @cr
            WHERE request_id = @id AND row_ver = @e;
            SELECT @@ROWCOUNT AS n, (SELECT row_ver FROM ops.guest_request WHERE request_id = @id) AS v;
            """);
        P(cmd, "@dept", SqlDbType.VarChar, updated.Dept, 12);
        P(cmd, "@a", SqlDbType.Int, updated.AssigneeId);
        P(cmd, "@st", SqlDbType.VarChar, updated.Status, 12);
        P(cmd, "@sta", SqlDbType.DateTime2, updated.StartedUtc);
        P(cmd, "@done", SqlDbType.DateTime2, updated.DoneUtc);
        P(cmd, "@note", SqlDbType.NVarChar, updated.GuestNote, 200);
        P(cmd, "@cr", SqlDbType.NVarChar, updated.CancelReason, 200);
        P(cmd, "@id", SqlDbType.BigInt, updated.RequestId);
        P(cmd, "@e", SqlDbType.Binary, expected, 8);
        return await VersionAfterAsync(cmd, "This request", ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> VersionAfterAsync(SqlCommand cmd, string what, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        SqlValues.EnsureVersionMatched(rd.GetInt32(0), what);
        return (byte[])rd[1];
    }

    public async Task<IReadOnlyList<RequestRecord>> RequestsAsync(int propertyId, RequestFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {RequestColumns} FROM ops.guest_request q JOIN inv.room r ON r.room_id = q.room_id
            WHERE q.property_id = @p
              AND (@a IS NULL OR q.assignee_id = @a)
              AND (@nf = 0 OR q.dept <> 'Front desk')
              AND (@open = 0 OR q.status NOT IN ('Done','Cancelled'))
            ORDER BY q.created_utc DESC, q.request_id DESC;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@a", SqlDbType.Int, filter.AssigneeId);
        P(cmd, "@nf", SqlDbType.Bit, filter.DeptOnly == "NotFrontDesk");
        P(cmd, "@open", SqlDbType.Bit, filter.OpenOnly);
        var list = new List<RequestRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRequest(rd));
        return list;
    }

    public async Task<RequestRecord?> RequestAsync(int propertyId, long requestId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RequestColumns} FROM ops.guest_request q JOIN inv.room r ON r.room_id = q.room_id WHERE q.request_id = @id AND q.property_id = @p;", conn);
        P(cmd, "@id", SqlDbType.BigInt, requestId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(rd) : null;
    }

    // ── Complaints ──

    private const string ComplaintColumns = """
        c.complaint_id, c.property_id, c.stay_id, c.room_id, r.room_no, c.guest_name, c.category, c.severity, c.description, c.owner_id, c.status, c.root_cause,
        c.recovery_action, c.follow_up_on, c.follow_up_outcome, c.follow_up_satisfied, c.follow_up_utc, c.follow_up_by, c.created_utc, c.resolved_utc, c.closed_utc,
        c.created_by, c.recovery_state, c.recovery_amount_paise, c.recovery_reason, c.recovery_folio_id, c.recovery_approval_id, c.recovery_charge_id, c.recovery_utc,
        c.recovery_by, a.status, c.row_ver
        """;

    private const string ComplaintFrom = "ops.guest_complaint c {0} LEFT JOIN inv.room r ON r.room_id = c.room_id LEFT JOIN bill.approval a ON a.approval_id = c.recovery_approval_id";

    private static ComplaintRecord ReadComplaint(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), L(rd, 2), I(rd, 3), S(rd, 4), rd.GetString(5), rd.GetString(6), rd.GetString(7),
        rd.GetString(8), rd.GetInt32(9), rd.GetString(10), S(rd, 11), S(rd, 12), D(rd, 13), S(rd, 14), rd.IsDBNull(15) ? null : rd.GetBoolean(15), T(rd, 16), I(rd, 17), U(rd, 18), T(rd, 19), T(rd, 20),
        rd.GetInt32(21), S(rd, 22), L(rd, 23), S(rd, 24), L(rd, 25), L(rd, 26), L(rd, 27), T(rd, 28), I(rd, 29), S(rd, 30), (byte[])rd[31]);

    public async Task<long> InsertComplaintAsync(ICommandTransaction tx, NewComplaint c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Sql(tx).Command("""
            INSERT ops.guest_complaint(property_id, stay_id, room_id, guest_name, category, severity, description, owner_id, status, created_utc, created_by)
            OUTPUT INSERTED.complaint_id
            VALUES (@p, @s, @r, @g, @cat, @sev, @d, @o, 'Open', @t, @by);
            """);
        P(cmd, "@p", SqlDbType.Int, c.PropertyId);
        P(cmd, "@s", SqlDbType.BigInt, c.StayId);
        P(cmd, "@r", SqlDbType.Int, c.RoomId);
        P(cmd, "@g", SqlDbType.NVarChar, c.GuestName, 200);
        P(cmd, "@cat", SqlDbType.VarChar, c.Category, 20);
        P(cmd, "@sev", SqlDbType.VarChar, c.Severity, 8);
        P(cmd, "@d", SqlDbType.NVarChar, c.Description, 400);
        P(cmd, "@o", SqlDbType.Int, c.OwnerId);
        P(cmd, "@t", SqlDbType.DateTime2, c.CreatedUtc);
        P(cmd, "@by", SqlDbType.Int, c.CreatedBy);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<ComplaintRecord?> LockComplaintAsync(ICommandTransaction tx, int propertyId, long complaintId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {ComplaintColumns} FROM " + string.Format(System.Globalization.CultureInfo.InvariantCulture, ComplaintFrom, "WITH (UPDLOCK, ROWLOCK)") + " WHERE c.complaint_id = @id AND c.property_id = @p;");
        P(cmd, "@id", SqlDbType.BigInt, complaintId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadComplaint(rd) : null;
    }

    public async Task<byte[]> UpdateComplaintAsync(ICommandTransaction tx, ComplaintRecord u, byte[] expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(u);
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.guest_complaint SET owner_id = @o, severity = @sev, status = @st, root_cause = @rc, recovery_action = @ra, follow_up_on = @fo, follow_up_outcome = @fout,
              follow_up_satisfied = @fs, follow_up_utc = @fu, follow_up_by = @fb, resolved_utc = @res, closed_utc = @cl, recovery_state = @rs, recovery_amount_paise = @ramt,
              recovery_reason = @rr, recovery_folio_id = @rf, recovery_approval_id = @rap, recovery_charge_id = @rch, recovery_utc = @ru, recovery_by = @rb
            WHERE complaint_id = @id AND row_ver = @e;
            SELECT @@ROWCOUNT AS n, (SELECT row_ver FROM ops.guest_complaint WHERE complaint_id = @id) AS v;
            """);
        P(cmd, "@o", SqlDbType.Int, u.OwnerId);
        P(cmd, "@sev", SqlDbType.VarChar, u.Severity, 8);
        P(cmd, "@st", SqlDbType.VarChar, u.Status, 13);
        P(cmd, "@rc", SqlDbType.NVarChar, u.RootCause, 300);
        P(cmd, "@ra", SqlDbType.NVarChar, u.RecoveryAction, 300);
        P(cmd, "@fo", SqlDbType.Date, u.FollowUpOn?.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@fout", SqlDbType.NVarChar, u.FollowUpOutcome, 300);
        P(cmd, "@fs", SqlDbType.Bit, u.FollowUpSatisfied);
        P(cmd, "@fu", SqlDbType.DateTime2, u.FollowUpUtc);
        P(cmd, "@fb", SqlDbType.Int, u.FollowUpBy);
        P(cmd, "@res", SqlDbType.DateTime2, u.ResolvedUtc);
        P(cmd, "@cl", SqlDbType.DateTime2, u.ClosedUtc);
        P(cmd, "@rs", SqlDbType.VarChar, u.RecoveryState, 10);
        P(cmd, "@ramt", SqlDbType.BigInt, u.RecoveryAmountPaise);
        P(cmd, "@rr", SqlDbType.NVarChar, u.RecoveryReason, 200);
        P(cmd, "@rf", SqlDbType.BigInt, u.RecoveryFolioId);
        P(cmd, "@rap", SqlDbType.BigInt, u.RecoveryApprovalId);
        P(cmd, "@rch", SqlDbType.BigInt, u.RecoveryChargeId);
        P(cmd, "@ru", SqlDbType.DateTime2, u.RecoveryUtc);
        P(cmd, "@rb", SqlDbType.Int, u.RecoveryBy);
        P(cmd, "@id", SqlDbType.BigInt, u.ComplaintId);
        P(cmd, "@e", SqlDbType.Binary, expected, 8);
        return await VersionAfterAsync(cmd, "This complaint", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ComplaintRecord>> ComplaintsAsync(int propertyId, bool openOnly, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {ComplaintColumns} FROM " + string.Format(System.Globalization.CultureInfo.InvariantCulture, ComplaintFrom, string.Empty)
            + " WHERE c.property_id = @p AND (@open = 0 OR c.status <> 'Closed') ORDER BY c.created_utc DESC, c.complaint_id DESC;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@open", SqlDbType.Bit, openOnly);
        var list = new List<ComplaintRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadComplaint(rd));
        return list;
    }

    public async Task<ComplaintRecord?> ComplaintAsync(int propertyId, long complaintId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {ComplaintColumns} FROM " + string.Format(System.Globalization.CultureInfo.InvariantCulture, ComplaintFrom, string.Empty)
            + " WHERE c.complaint_id = @id AND c.property_id = @p;", conn);
        P(cmd, "@id", SqlDbType.BigInt, complaintId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadComplaint(rd) : null;
    }

    // ── Lost & found ──

    private const string ItemColumns = """
        i.item_id, i.property_id, i.description, i.category, i.valuable, i.found_where, i.room_id, r.room_no, i.stay_id, i.found_utc, i.found_date, i.found_by, i.storage, i.status,
        i.claim_name, i.claim_phone, i.claim_id_type, i.claim_id_note, i.claim_match_note, i.claim_utc, i.claim_by, i.handover_to, i.handover_note, i.handover_utc, i.handover_by,
        i.disposal_method, i.disposal_note, i.disposal_utc, i.disposal_by, i.row_ver
        """;

    private const string ItemFrom = "ops.lost_item i {0} LEFT JOIN inv.room r ON r.room_id = i.room_id";

    private static ItemRecord ReadItem(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), rd.GetString(5), I(rd, 6), S(rd, 7), L(rd, 8),
        U(rd, 9), DateOnly.FromDateTime(rd.GetDateTime(10)), rd.GetInt32(11), rd.GetString(12), rd.GetString(13), S(rd, 14), S(rd, 15), S(rd, 16), S(rd, 17), S(rd, 18), T(rd, 19), I(rd, 20),
        S(rd, 21), S(rd, 22), T(rd, 23), I(rd, 24), S(rd, 25), S(rd, 26), T(rd, 27), I(rd, 28), (byte[])rd[29]);

    public async Task<long> InsertItemAsync(ICommandTransaction tx, NewItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var cmd = Sql(tx).Command("""
            INSERT ops.lost_item(property_id, description, category, valuable, found_where, room_id, stay_id, found_utc, found_date, found_by, storage, status)
            OUTPUT INSERTED.item_id
            VALUES (@p, @d, @c, @v, @w, @r, @s, @t, @fd, @fb, @st, 'Stored');
            """);
        P(cmd, "@p", SqlDbType.Int, item.PropertyId);
        P(cmd, "@d", SqlDbType.NVarChar, item.Description, 160);
        P(cmd, "@c", SqlDbType.VarChar, item.Category, 16);
        P(cmd, "@v", SqlDbType.Bit, item.Valuable);
        P(cmd, "@w", SqlDbType.NVarChar, item.FoundWhere, 120);
        P(cmd, "@r", SqlDbType.Int, item.RoomId);
        P(cmd, "@s", SqlDbType.BigInt, item.StayId);
        P(cmd, "@t", SqlDbType.DateTime2, item.FoundUtc);
        P(cmd, "@fd", SqlDbType.Date, item.FoundDate.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@fb", SqlDbType.Int, item.FoundBy);
        P(cmd, "@st", SqlDbType.VarChar, item.Storage, 22);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<ItemRecord?> LockItemAsync(ICommandTransaction tx, int propertyId, long itemId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {ItemColumns} FROM " + string.Format(System.Globalization.CultureInfo.InvariantCulture, ItemFrom, "WITH (UPDLOCK, ROWLOCK)") + " WHERE i.item_id = @id AND i.property_id = @p;");
        P(cmd, "@id", SqlDbType.BigInt, itemId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadItem(rd) : null;
    }

    public async Task<byte[]> UpdateItemAsync(ICommandTransaction tx, ItemRecord u, byte[] expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(u);
        await using var cmd = Sql(tx).Command("""
            UPDATE ops.lost_item SET status = @st, claim_name = @cn, claim_phone = @cp, claim_id_type = @cit, claim_id_note = @cin, claim_match_note = @cm, claim_utc = @cu, claim_by = @cb,
              handover_to = @ht, handover_note = @hn, handover_utc = @hu, handover_by = @hb, disposal_method = @dm, disposal_note = @dn, disposal_utc = @du, disposal_by = @db
            WHERE item_id = @id AND row_ver = @e;
            SELECT @@ROWCOUNT AS n, (SELECT row_ver FROM ops.lost_item WHERE item_id = @id) AS v;
            """);
        P(cmd, "@st", SqlDbType.VarChar, u.Status, 10);
        P(cmd, "@cn", SqlDbType.NVarChar, u.ClaimName, 100);
        P(cmd, "@cp", SqlDbType.VarChar, u.ClaimPhone, 15);
        P(cmd, "@cit", SqlDbType.VarChar, u.ClaimIdType, 30);
        P(cmd, "@cin", SqlDbType.NVarChar, u.ClaimIdNote, 60);
        P(cmd, "@cm", SqlDbType.NVarChar, u.ClaimMatchNote, 200);
        P(cmd, "@cu", SqlDbType.DateTime2, u.ClaimUtc);
        P(cmd, "@cb", SqlDbType.Int, u.ClaimBy);
        P(cmd, "@ht", SqlDbType.NVarChar, u.HandoverTo, 100);
        P(cmd, "@hn", SqlDbType.NVarChar, u.HandoverNote, 200);
        P(cmd, "@hu", SqlDbType.DateTime2, u.HandoverUtc);
        P(cmd, "@hb", SqlDbType.Int, u.HandoverBy);
        P(cmd, "@dm", SqlDbType.VarChar, u.DisposalMethod, 16);
        P(cmd, "@dn", SqlDbType.NVarChar, u.DisposalNote, 200);
        P(cmd, "@du", SqlDbType.DateTime2, u.DisposalUtc);
        P(cmd, "@db", SqlDbType.Int, u.DisposalBy);
        P(cmd, "@id", SqlDbType.BigInt, u.ItemId);
        P(cmd, "@e", SqlDbType.Binary, expected, 8);
        return await VersionAfterAsync(cmd, "This item", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ItemRecord>> ItemsAsync(int propertyId, int? foundBy, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {ItemColumns} FROM " + string.Format(System.Globalization.CultureInfo.InvariantCulture, ItemFrom, string.Empty)
            + " WHERE i.property_id = @p AND (@fb IS NULL OR i.found_by = @fb) ORDER BY i.found_utc DESC, i.item_id DESC;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@fb", SqlDbType.Int, foundBy);
        var list = new List<ItemRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadItem(rd));
        return list;
    }

    // ── History ──

    public async Task AppendLogAsync(ICommandTransaction tx, string kind, long recordId, DateTime utc, int principalId, string what, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("INSERT ops.guest_service_log(kind, record_id, at_utc, by_principal, what) VALUES (@k, @r, @t, @b, @w);");
        P(cmd, "@k", SqlDbType.VarChar, kind, 10);
        P(cmd, "@r", SqlDbType.BigInt, recordId);
        P(cmd, "@t", SqlDbType.DateTime2, utc);
        P(cmd, "@b", SqlDbType.Int, principalId);
        P(cmd, "@w", SqlDbType.NVarChar, what.Length > 300 ? what[..300] : what, 300);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LogEntry>> HistoryAsync(int propertyId, string kind, long recordId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        // The log carries no property column: join to the owning register so another property's records are never listed.
        await using var cmd = connections.Command("""
            SELECT l.at_utc, l.by_principal, l.what FROM ops.guest_service_log l
            WHERE l.kind = @k AND l.record_id = @r AND (
                   (@k = 'request' AND EXISTS (SELECT 1 FROM ops.guest_request x WHERE x.request_id = @r AND x.property_id = @p))
                OR (@k = 'complaint' AND EXISTS (SELECT 1 FROM ops.guest_complaint x WHERE x.complaint_id = @r AND x.property_id = @p))
                OR (@k = 'item' AND EXISTS (SELECT 1 FROM ops.lost_item x WHERE x.item_id = @r AND x.property_id = @p)))
            ORDER BY l.log_id;
            """, conn);
        P(cmd, "@k", SqlDbType.VarChar, kind, 10);
        P(cmd, "@r", SqlDbType.BigInt, recordId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<LogEntry>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new LogEntry(U(rd, 0), rd.GetInt32(1), rd.GetString(2)));
        return list;
    }

    public async Task<IReadOnlyList<(long RecordId, LogEntry Entry)>> HistoriesAsync(int propertyId, string kind, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT l.record_id, l.at_utc, l.by_principal, l.what FROM ops.guest_service_log l
            WHERE l.kind = @k AND (
                   (@k = 'request' AND EXISTS (SELECT 1 FROM ops.guest_request x WHERE x.request_id = l.record_id AND x.property_id = @p))
                OR (@k = 'complaint' AND EXISTS (SELECT 1 FROM ops.guest_complaint x WHERE x.complaint_id = l.record_id AND x.property_id = @p))
                OR (@k = 'item' AND EXISTS (SELECT 1 FROM ops.lost_item x WHERE x.item_id = l.record_id AND x.property_id = @p)))
            ORDER BY l.record_id, l.log_id;
            """, conn);
        P(cmd, "@k", SqlDbType.VarChar, kind, 10);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<(long, LogEntry)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((rd.GetInt64(0), new LogEntry(U(rd, 1), rd.GetInt32(2), rd.GetString(3))));
        return list;
    }

    public async Task<IReadOnlyList<StayLookupRow>> StaysAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.stay_id, s.reservation_id, s.guest_id, r.room_no, g.full_name, s.status
            FROM gst.stay s
            JOIN gst.guest g ON g.guest_id = s.guest_id
            JOIN gst.stay_segment seg ON seg.stay_id = s.stay_id AND seg.seq = (SELECT MAX(x.seq) FROM gst.stay_segment x WHERE x.stay_id = s.stay_id)
            JOIN inv.room r ON r.room_id = seg.room_id
            WHERE s.property_id = @p AND (s.status = 'InHouse'
                OR s.stay_id IN (SELECT stay_id FROM ops.guest_request WHERE property_id = @p AND stay_id IS NOT NULL)
                OR s.stay_id IN (SELECT stay_id FROM ops.guest_complaint WHERE property_id = @p AND stay_id IS NOT NULL)
                OR s.stay_id IN (SELECT stay_id FROM ops.lost_item WHERE property_id = @p AND stay_id IS NOT NULL))
            ORDER BY s.stay_id;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<StayLookupRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new StayLookupRow(rd.GetInt64(0), rd.GetInt64(1), L(rd, 2), rd.GetString(3), rd.GetString(4), rd.GetString(5)));
        return list;
    }
}
