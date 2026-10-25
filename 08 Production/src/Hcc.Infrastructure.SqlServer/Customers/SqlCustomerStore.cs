using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Customers;
using Hcc.Contracts;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Customers;

internal static class CrmSql
{
    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Customers SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    public static CrmMerge ReadMerge(SqlDataReader rd) =>
        new(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetString(6), rd.GetInt32(7), Utc(rd, 8),
            rd.IsDBNull(9) ? null : rd.GetInt32(9), rd.IsDBNull(10) ? null : Utc(rd, 10), rd.IsDBNull(11) ? null : rd.GetString(11), (byte[])rd[12]);

    public const string MergeColumns = "merge_id, source_key, target_key, source_name, target_name, reason, status, proposed_by, proposed_utc, decided_by, decided_utc, decision_note, row_ver";

    public static async Task<CrmOverlay> OverlayAsync(Func<string, SqlCommand> mk, int propertyId, string key, CancellationToken ct)
    {
        await using var cmd = mk("""
            SELECT tag FROM crm.customer_tag WHERE property_id = @p AND customer_key = @k ORDER BY tag;
            SELECT note_id, body, author_id, created_utc FROM crm.customer_note WHERE property_id = @p AND customer_key = @k ORDER BY created_utc DESC, note_id DESC;
            SELECT preferences, row_ver FROM crm.customer_pref WHERE property_id = @p AND customer_key = @k;
            SELECT alias_key FROM crm.customer_alias WHERE property_id = @p AND target_key = @k ORDER BY alias_key;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        var tags = new List<string>();
        var notes = new List<CrmNote>();
        var from = new List<string>();
        CrmPref? pref = null;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) tags.Add(rd.GetString(0));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) notes.Add(new CrmNote(rd.GetInt64(0), rd.GetString(1), rd.GetInt32(2), Utc(rd, 3)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        if (await rd.ReadAsync(ct).ConfigureAwait(false)) pref = new CrmPref(rd.GetString(0), (byte[])rd[1]);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) from.Add(rd.GetString(0));
        return new CrmOverlay(tags, notes, pref, from);
    }
}

/// <summary>Read side. The customer sources are read-only queries on inv.reservation, gst.guest/registration/stay and bill.charge/folio (never written; no ID columns selected, DEC-068).</summary>
public sealed class SqlCustomerReadStore(SqlConnectionFactory connections) : ICustomerReadStore
{
    public async Task<CrmSource> LoadAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT business_date FROM inv.property WHERE property_id = @p;
            SELECT guest_id, full_name, phone, email, preferences, notes FROM gst.guest WHERE property_id = @p;
            SELECT r.reservation_id, r.public_no, reg.guest_id, r.guest_name, r.arrival, r.departure, r.status, COALESCE(a.room_no, b.room_no)
              FROM inv.reservation r
              LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
              OUTER APPLY (SELECT TOP 1 rm.room_no FROM gst.stay s2 JOIN gst.stay_segment sg ON sg.stay_id = s2.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
                            WHERE s2.reservation_id = r.reservation_id ORDER BY sg.seq DESC) a
              OUTER APPLY (SELECT TOP 1 rm.room_no FROM inv.room_night rn JOIN inv.room rm ON rm.room_id = rn.room_id
                            WHERE rn.reservation_id = r.reservation_id ORDER BY rn.night DESC) b
             WHERE r.property_id = @p;
            SELECT f.reservation_id, c.kind, c.amount_paise, c.tax_bps,
                   CASE WHEN c.kind = 'room' AND c.reverses_charge_id IS NULL AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id) THEN 1 ELSE 0 END
              FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
             WHERE r.property_id = @p AND r.status IN ('InHouse','Departed');
            SELECT alias_key, target_key FROM crm.customer_alias WHERE property_id = @p;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var today = DateOnly.FromDateTime(rd.GetDateTime(0));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var guests = new List<CrmGuest>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            guests.Add(new CrmGuest(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4), rd.IsDBNull(5) ? null : rd.GetString(5)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var res = new List<(long Id, string No, long? Guest, string Name, DateOnly Arr, DateOnly Dep, string Status, string? Room)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            res.Add((rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetInt64(2), rd.GetString(3), DateOnly.FromDateTime(rd.GetDateTime(4)), DateOnly.FromDateTime(rd.GetDateTime(5)), rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var lines = new Dictionary<long, List<FolioLine>>();
        var rooms = new Dictionary<long, List<long>>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            var amount = rd.GetInt64(2);
            if (!lines.TryGetValue(id, out var l)) lines[id] = l = [];
            l.Add(new FolioLine(Paise.FromWire(amount), rd.GetInt32(3)));
            if (rd.GetInt32(4) == 1)
            {
                if (!rooms.TryGetValue(id, out var r)) rooms[id] = r = [];
                r.Add(amount);
            }
        }

        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) aliases[rd.GetString(0)] = rd.GetString(1);
        var stays = res.Select(x => new CrmStay(x.Id, x.No, x.Guest, x.Name, x.Arr, x.Dep, x.Status, x.Room,
            lines.TryGetValue(x.Id, out var ls) ? FolioTotals.Compute(ls, []).Total.Value : 0,
            rooms.TryGetValue(x.Id, out var rr) && rr.Count > 0 ? (long)Math.Round((decimal)rr.Sum() / rr.Count, MidpointRounding.AwayFromZero) : 0)).ToList();
        return new CrmSource(guests, stays, aliases, today);
    }

    public async Task<CrmOverlay> OverlayAsync(int propertyId, string customerKey, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await CrmSql.OverlayAsync(s => connections.Command(s, conn), propertyId, customerKey, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> TagsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("SELECT customer_key, tag FROM crm.customer_tag WHERE property_id = @p ORDER BY customer_key, tag;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var k = rd.GetString(0);
            if (!d.TryGetValue(k, out var l)) d[k] = l = [];
            l.Add(rd.GetString(1));
        }

        return d.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<CrmMerge>> MergesAsync(int propertyId, string? status, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {CrmSql.MergeColumns} FROM crm.customer_merge WHERE property_id = @p AND (@s IS NULL OR status = @s) ORDER BY merge_id DESC;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@s", SqlDbType.VarChar, status, 8);
        var list = new List<CrmMerge>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(CrmSql.ReadMerge(rd));
        return list;
    }
}

/// <summary>Write side inside the command transaction.</summary>
public sealed class SqlCustomerStore : ICustomerStore
{
    public async Task<long?> InsertMergeAsync(ICommandTransaction tx, int propertyId, string source, string target, string sourceName, string targetName, string reason, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = CrmSql.Sql(tx);
        await using (var chk = t.Command("""
            SELECT COUNT(*) FROM crm.customer_alias WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND alias_key IN (@s, @t);
            """))
        {
            chk.P("@p", SqlDbType.Int, propertyId);
            chk.P("@s", SqlDbType.VarChar, source, 80);
            chk.P("@t", SqlDbType.VarChar, target, 80);
            if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return null;
        }

        await using (var pend = t.Command("SELECT COUNT(*) FROM crm.customer_merge WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND source_key = @s AND status = 'Pending';"))
        {
            pend.P("@p", SqlDbType.Int, propertyId);
            pend.P("@s", SqlDbType.VarChar, source, 80);
            if ((int)(await pend.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return null;
        }

        await using var ins = t.Command("""
            INSERT crm.customer_merge(property_id, source_key, target_key, source_name, target_name, reason, status, proposed_by, proposed_utc)
            OUTPUT INSERTED.merge_id VALUES (@p, @s, @t, @sn, @tn, @r, 'Pending', @by, @u);
            """);
        ins.P("@p", SqlDbType.Int, propertyId);
        ins.P("@s", SqlDbType.VarChar, source, 80);
        ins.P("@t", SqlDbType.VarChar, target, 80);
        ins.P("@sn", SqlDbType.NVarChar, sourceName, 200);
        ins.P("@tn", SqlDbType.NVarChar, targetName, 200);
        ins.P("@r", SqlDbType.NVarChar, reason, 300);
        ins.P("@by", SqlDbType.Int, principalId);
        ins.P("@u", SqlDbType.DateTime2, utc);
        return (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<CrmMerge?> LockMergeAsync(ICommandTransaction tx, int propertyId, long mergeId, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command($"SELECT {CrmSql.MergeColumns} FROM crm.customer_merge WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND merge_id = @m;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.BigInt, mergeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? CrmSql.ReadMerge(rd) : null;
    }

    public async Task RejectMergeAsync(ICommandTransaction tx, long mergeId, int principalId, DateTime utc, string? note, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("UPDATE crm.customer_merge SET status = 'Rejected', decided_by = @by, decided_utc = @u, decision_note = @n WHERE merge_id = @m;");
        cmd.P("@by", SqlDbType.Int, principalId);
        cmd.P("@u", SqlDbType.DateTime2, utc);
        cmd.P("@n", SqlDbType.NVarChar, note, 300);
        cmd.P("@m", SqlDbType.BigInt, mergeId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ApproveMergeAsync(ICommandTransaction tx, CrmMerge merge, int principalId, DateTime utc, string? note, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(merge);
        var t = CrmSql.Sql(tx);
        int pid;
        await using (var pc = t.Command("SELECT property_id FROM crm.customer_merge WHERE merge_id = @m;"))
        {
            pc.P("@m", SqlDbType.BigInt, merge.MergeId);
            pid = (int)(await pc.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await using (var chk = t.Command("SELECT COUNT(*) FROM crm.customer_alias WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND alias_key IN (@s, @t);"))
        {
            chk.P("@p", SqlDbType.Int, pid);
            chk.P("@s", SqlDbType.VarChar, merge.SourceKey, 80);
            chk.P("@t", SqlDbType.VarChar, merge.TargetKey, 80);
            if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return false;
        }

        // Preferences: keep the target's; a different source text is kept as a note on the target (history is never dropped).
        string? srcPref = null, tgtPref = null;
        await using (var pr = t.Command("SELECT customer_key, preferences FROM crm.customer_pref WITH (UPDLOCK) WHERE property_id = @p AND customer_key IN (@s, @t);"))
        {
            pr.P("@p", SqlDbType.Int, pid);
            pr.P("@s", SqlDbType.VarChar, merge.SourceKey, 80);
            pr.P("@t", SqlDbType.VarChar, merge.TargetKey, 80);
            await using var rd = await pr.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                if (rd.GetString(0) == merge.SourceKey) srcPref = rd.GetString(1); else tgtPref = rd.GetString(1);
        }

        await using var cmd = t.Command("""
            INSERT crm.customer_alias(property_id, alias_key, target_key, merge_id, created_utc) VALUES (@p, @s, @t, @m, @u);
            UPDATE crm.customer_alias SET target_key = @t WHERE property_id = @p AND target_key = @s;
            INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc)
              SELECT property_id, @t, tag, added_by, added_utc FROM crm.customer_tag x WHERE property_id = @p AND customer_key = @s
                 AND NOT EXISTS (SELECT 1 FROM crm.customer_tag y WHERE y.property_id = @p AND y.customer_key = @t AND y.tag = x.tag);
            DELETE crm.customer_tag WHERE property_id = @p AND customer_key = @s;
            UPDATE crm.customer_note SET customer_key = @t WHERE property_id = @p AND customer_key = @s;
            DELETE crm.customer_pref WHERE property_id = @p AND customer_key = @s;
            UPDATE crm.customer_merge SET status = 'Approved', decided_by = @by, decided_utc = @u, decision_note = @n WHERE merge_id = @m;
            """);
        cmd.P("@p", SqlDbType.Int, pid);
        cmd.P("@s", SqlDbType.VarChar, merge.SourceKey, 80);
        cmd.P("@t", SqlDbType.VarChar, merge.TargetKey, 80);
        cmd.P("@m", SqlDbType.BigInt, merge.MergeId);
        cmd.P("@u", SqlDbType.DateTime2, utc);
        cmd.P("@by", SqlDbType.Int, principalId);
        cmd.P("@n", SqlDbType.NVarChar, note, 300);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (srcPref is not null && tgtPref is null) await SetPrefAsync(tx, pid, merge.TargetKey, srcPref, principalId, utc, ct).ConfigureAwait(false);
        else if (srcPref is not null && srcPref != tgtPref) await AddNoteAsync(tx, pid, merge.TargetKey, Clip("Preferences before merge (" + merge.SourceName + "): " + srcPref), principalId, utc, ct).ConfigureAwait(false);
        return true;
    }

    private static string Clip(string s) => s.Length <= 500 ? s : s[..500];

    public async Task<bool> AddTagAsync(ICommandTransaction tx, int propertyId, string key, string tag, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("""
            IF EXISTS (SELECT 1 FROM crm.customer_tag WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND customer_key = @k AND tag = @g) SELECT 0;
            ELSE BEGIN INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc) VALUES (@p, @k, @g, @by, @u); SELECT 1; END
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        cmd.P("@g", SqlDbType.NVarChar, tag, 30);
        cmd.P("@by", SqlDbType.Int, principalId);
        cmd.P("@u", SqlDbType.DateTime2, utc);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    public async Task<bool> RemoveTagAsync(ICommandTransaction tx, int propertyId, string key, string tag, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("DELETE crm.customer_tag WHERE property_id = @p AND customer_key = @k AND tag = @g;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        cmd.P("@g", SqlDbType.NVarChar, tag, 30);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<int> TagCountAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("SELECT COUNT(*) FROM crm.customer_tag WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND customer_key = @k;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> AddNoteAsync(ICommandTransaction tx, int propertyId, string key, string body, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("INSERT crm.customer_note(property_id, customer_key, body, author_id, created_utc) OUTPUT INSERTED.note_id VALUES (@p, @k, @b, @by, @u);");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        cmd.P("@b", SqlDbType.NVarChar, body, 500);
        cmd.P("@by", SqlDbType.Int, principalId);
        cmd.P("@u", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<CrmPref?> LockPrefAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("SELECT preferences, row_ver FROM crm.customer_pref WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p AND customer_key = @k;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new CrmPref(rd.GetString(0), (byte[])rd[1]) : null;
    }

    public async Task SetPrefAsync(ICommandTransaction tx, int propertyId, string key, string text, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = CrmSql.Sql(tx).Command("""
            IF @t = N'' DELETE crm.customer_pref WHERE property_id = @p AND customer_key = @k;
            ELSE IF EXISTS (SELECT 1 FROM crm.customer_pref WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND customer_key = @k)
              UPDATE crm.customer_pref SET preferences = @t, updated_by = @by, updated_utc = @u WHERE property_id = @p AND customer_key = @k;
            ELSE INSERT crm.customer_pref(property_id, customer_key, preferences, updated_by, updated_utc) VALUES (@p, @k, @t, @by, @u);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.VarChar, key, 80);
        cmd.P("@t", SqlDbType.NVarChar, text, 500);
        cmd.P("@by", SqlDbType.Int, principalId);
        cmd.P("@u", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
