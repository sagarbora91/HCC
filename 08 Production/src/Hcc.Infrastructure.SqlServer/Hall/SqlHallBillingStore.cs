using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Hall;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Hall;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Commands;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Hall;

/// <summary>SQL adapter of <see cref="IHallBillingStore"/> (F6 H1: migrations 4300 hall schema, 4320 billing, 4740 source kinds).</summary>
public sealed class SqlHallBillingStore : IHallBillingStore
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

    public async Task<IReadOnlyList<HallVenueRow>> VenuesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT venue_id, code, name, active, combined_of, cap_theatre, cap_round, cap_ushape, cap_floating, cap_classroom
            FROM hall.venue WHERE property_id = @p ORDER BY venue_id;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var raw = new List<(int Id, string Code, string Name, bool Active, string? Combined, Dictionary<string, int> Cap)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            string[] setups = ["THEATRE", "ROUND", "USHAPE", "FLOATING", "CLASSROOM"];
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var cap = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < setups.Length; i++) if (!r.IsDBNull(5 + i)) cap[setups[i]] = r.GetInt32(5 + i);
                raw.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), S(r, 4), cap));
            }
        }

        var byCode = raw.ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);
        return raw.Select(x => new HallVenueRow(x.Id, x.Code, x.Name, x.Active,
            x.Combined is null ? null : x.Combined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(byCode.ContainsKey).Select(c => byCode[c]).ToArray(),
            x.Cap)).ToList();
    }

    public async Task<int?> SingleOpenTillAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT t.till_id FROM pay.till t JOIN pay.shift s ON s.till_id = t.till_id AND s.status IN ('Open','Submitted') WHERE t.property_id = @p;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var ids = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) ids.Add(r.GetInt32(0));
        return ids.Count == 1 ? ids[0] : null;
    }

    private static async Task<List<HallLine>> LinesAsync(ICommandTransaction tx, string table, string key, long id, string extraFilter, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, $"SELECT seq, kind, descr, qty, unit, rate_paise, list_rate_paise, discount_paise, gst_class{(table == "hall.event_line" ? ", tax_bps" : ", CAST(NULL AS INT)")} FROM {table} WHERE {key} = @id{extraFilter} ORDER BY seq;");
        P(cmd, "@id", SqlDbType.BigInt, id);
        var list = new List<HallLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallLine("L" + r.GetInt32(0).ToString(CultureInfo.InvariantCulture), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), new Paise(r.GetInt64(5)),
                new Paise(r.GetInt64(6)), new Paise(r.GetInt64(7)), r.GetString(8), r.IsDBNull(9) ? null : r.GetInt32(9)));
        return list;
    }

    private static async Task<List<HallAdvanceRow>> AdvanceAsync(ICommandTransaction tx, string sql, long id, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, sql);
        P(cmd, "@id", SqlDbType.BigInt, id);
        var list = new List<HallAdvanceRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallAdvanceRow("ADV-" + r.GetInt32(0).ToString(CultureInfo.InvariantCulture), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                new Paise(r.IsDBNull(3) ? 0 : r.GetInt64(3)), r.GetString(4), new Paise(r.GetInt64(5))));
        return list;
    }

    public async Task<HallQuoteRow?> LockQuotationAsync(ICommandTransaction tx, int propertyId, long quotationId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT q.quotation_id, q.number, q.enquiry_id, q.current_version, q.customer_name, q.customer_phone, q.company_id,
                   v.version_id, v.v, v.status, v.event_type, v.dates, v.slot, v.venue_id, v.setup, v.pax, v.guaranteed_pax, v.valid_until
            FROM hall.quotation q WITH (UPDLOCK, ROWLOCK)
            JOIN hall.quotation_version v WITH (UPDLOCK) ON v.quotation_id = q.quotation_id AND v.v = q.current_version
            WHERE q.quotation_id = @q AND q.property_id = @p;
            """);
        P(cmd, "@q", SqlDbType.BigInt, quotationId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        HallQuoteRow row;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            row = new HallQuoteRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6),
                r.GetInt64(7), r.GetInt32(8), r.GetString(9), r.GetString(10), Dates(r.GetString(11)), r.GetString(12), r.GetInt32(13), r.GetString(14), r.GetInt32(15), r.GetInt32(16),
                Day(r.GetDateTime(17)), [], []);
        }

        var lines = await LinesAsync(tx, "hall.quotation_line", "version_id", row.VersionId, "", ct).ConfigureAwait(false);
        var adv = await AdvanceAsync(tx, "SELECT seq, label, pct_bps, amount_paise, due, CAST(0 AS BIGINT) FROM hall.quotation_advance WHERE version_id = @id ORDER BY seq;", row.VersionId, ct).ConfigureAwait(false);
        return row with { Lines = lines, Advance = adv };
    }

    public async Task<IReadOnlyList<HallHold>> HoldsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dates);
        if (dates.Count == 0) return [];
        await using var cmd = Cmd(tx, """
            SELECT event_id, venue_id, dates, slot FROM hall.event WITH (UPDLOCK)
            WHERE property_id = @p AND status <> 'CANCELLED' AND event_date BETWEEN DATEADD(DAY, -7, @from) AND @to;
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@from", SqlDbType.Date, D(dates.Min()));
        P(cmd, "@to", SqlDbType.Date, D(dates.Max()));
        var list = new List<HallHold>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallHold(r.GetInt64(0).ToString(CultureInfo.InvariantCulture), [r.GetInt32(1).ToString(CultureInfo.InvariantCulture)], Dates(r.GetString(2)), r.GetString(3)));
        return list;
    }

    private static async Task InsertLineAsync(ICommandTransaction tx, long eventId, int seq, bool extra, HallLine l, int? taxBps, CancellationToken ct)
    {
        await using var li = Cmd(tx, """
            INSERT hall.event_line(event_id, seq, is_extra, kind, descr, qty, unit, rate_paise, list_rate_paise, discount_paise, gst_class, tax_bps)
            VALUES (@e, @s, @x, @k, @d, @q, @u, @r, @l, @disc, @g, @bps);
            """);
        P(li, "@e", SqlDbType.BigInt, eventId);
        P(li, "@s", SqlDbType.Int, seq);
        P(li, "@x", SqlDbType.Bit, extra);
        P(li, "@k", SqlDbType.VarChar, l.Kind);
        P(li, "@d", SqlDbType.NVarChar, l.Desc.Length <= 120 ? l.Desc : l.Desc[..120]);
        P(li, "@q", SqlDbType.Int, l.Qty);
        P(li, "@u", SqlDbType.VarChar, l.Unit.Length <= 12 ? l.Unit : l.Unit[..12]);
        P(li, "@r", SqlDbType.BigInt, l.Rate.Value);
        P(li, "@l", SqlDbType.BigInt, l.ListRate.Value);
        P(li, "@disc", SqlDbType.BigInt, l.Discount.Value);
        P(li, "@g", SqlDbType.VarChar, l.GstClass);
        P(li, "@bps", SqlDbType.Int, taxBps);
        await li.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertEventAsync(ICommandTransaction tx, NewHallEvent e, IReadOnlyList<HallLine> lines, IReadOnlyList<HallAdvanceRow> plan,
        IReadOnlyList<(int VenueId, DateOnly Date, string Part)> holds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(holds);
        await using var cmd = Cmd(tx, """
            INSERT hall.event(property_id, quotation_id, version, venue_id, event_date, dates, slot, event_type, guaranteed_pax, status, customer_name, customer_phone,
              company_id, created_by, created_utc, idem_key)
            OUTPUT INSERTED.event_id
            VALUES (@p, @q, @v, @ven, @d, @dates, @slot, @type, @g, 'CONFIRMED', @n, @ph, @co, @by, @u, @k);
            """);
        P(cmd, "@p", SqlDbType.Int, e.PropertyId);
        P(cmd, "@q", SqlDbType.BigInt, e.QuotationId);
        P(cmd, "@v", SqlDbType.Int, e.Version);
        P(cmd, "@ven", SqlDbType.Int, e.VenueId);
        P(cmd, "@d", SqlDbType.Date, D(e.Dates[0]));
        P(cmd, "@dates", SqlDbType.VarChar, string.Join(',', e.Dates.Select(Wire)));
        P(cmd, "@slot", SqlDbType.VarChar, e.Slot);
        P(cmd, "@type", SqlDbType.VarChar, e.EventType);
        P(cmd, "@g", SqlDbType.Int, e.GuaranteedPax);
        P(cmd, "@n", SqlDbType.NVarChar, e.CustomerName);
        P(cmd, "@ph", SqlDbType.VarChar, e.CustomerPhone);
        P(cmd, "@co", SqlDbType.BigInt, e.CompanyId);
        P(cmd, "@by", SqlDbType.Int, e.By);
        P(cmd, "@u", SqlDbType.DateTime2, e.Utc);
        P(cmd, "@k", SqlDbType.VarChar, e.IdemKey.Length <= 80 ? e.IdemKey : null);
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var seq = 0;
        foreach (var l in lines) await InsertLineAsync(tx, id, ++seq, false, l, null, ct).ConfigureAwait(false);
        seq = 0;
        foreach (var a in plan)
        {
            await using var ai = Cmd(tx, "INSERT hall.event_advance(event_id, seq, label, pct_bps, amount_paise, due, paid_paise) VALUES (@e, @s, @l, @pct, @a, @due, 0);");
            P(ai, "@e", SqlDbType.BigInt, id);
            P(ai, "@s", SqlDbType.Int, ++seq);
            P(ai, "@l", SqlDbType.NVarChar, a.Label);
            P(ai, "@pct", SqlDbType.Int, a.PctBps);
            P(ai, "@a", SqlDbType.BigInt, a.Amount.Value);
            P(ai, "@due", SqlDbType.VarChar, a.Due);
            await ai.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (venueId, date, part) in holds)
        {
            await using var hi = Cmd(tx, "INSERT hall.calendar_hold(property_id, venue_id, hold_date, part, kind, event_id) VALUES (@p, @v, @d, @part, 'HARD', @e);");
            P(hi, "@p", SqlDbType.Int, e.PropertyId);
            P(hi, "@v", SqlDbType.Int, venueId);
            P(hi, "@d", SqlDbType.Date, D(date));
            P(hi, "@part", SqlDbType.VarChar, part);
            P(hi, "@e", SqlDbType.BigInt, id);
            try
            {
                await hi.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                throw new HccException(HccErrorCode.HCC_HALL_CLASH, $"The hall is already booked on {Wire(date)} ({part}).");
            }
        }

        return id;
    }

    public async Task MarkAcceptedAsync(ICommandTransaction tx, long versionId, long enquiryId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            UPDATE hall.quotation_version SET status = 'ACCEPTED' WHERE version_id = @v;
            UPDATE hall.enquiry SET status = 'WON', soft_venue_id = NULL, soft_until_utc = NULL, soft_by = NULL, version = version + 1 WHERE enquiry_id = @e;
            """);
        P(cmd, "@v", SqlDbType.BigInt, versionId);
        P(cmd, "@e", SqlDbType.BigInt, enquiryId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(long BlockId, byte[] Version)?> OpenEventBlockAsync(ICommandTransaction tx, long eventId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT g.group_block_id, g.row_ver FROM hall.event e JOIN inv.group_block g WITH (UPDLOCK) ON g.group_block_id = e.block_id WHERE e.event_id = @e AND g.status = 'Open';");
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetInt64(0), (byte[])rd[1]) : null;
    }

    public async Task SetEventBlockAsync(ICommandTransaction tx, long eventId, long blockId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE hall.event SET block_id = @b WHERE event_id = @e;");
        P(cmd, "@b", SqlDbType.BigInt, blockId);
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<HallEventRow?> LockEventAsync(ICommandTransaction tx, int propertyId, long eventId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT e.event_id, e.property_id, e.quotation_id, e.venue_id, e.event_date, e.dates, e.slot, e.status, e.guaranteed_pax, e.customer_name, e.invoice_no,
                   e.refund_state, e.cancel_refund_paise, e.cancel_by,
                   ISNULL((SELECT SUM(CASE r.kind WHEN 'ADVANCE' THEN r.amount_paise WHEN 'REFUND' THEN -r.amount_paise ELSE 0 END) FROM hall.event_receipt r WHERE r.event_id = e.event_id), 0)
            FROM hall.event e WITH (UPDLOCK, ROWLOCK) WHERE e.event_id = @e AND e.property_id = @p;
            """);
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        HallEventRow row;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            row = new HallEventRow(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.GetInt32(3), Day(r.GetDateTime(4)), Dates(r.GetString(5)), r.GetString(6), r.GetString(7), r.GetInt32(8),
                r.GetString(9), [], [], r.GetInt64(14), S(r, 10), S(r, 11), r.IsDBNull(12) ? null : r.GetInt64(12), r.IsDBNull(13) ? null : r.GetInt32(13));
        }

        var lines = await LinesAsync(tx, "hall.event_line", "event_id", eventId, " AND is_extra = 0", ct).ConfigureAwait(false);
        var adv = await AdvanceAsync(tx, "SELECT seq, label, pct_bps, amount_paise, due, paid_paise FROM hall.event_advance WHERE event_id = @id ORDER BY seq;", eventId, ct).ConfigureAwait(false);
        return row with { Lines = lines, Advance = adv };
    }

    public async Task<long> InsertReceiptAsync(ICommandTransaction tx, HallReceiptIn r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Cmd(tx, """
            INSERT hall.event_receipt(event_id, business_date, kind, tender, company_id, udhaar, reference, amount_paise, by_user, at_utc)
            OUTPUT INSERTED.receipt_id VALUES (@e, @d, @k, @t, @co, @ud, @ref, @a, @by, @u);
            """);
        P(cmd, "@e", SqlDbType.BigInt, r.EventId);
        P(cmd, "@d", SqlDbType.Date, D(r.Date));
        P(cmd, "@k", SqlDbType.VarChar, r.Kind);
        P(cmd, "@t", SqlDbType.VarChar, r.Tender);
        P(cmd, "@co", SqlDbType.BigInt, r.CompanyId);
        P(cmd, "@ud", SqlDbType.Bit, r.Udhaar);
        P(cmd, "@ref", SqlDbType.NVarChar, r.Reference);
        P(cmd, "@a", SqlDbType.BigInt, r.Amount.Value);
        P(cmd, "@by", SqlDbType.Int, r.By);
        P(cmd, "@u", SqlDbType.DateTime2, r.Utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task SetAdvancePaidAsync(ICommandTransaction tx, long eventId, IReadOnlyList<HallAdvanceRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        for (var i = 0; i < rows.Count; i++)
        {
            await using var cmd = Cmd(tx, "UPDATE hall.event_advance SET paid_paise = @paid WHERE event_id = @e AND seq = @s;");
            P(cmd, "@paid", SqlDbType.BigInt, rows[i].Paid.Value);
            P(cmd, "@e", SqlDbType.BigInt, eventId);
            P(cmd, "@s", SqlDbType.Int, i + 1);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task BillAsync(ICommandTransaction tx, long eventId, DateOnly date, HallBill bill, IReadOnlyList<HallLine> extras, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(extras);
        var accepted = bill.Lines.Count - extras.Count;
        for (var i = 0; i < accepted; i++)
        {
            var l = bill.Lines[i];
            await using var u = Cmd(tx, "UPDATE hall.event_line SET qty = @q, discount_paise = @disc, tax_bps = @bps WHERE event_id = @e AND seq = @s;");
            P(u, "@q", SqlDbType.Int, l.Line.Qty);
            P(u, "@disc", SqlDbType.BigInt, l.Line.Discount.Value);
            P(u, "@bps", SqlDbType.Int, l.TaxBps);
            P(u, "@e", SqlDbType.BigInt, eventId);
            P(u, "@s", SqlDbType.Int, i + 1);
            await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        for (var j = 0; j < extras.Count; j++) await InsertLineAsync(tx, eventId, accepted + j + 1, true, extras[j], bill.Lines[accepted + j].TaxBps, ct).ConfigureAwait(false);
        await using var cmd = Cmd(tx, """
            UPDATE hall.event SET status = 'BILLED', actual_pax = @a, billed_pax = @b, invoice_date = @d, taxable_paise = @tx, cgst_paise = @cg, sgst_paise = @sg, total_paise = @tot,
              advance_applied_paise = @ap, balance_paise = @bal, settle_mode = @m, invoice_no = 'PENDING'
            WHERE event_id = @e;
            """);
        P(cmd, "@a", SqlDbType.Int, bill.ActualPax);
        P(cmd, "@b", SqlDbType.Int, bill.BilledPax);
        P(cmd, "@d", SqlDbType.Date, D(date));
        P(cmd, "@tx", SqlDbType.BigInt, bill.Totals.Taxable.Value);
        P(cmd, "@cg", SqlDbType.BigInt, bill.Totals.Cgst.Value);
        P(cmd, "@sg", SqlDbType.BigInt, bill.Totals.Sgst.Value);
        P(cmd, "@tot", SqlDbType.BigInt, bill.Totals.Total.Value);
        P(cmd, "@ap", SqlDbType.BigInt, bill.Applied.Value);
        P(cmd, "@bal", SqlDbType.BigInt, bill.Balance.Value);
        P(cmd, "@m", SqlDbType.VarChar, bill.SettleMode);
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetInvoiceAsync(ICommandTransaction tx, long eventId, long invoiceId, string invoiceNo, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE hall.event SET invoice_id = @i, invoice_no = @no WHERE event_id = @e;");
        P(cmd, "@i", SqlDbType.BigInt, invoiceId);
        P(cmd, "@no", SqlDbType.VarChar, invoiceNo);
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(ICommandTransaction tx, long eventId, HallCancellationIn c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var cmd = Cmd(tx, """
            UPDATE hall.event SET status = 'CANCELLED', cancel_date = @d, cancel_days = @days, cancel_kept_bps = @kb, cancel_tax_bps = @tb, cancel_kept_paise = @k,
              cancel_refund_paise = @r, cancel_by = @by, cancel_reason = @why, refund_state = @state
            WHERE event_id = @e;
            UPDATE hall.calendar_hold SET released_utc = SYSUTCDATETIME() WHERE event_id = @e AND released_utc IS NULL;
            """);
        P(cmd, "@d", SqlDbType.Date, D(c.Date));
        P(cmd, "@days", SqlDbType.Int, c.Quote.DaysBefore);
        P(cmd, "@kb", SqlDbType.Int, c.Quote.KeptBps);
        P(cmd, "@tb", SqlDbType.Int, c.Quote.TaxBps);
        P(cmd, "@k", SqlDbType.BigInt, c.Quote.Kept.Value);
        P(cmd, "@r", SqlDbType.BigInt, c.Quote.Refund.Value);
        P(cmd, "@by", SqlDbType.Int, c.By);
        P(cmd, "@why", SqlDbType.NVarChar, c.Reason.Length <= 200 ? c.Reason : c.Reason[..200]);
        P(cmd, "@state", SqlDbType.VarChar, c.Quote.Refund.Value > 0 ? "PENDING" : "NONE");
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkRefundedAsync(ICommandTransaction tx, long eventId, int by, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE hall.event SET refund_state = 'PAID', refund_by = @by WHERE event_id = @e;");
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(HallEventRow Event, IReadOnlyList<HallPricedLine> Lines, DateOnly InvoiceDate)?> InvoiceLinesAsync(ICommandTransaction tx, int propertyId, long eventId, CancellationToken ct)
    {
        var e = await LockEventAsync(tx, propertyId, eventId, ct).ConfigureAwait(false);
        if (e is null || e.Status != "BILLED") return null;
        await using var cmd = Cmd(tx, "SELECT invoice_date FROM hall.event WHERE event_id = @e;");
        P(cmd, "@e", SqlDbType.BigInt, eventId);
        var date = Day(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Billed event without an invoice date."));
        var all = await LinesAsync(tx, "hall.event_line", "event_id", eventId, "", ct).ConfigureAwait(false);
        var priced = all.Select(l =>
        {
            var bps = l.TaxBps ?? throw new InvalidOperationException("Billed hall line without a tax snapshot.");
            var s = GstMath.SplitLine(l.Taxable, bps);
            return new HallPricedLine(l, bps, l.Taxable, s.Cgst, s.Sgst);
        }).ToList();
        return (e, priced, date);
    }
}
