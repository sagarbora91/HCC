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
/// SQL adapter of the compliance ports = SCC's tax module in full (migrations 1100 + 4130 / 4131, schema cmp). Command side uses the command
/// transaction (the settings row is the per-property lock, taken with UPDLOCK); read side opens its own connection. The ≈₹ payable feed reads
/// the locked pay runs (hr.pay_run / pay_run_line) and the issued invoices and credit notes (ivc.*).
/// </summary>
public sealed class SqlComplianceStore(SqlConnectionFactory connections, Hcc.Application.Documents.IDocumentObjectStore? objects = null) : IComplianceStore, IComplianceReadStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The compliance SQL store needs the SQL command transaction.");

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size != 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateOnly? DateN(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));

    private static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static int? IntN(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetInt32(i);

    private static DateTime? Utc(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetDateTime(i);

    private static object? Day(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    private static IReadOnlyList<string> Json(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

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

    // ── Snapshot (SCC STATE of the property's firm) ──

    private static readonly string SnapshotSql = $"""
        SELECT firm_name, pan, gstin, entity, gst_type, has_employees, qrmp, notes, updated_by, updated_utc, row_ver, fssai_no, fssai_expiry, lodging_no, lodging_expiry
          FROM cmp.profile WHERE property_id = @p;
        SELECT ca_name, ca_email, due_soon_days, fy_start, row_ver, updated_by, fy_chosen_in FROM cmp.setting WHERE property_id = @p;
        SELECT fy_start, item_id, done, na, filed_on, notes, evidence_ref, reviewer_note, due_date, done_by, done_utc, na_by, na_utc, legacy
          FROM cmp.record WHERE property_id = @p ORDER BY fy_start, item_id;
        SELECT custom_id, cat, name, freq, due_day, months_json, form, who, what, why, penalty, responsible, reviewer, risk, buffer_days, checklist_json, workflow_json,
          evidence_json, updated_by, updated_utc, row_ver FROM cmp.custom_obligation WHERE property_id = @p AND deleted_utc IS NULL ORDER BY custom_id;
        SELECT archive_key FROM cmp.archive WHERE property_id = @p ORDER BY archived_utc, archive_key;
        SELECT cat FROM cmp.archive_category WHERE property_id = @p ORDER BY archived_utc, cat;
        SELECT evidence_id, fy_start, item_id, file_name, content_type, size_bytes, added_by, added_utc FROM cmp.evidence WHERE property_id = @p ORDER BY evidence_id;
        SELECT TOP ({ComplianceRules.MaxReopened}) fy_start, item_id, part, was_done, was_na, filed_on, notes, evidence_ref, reviewer_note, due_date, recorded_by, recorded_utc,
          reopened_by, reopened_utc, reopen_reason FROM cmp.record_reopened WHERE property_id = @p ORDER BY reopen_id DESC;
        """;

    private static async Task<ComplianceSnapshot> ReadSnapshotAsync(SqlCommand cmd, int propertyId, CancellationToken ct)
    {
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        ProfileRecord? profile = null;
        if (await rd.ReadAsync(ct).ConfigureAwait(false))
            profile = new ProfileRecord(new CmpProfile(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetBoolean(5), rd.GetBoolean(6), rd.GetString(7),
                Str(rd, 11), DateN(rd, 12), Str(rd, 13), DateN(rd, 14)), IntN(rd, 8), Utc(rd, 9), (byte[])rd[10]);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var settings = DefaultSettings();
        if (await rd.ReadAsync(ct).ConfigureAwait(false)) settings = ReadSettings(rd);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var records = new List<CmpRecord>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            records.Add(new CmpRecord(rd.GetInt16(0), rd.GetString(1), rd.GetBoolean(2), rd.GetBoolean(3), DateN(rd, 4), Str(rd, 5), Str(rd, 6), Str(rd, 7), DateN(rd, 8),
                IntN(rd, 9), Utc(rd, 10), IntN(rd, 11), Utc(rd, 12), Str(rd, 13)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var custom = new List<CustomRecord>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            custom.Add(new CustomRecord(new CmpCustom(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetByte(4), Json(rd.GetString(5)), rd.GetString(6), rd.GetString(7),
                rd.GetString(8), rd.GetString(9), rd.GetString(10), rd.GetString(11), rd.GetString(12), Str(rd, 13), rd.IsDBNull(14) ? null : rd.GetByte(14), Json(rd.GetString(15)),
                Json(rd.GetString(16)), Json(rd.GetString(17))), IntN(rd, 18), Utc(rd, 19), (byte[])rd[20]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var archived = new List<string>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) archived.Add(rd.GetString(0));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var cats = new List<string>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) cats.Add(rd.GetString(0));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var evidence = new List<CmpEvidence>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) evidence.Add(ReadEvidence(rd));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var reopened = new List<ReopenedRecord>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var fy = rd.GetInt16(0);
            var item = rd.GetString(1);
            var wasDone = rd.GetBoolean(3);
            var wasNa = rd.GetBoolean(4);
            var by = IntN(rd, 10);
            var at = Utc(rd, 11);
            var was = new CmpRecord(fy, item, wasDone, wasNa, DateN(rd, 5), Str(rd, 6), Str(rd, 7), Str(rd, 8), DateN(rd, 9), wasDone ? by : null, wasDone ? at : null,
                wasNa && !wasDone ? by : null, wasNa && !wasDone ? at : null, null);
            reopened.Add(new ReopenedRecord(fy, item, rd.GetString(2), was, rd.GetInt32(12), rd.GetDateTime(13), rd.GetString(14)));
        }

        return new ComplianceSnapshot(profile, settings, records, custom, archived, cats, evidence, reopened);
    }

    private static CmpEvidence ReadEvidence(SqlDataReader rd) =>
        new(rd.GetInt64(0), rd.GetInt16(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetDateTime(7));

    private static SettingsRecord DefaultSettings() =>
        new(ComplianceRules.DefaultCaName, ComplianceRules.DefaultCaEmail, ComplianceRules.DefaultDueSoonDays, SccTaxRules.FyMin, new byte[8], true);

    public async Task<ComplianceSnapshot> SnapshotAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command(SnapshotSql);
        return await ReadSnapshotAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<ComplianceSnapshot> SnapshotAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(SnapshotSql, conn);
        return await ReadSnapshotAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    // ── Settings (the per-property lock) ──

    public async Task<SettingsRecord> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            MERGE cmp.setting WITH (HOLDLOCK) AS t USING (SELECT @p AS property_id) AS s ON t.property_id = s.property_id
            WHEN NOT MATCHED THEN INSERT(property_id, ca_name, ca_email, due_soon_days) VALUES (@p, @name, @email, @days);
            SELECT ca_name, ca_email, due_soon_days, fy_start, row_ver, updated_by, fy_chosen_in FROM cmp.setting WITH (UPDLOCK, ROWLOCK) WHERE property_id = @p;
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@name", SqlDbType.NVarChar, ComplianceRules.DefaultCaName, 60);
        Add(cmd, "@email", SqlDbType.NVarChar, ComplianceRules.DefaultCaEmail, 80);
        Add(cmd, "@days", SqlDbType.TinyInt, (byte)ComplianceRules.DefaultDueSoonDays);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return ReadSettings(rd);
    }

    /// <summary>ca_name, ca_email, due_soon_days, fy_start, row_ver, updated_by, fy_chosen_in (DEC-125).</summary>
    private static SettingsRecord ReadSettings(SqlDataReader rd) =>
        new(rd.GetString(0), rd.GetString(1), rd.GetByte(2), rd.GetInt16(3), (byte[])rd[4], rd.IsDBNull(5), rd.IsDBNull(6) ? null : rd.GetInt16(6));

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

    public async Task SetFyAsync(ICommandTransaction tx, int propertyId, int fyStart, int chosenIn, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE cmp.setting SET fy_start = @fy, fy_chosen_in = @chosen, updated_by = @by, updated_utc = @utc WHERE property_id = @p;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)fyStart);
        Add(cmd, "@chosen", SqlDbType.SmallInt, (short)chosenIn);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Profile ──

    public async Task<byte[]> SaveProfileAsync(ICommandTransaction tx, int propertyId, byte[]? expectedVersion, CmpProfile profile, int by, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var cmd = Sql(tx).Command(expectedVersion is null
            ? """
              INSERT cmp.profile(property_id, firm_name, pan, gstin, entity, gst_type, has_employees, qrmp, notes, fssai_no, fssai_expiry, lodging_no, lodging_expiry, updated_by, updated_utc)
              OUTPUT INSERTED.row_ver VALUES (@p, @name, @pan, @gstin, @entity, @type, @emp, @qrmp, @notes, @fno, @fexp, @lno, @lexp, @by, @utc);
              """
            : """
              UPDATE cmp.profile SET firm_name = @name, pan = @pan, gstin = @gstin, entity = @entity, gst_type = @type, has_employees = @emp, qrmp = @qrmp, notes = @notes,
                fssai_no = @fno, fssai_expiry = @fexp, lodging_no = @lno, lodging_expiry = @lexp, updated_by = @by, updated_utc = @utc
              OUTPUT INSERTED.row_ver WHERE property_id = @p AND row_ver = @v;
              """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        Add(cmd, "@name", SqlDbType.NVarChar, profile.Name, ComplianceRules.MaxFirmName);
        Add(cmd, "@pan", SqlDbType.NVarChar, profile.Pan, ComplianceRules.MaxPan);
        Add(cmd, "@gstin", SqlDbType.NVarChar, profile.Gstin, ComplianceRules.MaxGstin);
        Add(cmd, "@entity", SqlDbType.VarChar, profile.Entity, 14);
        Add(cmd, "@type", SqlDbType.VarChar, profile.GstType, 12);
        Add(cmd, "@emp", SqlDbType.Bit, profile.HasEmployees);
        Add(cmd, "@qrmp", SqlDbType.Bit, profile.Qrmp);
        Add(cmd, "@notes", SqlDbType.NVarChar, profile.Notes, ComplianceRules.MaxNotes);
        Add(cmd, "@fno", SqlDbType.NVarChar, profile.FssaiNo, ComplianceRules.MaxLicenceNo);
        Add(cmd, "@fexp", SqlDbType.Date, Day(profile.FssaiExpiry));
        Add(cmd, "@lno", SqlDbType.NVarChar, profile.LodgingNo, ComplianceRules.MaxLicenceNo);
        Add(cmd, "@lexp", SqlDbType.Date, Day(profile.LodgingExpiry));
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (v is not byte[] ver) { SqlValues.EnsureVersionMatched(0, "The compliance firm profile"); return []; }
        return ver;
    }

    // ── Records ──

    public async Task UpsertRecordAsync(ICommandTransaction tx, int propertyId, CmpRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var cmd = Sql(tx).Command("""
            MERGE cmp.record WITH (HOLDLOCK) AS t
            USING (SELECT @p AS property_id, @fy AS fy_start, @item AS item_id) AS s
              ON t.property_id = s.property_id AND t.fy_start = s.fy_start AND t.item_id = s.item_id
            WHEN MATCHED THEN UPDATE SET done = @done, na = @na, filed_on = @filed, notes = @notes, evidence_ref = @ev, reviewer_note = @rev, due_date = @due,
              done_by = @dby, done_utc = @dutc, na_by = @nby, na_utc = @nutc, legacy = @legacy
            WHEN NOT MATCHED THEN INSERT(property_id, fy_start, item_id, done, na, filed_on, notes, evidence_ref, reviewer_note, due_date, done_by, done_utc, na_by, na_utc, legacy)
              VALUES (@p, @fy, @item, @done, @na, @filed, @notes, @ev, @rev, @due, @dby, @dutc, @nby, @nutc, @legacy);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)record.FyStart);
        Add(cmd, "@item", SqlDbType.VarChar, record.ItemId, 48);
        Add(cmd, "@done", SqlDbType.Bit, record.Done);
        Add(cmd, "@na", SqlDbType.Bit, record.Na);
        Add(cmd, "@filed", SqlDbType.Date, Day(record.FiledOn));
        Add(cmd, "@notes", SqlDbType.NVarChar, record.Notes, ComplianceRules.MaxNotes);
        Add(cmd, "@ev", SqlDbType.NVarChar, record.EvidenceRef, ComplianceRules.MaxEvidenceRef);
        Add(cmd, "@rev", SqlDbType.NVarChar, record.ReviewerNote, ComplianceRules.MaxReviewer);
        Add(cmd, "@due", SqlDbType.Date, Day(record.DueDate));
        Add(cmd, "@dby", SqlDbType.Int, record.DoneBy);
        Add(cmd, "@dutc", SqlDbType.DateTime2, record.DoneUtc);
        Add(cmd, "@nby", SqlDbType.Int, record.NaBy);
        Add(cmd, "@nutc", SqlDbType.DateTime2, record.NaUtc);
        Add(cmd, "@legacy", SqlDbType.VarChar, record.Legacy, 40);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteRecordAsync(ICommandTransaction tx, int propertyId, int fyStart, string itemId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("DELETE cmp.record WHERE property_id = @p AND fy_start = @fy AND item_id = @item;");
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)fyStart);
        Add(cmd, "@item", SqlDbType.VarChar, itemId, 48);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertReopenedAsync(ICommandTransaction tx, int propertyId, ReopenedRecord reopened, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reopened);
        var w = reopened.Was;
        await using var cmd = Sql(tx).Command($"""
            INSERT cmp.record_reopened(property_id, fy_start, item_id, part, was_done, was_na, filed_on, notes, evidence_ref, reviewer_note, due_date, recorded_by, recorded_utc,
              reopened_by, reopened_utc, reopen_reason)
            VALUES (@p, @fy, @item, @part, @wd, @wn, @filed, @notes, @ev, @rev, @due, @rby, @rutc, @by, @utc, @reason);
            DELETE FROM cmp.record_reopened WHERE property_id = @p AND reopen_id NOT IN
              (SELECT TOP ({ComplianceRules.MaxReopened}) reopen_id FROM cmp.record_reopened WHERE property_id = @p ORDER BY reopen_id DESC);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)reopened.FyStart);
        Add(cmd, "@item", SqlDbType.VarChar, reopened.ItemId, 48);
        Add(cmd, "@part", SqlDbType.VarChar, reopened.Part, 4);
        Add(cmd, "@wd", SqlDbType.Bit, w.Done);
        Add(cmd, "@wn", SqlDbType.Bit, w.Na);
        Add(cmd, "@filed", SqlDbType.Date, Day(w.FiledOn));
        Add(cmd, "@notes", SqlDbType.NVarChar, w.Notes, ComplianceRules.MaxNotes);
        Add(cmd, "@ev", SqlDbType.NVarChar, w.EvidenceRef, ComplianceRules.MaxEvidenceRef);
        Add(cmd, "@rev", SqlDbType.NVarChar, w.ReviewerNote, ComplianceRules.MaxReviewer);
        Add(cmd, "@due", SqlDbType.Date, Day(w.DueDate));
        Add(cmd, "@rby", SqlDbType.Int, reopened.Part == "na" ? w.NaBy : w.DoneBy);
        Add(cmd, "@rutc", SqlDbType.DateTime2, reopened.Part == "na" ? w.NaUtc : w.DoneUtc);
        Add(cmd, "@by", SqlDbType.Int, reopened.ReopenedBy);
        Add(cmd, "@utc", SqlDbType.DateTime2, reopened.ReopenedUtc);
        Add(cmd, "@reason", SqlDbType.NVarChar, reopened.Reason, 120);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Archive ──

    private static async Task<bool> ToggleAsync(SqlCommandTransaction sql, string table, string column, SqlDbType type, int size, int propertyId, string value, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = sql.Command($"""
            IF EXISTS (SELECT 1 FROM cmp.{table} WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND {column} = @k)
              BEGIN DELETE cmp.{table} WHERE property_id = @p AND {column} = @k; SELECT CAST(0 AS BIT); END
            ELSE
              BEGIN INSERT cmp.{table}(property_id, {column}, archived_by, archived_utc) VALUES (@p, @k, @by, @utc); SELECT CAST(1 AS BIT); END
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@k", type, value, size);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        return (bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public Task<bool> ToggleArchiveAsync(ICommandTransaction tx, int propertyId, string key, int by, DateTime utc, CancellationToken ct = default) =>
        ToggleAsync(Sql(tx), "archive", "archive_key", SqlDbType.NVarChar, 200, propertyId, key, by, utc, ct);

    public Task<bool> ToggleArchiveCategoryAsync(ICommandTransaction tx, int propertyId, string cat, int by, DateTime utc, CancellationToken ct = default) =>
        ToggleAsync(Sql(tx), "archive_category", "cat", SqlDbType.VarChar, 4, propertyId, cat, by, utc, ct);

    // ── Custom obligations ──

    private static void CustomParams(SqlCommand cmd, CmpCustom c)
    {
        Add(cmd, "@cat", SqlDbType.VarChar, c.Cat, 4);
        Add(cmd, "@name", SqlDbType.NVarChar, c.Name, ComplianceRules.MaxName);
        Add(cmd, "@freq", SqlDbType.VarChar, c.Freq, 10);
        Add(cmd, "@due", SqlDbType.TinyInt, (byte)c.DueDay);
        Add(cmd, "@months", SqlDbType.NVarChar, JsonSerializer.Serialize(c.Months), 400);
        Add(cmd, "@form", SqlDbType.NVarChar, c.Form, ComplianceRules.MaxShort);
        Add(cmd, "@who", SqlDbType.NVarChar, c.Who, ComplianceRules.MaxText);
        Add(cmd, "@what", SqlDbType.NVarChar, c.What, ComplianceRules.MaxText);
        Add(cmd, "@why", SqlDbType.NVarChar, c.Why, ComplianceRules.MaxText);
        Add(cmd, "@penalty", SqlDbType.NVarChar, c.Penalty, ComplianceRules.MaxText);
        Add(cmd, "@resp", SqlDbType.NVarChar, c.Responsible, ComplianceRules.MaxShort);
        Add(cmd, "@rev", SqlDbType.NVarChar, c.Reviewer, ComplianceRules.MaxShort);
        Add(cmd, "@risk", SqlDbType.VarChar, c.Risk, 6);
        Add(cmd, "@buffer", SqlDbType.TinyInt, c.Buffer is { } b ? (byte)b : null);
        Add(cmd, "@check", SqlDbType.NVarChar, JsonSerializer.Serialize(c.Checklist), -1);
        Add(cmd, "@flow", SqlDbType.NVarChar, JsonSerializer.Serialize(c.Workflow), -1);
        Add(cmd, "@evid", SqlDbType.NVarChar, JsonSerializer.Serialize(c.Evidence), -1);
    }

    public async Task<(long CustomId, byte[] Version)> InsertCustomAsync(ICommandTransaction tx, int propertyId, CmpCustom custom, int by, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(custom);
        await using var cmd = Sql(tx).Command("""
            INSERT cmp.custom_obligation(property_id, cat, name, freq, due_day, months_json, form, who, what, why, penalty, responsible, reviewer, risk, buffer_days,
              checklist_json, workflow_json, evidence_json, updated_by, updated_utc)
            OUTPUT INSERTED.custom_id, INSERTED.row_ver
            VALUES (@p, @cat, @name, @freq, @due, @months, @form, @who, @what, @why, @penalty, @resp, @rev, @risk, @buffer, @check, @flow, @evid, @by, @utc);
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        CustomParams(cmd, custom);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetInt64(0), (byte[])rd[1]);
    }

    public async Task<byte[]> UpdateCustomAsync(ICommandTransaction tx, int propertyId, CmpCustom custom, byte[] expectedVersion, int by, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(custom);
        await using var cmd = Sql(tx).Command("""
            UPDATE cmp.custom_obligation SET cat = @cat, name = @name, freq = @freq, due_day = @due, months_json = @months, form = @form, who = @who, what = @what, why = @why,
              penalty = @penalty, responsible = @resp, reviewer = @rev, risk = @risk, buffer_days = @buffer, checklist_json = @check, workflow_json = @flow, evidence_json = @evid,
              updated_by = @by, updated_utc = @utc
            OUTPUT INSERTED.row_ver
            WHERE custom_id = @id AND property_id = @p AND row_ver = @v AND deleted_utc IS NULL;
            """);
        Add(cmd, "@id", SqlDbType.BigInt, custom.CustomId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@v", SqlDbType.Binary, expectedVersion, 8);
        CustomParams(cmd, custom);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (v is not byte[] ver) { SqlValues.EnsureVersionMatched(0, "This custom obligation"); return []; }
        return ver;
    }

    public async Task<int> DeleteCustomAsync(ICommandTransaction tx, int propertyId, long customId, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE cmp.custom_obligation SET deleted_by = @by, deleted_utc = @utc WHERE custom_id = @id AND property_id = @p AND deleted_utc IS NULL;
            INSERT plat.outbox(event_type, aggregate_id, payload, property_id)
            SELECT 'DocumentObjectReleased', e.evidence_id, CONCAT('{"objectRef":"', c.object_ref, '","kind":"compliance-evidence"}'), @p
              FROM cmp.evidence e JOIN cmp.evidence_content c ON c.evidence_id = e.evidence_id
             WHERE e.property_id = @p AND e.item_id LIKE @prefix AND c.object_ref IS NOT NULL;   -- LF-docdb: released in HccDocuments by the consumer
            DELETE cmp.evidence WHERE property_id = @p AND item_id LIKE @prefix;
            SELECT @@ROWCOUNT;
            """);
        Add(cmd, "@id", SqlDbType.BigInt, customId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        Add(cmd, "@prefix", SqlDbType.VarChar, "cust-" + customId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-%", 48);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── Evidence ──

    public async Task<long> InsertEvidenceAsync(ICommandTransaction tx, int propertyId, int fyStart, string itemId, string fileName, string contentType, byte[] content, int by, DateTime utc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        // LF-docdb (contract §1.8): the object first, in its own committed transaction; this Hcc transaction stores the ref (owner id is informational).
        Guid? objectRef = objects is null ? null : await objects.PutAsync(new Hcc.Application.Documents.NewDocumentObject(propertyId, Hcc.Application.Documents.DocumentObjectKind.ComplianceEvidence,
            "cmp.evidence", 0, contentType.Length > 30 ? contentType[..30] : contentType, content, null, by), ct).ConfigureAwait(false);
        await using var cmd = Sql(tx).Command("""
            DECLARE @id TABLE(evidence_id BIGINT);
            INSERT cmp.evidence(property_id, fy_start, item_id, file_name, content_type, size_bytes, added_by, added_utc)
            OUTPUT INSERTED.evidence_id INTO @id VALUES (@p, @fy, @item, @name, @type, @size, @by, @utc);
            INSERT cmp.evidence_content(evidence_id, content, object_ref) SELECT evidence_id, @content, @ref FROM @id;
            SELECT evidence_id FROM @id;
            """);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)fyStart);
        Add(cmd, "@item", SqlDbType.VarChar, itemId, 48);
        Add(cmd, "@name", SqlDbType.NVarChar, fileName, ComplianceRules.MaxFileName);
        Add(cmd, "@type", SqlDbType.VarChar, contentType, 100);
        Add(cmd, "@size", SqlDbType.Int, content.Length);
        Add(cmd, "@by", SqlDbType.Int, by);
        Add(cmd, "@utc", SqlDbType.DateTime2, utc);
        cmd.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = objectRef is null ? content : DBNull.Value;
        cmd.Parameters.Add("@ref", SqlDbType.UniqueIdentifier).Value = objectRef is null ? DBNull.Value : objectRef.Value;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private const string EvidenceCols = "e.evidence_id, e.fy_start, e.item_id, e.file_name, e.content_type, e.size_bytes, e.added_by, e.added_utc";

    public async Task<CmpEvidence?> DeleteEvidenceAsync(ICommandTransaction tx, int propertyId, long evidenceId, CancellationToken ct = default)
    {
        CmpEvidence? meta = null;
        var sql = Sql(tx);
        await using (var cmd = sql.Command($"SELECT {EvidenceCols} FROM cmp.evidence e WITH (UPDLOCK) WHERE e.evidence_id = @id AND e.property_id = @p;"))
        {
            Add(cmd, "@id", SqlDbType.BigInt, evidenceId);
            Add(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await rd.ReadAsync(ct).ConfigureAwait(false)) meta = ReadEvidence(rd);
        }

        if (meta is null) return null;
        Guid? oldRef;
        await using (var rq = sql.Command("SELECT object_ref FROM cmp.evidence_content WHERE evidence_id = @id;"))
        {
            Add(rq, "@id", SqlDbType.BigInt, evidenceId);
            oldRef = await rq.ExecuteScalarAsync(ct).ConfigureAwait(false) as Guid?;
        }

        if (oldRef is { } released) await Documents.DocumentObjectAccess.PublishReleaseAsync(tx, released, "compliance-evidence", evidenceId, ct).ConfigureAwait(false);
        await using (var del = sql.Command("DELETE cmp.evidence WHERE evidence_id = @id AND property_id = @p;"))
        {
            Add(del, "@id", SqlDbType.BigInt, evidenceId);
            Add(del, "@p", SqlDbType.Int, propertyId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return meta;
    }

    public async Task<(CmpEvidence Meta, byte[] Content)?> EvidenceFileAsync(int propertyId, long evidenceId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {EvidenceCols}, c.content, c.object_ref FROM cmp.evidence e JOIN cmp.evidence_content c ON c.evidence_id = e.evidence_id WHERE e.evidence_id = @id AND e.property_id = @p;", conn);
        Add(cmd, "@id", SqlDbType.BigInt, evidenceId);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        CmpEvidence meta; byte[]? inline; Guid? objectRef;
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (meta, inline, objectRef) = (ReadEvidence(rd), rd.IsDBNull(8) ? null : (byte[])rd[8], rd.IsDBNull(9) ? null : rd.GetGuid(9));
        }

        // LF-docdb: a ref reads from HccDocuments; no ref still reads the inline column (until migration 4719).
        return (meta, objectRef is { } r ? await Documents.DocumentObjectAccess.ReadAsync(objects, r, ct).ConfigureAwait(false) : inline ?? throw Documents.DocumentObjectAccess.Missing());
    }

    public async Task<IReadOnlyList<(CmpEvidence Meta, byte[] Content)>> EvidenceFilesAsync(int propertyId, int fyStart, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {EvidenceCols}, c.content, c.object_ref FROM cmp.evidence e JOIN cmp.evidence_content c ON c.evidence_id = e.evidence_id WHERE e.property_id = @p AND e.fy_start = @fy ORDER BY e.evidence_id;", conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@fy", SqlDbType.SmallInt, (short)fyStart);
        var rows = new List<(CmpEvidence Meta, byte[]? Inline, Guid? Ref)>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((ReadEvidence(rd), rd.IsDBNull(8) ? null : (byte[])rd[8], rd.IsDBNull(9) ? null : rd.GetGuid(9)));
        var list = new List<(CmpEvidence, byte[])>();
        foreach (var (meta, inline, objectRef) in rows)
            list.Add((meta, objectRef is { } r ? await Documents.DocumentObjectAccess.ReadAsync(objects, r, ct).ConfigureAwait(false) : inline ?? throw Documents.DocumentObjectAccess.Missing()));
        return list;
    }

    // ── ≈₹ payable from HCC's own data ──

    public async Task<IReadOnlyList<CmpPayableMonth>> PayableAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var payroll = new Dictionary<DateOnly, (long Pf, long Esic, long Pt)>();
        var gst = new Dictionary<DateOnly, long>();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT r.pay_month, SUM(ISNULL(l.pf_paise, 0) + ISNULL(l.pf_er_paise, 0)), SUM(ISNULL(l.esic_paise, 0) + ISNULL(l.esic_er_paise, 0)), SUM(ISNULL(l.pt_paise, 0))
              FROM hr.pay_run r LEFT JOIN hr.pay_run_line l ON l.run_id = r.run_id
             WHERE r.property_id = @p AND r.status = 'Locked' AND r.pay_month BETWEEN @f AND @t GROUP BY r.pay_month;
            SELECT DATEFROMPARTS(YEAR(i.invoice_date), MONTH(i.invoice_date), 1), SUM(l.tax_paise)
              FROM ivc.invoice i JOIN ivc.invoice_line l ON l.invoice_id = i.invoice_id
             WHERE i.property_id = @p AND i.invoice_date >= @f AND i.invoice_date < DATEADD(MONTH, 1, @t)
             GROUP BY DATEFROMPARTS(YEAR(i.invoice_date), MONTH(i.invoice_date), 1);
            SELECT DATEFROMPARTS(YEAR(n.note_date), MONTH(n.note_date), 1), SUM(l.tax_paise)
              FROM ivc.credit_note n JOIN ivc.credit_note_line l ON l.credit_note_id = n.credit_note_id
             WHERE n.property_id = @p AND n.note_date >= @f AND n.note_date < DATEADD(MONTH, 1, @t)
             GROUP BY DATEFROMPARTS(YEAR(n.note_date), MONTH(n.note_date), 1);
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, propertyId);
        Add(cmd, "@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        Add(cmd, "@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) payroll[DateOnly.FromDateTime(rd.GetDateTime(0))] = (rd.GetInt64(1), rd.GetInt64(2), rd.GetInt64(3));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) gst[DateOnly.FromDateTime(rd.GetDateTime(0))] = rd.GetInt64(1);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var m = DateOnly.FromDateTime(rd.GetDateTime(0));
                gst[m] = (gst.TryGetValue(m, out var g) ? g : 0) - rd.GetInt64(1);
            }
        }

        var list = new List<CmpPayableMonth>();
        foreach (var m in payroll.Keys.Concat(gst.Where(kv => kv.Value != 0).Select(kv => kv.Key)).Distinct().Order())
        {
            var ym = m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            var sources = new List<string>();
            var hasRun = payroll.TryGetValue(m, out var p);
            var g = gst.TryGetValue(m, out var gv) ? gv : 0;
            if (hasRun) sources.Add($"Payroll {SccTaxRules.YmLabel(ym)} (locked run)");
            if (g != 0) sources.Add($"GST on invoices issued {SccTaxRules.YmLabel(ym)} (net of credit notes)");
            list.Add(new CmpPayableMonth(ym, p.Pf, p.Esic, p.Pt, g, sources));
        }

        return list;
    }

    // ── CA pack ──

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
