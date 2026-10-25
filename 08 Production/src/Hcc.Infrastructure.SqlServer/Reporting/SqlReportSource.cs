using System.Data;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>
/// Source records of the daily pack, read from the tables the P2–P4 commands write (one batch, one snapshot of the data when run inside
/// the close transaction): rooms and active blocks (P2 <c>inv.room_block</c>, P3 <c>ops.room_block</c>), stays (P3), charges with their
/// tax snapshot (P4), receipts, shifts and cash movements (P4 shared till).
/// </summary>
public sealed class SqlReportSource(SqlConnectionFactory connections) : IReportSource
{
    private const string Sql = """
        SELECT room_id FROM inv.room WHERE property_id = @p ORDER BY room_id;
        SELECT b.room_id, b.from_night, b.to_night FROM inv.room_block b JOIN inv.room r ON r.room_id = b.room_id
         WHERE r.property_id = @p AND b.active = 1 AND (b.from_night IS NULL OR b.from_night <= @to) AND (b.to_night IS NULL OR b.to_night > @from)
        UNION ALL
        SELECT room_id, from_date, to_date FROM ops.room_block
         WHERE property_id = @p AND released_utc IS NULL AND (from_date IS NULL OR from_date <= @to) AND (to_date IS NULL OR to_date > @from);
        SELECT stay_id, reservation_id, arrival, departure, checked_out_date, CASE WHEN status = 'CheckedOut' THEN 1 ELSE 0 END
          FROM gst.stay WHERE property_id = @p
           AND ((arrival <= @to AND departure > @from) OR arrival BETWEEN @from AND @to OR checked_out_date BETWEEN @from AND @to)
         ORDER BY stay_id;
        SELECT c.charge_id, c.folio_id, c.kind, c.service_date, c.label, c.amount_paise, c.tax_bps, c.tax_paise, c.reverses_charge_id, o.kind, o.label
          FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
          LEFT JOIN bill.charge o ON o.charge_id = c.reverses_charge_id
         WHERE r.property_id = @p AND c.service_date BETWEEN @from AND @to ORDER BY c.charge_id;
        SELECT x.receipt_id, x.folio_id, x.kind, x.method, x.amount_paise, x.business_date
          FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
         WHERE r.property_id = @p AND x.business_date BETWEEN @from AND @to ORDER BY x.receipt_id;
        SELECT s.shift_id, s.till_id, t.code, s.business_date, s.status, s.opening_float_paise, s.expected_paise, s.counted_paise, s.submitted_by,
               a.decided_by, a.reason
          FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
          LEFT JOIN bill.approval a ON a.approval_id = s.approval_id AND a.status = 'Approved'
         WHERE t.property_id = @p AND s.business_date BETWEEN @from AND @to ORDER BY s.shift_id;
        SELECT m.movement_id, m.shift_id, m.business_date, m.kind, m.amount_paise
          FROM pay.cash_movement m JOIN pay.till t ON t.till_id = m.till_id
         WHERE t.property_id = @p AND m.business_date BETWEEN @from AND @to ORDER BY m.movement_id;
        """;

    public Task<ReportData> LoadAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, tx, Sql, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var rooms = new List<int>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rooms.Add(rd.GetInt32(0));
            var blocks = new List<ReportRoomBlock>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                blocks.Add(new ReportRoomBlock(rd.GetInt32(0), rd.IsDBNull(1) ? null : ClosingSql.Date(rd, 1), rd.IsDBNull(2) ? null : ClosingSql.Date(rd, 2)));
            var stays = new List<ReportStay>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                stays.Add(new ReportStay(rd.GetInt64(0), rd.GetInt64(1), ClosingSql.Date(rd, 2), ClosingSql.Date(rd, 3), rd.IsDBNull(4) ? null : ClosingSql.Date(rd, 4), rd.GetInt32(5) == 1));
            var lines = new List<ReportLine>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                lines.Add(new ReportLine(rd.GetInt64(0), rd.GetInt64(1), BillingCodes.ParseChargeKind(rd.GetString(2)), ClosingSql.Date(rd, 3), rd.GetString(4),
                    new Paise(rd.GetInt64(5)), rd.GetInt32(6), new Paise(rd.GetInt64(7)), rd.IsDBNull(8) ? null : rd.GetInt64(8),
                    rd.IsDBNull(9) ? null : BillingCodes.ParseChargeKind(rd.GetString(9)), ClosingSql.Str(rd, 10)));
            var receipts = new List<ReportReceipt>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                receipts.Add(new ReportReceipt(rd.GetInt64(0), rd.GetInt64(1), FolioTotals.ParseKind(rd.GetString(2)), rd.GetString(3), new Paise(rd.GetInt64(4)), ClosingSql.Date(rd, 5)));
            var shifts = new List<ReportShift>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                shifts.Add(new ReportShift(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), ClosingSql.Date(rd, 3), rd.GetString(4), new Paise(rd.GetInt64(5)),
                    rd.IsDBNull(6) ? null : new Paise(rd.GetInt64(6)), rd.IsDBNull(7) ? null : new Paise(rd.GetInt64(7)), rd.IsDBNull(8) ? null : rd.GetInt32(8),
                    rd.IsDBNull(9) ? null : rd.GetInt32(9), ClosingSql.Str(rd, 10)));
            var movements = new List<ReportMovement>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                movements.Add(new ReportMovement(rd.GetInt64(0), rd.GetInt64(1), ClosingSql.Date(rd, 2), CashDrawer.Parse(rd.GetString(3)), new Paise(rd.GetInt64(4))));
            return new ReportData(from, to, rooms, blocks, stays, lines, receipts, shifts, movements);
        }, ct, ("@p", SqlDbType.Int, propertyId), ("@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)), ("@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue)));
}
