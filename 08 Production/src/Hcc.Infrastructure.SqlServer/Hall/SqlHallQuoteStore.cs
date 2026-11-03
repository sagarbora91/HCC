using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Hall;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Hall;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Hall;

/// <summary>SQL adapter of <see cref="IHallQuoteStore"/> (F6 H2: 4300 quotation tables, 4320 quotation_advance, 4330 QUO series, 4331 T&amp;C
/// templates, 4332 snapshots, 4333 menu packages, 4334 lost reasons).</summary>
public sealed class SqlHallQuoteStore : IHallQuoteStore
{
    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The hall store needs the SQL command transaction.")).Command(sql);

    private static void P(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private static DateOnly Day(object v) => DateOnly.FromDateTime((DateTime)v);

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<DateOnly> Dates(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => DateOnly.ParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return Day(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found."));
    }

    public async Task<IReadOnlySet<string>> RolesAsync(ICommandTransaction tx, int principalId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT r.name FROM plat.user_role ur JOIN plat.role r ON r.role_code = ur.role_code
            WHERE ur.user_id = @u AND ur.valid_from_utc <= SYSUTCDATETIME() AND (ur.valid_until_utc IS NULL OR ur.valid_until_utc > SYSUTCDATETIME());
            """);
        P(cmd, "@u", SqlDbType.Int, principalId);
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) set.Add(r.GetString(0));
        return set;
    }

    public async Task<HallEnquiryRow?> LockEnquiryAsync(ICommandTransaction tx, int propertyId, long enquiryId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT enquiry_id, status, name, phone, event_type, dates, slot, pax, venue_id, company_id
            FROM hall.enquiry WITH (UPDLOCK, ROWLOCK) WHERE enquiry_id = @e AND property_id = @p;
            """);
        P(cmd, "@e", SqlDbType.BigInt, enquiryId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new HallEnquiryRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), Dates(r.GetString(5)), r.GetString(6), r.GetInt32(7),
            r.IsDBNull(8) ? null : r.GetInt32(8), r.IsDBNull(9) ? null : r.GetInt64(9));
    }

