using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Handover;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Handover;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Handover;

/// <summary>Shared SQL helpers of the handover adapters (ops.hov_* tables, migration 1700).</summary>
internal static class HovSql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Handover SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static int? Int(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    private static DateTime? Utc(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    private static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    public static async Task<IReadOnlyList<HovTemplateRec>> TemplatesAsync(Func<string, SqlCommand> mk, int propertyId, long? templateId, bool lockRows, CancellationToken ct)
    {
        var hint = lockRows ? " WITH (UPDLOCK, ROWLOCK)" : string.Empty;
        await using var cmd = mk($"""
            SELECT t.template_id, t.name, t.desk, t.shift, t.active, t.version, t.row_ver FROM ops.hov_template t{hint}
             WHERE t.property_id = @p AND (@t IS NULL OR t.template_id = @t) ORDER BY t.template_id;
            SELECT r.template_id, r.role_code FROM ops.hov_template_role r JOIN ops.hov_template t ON t.template_id = r.template_id
             WHERE t.property_id = @p AND (@t IS NULL OR t.template_id = @t) ORDER BY r.template_id, r.role_code;
            SELECT i.template_id, i.item_key, i.text, i.mandatory, i.due, i.photo, i.link FROM ops.hov_template_item i JOIN ops.hov_template t ON t.template_id = i.template_id
             WHERE t.property_id = @p AND (@t IS NULL OR t.template_id = @t) ORDER BY i.template_id, i.seq;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@t", SqlDbType.BigInt, templateId);
        var heads = new List<(long Id, string Name, string Desk, string Shift, bool Active, int Version, byte[] Ver)>();
        var roles = new Dictionary<long, List<string>>();
        var items = new Dictionary<long, List<HovItemDef>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) heads.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), rd.GetInt32(5), (byte[])rd[6]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) Add(roles, rd.GetInt64(0), rd.GetString(1));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) Add(items, rd.GetInt64(0), new HovItemDef(rd.GetString(1), rd.GetString(2), rd.GetBoolean(3), Str(rd, 4), rd.GetBoolean(5), Str(rd, 6)));
        return heads.Select(h => new HovTemplateRec(h.Id, h.Name, h.Desk, h.Shift, h.Active, h.Version, h.Ver, roles.GetValueOrDefault(h.Id) ?? [], items.GetValueOrDefault(h.Id) ?? [])).ToList();
    }

    private static void Add<T>(Dictionary<long, List<T>> d, long key, T value)
    {
        if (!d.TryGetValue(key, out var l)) d[key] = l = [];
        l.Add(value);
    }

    public static async Task<IReadOnlyList<HovRun>> RunsAsync(Func<string, SqlCommand> mk, int propertyId, DateOnly date, long? templateId, bool lockRows, CancellationToken ct)
    {
        var hint = lockRows ? " WITH (UPDLOCK, ROWLOCK)" : string.Empty;
        await using var cmd = mk($"""
            SELECT r.run_id, r.template_id, r.template_version, r.name, r.desk, r.shift, r.business_date, r.status, r.started_by, r.submitted_by, r.submitted_utc, r.late, r.reopen_reason
              FROM ops.hov_run r{hint} WHERE r.property_id = @p AND r.business_date = @d AND (@t IS NULL OR r.template_id = @t) ORDER BY r.run_id;
            SELECT x.run_id, x.role_code FROM ops.hov_run_role x JOIN ops.hov_run r ON r.run_id = x.run_id
             WHERE r.property_id = @p AND r.business_date = @d AND (@t IS NULL OR r.template_id = @t) ORDER BY x.run_id, x.role_code;
            SELECT i.run_id, i.item_key, i.text, i.mandatory, i.due, i.photo, i.link, i.done, i.done_by, i.done_utc, i.note, i.photo_name
              FROM ops.hov_run_item i JOIN ops.hov_run r ON r.run_id = i.run_id
             WHERE r.property_id = @p AND r.business_date = @d AND (@t IS NULL OR r.template_id = @t) ORDER BY i.run_id, i.seq;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        cmd.P("@t", SqlDbType.BigInt, templateId);
        var heads = new List<(long Id, long Template, int Version, string Name, string Desk, string Shift, DateOnly Date, bool Open, int StartedBy, int? SubBy, DateTime? SubUtc, bool? Late, string? Reopen)>();
        var roles = new Dictionary<long, List<string>>();
        var items = new Dictionary<long, List<HovItemState>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), Date(rd, 6), rd.GetString(7) == "Open", rd.GetInt32(8), Int(rd, 9), Utc(rd, 10), rd.IsDBNull(11) ? null : rd.GetBoolean(11), Str(rd, 12)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) Add(roles, rd.GetInt64(0), rd.GetString(1));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            Add(items, rd.GetInt64(0), new HovItemState(new HovItemDef(rd.GetString(1), rd.GetString(2), rd.GetBoolean(3), Str(rd, 4), rd.GetBoolean(5), Str(rd, 6)),
                rd.GetBoolean(7), Int(rd, 8), Utc(rd, 9), Str(rd, 10), Str(rd, 11)));
        return heads.Select(h => new HovRun(h.Id, h.Template, h.Version, h.Name, h.Desk, h.Shift, roles.GetValueOrDefault(h.Id) ?? [], h.Date, h.Open, true, h.StartedBy, h.SubBy, h.SubUtc, h.Late, h.Reopen,
            items.GetValueOrDefault(h.Id) ?? [])).ToList();
    }

    /// <param name="where">Internal constant predicate over alias <c>n</c>; <paramref name="bind"/> adds its parameters.</param>
    public static async Task<IReadOnlyList<HovNoteRec>> NotesAsync(Func<string, SqlCommand> mk, string where, Action<SqlCommand> bind, bool lockRows, CancellationToken ct)
    {
        var hint = lockRows ? " WITH (UPDLOCK, ROWLOCK)" : string.Empty;
        await using var cmd = mk($"""
            SELECT n.note_id, n.desk, n.business_date, n.shift, n.to_shift, n.author_id, ISNULL(u.display_name, N'?'), n.written_utc, n.issues, n.nothing, n.keys, n.guests,
                   n.cash_paise, n.cash_note, n.status, n.ack_by, n.ack_utc, n.ack_remark, n.row_ver
              FROM ops.hov_note n{hint} LEFT JOIN plat.app_user u ON u.user_id = n.author_id WHERE {where} ORDER BY n.written_utc DESC, n.note_id DESC;
            SELECT c.note_id, c.carry_id, c.text, c.done, c.done_by, c.done_utc FROM ops.hov_carry c JOIN ops.hov_note n ON n.note_id = c.note_id WHERE {where} ORDER BY c.note_id, c.seq;
            """);
        bind(cmd);
        var heads = new List<HovNoteRec>();
        var carry = new Dictionary<long, List<HovCarryRec>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add(new HovNoteRec(rd.GetInt64(0), rd.GetString(1), Date(rd, 2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetString(6), DateTime.SpecifyKind(rd.GetDateTime(7), DateTimeKind.Utc),
                rd.GetString(8), rd.GetBoolean(9), rd.GetString(10), rd.GetString(11), rd.IsDBNull(12) ? null : rd.GetInt64(12), Str(rd, 13), rd.GetString(14), Int(rd, 15), Utc(rd, 16), Str(rd, 17), (byte[])rd[18], []));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) Add(carry, rd.GetInt64(0), new HovCarryRec(rd.GetInt64(1), rd.GetString(2), rd.GetBoolean(3), Int(rd, 4), Utc(rd, 5)));
        return heads.Select(h => h with { Carry = carry.GetValueOrDefault(h.NoteId) ?? [] }).ToList();
    }
}

