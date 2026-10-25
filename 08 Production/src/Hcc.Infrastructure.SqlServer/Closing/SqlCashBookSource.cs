using System.Data;
using Hcc.Application.Closing;
using Hcc.Domain.Closing;
using Hcc.Contracts.Closing;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Closing;

/// <summary>Read-only cash-book source: P4's shared drawer (pay.shift, pay.cash_movement) plus the P6 lock tables (cls.*).</summary>
public sealed class SqlCashBookSource(SqlConnectionFactory connections) : ICashBookSource
{
    private const string Sql = """
        SELECT s.shift_id, t.code, s.business_date, s.status, s.opening_float_paise, s.expected_paise, s.counted_paise, s.submitted_by, a.decided_by,
               COALESCE(s.difference_reason, a.reason)
          FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
          LEFT JOIN bill.approval a ON a.approval_id = s.approval_id AND a.status = 'Approved'
         WHERE t.property_id = @p AND s.business_date BETWEEN @from AND @to ORDER BY s.shift_id;
        SELECT m.movement_id, m.shift_id, t.code, m.business_date, m.kind, m.amount_paise, m.label, m.reference, m.principal_id, m.recorded_utc
          FROM pay.cash_movement m JOIN pay.till t ON t.till_id = m.till_id
         WHERE t.property_id = @p AND m.business_date BETWEEN @from AND @to ORDER BY m.movement_id;
        SELECT x.code, x.carry FROM (
          SELECT t.code, ROW_NUMBER() OVER (PARTITION BY t.till_id ORDER BY s.shift_id DESC) rn,
                 COALESCE(s.counted_paise, s.opening_float_paise + ISNULL((SELECT SUM(m.amount_paise) FROM pay.cash_movement m WHERE m.shift_id = s.shift_id), 0)) carry
            FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id
           WHERE t.property_id = @p AND s.business_date < @from) x WHERE x.rn = 1;
        SELECT business_date, locked, locked_by, locked_utc FROM cls.cashbook_day WHERE property_id = @p AND business_date BETWEEN @from AND @to;
        SELECT business_date, action, principal_id, at_utc, reason FROM cls.cashbook_event
         WHERE property_id = @p AND business_date BETWEEN @from AND @to ORDER BY event_id;
        SELECT business_date FROM cls.business_day WHERE property_id = @p AND business_date BETWEEN @from AND @to;
        """;

    public Task<CashBookData> LoadAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ClosingSql.RunAsync(connections, null, Sql, async c =>
        {
            await using var rd = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var shifts = new List<CashBookShift>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                shifts.Add(new CashBookShift(rd.GetInt64(0), rd.GetString(1), ClosingSql.Date(rd, 2), rd.GetString(3), rd.GetInt64(4), rd.IsDBNull(5) ? null : rd.GetInt64(5),
                    rd.IsDBNull(6) ? null : rd.GetInt64(6), rd.IsDBNull(7) ? null : rd.GetInt32(7), rd.IsDBNull(8) ? null : rd.GetInt32(8), ClosingSql.Str(rd, 9)));
            var moves = new List<CashBookMovement>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                moves.Add(new CashBookMovement(rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2), ClosingSql.Date(rd, 3), rd.GetString(4), rd.GetInt64(5), rd.GetString(6),
                    rd.GetString(7), rd.GetInt32(8), rd.GetDateTime(9)));
            var carry = new Dictionary<string, long>(StringComparer.Ordinal);
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) carry[rd.GetString(0)] = rd.GetInt64(1);
            var locks = new List<CashBookLock>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                locks.Add(new CashBookLock(ClosingSql.Date(rd, 0), rd.GetBoolean(1), rd.IsDBNull(2) ? null : rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetDateTime(3)));
            var events = new Dictionary<DateOnly, List<CashBookEventView>>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var d = ClosingSql.Date(rd, 0);
                if (!events.TryGetValue(d, out var l)) events[d] = l = [];
                l.Add(new CashBookEventView(rd.GetString(1), rd.GetInt32(2), rd.GetDateTime(3), ClosingSql.Str(rd, 4)));
            }

            var closed = new HashSet<DateOnly>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) closed.Add(ClosingSql.Date(rd, 0));
            return new CashBookData(shifts, moves, carry, locks, events.ToDictionary(e => e.Key, e => (IReadOnlyList<CashBookEventView>)e.Value), closed);
        }, ct, ("@p", SqlDbType.Int, propertyId), ("@from", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)), ("@to", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue)));
}
