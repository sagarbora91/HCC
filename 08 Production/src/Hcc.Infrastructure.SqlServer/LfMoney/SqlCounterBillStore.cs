using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Counter;
using Hcc.Contracts;
using Hcc.Domain.Counter;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Commands;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.LfMoney;

/// <summary>SQL adapter of <see cref="ICounterBillStore"/> (LF A7: 4688 ctr.bill / ctr.bill_line, 4691 ops.udhaar_receivable.counter_bill_id).</summary>
public sealed class SqlCounterBillStore : ICounterBillStore
{
    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The counter-bill store needs the SQL command transaction.")).Command(sql);

    private static void P(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return DateOnly.FromDateTime((DateTime)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.")));
    }

    public async Task<long> InsertAsync(ICommandTransaction tx, NewCounterBill b, IReadOnlyList<CounterLine> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(lines);
        await using var cmd = Cmd(tx, """
            INSERT ctr.bill(property_id, invoice_no, business_date, issued_utc, issued_by, party_name, party_phone, party_gstin, party_address, guest_id, company_id,
              discount_paise, discount_reason, net_paise, tax_paise, cgst_paise, sgst_paise, total_paise, remark, settlement_mode, method, reference, status, idem_key)
            OUTPUT INSERTED.bill_id
            VALUES (@p, @no, @d, @u, @by, @n, @ph, @gst, @addr, @g, @co, @disc, @dr, @net, @tax, @cg, @sg, @tot, @rem, @mode, @m, @ref, 'Issued', @k);
            """);
        P(cmd, "@p", SqlDbType.Int, b.PropertyId);
        P(cmd, "@no", SqlDbType.VarChar, b.TempNo);
        P(cmd, "@d", SqlDbType.Date, D(b.BusinessDate));
        P(cmd, "@u", SqlDbType.DateTime2, b.Utc);
        P(cmd, "@by", SqlDbType.Int, b.By);
        P(cmd, "@n", SqlDbType.NVarChar, b.Party.Name);
        P(cmd, "@ph", SqlDbType.VarChar, b.Party.Phone);
        P(cmd, "@gst", SqlDbType.Char, b.Party.Gstin);
        P(cmd, "@addr", SqlDbType.NVarChar, b.Party.Address is { Length: > 200 } a ? a[..200] : b.Party.Address);
        P(cmd, "@g", SqlDbType.BigInt, b.Party.GuestId);
        P(cmd, "@co", SqlDbType.BigInt, b.CompanyId);
        P(cmd, "@disc", SqlDbType.BigInt, b.Discount.Value);
        P(cmd, "@dr", SqlDbType.NVarChar, b.DiscountReason);
        P(cmd, "@net", SqlDbType.BigInt, b.Totals.Net.Value);
        P(cmd, "@tax", SqlDbType.BigInt, b.Totals.Tax.Value);
        P(cmd, "@cg", SqlDbType.BigInt, b.Totals.Cgst.Value);
        P(cmd, "@sg", SqlDbType.BigInt, b.Totals.Sgst.Value);
        P(cmd, "@tot", SqlDbType.BigInt, b.Totals.Total.Value);
        P(cmd, "@rem", SqlDbType.NVarChar, b.Remark);
        P(cmd, "@mode", SqlDbType.VarChar, b.Mode);
        P(cmd, "@m", SqlDbType.VarChar, b.Method);
        P(cmd, "@ref", SqlDbType.NVarChar, b.Reference);
        P(cmd, "@k", SqlDbType.VarChar, b.IdemKey.Length <= 80 ? b.IdemKey : null);
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        foreach (var l in lines)
        {
            await using var li = Cmd(tx, """
                INSERT ctr.bill_line(bill_id, seq, label, extra_id, menu_item_id, qty, unit_paise, amount_paise, gst_category, tax_bps, sac)
                VALUES (@b, @s, @l, @e, @mi, @q, @u, @a, @c, @bps, @sac);
                """);
            P(li, "@b", SqlDbType.BigInt, id);
            P(li, "@s", SqlDbType.Int, l.Seq);
            P(li, "@l", SqlDbType.NVarChar, l.Label);
            P(li, "@e", SqlDbType.VarChar, l.ExtraId);
            P(li, "@mi", SqlDbType.VarChar, l.MenuItemId);
            P(li, "@q", SqlDbType.Int, l.Qty);
            P(li, "@u", SqlDbType.BigInt, l.Unit.Value);
            P(li, "@a", SqlDbType.BigInt, l.Amount.Value);
            P(li, "@c", SqlDbType.VarChar, GstCategories.Code(l.Category));
            P(li, "@bps", SqlDbType.Int, l.Bps);
            P(li, "@sac", SqlDbType.Char, l.Sac);
            await li.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task SetInvoiceAsync(ICommandTransaction tx, long billId, long invoiceId, string invoiceNo, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE ctr.bill SET invoice_id = @i, invoice_no = @no WHERE bill_id = @b;");
        P(cmd, "@i", SqlDbType.BigInt, invoiceId);
        P(cmd, "@no", SqlDbType.VarChar, invoiceNo);
        P(cmd, "@b", SqlDbType.BigInt, billId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetSettlementAsync(ICommandTransaction tx, long billId, int? tillId, long? movementId, long? udhaarId, CancellationToken ct)
    {
        // receipt_id carries the drawer movement id of a cash bill (a counter bill has no folio receipt); its till is read back from pay.cash_movement
        await using var cmd = Cmd(tx, "UPDATE ctr.bill SET receipt_id = @r, udhaar_id = @u WHERE bill_id = @b;");
        P(cmd, "@r", SqlDbType.BigInt, movementId);
        P(cmd, "@u", SqlDbType.BigInt, udhaarId);
        P(cmd, "@b", SqlDbType.BigInt, billId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(CounterBillHeader Bill, IReadOnlyList<CounterLine> Lines)?> LockAsync(ICommandTransaction tx, int propertyId, long billId, CancellationToken ct)
    {
        CounterBillHeader? h = null;
        await using (var cmd = Cmd(tx, """
            SELECT b.bill_id, b.property_id, b.invoice_no, b.invoice_id, b.business_date, b.party_name, b.party_phone, b.party_gstin, b.party_address, b.guest_id, b.company_id,
                   b.discount_paise, b.net_paise, b.tax_paise, b.cgst_paise, b.sgst_paise, b.total_paise, b.settlement_mode, b.method, b.reference, b.udhaar_id, b.status,
                   b.void_approval_id, (SELECT TOP (1) m.till_id FROM pay.cash_movement m WHERE m.movement_id = b.receipt_id AND b.settlement_mode = 'paid')
            FROM ctr.bill b WITH (UPDLOCK, ROWLOCK) WHERE b.bill_id = @b AND b.property_id = @p;
            """))
        {
            P(cmd, "@b", SqlDbType.BigInt, billId);
            P(cmd, "@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
            long? L(int i) => rd.IsDBNull(i) ? null : rd.GetInt64(i);
            var totals = new CounterTotals(new Paise(rd.GetInt64(12)), new Paise(rd.GetInt64(13)), new Paise(rd.GetInt64(14)), new Paise(rd.GetInt64(15)), new Paise(rd.GetInt64(16)));
            h = new CounterBillHeader(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), L(3), DateOnly.FromDateTime(rd.GetDateTime(4)), rd.GetString(5), S(6), S(7), S(8), L(9), L(10),
                new Paise(rd.GetInt64(11)), totals, rd.GetString(17), S(18), S(19), rd.IsDBNull(23) ? null : rd.GetInt32(23), L(20), rd.GetString(21), L(22));
        }

        var lines = new List<CounterLine>();
        await using (var li = Cmd(tx, "SELECT seq, label, extra_id, menu_item_id, qty, unit_paise, gst_category, tax_bps FROM ctr.bill_line WHERE bill_id = @b ORDER BY seq;"))
        {
            P(li, "@b", SqlDbType.BigInt, billId);
            await using var rd = await li.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                lines.Add(new CounterLine(rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetInt32(4),
                    new Paise(rd.GetInt64(5)), GstCategories.Parse(rd.GetString(6)), rd.GetInt32(7)));
        }

        return (h, lines);
    }

    public async Task SetVoidApprovalAsync(ICommandTransaction tx, long billId, long approvalId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE ctr.bill SET void_approval_id = @a WHERE bill_id = @b;");
        P(cmd, "@a", SqlDbType.BigInt, approvalId);
        P(cmd, "@b", SqlDbType.BigInt, billId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkCreditedAsync(ICommandTransaction tx, long billId, string creditNoteNo, string reason, DateOnly date, long approvalId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "UPDATE ctr.bill SET status = 'Credited', credit_note_no = @n, credit_note_reason = @r, credit_date = @d, void_approval_id = @a WHERE bill_id = @b;");
        P(cmd, "@n", SqlDbType.VarChar, creditNoteNo.Length <= 16 ? creditNoteNo : creditNoteNo[..16]);
        P(cmd, "@r", SqlDbType.NVarChar, reason.Length <= 200 ? reason : reason[..200]);
        P(cmd, "@d", SqlDbType.Date, D(date));
        P(cmd, "@a", SqlDbType.BigInt, approvalId);
        P(cmd, "@b", SqlDbType.BigInt, billId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertUdhaarAsync(ICommandTransaction tx, int propertyId, string name, string? mobile, Paise amount, DateOnly date, string kind, int gstBps, long billId,
        string note, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            INSERT ops.udhaar_receivable(property_id, customer_name, mobile, amount_paise, credit_date, note, created_by, created_utc, kind, gst_bps, counter_bill_id)
            OUTPUT INSERTED.receivable_id VALUES (@p, @n, @m, @a, @d, @note, @by, @u, @k, @g, @b);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@n", SqlDbType.NVarChar, name);
        P(cmd, "@m", SqlDbType.Char, mobile);
        P(cmd, "@a", SqlDbType.BigInt, amount.Value);
        P(cmd, "@d", SqlDbType.Date, D(date));
        P(cmd, "@note", SqlDbType.NVarChar, note.Length <= 200 ? note : note[..200]);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@u", SqlDbType.DateTime2, utc);
        P(cmd, "@k", SqlDbType.VarChar, kind);
        P(cmd, "@g", SqlDbType.Int, gstBps);
        P(cmd, "@b", SqlDbType.BigInt, billId);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task VoidUdhaarAsync(ICommandTransaction tx, long udhaarId, string reason, int by, DateOnly date, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            IF EXISTS (SELECT 1 FROM ops.udhaar_settlement WHERE receivable_id = @r) SELECT -1;
            ELSE BEGIN UPDATE ops.udhaar_receivable SET is_void = 1, void_reason = @why, voided_by = @by, voided_date = @d WHERE receivable_id = @r AND is_void = 0; SELECT @@ROWCOUNT; END
            """);
        P(cmd, "@r", SqlDbType.BigInt, udhaarId);
        P(cmd, "@why", SqlDbType.NVarChar, reason.Length <= 200 ? reason : reason[..200]);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@d", SqlDbType.Date, D(date));
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) < 0)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Money was already received against this bill's Udhaar credit: reverse that payment first.");
    }

    public async Task<Paise> OpenCompanyBillsAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COALESCE(SUM(total_paise), 0) FROM ctr.bill WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND company_id = @c AND status = 'Issued';");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        return new Paise(Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture));
    }
}
