using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Compliance;
using Hcc.Domain.Compliance;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Compliance;

/// <summary>
/// SQL adapter of the compliance ports (migration 1100, schema cmp). Command side uses the command transaction (row locks UPDLOCK,
/// check-then-insert under the lock); read side opens its own connection.
/// </summary>
public sealed class SqlComplianceStore(SqlConnectionFactory connections) : IComplianceStore, IComplianceReadStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The compliance SQL store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static byte? Tiny(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetByte(i);

    // ── Business date ──

    private const string BusinessDateSql = "SELECT business_date FROM inv.property WHERE property_id = @p;";

    private static async Task<DateOnly> ReadBusinessDateAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is DateTime d ? DateOnly.FromDateTime(d) : throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(BusinessDateSql);
        return await ReadBusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(BusinessDateSql, conn);
        return await ReadBusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    // ── Reading obligations (rules attached) ──

    private const string ObligationCols = "o.obligation_id, o.property_id, o.kind, o.name, o.form, o.frequency, o.owner_role, o.from_period, o.active, o.note, o.row_ver";

    private static async Task<List<ObligationRecord>> ReadObligationsAsync(SqlCommand cmd, CancellationToken ct)
    {
        // Result 1: obligations; result 2: their rules.
        var heads = new List<(long Id, int Prop, string Kind, string Name, string Form, string Freq, string Owner, string From, bool Active, string? Note, byte[] Ver)>();
        var rules = new Dictionary<long, List<DatedRule>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetString(6), rd.GetString(7), rd.GetBoolean(8), Str(rd, 9), (byte[])rd[10]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            var type = rd.GetString(2) switch { "month-end" => DueRuleType.MonthEnd, "fixed" => DueRuleType.Fixed, _ => DueRuleType.Day };
            var special = Tiny(rd, 5) is { } sm && Tiny(rd, 6) is { } sd ? new SpecialDate(sm, sd) : null;
            if (!rules.TryGetValue(id, out var list)) rules[id] = list = [];
            list.Add(new DatedRule(Date(rd, 1), new DueRule(type, Tiny(rd, 3) ?? 0, Tiny(rd, 4) ?? 0, special)));
        }

        return heads.Select(h => new ObligationRecord(h.Id, h.Prop, h.Kind, h.Name, h.Form, h.Freq, h.Owner, h.From, h.Active, h.Note, h.Ver,
            rules.TryGetValue(h.Id, out var r) ? r.OrderBy(x => x.From).ToList() : [])).ToList();
    }

    private const string RulesSql = "SELECT r.obligation_id, r.effective_from, r.rule_type, r.due_day, r.due_month, r.special_month, r.special_day";

    public async Task<ObligationRecord?> LockObligationAsync(ICommandTransaction tx, int propertyId, long obligationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"""
            SELECT {ObligationCols} FROM cmp.obligation o WITH (UPDLOCK, ROWLOCK) WHERE o.obligation_id = @id AND o.property_id = @p;
            {RulesSql} FROM cmp.obligation_rule r WHERE r.obligation_id = @id;
            """);
        Add(cmd, "@id", SqlDbType.BigInt, obligationId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return (await ReadObligationsAsync(cmd, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    private static async Task<ComplianceSnapshot> ReadSnapshotAsync(Func<string, SqlCommand> make, int propertyId, CancellationToken ct)
    {
        List<ObligationRecord> obligations;
        await using (var cmd = make($"""
            SELECT {ObligationCols} FROM cmp.obligation o WHERE o.property_id = @p ORDER BY o.obligation_id;
            {RulesSql} FROM cmp.obligation_rule r JOIN cmp.obligation o ON o.obligation_id = r.obligation_id WHERE o.property_id = @p;
            """))
        {
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            obligations = await ReadObligationsAsync(cmd, ct).ConfigureAwait(false);
        }

        var filings = new List<FilingRecord>();
        await using (var cmd = make("""
            SELECT f.obligation_id, f.period, f.status, f.filed_by, f.filed_utc, f.reference, f.reason
            FROM cmp.filing f JOIN cmp.obligation o ON o.obligation_id = f.obligation_id WHERE o.property_id = @p;
            """))
        {
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) filings.Add(ReadFiling(rd));
        }

        SettingsRecord? settings = null;
        await using (var cmd = make("SELECT ca_name, ca_email, due_soon_days, row_ver, updated_by FROM cmp.setting WHERE property_id = @p;"))
        {
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await rd.ReadAsync(ct).ConfigureAwait(false)) settings = new SettingsRecord(rd.GetString(0), rd.GetString(1), rd.GetByte(2), (byte[])rd[3], rd.IsDBNull(4));
        }

        return new ComplianceSnapshot(obligations, filings, settings ?? DefaultSettings());
    }

    private static SettingsRecord DefaultSettings() => new(ComplianceRules.DefaultCaName, ComplianceRules.DefaultCaEmail, ComplianceRules.DefaultDueSoonDays, new byte[8], true);

    private static FilingRecord ReadFiling(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetDateTime(4), Str(rd, 5), Str(rd, 6));

    public async Task<ComplianceSnapshot> SnapshotAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        var sql = Sql(tx);
        return await ReadSnapshotAsync(sql.Command, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<ComplianceSnapshot> SnapshotAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await ReadSnapshotAsync(s => connections.Command(s, conn), propertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackRecord>> PacksAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT TOP ({ComplianceRules.MaxPacks}) pack_id, created_utc, created_by, to_email, to_name, overdue_count, due_count, filed_count, na_count, lines_json
            FROM cmp.ca_pack WHERE property_id = @p ORDER BY pack_id DESC;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        var packs = new List<PackRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            packs.Add(new PackRecord(rd.GetInt64(0), rd.GetDateTime(1), rd.GetInt32(2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7), rd.GetInt32(8),
                JsonSerializer.Deserialize<List<string>>(rd.GetString(9)) ?? []));
        return packs;
    }

    // ── Filings ──

    public async Task<FilingRecord?> GetFilingAsync(ICommandTransaction tx, long obligationId, string period, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT obligation_id, period, status, filed_by, filed_utc, reference, reason FROM cmp.filing WHERE obligation_id = @o AND period = @period;");
        Add(cmd, "@o", SqlDbType.BigInt, obligationId);
        Add(cmd, "@period", SqlDbType.VarChar, period, 7);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadFiling(rd) : null;
    }

    public async Task InsertFilingAsync(ICommandTransaction tx, FilingRecord filing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filing);
        await using var cmd = Sql(tx).Command("INSERT cmp.filing(obligation_id, period, status, filed_by, filed_utc, reference, reason) VALUES (@o, @period, @status, @by, @utc, @ref, @reason);");
        Add(cmd, "@o", SqlDbType.BigInt, filing.ObligationId);
        Add(cmd, "@period", SqlDbType.VarChar, filing.Period, 7);
        Add(cmd, "@status", SqlDbType.VarChar, filing.Status, 14);
        Add(cmd, "@by", SqlDbType.Int, filing.By);
        Add(cmd, "@utc", SqlDbType.DateTime2, filing.AtUtc);
        Add(cmd, "@ref", SqlDbType.NVarChar, filing.Reference, 40);
        Add(cmd, "@reason", SqlDbType.NVarChar, filing.Reason, 120);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteFilingAsync(ICommandTransaction tx, long obligationId, string period, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("DELETE cmp.filing WHERE obligation_id = @o AND period = @period;");
        Add(cmd, "@o", SqlDbType.BigInt, obligationId);
        Add(cmd, "@period", SqlDbType.VarChar, period, 7);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertReopenedAsync(ICommandTransaction tx, int propertyId, FilingRecord filing, int reopenedBy, DateTime reopenedUtc, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filing);
        await using var cmd = Sql(tx).Command($"""
            INSERT cmp.filing_reopened(property_id, obligation_id, period, status, filed_by, filed_utc, reference, reason, reopened_by, reopened_utc, reopen_reason)
            VALUES (@p, @o, @period, @status, @by, @utc, @ref, @reason, @rby, @rutc, @rreason);
            DELETE FROM cmp.filing_reopened WHERE property_id = @p AND reopen_id NOT IN
              (SELECT TOP ({ComplianceRules.MaxReopened}) reopen_id FROM cmp.filing_reopened WHERE property_id = @p ORDER BY reopen_id DESC);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@o", SqlDbType.BigInt, filing.ObligationId);
        Add(cmd, "@period", SqlDbType.VarChar, filing.Period, 7);
        Add(cmd, "@status", SqlDbType.VarChar, filing.Status, 14);
        Add(cmd, "@by", SqlDbType.Int, filing.By);
        Add(cmd, "@utc", SqlDbType.DateTime2, filing.AtUtc);
        Add(cmd, "@ref", SqlDbType.NVarChar, filing.Reference, 40);
        Add(cmd, "@reason", SqlDbType.NVarChar, filing.Reason, 120);
        Add(cmd, "@rby", SqlDbType.Int, reopenedBy);
        Add(cmd, "@rutc", SqlDbType.DateTime2, reopenedUtc);
        Add(cmd, "@rreason", SqlDbType.NVarChar, reason, 120);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Obligations ──

    public async Task<bool> NameExistsAsync(ICommandTransaction tx, int propertyId, string name, long? excludeId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT TOP 1 1 FROM cmp.obligation WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND name = @n AND (@x IS NULL OR obligation_id <> @x);");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@n", SqlDbType.NVarChar, name, 60);
        Add(cmd, "@x", SqlDbType.BigInt, excludeId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<int> CountObligationsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM cmp.obligation WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<long> InsertObligationAsync(ICommandTransaction tx, int propertyId, string kind, string name, string form, string frequency, string owner,
        string fromPeriod, string? note, DueRule firstRule, int by, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(firstRule);
        var sql = Sql(tx);
        long id;
        await using (var cmd = sql.Command("""
            INSERT cmp.obligation(property_id, kind, name, form, frequency, owner_role, from_period, active, note, updated_by, updated_utc)
            OUTPUT INSERTED.obligation_id
            VALUES (@p, @kind, @name, @form, @freq, @owner, @from, 1, @note, @by, @utc);
            """))
        {
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            Add(cmd, "@kind", SqlDbType.VarChar, kind, 10);
            Add(cmd, "@name", SqlDbType.NVarChar, name, 60);
            Add(cmd, "@form", SqlDbType.NVarChar, form, 60);
            Add(cmd, "@freq", SqlDbType.VarChar, frequency, 7);
            Add(cmd, "@owner", SqlDbType.NVarChar, owner, 30);
            Add(cmd, "@from", SqlDbType.VarChar, fromPeriod, 7);
            Add(cmd, "@note", SqlDbType.NVarChar, note, 300);
            Add(cmd, "@by", SqlDbType.Int, by);
            Add(cmd, "@utc", SqlDbType.DateTime2, utc);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await UpsertRuleAsync(tx, id, new DatedRule(ComplianceRules.RuleOrigin, firstRule), ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateObligationAsync(ICommandTransaction tx, long obligationId, byte[] expectedVersion, string name, string form, string owner, string? note, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE cmp.obligation SET name = @name, form = @form, owner_role = @owner, note = @note, updated_by = @by, updated_utc = @utc
            WHERE obligation_id = @id AND row_ver = @v;
            """);
        Add(cmd, "@id", SqlDbType.BigInt, obligationId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        Add(cmd, "@name", SqlDbType.NVarChar, name, 60);
        Add(cmd, "@form", SqlDbType.NVarChar, form, 60);
        Add(cmd, "@owner", SqlDbType.NVarChar, owner, 30);
        Add(cmd, "@note", SqlDbType.NVarChar, note, 300);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This obligation");
    }

    public async Task UpsertRuleAsync(ICommandTransaction tx, long obligationId, DatedRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var r = rule.Rule;
        await using var cmd = Sql(tx).Command("""
            DELETE cmp.obligation_rule WHERE obligation_id = @o AND effective_from = @from;
            INSERT cmp.obligation_rule(obligation_id, effective_from, rule_type, due_day, due_month, special_month, special_day)
            VALUES (@o, @from, @type, @day, @month, @sm, @sd);
            """);
        Add(cmd, "@o", SqlDbType.BigInt, obligationId);
        Add(cmd, "@from", SqlDbType.Date, rule.From.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@type", SqlDbType.VarChar, r.Type switch { DueRuleType.MonthEnd => "month-end", DueRuleType.Fixed => "fixed", _ => "day" }, 9);
        Add(cmd, "@day", SqlDbType.TinyInt, r.Type == DueRuleType.MonthEnd ? null : (byte)r.Day);
        Add(cmd, "@month", SqlDbType.TinyInt, r.Type == DueRuleType.Fixed ? (byte)r.Month : null);
        Add(cmd, "@sm", SqlDbType.TinyInt, r.Special is null ? null : (byte)r.Special.Month);
        Add(cmd, "@sd", SqlDbType.TinyInt, r.Special is null ? null : (byte)r.Special.Day);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetActiveAsync(ICommandTransaction tx, long obligationId, byte[] expectedVersion, bool active, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE cmp.obligation SET active = @a, updated_by = @by, updated_utc = @utc WHERE obligation_id = @id AND row_ver = @v;");
        Add(cmd, "@id", SqlDbType.BigInt, obligationId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        Add(cmd, "@a", SqlDbType.Bit, active);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This obligation");
    }

    public async Task<byte[]> ObligationVersionAsync(ICommandTransaction tx, long obligationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT row_ver FROM cmp.obligation WHERE obligation_id = @id;");
        Add(cmd, "@id", SqlDbType.BigInt, obligationId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    // ── Settings ──

    public async Task<SettingsRecord> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            MERGE cmp.setting WITH (HOLDLOCK) AS t USING (SELECT @p AS property_id) AS s ON t.property_id = s.property_id
            WHEN NOT MATCHED THEN INSERT(property_id, ca_name, ca_email, due_soon_days) VALUES (@p, @name, @email, @days);
            SELECT ca_name, ca_email, due_soon_days, row_ver, updated_by FROM cmp.setting WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p;
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@name", SqlDbType.NVarChar, ComplianceRules.DefaultCaName, 60);
        Add(cmd, "@email", SqlDbType.NVarChar, ComplianceRules.DefaultCaEmail, 80);
        Add(cmd, "@days", SqlDbType.TinyInt, (byte)ComplianceRules.DefaultDueSoonDays);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new SettingsRecord(rd.GetString(0), rd.GetString(1), rd.GetByte(2), (byte[])rd[3], rd.IsDBNull(4));
    }

    public async Task UpdateSettingsAsync(ICommandTransaction tx, int propertyId, byte[] expectedVersion, string caName, string caEmail, int dueSoonDays, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE cmp.setting SET ca_name = @name, ca_email = @email, due_soon_days = @days, updated_by = @by, updated_utc = @utc
            WHERE property_id = @p AND row_ver = @v;
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        Add(cmd, "@name", SqlDbType.NVarChar, caName, 60);
        Add(cmd, "@email", SqlDbType.NVarChar, caEmail, 80);
        Add(cmd, "@days", SqlDbType.TinyInt, (byte)dueSoonDays);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The compliance settings");
    }

    // ── CA pack ──

    public async Task<long> InsertPackAsync(ICommandTransaction tx, int propertyId, PackRecord pack, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        await using var cmd = Sql(tx).Command($"""
            INSERT cmp.ca_pack(property_id, created_utc, created_by, to_email, to_name, overdue_count, due_count, filed_count, na_count, lines_json)
            OUTPUT INSERTED.pack_id VALUES (@p, @utc, @by, @email, @name, @o, @d, @f, @n, @lines);
            DELETE FROM cmp.ca_pack WHERE property_id = @p AND pack_id NOT IN
              (SELECT TOP ({ComplianceRules.MaxPacks}) pack_id FROM cmp.ca_pack WHERE property_id = @p ORDER BY pack_id DESC);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@utc", SqlDbType.DateTime2, pack.AtUtc);
        Add(cmd, "@by", SqlDbType.Int, pack.By);
        Add(cmd, "@email", SqlDbType.NVarChar, pack.To, 80);
        Add(cmd, "@name", SqlDbType.NVarChar, pack.ToName, 60);
        Add(cmd, "@o", SqlDbType.Int, pack.Overdue);
        Add(cmd, "@d", SqlDbType.Int, pack.Due);
        Add(cmd, "@f", SqlDbType.Int, pack.Filed);
        Add(cmd, "@n", SqlDbType.Int, pack.NotApplicable);
        Add(cmd, "@lines", SqlDbType.NVarChar, JsonSerializer.Serialize(pack.Lines), -1);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
