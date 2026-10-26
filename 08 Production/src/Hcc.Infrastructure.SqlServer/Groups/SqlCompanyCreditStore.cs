using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Groups;

/// <summary>
/// SQL adapter of company credit (migration 3500, DEC-076). Reads the company's exposure facts in one batch and writes only grp.company_payment.
/// Live stays = Held / Confirmed / InHouse reservations with <c>company_id</c> = the company, except members (block pickups or attached) of an open
/// group master billed to the same company — that master carries their company share.
/// </summary>
public sealed class SqlCompanyCreditStore(SqlConnectionFactory connections) : ICompanyCreditStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The company credit SQL store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static DateOnly D(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    private const string FactsSql = """
        SET NOCOUNT ON;
        SELECT business_date FROM inv.property WHERE property_id = @p;
        SELECT rc.receipt_id, r.reservation_id, r.public_no, rc.business_date, rc.amount_paise
          FROM pay.receipt rc JOIN bill.folio f ON f.folio_id = rc.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
         WHERE rc.company_id = @c AND rc.kind = 'company-credit' AND r.property_id = @p
         ORDER BY rc.receipt_id;
        SELECT cp.payment_id, cp.business_date, cp.tender, cp.reference, cp.invoice_no, cp.amount_paise, cp.principal_id, cp.recorded_utc
          FROM grp.company_payment cp JOIN grp.company_account a ON a.company_id = cp.company_id
         WHERE cp.company_id = @c AND a.property_id = @p
         ORDER BY cp.payment_id;
        DECLARE @s TABLE(reservation_id BIGINT PRIMARY KEY);
        INSERT @s(reservation_id)
          SELECT r.reservation_id FROM inv.reservation r
           WHERE r.company_id = @c AND r.property_id = @p AND r.status IN ('Held', 'Confirmed', 'InHouse')
             AND NOT EXISTS (SELECT 1 FROM grp.group_account g
                              WHERE g.company_id = @c AND g.property_id = @p AND g.status = 'Open'
                                AND ((g.group_block_id IS NOT NULL AND g.group_block_id = r.group_block_id)
                                     OR EXISTS (SELECT 1 FROM grp.member m WHERE m.group_id = g.group_id AND m.reservation_id = r.reservation_id)));
        SELECT r.reservation_id, r.public_no, r.guest_name, r.status,
               ISNULL((SELECT SUM(CASE rc.kind WHEN 'payment' THEN rc.amount_paise WHEN 'refund' THEN -rc.amount_paise ELSE 0 END)
                         FROM pay.receipt rc JOIN bill.folio f ON f.folio_id = rc.folio_id WHERE f.reservation_id = r.reservation_id), 0),
               ISNULL((SELECT SUM(rc.amount_paise)
                         FROM pay.receipt rc JOIN bill.folio f ON f.folio_id = rc.folio_id WHERE f.reservation_id = r.reservation_id AND rc.kind = 'company-credit'), 0)
          FROM @s s JOIN inv.reservation r ON r.reservation_id = s.reservation_id
         ORDER BY r.public_no;
        SELECT f.reservation_id, c.charge_id, c.kind, c.service_date, c.label, c.amount_paise, c.tax_bps, c.tax_paise, c.reverses_charge_id
          FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN @s s ON s.reservation_id = f.reservation_id
         ORDER BY c.charge_id;
        SELECT n.reservation_id, n.night, n.rate_paise, n.tax_bps
          FROM inv.reservation_night n JOIN @s s ON s.reservation_id = n.reservation_id
         ORDER BY n.reservation_id, n.night;
        SELECT g.group_id FROM grp.group_account g WHERE g.company_id = @c AND g.property_id = @p AND g.status = 'Open' ORDER BY g.group_id;
        """;

    public async Task<CompanyCreditFacts> LoadAsync(ICommandTransaction? tx, int propertyId, long companyId, CancellationToken ct = default)
    {
        if (tx is not null)
        {
            await using var cmd = Sql(tx).Command(FactsSql);
            return await ReadFactsAsync(cmd, propertyId, companyId, ct).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var c2 = connections.Command(FactsSql, conn);
        return await ReadFactsAsync(c2, propertyId, companyId, ct).ConfigureAwait(false);
    }

    private static async Task<CompanyCreditFacts> ReadFactsAsync(SqlCommand cmd, int propertyId, long companyId, CancellationToken ct)
    {
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var businessDate = await rd.ReadAsync(ct).ConfigureAwait(false) ? D(rd, 0) : DateOnly.MinValue;
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var credits = new List<CompanyCreditReceiptRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            credits.Add(new CompanyCreditReceiptRow(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), D(rd, 3), new Paise(rd.GetInt64(4))));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var payments = new List<CompanyPaymentRow>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            payments.Add(new CompanyPaymentRow(rd.GetInt64(0), D(rd, 1), rd.GetString(2), rd.GetString(3), S(rd, 4), new Paise(rd.GetInt64(5)), rd.GetInt32(6),
                DateTime.SpecifyKind(rd.GetDateTime(7), DateTimeKind.Utc)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var heads = new List<(long Id, string No, string Guest, string Status, Paise Paid, Paise Credit)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            heads.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), new Paise(rd.GetInt64(4)), new Paise(rd.GetInt64(5))));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var lines = new List<(long ReservationId, PostedLine Line)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            lines.Add((rd.GetInt64(0), new PostedLine(rd.GetInt64(1), BillingCodes.ParseChargeKind(rd.GetString(2)), D(rd, 3), rd.GetString(4), new Paise(rd.GetInt64(5)),
                rd.GetInt32(6), new Paise(rd.GetInt64(7)), rd.IsDBNull(8) ? null : rd.GetInt64(8))));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var nights = new List<(long ReservationId, DateOnly Night, Paise Rate, int Bps)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            nights.Add((rd.GetInt64(0), D(rd, 1), new Paise(rd.GetInt64(2)), rd.GetInt32(3)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var groups = new List<long>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) groups.Add(rd.GetInt64(0));
        var stays = heads.Select(h => new CompanyStayRow(h.Id, h.No, h.Guest, h.Status, lines.Where(l => l.ReservationId == h.Id).Select(l => l.Line).ToList(), h.Paid, h.Credit,
            nights.Where(n => n.ReservationId == h.Id).Select(n => (n.Night, n.Rate, n.Bps)).ToList())).ToList();
        return new CompanyCreditFacts(businessDate, credits, payments, stays, groups);
    }

    public async Task<long> InsertPaymentAsync(ICommandTransaction tx, NewCompanyPayment payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        await using var cmd = Sql(tx).Command("""
            INSERT grp.company_payment(company_id, business_date, tender, reference, invoice_no, amount_paise, principal_id, recorded_utc)
            OUTPUT INSERTED.payment_id VALUES (@c, @d, @t, @r, @i, @a, @u, @utc);
            """);
        P(cmd, "@c", SqlDbType.BigInt, payment.CompanyId);
        P(cmd, "@d", SqlDbType.Date, payment.BusinessDate.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@t", SqlDbType.VarChar, payment.Tender, 16);
        P(cmd, "@r", SqlDbType.NVarChar, payment.Reference, 120);
        P(cmd, "@i", SqlDbType.VarChar, payment.InvoiceNo, 16);
        P(cmd, "@a", SqlDbType.BigInt, payment.Amount.EnsureReceiptAmount().Value);
        P(cmd, "@u", SqlDbType.Int, payment.PrincipalId);
        P(cmd, "@utc", SqlDbType.DateTime2, payment.RecordedUtc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