/// <summary>SQL adapter of <see cref="IHandoverStore"/>. Rows are locked with UPDLOCK; checks come before inserts (contracts §4 rules 7–8).</summary>
public sealed class SqlHandoverStore : IHandoverStore
{
    private static Func<string, SqlCommand> Mk(ICommandTransaction tx) { var t = HovSql.Sql(tx); return t.Command; }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<HovTemplateRec?> LockTemplateAsync(ICommandTransaction tx, int propertyId, long templateId, CancellationToken ct = default)
        => (await HovSql.TemplatesAsync(Mk(tx), propertyId, templateId, true, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<bool> TemplateNameTakenAsync(ICommandTransaction tx, int propertyId, string name, long? exceptTemplateId, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("SELECT COUNT(*) FROM ops.hov_template WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND name = @n AND (@x IS NULL OR template_id <> @x);");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@x", SqlDbType.BigInt, exceptTemplateId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    public async Task<long> InsertTemplateAsync(ICommandTransaction tx, int propertyId, HandoverRules.CleanTemplate t, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        await using var cmd = HovSql.Sql(tx).Command("""
            INSERT ops.hov_template(property_id, name, desk, shift, active, version, updated_by, updated_utc)
            OUTPUT INSERTED.template_id VALUES (@p, @n, @d, @s, 1, 1, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@n", SqlDbType.NVarChar, t.Name, 60);
        cmd.P("@d", SqlDbType.VarChar, t.Desk, 12);
        cmd.P("@s", SqlDbType.VarChar, t.Shift, 7);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        await WriteChildrenAsync(tx, id, t, ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateTemplateAsync(ICommandTransaction tx, long templateId, byte[] expectedVersion, HandoverRules.CleanTemplate t, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        await using (var cmd = HovSql.Sql(tx).Command("""
            UPDATE ops.hov_template SET name = @n, desk = @d, shift = @s, version = version + 1, updated_by = @u, updated_utc = @utc WHERE template_id = @t AND row_ver = @v;
            """))
        {
            cmd.P("@n", SqlDbType.NVarChar, t.Name, 60);
            cmd.P("@d", SqlDbType.VarChar, t.Desk, 12);
            cmd.P("@s", SqlDbType.VarChar, t.Shift, 7);
            cmd.P("@u", SqlDbType.Int, principalId);
            cmd.P("@utc", SqlDbType.DateTime2, utc);
            cmd.P("@t", SqlDbType.BigInt, templateId);
            cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This checklist template");
        }

        await using (var del = HovSql.Sql(tx).Command("DELETE ops.hov_template_role WHERE template_id = @t; DELETE ops.hov_template_item WHERE template_id = @t;"))
        {
            del.P("@t", SqlDbType.BigInt, templateId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await WriteChildrenAsync(tx, templateId, t, ct).ConfigureAwait(false);
    }

    private static async Task WriteChildrenAsync(ICommandTransaction tx, long id, HandoverRules.CleanTemplate t, CancellationToken ct)
    {
        foreach (var r in t.Roles)
        {
            await using var cmd = HovSql.Sql(tx).Command("INSERT ops.hov_template_role(template_id, role_code) VALUES (@t, @r);");
            cmd.P("@t", SqlDbType.BigInt, id);
            cmd.P("@r", SqlDbType.VarChar, r, 3);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        short seq = 0;
        foreach (var i in t.Items)
        {
            await using var cmd = HovSql.Sql(tx).Command("INSERT ops.hov_template_item(template_id, seq, item_key, text, mandatory, due, photo, link) VALUES (@t, @q, @k, @x, @m, @d, @ph, @l);");
            cmd.P("@t", SqlDbType.BigInt, id);
            cmd.P("@q", SqlDbType.SmallInt, ++seq);
            cmd.P("@k", SqlDbType.VarChar, i.Key, 20);
            cmd.P("@x", SqlDbType.NVarChar, i.Text, 120);
            cmd.P("@m", SqlDbType.Bit, i.Mandatory);
            cmd.P("@d", SqlDbType.Char, i.Due, 5);
            cmd.P("@ph", SqlDbType.Bit, i.Photo);
            cmd.P("@l", SqlDbType.VarChar, i.Link, 40);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task SetTemplateActiveAsync(ICommandTransaction tx, long templateId, byte[] expectedVersion, bool active, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_template SET active = @a, updated_by = @u, updated_utc = @utc WHERE template_id = @t AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@t", SqlDbType.BigInt, templateId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This checklist template");
    }

    public async Task<HovRun?> LockRunAsync(ICommandTransaction tx, int propertyId, long templateId, DateOnly date, CancellationToken ct = default)
        => (await HovSql.RunsAsync(Mk(tx), propertyId, date, templateId, true, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<HovRun> CreateRunAsync(ICommandTransaction tx, int propertyId, HovTemplateRec template, DateOnly date, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        long runId;
        await using (var cmd = HovSql.Sql(tx).Command("""
            INSERT ops.hov_run(property_id, template_id, template_version, name, desk, shift, business_date, status, started_by, started_utc)
            OUTPUT INSERTED.run_id VALUES (@p, @t, @v, @n, @d, @s, @bd, 'Open', @u, @utc);
            """))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            cmd.P("@t", SqlDbType.BigInt, template.TemplateId);
            cmd.P("@v", SqlDbType.Int, template.Version);
            cmd.P("@n", SqlDbType.NVarChar, template.Name, 60);
            cmd.P("@d", SqlDbType.VarChar, template.Desk, 12);
            cmd.P("@s", SqlDbType.VarChar, template.Shift, 7);
            cmd.P("@bd", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            cmd.P("@u", SqlDbType.Int, principalId);
            cmd.P("@utc", SqlDbType.DateTime2, utc);
            runId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var r in template.Roles)
        {
            await using var cmd = HovSql.Sql(tx).Command("INSERT ops.hov_run_role(run_id, role_code) VALUES (@r, @c);");
            cmd.P("@r", SqlDbType.BigInt, runId);
            cmd.P("@c", SqlDbType.VarChar, r, 3);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        short seq = 0;
        foreach (var i in template.Items)
        {
            await using var cmd = HovSql.Sql(tx).Command("INSERT ops.hov_run_item(run_id, seq, item_key, text, mandatory, due, photo, link) VALUES (@r, @q, @k, @x, @m, @d, @ph, @l);");
            cmd.P("@r", SqlDbType.BigInt, runId);
            cmd.P("@q", SqlDbType.SmallInt, ++seq);
            cmd.P("@k", SqlDbType.VarChar, i.Key, 20);
            cmd.P("@x", SqlDbType.NVarChar, i.Text, 120);
            cmd.P("@m", SqlDbType.Bit, i.Mandatory);
            cmd.P("@d", SqlDbType.Char, i.Due, 5);
            cmd.P("@ph", SqlDbType.Bit, i.Photo);
            cmd.P("@l", SqlDbType.VarChar, i.Link, 40);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return (await HovSql.RunsAsync(Mk(tx), propertyId, date, template.TemplateId, false, ct).ConfigureAwait(false)).First();
    }

    public async Task UpdateItemAsync(ICommandTransaction tx, long runId, HovItemState item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_run_item SET done = @d, done_by = @b, done_utc = @u, note = @n, photo_name = @ph WHERE run_id = @r AND item_key = @k;");
        cmd.P("@d", SqlDbType.Bit, item.Done);
        cmd.P("@b", SqlDbType.Int, item.DoneBy);
        cmd.P("@u", SqlDbType.DateTime2, item.DoneUtc);
        cmd.P("@n", SqlDbType.NVarChar, item.Note, 200);
        cmd.P("@ph", SqlDbType.NVarChar, item.PhotoName, 80);
        cmd.P("@r", SqlDbType.BigInt, runId);
        cmd.P("@k", SqlDbType.VarChar, item.Key, 20);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SubmitRunAsync(ICommandTransaction tx, long runId, int principalId, DateTime utc, bool late, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_run SET status = 'Submitted', submitted_by = @u, submitted_utc = @utc, late = @l WHERE run_id = @r;");
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@l", SqlDbType.Bit, late);
        cmd.P("@r", SqlDbType.BigInt, runId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ReopenRunAsync(ICommandTransaction tx, long runId, string reason, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_run SET status = 'Open', submitted_by = NULL, submitted_utc = NULL, late = NULL, reopen_reason = @x WHERE run_id = @r;");
        cmd.P("@x", SqlDbType.NVarChar, reason, 200);
        cmd.P("@r", SqlDbType.BigInt, runId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<HovNoteRec>> IncomingAsync(ICommandTransaction tx, int propertyId, int principalId, string desk, CancellationToken ct = default)
        => HovSql.NotesAsync(Mk(tx), "n.property_id = @p AND n.desk = @d AND n.status = 'Submitted' AND n.author_id <> @u", c =>
        {
            c.P("@p", SqlDbType.Int, propertyId);
            c.P("@d", SqlDbType.VarChar, desk, 12);
            c.P("@u", SqlDbType.Int, principalId);
        }, false, ct);

    public async Task<HovNoteRec?> LockNoteByKeyAsync(ICommandTransaction tx, int propertyId, string desk, DateOnly date, string shift, CancellationToken ct = default)
        => (await HovSql.NotesAsync(Mk(tx), "n.property_id = @p AND n.desk = @d AND n.business_date = @bd AND n.shift = @s", c =>
        {
            c.P("@p", SqlDbType.Int, propertyId);
            c.P("@d", SqlDbType.VarChar, desk, 12);
            c.P("@bd", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            c.P("@s", SqlDbType.VarChar, shift, 7);
        }, true, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<HovNoteRec?> LockNoteAsync(ICommandTransaction tx, int propertyId, long noteId, CancellationToken ct = default)
        => (await HovSql.NotesAsync(Mk(tx), "n.property_id = @p AND n.note_id = @i", c =>
        {
            c.P("@p", SqlDbType.Int, propertyId);
            c.P("@i", SqlDbType.BigInt, noteId);
        }, true, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<long> InsertNoteAsync(ICommandTransaction tx, int propertyId, HovNoteWrite n, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(n);
        long id;
        await using (var cmd = HovSql.Sql(tx).Command("""
            INSERT ops.hov_note(property_id, desk, business_date, shift, to_shift, author_id, written_utc, issues, nothing, keys, guests, cash_paise, cash_note, status)
            OUTPUT INSERTED.note_id VALUES (@p, @d, @bd, @s, @ts, @u, @utc, @i, @no, @k, @g, @c, @cn, 'Submitted');
            """))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            Bind(cmd, n, principalId, utc);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        await WriteCarryAsync(tx, id, n.Carry, ct).ConfigureAwait(false);
        return id;
    }

    public async Task ReplaceNoteAsync(ICommandTransaction tx, long noteId, HovNoteWrite n, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(n);
        await using (var cmd = HovSql.Sql(tx).Command("""
            UPDATE ops.hov_note SET author_id = @u, written_utc = @utc, to_shift = @ts, issues = @i, nothing = @no, keys = @k, guests = @g, cash_paise = @c, cash_note = @cn WHERE note_id = @id;
            DELETE ops.hov_carry WHERE note_id = @id;
            """))
        {
            Bind(cmd, n, principalId, utc);
            cmd.P("@id", SqlDbType.BigInt, noteId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await WriteCarryAsync(tx, noteId, n.Carry, ct).ConfigureAwait(false);
    }

    private static void Bind(SqlCommand cmd, HovNoteWrite n, int principalId, DateTime utc)
    {
        cmd.P("@d", SqlDbType.VarChar, n.Desk, 12);
        cmd.P("@bd", SqlDbType.Date, n.Date.ToDateTime(TimeOnly.MinValue));
        cmd.P("@s", SqlDbType.VarChar, n.Shift, 7);
        cmd.P("@ts", SqlDbType.VarChar, n.ToShift, 7);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@i", SqlDbType.NVarChar, n.Issues, 400);
        cmd.P("@no", SqlDbType.Bit, n.Nothing);
        cmd.P("@k", SqlDbType.NVarChar, n.Keys, 400);
        cmd.P("@g", SqlDbType.NVarChar, n.Guests, 400);
        cmd.P("@c", SqlDbType.BigInt, n.CashPaise);
        cmd.P("@cn", SqlDbType.NVarChar, n.CashNote, 200);
    }

    private static async Task WriteCarryAsync(ICommandTransaction tx, long noteId, IReadOnlyList<string> carry, CancellationToken ct)
    {
        short seq = 0;
        foreach (var text in carry)
        {
            await using var cmd = HovSql.Sql(tx).Command("INSERT ops.hov_carry(note_id, seq, text) VALUES (@n, @q, @t);");
            cmd.P("@n", SqlDbType.BigInt, noteId);
            cmd.P("@q", SqlDbType.SmallInt, ++seq);
            cmd.P("@t", SqlDbType.NVarChar, text, 120);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task AcknowledgeNoteAsync(ICommandTransaction tx, long noteId, int principalId, DateTime utc, string? remark, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_note SET status = 'Acknowledged', ack_by = @u, ack_utc = @utc, ack_remark = @r WHERE note_id = @n AND status = 'Submitted';");
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@r", SqlDbType.NVarChar, remark, 200);
        cmd.P("@n", SqlDbType.BigInt, noteId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteCarryAsync(ICommandTransaction tx, long carryId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = HovSql.Sql(tx).Command("UPDATE ops.hov_carry SET done = 1, done_by = @u, done_utc = @utc WHERE carry_id = @c AND done = 0;");
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@c", SqlDbType.BigInt, carryId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>SQL adapter of <see cref="IHandoverReadStore"/>.</summary>
public sealed class SqlHandoverReadStore(SqlConnectionFactory connections) : IHandoverReadStore
{
    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT business_date FROM inv.property WHERE property_id = @p;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<IReadOnlyList<HovTemplateRec>> TemplatesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await HovSql.TemplatesAsync(s => connections.Command(s, conn), propertyId, null, false, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HovRun>> RunsAsync(int propertyId, DateOnly date, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await HovSql.RunsAsync(s => connections.Command(s, conn), propertyId, date, null, false, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HovNoteRec>> NotesAsync(int propertyId, DateOnly businessDate, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await HovSql.NotesAsync(s => connections.Command(s, conn),
            "n.property_id = @p AND (n.business_date >= @from OR n.status = 'Submitted' OR EXISTS (SELECT 1 FROM ops.hov_carry k WHERE k.note_id = n.note_id AND k.done = 0))", c =>
            {
                c.P("@p", SqlDbType.Int, propertyId);
                c.P("@from", SqlDbType.Date, businessDate.AddDays(-45).ToDateTime(TimeOnly.MinValue));
            }, false, ct).ConfigureAwait(false);
    }
}

/// <summary>Active role codes of a principal from the access tables (read only).</summary>
public sealed class SqlHandoverPrincipals(SqlConnectionFactory connections) : IHandoverPrincipals
{
    public async Task<IReadOnlyList<string>> RoleCodesAsync(int propertyId, int principalId, DateTime utcNow, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT r.role_code FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.user_id = @u AND u.property_id = @p AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now);
            """, conn);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@now", SqlDbType.DateTime2, utcNow);
        var list = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
        return list;
    }
}