    public async Task<IReadOnlyList<HallVenueSlotsRow>> VenuesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT v.venue_id, v.code, v.name, v.active, v.gst_class, s.slot, s.rate_paise
            FROM hall.venue v LEFT JOIN hall.venue_slot s ON s.venue_id = v.venue_id WHERE v.property_id = @p ORDER BY v.venue_id;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var map = new Dictionary<int, (string Code, string Name, bool Active, string Gst, Dictionary<string, long> Rates)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = r.GetInt32(0);
                if (!map.TryGetValue(id, out var v)) map[id] = v = (r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetString(4), new Dictionary<string, long>(StringComparer.Ordinal));
                if (!r.IsDBNull(5)) v.Rates[r.GetString(5)] = r.GetInt64(6);
            }
        }

        return map.Select(kv => new HallVenueSlotsRow(kv.Key, kv.Value.Code, kv.Value.Name, kv.Value.Active, kv.Value.Gst, kv.Value.Rates)).ToList();
    }

    public async Task<IReadOnlyList<HallMenuRow>> MenusAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT code, name, per_plate_paise, active FROM hall.menu_package WHERE property_id = @p ORDER BY menu_id;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<HallMenuRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new HallMenuRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetBoolean(3)));
        return list;
    }

    public async Task<IReadOnlyList<HallTncTemplate>> TemplatesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT t.template_id, t.code, t.event_type, v.v, v.effective_from, c.n, c.heading, c.body
            FROM hall.tnc_template t
            JOIN hall.tnc_template_version v ON v.template_id = t.template_id
            LEFT JOIN hall.tnc_clause c ON c.template_id = v.template_id AND c.v = v.v
            WHERE t.property_id = @p ORDER BY t.template_id, v.v, c.n;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = new List<(int Id, string Code, string Type, int V, DateOnly Eff, HallTncClause? C)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                rows.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), Day(r.GetDateTime(4)), r.IsDBNull(5) ? null : new HallTncClause(r.GetInt32(5), r.GetString(6), r.GetString(7))));
        }

        return rows.GroupBy(x => (x.Id, x.Code, x.Type)).Select(t => new HallTncTemplate(t.Key.Code, t.Key.Type,
            t.GroupBy(x => (x.V, x.Eff)).Select(v => new HallTncVersion(v.Key.V, v.Key.Eff, v.Where(x => x.C is not null).Select(x => x.C!).ToList())).ToList())).ToList();
    }

    public async Task<IReadOnlyList<HallLostReason>> LostReasonsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT reason_id, label, needs_note FROM hall.lost_reason WHERE property_id = @p AND active = 1 ORDER BY sort_order;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<HallLostReason>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new HallLostReason(r.GetString(0), r.GetString(1), r.GetBoolean(2)));
        return list;
    }

    public async Task<(string Prefix, int Seq)> NextNumberAsync(ICommandTransaction tx, int propertyId, string fyCode, DateOnly date, DateTime utc, CancellationToken ct)
    {
        // Exclusive transaction-scoped app lock per property × FY (as ivc.series): check-then-insert or a single-row advance, gap-free.
        await using var cmd = Cmd(tx, """
            DECLARE @prefix VARCHAR(8), @seq INT, @lock INT, @start INT;
            DECLARE @res NVARCHAR(255) = CONCAT(N'hall.quote_series:', @p, N':', @fy);
            EXEC @lock = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @lock < 0 THROW 52406, 'The quotation numbering is busy; try again.', 1;
            SELECT @prefix = prefix, @seq = last_seq FROM hall.quote_series WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND fy_code = @fy;
            IF @prefix IS NULL
            BEGIN
              SELECT TOP (1) @prefix = prefix, @start = start_no FROM bill.number_series
               WHERE property_id = @p AND document = 'QUO' AND active = 1 AND active_from <= @d ORDER BY active_from DESC;
              SET @prefix = ISNULL(@prefix, 'QT'); SET @start = ISNULL(@start, 1);
              INSERT hall.quote_series(property_id, fy_code, prefix, last_seq, created_utc) VALUES (@p, @fy, @prefix, @start, @utc);
              SELECT @prefix, @start;
            END
            ELSE
            BEGIN
              UPDATE hall.quote_series SET last_seq = last_seq + 1 WHERE property_id = @p AND fy_code = @fy;
              SELECT @prefix, @seq + 1;
            END
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@fy", SqlDbType.Char, fyCode);
        P(cmd, "@d", SqlDbType.Date, D(date));
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return (rd.GetString(0), rd.GetInt32(1));
    }

    public async Task<long> InsertQuotationAsync(ICommandTransaction tx, NewHallQuotation q, HallQuoteVersionData v1, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        await using var cmd = Cmd(tx, """
            INSERT hall.quotation(property_id, number, fy, enquiry_id, current_version, customer_name, customer_phone, company_id, created_by, created_utc)
            OUTPUT INSERTED.quotation_id VALUES (@p, @n, @fy, @e, 1, @cn, @cp, @co, @by, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, q.PropertyId);
        P(cmd, "@n", SqlDbType.VarChar, q.Number);
        P(cmd, "@fy", SqlDbType.Char, q.Fy);
        P(cmd, "@e", SqlDbType.BigInt, q.EnquiryId);
        P(cmd, "@cn", SqlDbType.NVarChar, q.CustomerName);
        P(cmd, "@cp", SqlDbType.VarChar, q.CustomerPhone);
        P(cmd, "@co", SqlDbType.BigInt, q.CompanyId);
        P(cmd, "@by", SqlDbType.Int, q.By);
        P(cmd, "@utc", SqlDbType.DateTime2, q.Utc);
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await InsertVersionRowAsync(tx, id, 1, v1, ct).ConfigureAwait(false);
        return id;
    }

    private static async Task<long> InsertVersionRowAsync(ICommandTransaction tx, long quotationId, int v, HallQuoteVersionData d, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT hall.quotation_version(quotation_id, v, status, event_type, dates, slot, venue_id, setup, pax, guaranteed_pax, valid_until, created_by, created_utc,
                                          tnc_template_code, tnc_v, tnc_has_placeholder)
            OUTPUT INSERTED.version_id VALUES (@q, @v, 'DRAFT', @et, @dates, @slot, @venue, @setup, @pax, @gpax, @until, @by, @utc, @tc, @tv, @ph);
            """);
        P(cmd, "@q", SqlDbType.BigInt, quotationId);
        P(cmd, "@v", SqlDbType.Int, v);
        P(cmd, "@by", SqlDbType.Int, d.CreatedBy);
        P(cmd, "@utc", SqlDbType.DateTime2, d.CreatedUtc);
        Header(cmd, d);
        var vid = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await ChildrenAsync(tx, vid, d, ct).ConfigureAwait(false);
        return vid;
    }

    private static void Header(SqlCommand cmd, HallQuoteVersionData d)
    {
        P(cmd, "@et", SqlDbType.VarChar, d.Header.EventType);
        P(cmd, "@dates", SqlDbType.VarChar, string.Join(',', d.Header.Dates.Select(Wire)));
        P(cmd, "@slot", SqlDbType.VarChar, d.Header.Slot);
        P(cmd, "@venue", SqlDbType.Int, d.Header.VenueId);
        P(cmd, "@setup", SqlDbType.VarChar, d.Header.Setup);
        P(cmd, "@pax", SqlDbType.Int, d.Header.Pax);
        P(cmd, "@gpax", SqlDbType.Int, d.Header.GuaranteedPax);
        P(cmd, "@until", SqlDbType.Date, D(d.ValidUntil));
        P(cmd, "@tc", SqlDbType.VarChar, d.Tnc.TemplateCode);
        P(cmd, "@tv", SqlDbType.Int, d.Tnc.V);
        P(cmd, "@ph", SqlDbType.Bit, d.Tnc.HasPlaceholder);
    }

    /// <summary>Lines, advance rows and the T&amp;C snapshot of a version (after deleting a draft's old children).</summary>
    private static async Task ChildrenAsync(ICommandTransaction tx, long versionId, HallQuoteVersionData d, CancellationToken ct)
    {
        for (var i = 0; i < d.Lines.Count; i++)
        {
            var (l, refId) = (d.Lines[i].Line, d.Lines[i].RefId);
            await using var c = Cmd(tx, """
                INSERT hall.quotation_line(version_id, seq, kind, ref_id, descr, qty, unit, rate_paise, list_rate_paise, discount_paise, gst_class)
                VALUES (@v, @s, @k, @ref, @d, @q, @u, @r, @l, @disc, @g);
                """);
            P(c, "@v", SqlDbType.BigInt, versionId);
            P(c, "@s", SqlDbType.Int, i + 1);
            P(c, "@k", SqlDbType.VarChar, l.Kind);
            P(c, "@ref", SqlDbType.VarChar, refId);
            P(c, "@d", SqlDbType.NVarChar, l.Desc);
            P(c, "@q", SqlDbType.Int, l.Qty);
            P(c, "@u", SqlDbType.VarChar, l.Unit);
            P(c, "@r", SqlDbType.BigInt, l.Rate.Value);
            P(c, "@l", SqlDbType.BigInt, l.ListRate.Value);
            P(c, "@disc", SqlDbType.BigInt, l.Discount.Value);
            P(c, "@g", SqlDbType.VarChar, l.GstClass);
            await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        for (var i = 0; i < d.Advance.Count; i++)
        {
            var a = d.Advance[i];
            await using var c = Cmd(tx, "INSERT hall.quotation_advance(version_id, seq, label, pct_bps, amount_paise, due) VALUES (@v, @s, @lbl, @pct, @amt, @due);");
            P(c, "@v", SqlDbType.BigInt, versionId);
            P(c, "@s", SqlDbType.Int, i + 1);
            P(c, "@lbl", SqlDbType.NVarChar, a.Label);
            P(c, "@pct", SqlDbType.Int, a.PctBps);
            P(c, "@amt", SqlDbType.BigInt, a.PctBps is null && a.Amount.Value > 0 ? a.Amount.Value : null);
            P(c, "@due", SqlDbType.VarChar, a.Due);
            await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await SnapshotAsync(tx, versionId, d.Tnc, ct).ConfigureAwait(false);
    }

    private static async Task SnapshotAsync(ICommandTransaction tx, long versionId, HallTncSnapshot tnc, CancellationToken ct)
    {
        foreach (var cl in tnc.Clauses)
        {
            await using var c = Cmd(tx, "INSERT hall.quotation_tnc_snapshot(version_id, n, heading, body) VALUES (@v, @n, @h, @b);");
            P(c, "@v", SqlDbType.BigInt, versionId);
            P(c, "@n", SqlDbType.Int, cl.N);
            P(c, "@h", SqlDbType.NVarChar, cl.Heading);
            P(c, "@b", SqlDbType.NVarChar, cl.Text);
            await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<HallQuoteState?> LockAsync(ICommandTransaction tx, int propertyId, long quotationId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT q.quotation_id, q.number, q.fy, q.enquiry_id, q.current_version,
                   v.version_id, v.v, v.status, v.event_type, v.dates, v.slot, v.venue_id, v.setup, v.pax, v.guaranteed_pax, v.valid_until, v.created_by, v.created_utc,
                   v.sent_utc, v.override_by, v.approval_by, v.tnc_template_code, v.tnc_v, v.tnc_has_placeholder
            FROM hall.quotation q WITH (UPDLOCK, ROWLOCK)
            JOIN hall.quotation_version v WITH (UPDLOCK) ON v.quotation_id = q.quotation_id
            WHERE q.quotation_id = @q AND q.property_id = @p ORDER BY v.v;
            """);
        P(cmd, "@q", SqlDbType.BigInt, quotationId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        (long Id, string Number, string Fy, long Enq, int Current)? head = null;
        var raw = new List<(long Vid, int V, string Status, HallQuoteHeader H, DateOnly Until, int By, DateTime Utc, DateTime? Sent, int? Ovr, int? Appr, string? Tc, int? Tv, bool Ph)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                head ??= (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt32(4));
                raw.Add((r.GetInt64(5), r.GetInt32(6), r.GetString(7),
                    new HallQuoteHeader(r.GetString(8), Dates(r.GetString(9)), r.GetString(10), r.GetInt32(11), r.GetString(12), r.GetInt32(13), r.GetInt32(14)),
                    Day(r.GetDateTime(15)), r.GetInt32(16), r.GetDateTime(17), r.IsDBNull(18) ? null : r.GetDateTime(18), r.IsDBNull(19) ? null : r.GetInt32(19),
                    r.IsDBNull(20) ? null : r.GetInt32(20), S(r, 21), r.IsDBNull(22) ? null : r.GetInt32(22), r.GetBoolean(23)));
            }
        }

        if (head is not { } h) return null;
        var versions = new List<HallQuoteVersionRow>();
        foreach (var x in raw)
        {
            var lines = await LinesAsync(tx, x.Vid, ct).ConfigureAwait(false);
            var adv = await AdvanceAsync(tx, x.Vid, ct).ConfigureAwait(false);
            var clauses = await ClausesAsync(tx, x.Vid, ct).ConfigureAwait(false);
            versions.Add(new HallQuoteVersionRow(x.Vid, x.V, x.Status, new HallQuoteVersionData(x.H, lines, adv, new HallTncSnapshot(x.Tc ?? "", x.Tv ?? 0, clauses, x.Ph), x.Until, x.By, x.Utc),
                x.Sent, x.Ovr, x.Appr));
        }

        return new HallQuoteState(h.Id, h.Number, h.Fy, h.Enq, h.Current, versions);
    }

    private static async Task<List<HallQuoteLine>> LinesAsync(ICommandTransaction tx, long versionId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT seq, kind, descr, qty, unit, rate_paise, list_rate_paise, discount_paise, gst_class, ref_id FROM hall.quotation_line WHERE version_id = @v ORDER BY seq;");
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        var list = new List<HallQuoteLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallQuoteLine(new HallLine("L" + r.GetInt32(0).ToString(CultureInfo.InvariantCulture), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4),
                new Paise(r.GetInt64(5)), new Paise(r.GetInt64(6)), new Paise(r.GetInt64(7)), r.GetString(8)), S(r, 9)));
        return list;
    }

    private static async Task<List<HallAdvanceRow>> AdvanceAsync(ICommandTransaction tx, long versionId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT seq, label, pct_bps, amount_paise, due FROM hall.quotation_advance WHERE version_id = @v ORDER BY seq;");
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        var list = new List<HallAdvanceRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallAdvanceRow("ADV-" + r.GetInt32(0).ToString(CultureInfo.InvariantCulture), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                new Paise(r.IsDBNull(3) ? 0 : r.GetInt64(3)), r.GetString(4), Paise.Zero));
        return list;
    }

    private static async Task<List<HallTncClause>> ClausesAsync(ICommandTransaction tx, long versionId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT n, heading, body FROM hall.quotation_tnc_snapshot WHERE version_id = @v ORDER BY n;");
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        var list = new List<HallTncClause>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new HallTncClause(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public async Task InsertVersionAsync(ICommandTransaction tx, long quotationId, int v, HallQuoteVersionData data, CancellationToken ct)
    {
        await InsertVersionRowAsync(tx, quotationId, v, data, ct).ConfigureAwait(false);
        await using var cmd = Cmd(tx, "UPDATE hall.quotation SET current_version = @v WHERE quotation_id = @q;");
        P(cmd, "@v", SqlDbType.Int, v);
        P(cmd, "@q", SqlDbType.BigInt, quotationId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateDraftAsync(ICommandTransaction tx, long versionId, HallQuoteVersionData data, bool clearApproval, bool clearOverride, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        await using (var cmd = Cmd(tx, $"""
            UPDATE hall.quotation_version SET event_type = @et, dates = @dates, slot = @slot, venue_id = @venue, setup = @setup, pax = @pax, guaranteed_pax = @gpax,
                   valid_until = @until, tnc_template_code = @tc, tnc_v = @tv, tnc_has_placeholder = @ph, updated_utc = SYSUTCDATETIME()
                   {(clearApproval ? ", approval_by = NULL, approval_utc = NULL, approval_bps = NULL" : "")}
                   {(clearOverride ? ", override_by = NULL, override_reason = NULL, override_utc = NULL" : "")}
            WHERE version_id = @vid AND status = 'DRAFT';
            DELETE hall.quotation_line WHERE version_id = @vid;
            DELETE hall.quotation_advance WHERE version_id = @vid;
            DELETE hall.quotation_tnc_snapshot WHERE version_id = @vid;
            """))
        {
            P(cmd, "@vid", SqlDbType.BigInt, versionId);
            Header(cmd, data);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await ChildrenAsync(tx, versionId, data, ct).ConfigureAwait(false);
    }

    public async Task SendAsync(ICommandTransaction tx, long versionId, HallTncSnapshot tnc, DateOnly validUntil, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tnc);
        await using (var cmd = Cmd(tx, """
            UPDATE hall.quotation_version SET status = 'SENT', sent_utc = @utc, valid_until = @until, tnc_template_code = @tc, tnc_v = @tv, tnc_has_placeholder = @ph, updated_utc = @utc
            WHERE version_id = @vid AND status = 'DRAFT';
            IF @@ROWCOUNT <> 1 THROW 52409, 'The quotation version is no longer a draft.', 1;
            DELETE hall.quotation_tnc_snapshot WHERE version_id = @vid;
            """))
        {
            P(cmd, "@vid", SqlDbType.BigInt, versionId);
            P(cmd, "@utc", SqlDbType.DateTime2, utc);
            P(cmd, "@until", SqlDbType.Date, D(validUntil));
            P(cmd, "@tc", SqlDbType.VarChar, tnc.TemplateCode);
            P(cmd, "@tv", SqlDbType.Int, tnc.V);
            P(cmd, "@ph", SqlDbType.Bit, tnc.HasPlaceholder);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await SnapshotAsync(tx, versionId, tnc, ct).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(ICommandTransaction tx, long versionId, string status, string? lostReasonId, string? lostNote, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE hall.quotation_version SET status = @s, lost_reason_id = COALESCE(@r, lost_reason_id), lost_note = COALESCE(@n, lost_note), updated_utc = @utc WHERE version_id = @vid;
            """);
        P(cmd, "@vid", SqlDbType.BigInt, versionId);
        P(cmd, "@s", SqlDbType.VarChar, status);
        P(cmd, "@r", SqlDbType.VarChar, lostReasonId);
        P(cmd, "@n", SqlDbType.NVarChar, lostNote);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetOverrideAsync(ICommandTransaction tx, long versionId, int by, string reason, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE hall.quotation_version SET override_by = @by, override_reason = @r, override_utc = @utc WHERE version_id = @vid;");
        P(cmd, "@vid", SqlDbType.BigInt, versionId);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@r", SqlDbType.NVarChar, reason);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetApprovalAsync(ICommandTransaction tx, long versionId, int by, int bps, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE hall.quotation_version SET approval_by = @by, approval_bps = @bps, approval_utc = @utc WHERE version_id = @vid;");
        P(cmd, "@vid", SqlDbType.BigInt, versionId);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@bps", SqlDbType.Int, bps);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetEnquiryAsync(ICommandTransaction tx, long enquiryId, string status, string? lostReasonId, string? lostNote, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, status == "LOST"
            ? """
              UPDATE hall.enquiry SET status = 'LOST', lost_reason_id = @r, lost_note = @n, soft_venue_id = NULL, soft_until_utc = NULL, soft_by = NULL, version = version + 1
              WHERE enquiry_id = @e AND status IN ('OPEN','QUOTED');
              """
            : "UPDATE hall.enquiry SET status = @s, version = version + 1 WHERE enquiry_id = @e AND status = 'OPEN';");
        P(cmd, "@e", SqlDbType.BigInt, enquiryId);
        P(cmd, "@s", SqlDbType.VarChar, status);
        P(cmd, "@r", SqlDbType.VarChar, lostReasonId);
        P(cmd, "@n", SqlDbType.NVarChar, lostNote);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ExpireAsync(ICommandTransaction tx, int propertyId, DateOnly today, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE v SET v.status = 'EXPIRED', v.updated_utc = @utc
            FROM hall.quotation_version v JOIN hall.quotation q ON q.quotation_id = v.quotation_id AND q.current_version = v.v
            WHERE q.property_id = @p AND v.status = 'SENT' AND v.valid_until < @d;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@d", SqlDbType.Date, D(today));
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> SaveTemplateVersionAsync(ICommandTransaction tx, int propertyId, string code, string eventType, DateOnly effectiveFrom, IReadOnlyList<HallTncClause> clauses,
        int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        int templateId, v;
        await using (var cmd = Cmd(tx, """
            DECLARE @t INT = (SELECT template_id FROM hall.tnc_template WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND event_type = @et);
            IF @t IS NULL BEGIN INSERT hall.tnc_template(property_id, code, event_type) VALUES (@p, @code, @et); SET @t = SCOPE_IDENTITY(); END
            DECLARE @v INT = ISNULL((SELECT MAX(v) FROM hall.tnc_template_version WHERE template_id = @t), 0) + 1;
            INSERT hall.tnc_template_version(template_id, v, effective_from, created_by, created_utc) VALUES (@t, @v, @eff, @by, @utc);
            SELECT @t, @v;
            """))
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@et", SqlDbType.VarChar, eventType);
            P(cmd, "@code", SqlDbType.VarChar, code);
            P(cmd, "@eff", SqlDbType.Date, D(effectiveFrom));
            P(cmd, "@by", SqlDbType.Int, by);
            P(cmd, "@utc", SqlDbType.DateTime2, utc);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await r.ReadAsync(ct).ConfigureAwait(false);
            (templateId, v) = (r.GetInt32(0), r.GetInt32(1));
        }

        foreach (var cl in clauses)
        {
            await using var c = Cmd(tx, "INSERT hall.tnc_clause(template_id, v, n, heading, body) VALUES (@t, @v, @n, @h, @b);");
            P(c, "@t", SqlDbType.Int, templateId);
            P(c, "@v", SqlDbType.Int, v);
            P(c, "@n", SqlDbType.Int, cl.N);
            P(c, "@h", SqlDbType.NVarChar, cl.Heading);
            P(c, "@b", SqlDbType.NVarChar, cl.Text);
            await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return v;
    }
}
