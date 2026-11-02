using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Reminders;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reminders;

/// <summary>SQL adapter of the desk-reminder port (migration 4704: plat.reminder; roles from plat.user_role / plat.role).</summary>
public sealed class SqlReminderStore(SqlConnectionFactory connections) : IReminderStore
{
    private const string Select = """
        SELECT reminder_id, subject, remind_date, remind_time, lead_days, role_code, note, repeat_rule, status, next_date, created_by, created_utc, done_by, done_utc
          FROM plat.reminder
        """;

    private static SqlCommandTransaction Sql(ICommandTransaction tx) => tx as SqlCommandTransaction ?? throw new InvalidOperationException("The reminder store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static ReminderRec Rec(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetString(1), DateOnly.FromDateTime(rd.GetDateTime(2)), rd.IsDBNull(3) ? null : rd.GetString(3).TrimEnd(), rd.GetInt32(4),
        rd.GetString(5), rd.IsDBNull(6) ? null : rd.GetString(6), rd.GetString(7), rd.GetString(8), rd.IsDBNull(9) ? null : DateOnly.FromDateTime(rd.GetDateTime(9)), rd.GetInt32(10), Utc(rd, 11),
        rd.IsDBNull(12) ? null : rd.GetInt32(12), rd.IsDBNull(13) ? null : Utc(rd, 13));

    private static void Fill(SqlCommand cmd, int propertyId, ReminderRec r)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@sub", SqlDbType.NVarChar, r.Subject, 80);
        P(cmd, "@date", SqlDbType.Date, r.Date.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@time", SqlDbType.Char, r.Time, 5);
        P(cmd, "@lead", SqlDbType.Int, r.LeadDays);
        P(cmd, "@role", SqlDbType.VarChar, r.Role, 3);
        P(cmd, "@note", SqlDbType.NVarChar, r.Note, 200);
        P(cmd, "@rep", SqlDbType.VarChar, r.Repeat, 7);
        P(cmd, "@st", SqlDbType.VarChar, r.Status, 9);
        P(cmd, "@next", SqlDbType.Date, r.NextDate?.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@by", SqlDbType.Int, r.CreatedBy);
        P(cmd, "@cu", SqlDbType.DateTime2, r.CreatedUtc);
        P(cmd, "@dby", SqlDbType.Int, r.DoneBy);
        P(cmd, "@du", SqlDbType.DateTime2, r.DoneUtc);
    }

    private static async Task<List<string>> RolesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0).Trim());
        return list;
    }

    public async Task<IReadOnlyCollection<string>> RolesOfUserAsync(ICommandTransaction tx, int userId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT DISTINCT role_code FROM plat.v_user_role_effective WHERE user_id = @u AND valid_from_utc <= SYSUTCDATETIME() AND (valid_until_utc IS NULL OR valid_until_utc > SYSUTCDATETIME())");
        P(cmd, "@u", SqlDbType.Int, userId);
        return await RolesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> KnownRolesAsync(ICommandTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT role_code FROM plat.role");
        return await RolesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<ReminderRec?> LockAsync(ICommandTransaction tx, int propertyId, long reminderId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(Select.Replace("FROM plat.reminder", "FROM plat.reminder WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal) + " WHERE property_id = @p AND reminder_id = @id");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@id", SqlDbType.BigInt, reminderId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Rec(rd) : null;
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, int propertyId, ReminderRec r, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT plat.reminder(property_id, subject, remind_date, remind_time, lead_days, role_code, note, repeat_rule, status, next_date, created_by, created_utc, done_by, done_utc)
            OUTPUT INSERTED.reminder_id
            VALUES(@p, @sub, @date, @time, @lead, @role, @note, @rep, @st, @next, @by, @cu, @dby, @du)
            """);
        Fill(cmd, propertyId, r);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task UpdateAsync(ICommandTransaction tx, int propertyId, ReminderRec r, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE plat.reminder SET subject = @sub, remind_date = @date, remind_time = @time, lead_days = @lead, role_code = @role, note = @note, repeat_rule = @rep, status = @st,
                   next_date = @next, done_by = @dby, done_utc = @du
             WHERE property_id = @p AND reminder_id = @id
            """);
        Fill(cmd, propertyId, r);
        P(cmd, "@id", SqlDbType.BigInt, r.ReminderId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReminderRec>> ListAsync(int propertyId, int userId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(Select + """
             WHERE property_id = @p AND (role_code = 'all' OR created_by = @u
                    OR role_code IN (SELECT role_code FROM plat.v_user_role_effective WHERE user_id = @u AND valid_from_utc <= SYSUTCDATETIME() AND (valid_until_utc IS NULL OR valid_until_utc > SYSUTCDATETIME()))
                    OR EXISTS (SELECT 1 FROM plat.user_role x WHERE x.user_id = @u AND x.role_code = 'OWN' AND x.valid_from_utc <= SYSUTCDATETIME() AND (x.valid_until_utc IS NULL OR x.valid_until_utc > SYSUTCDATETIME())))
             ORDER BY remind_date, reminder_id
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@u", SqlDbType.Int, userId);
        var list = new List<ReminderRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Rec(rd));
        return list;
    }

    public async Task<IReadOnlyCollection<string>> RolesOfUserAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT DISTINCT role_code FROM plat.v_user_role_effective WHERE user_id = @u AND valid_from_utc <= SYSUTCDATETIME() AND (valid_until_utc IS NULL OR valid_until_utc > SYSUTCDATETIME())", conn);
        P(cmd, "@u", SqlDbType.Int, userId);
        return await RolesAsync(cmd, ct).ConfigureAwait(false);
    }
}
