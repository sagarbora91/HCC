using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// SQL Server implementation of <see cref="IGstMonthStore"/> (ACC-2 acc2-gst; migrations 4246 acc.month_lock / acc.month_lock_request,
/// 4247 acc.gst_setoff, 4248 cmp.profile.gst_on_advances). Sources go through <see cref="AccountingVoucherProjector.InsertSourceAsync"/> in the
/// command's transaction (event type <c>command:&lt;op&gt;</c>, no outbox row).
/// </summary>
public sealed class SqlGstMonthStore(SqlConnectionFactory connections) : IGstMonthStore
{
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The GST month store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    private static DateTime Utc(SqlDataReader rd, int i) => DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    public Task<IReadOnlyList<StoredGstSetoff>> SetoffsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT setoff_id, setoff_month, posted_on, source_key, output_igst_paise, output_cgst_paise, output_sgst_paise, credit_igst_paise, credit_cgst_paise,
                       credit_sgst_paise, cash_available_paise, posted_by, posted_utc
                FROM acc.gst_setoff WHERE property_id = @p AND superseded_utc IS NULL ORDER BY setoff_month;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<StoredGstSetoff>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var figures = GstSetOff.Compute(new GstHeads(rd.GetInt64(4), rd.GetInt64(5), rd.GetInt64(6)), new GstHeads(rd.GetInt64(7), rd.GetInt64(8), rd.GetInt64(9)), rd.GetInt64(10));
                list.Add(new StoredGstSetoff(rd.GetInt64(0), rd.GetString(1), AccSql.Date(rd, 2), rd.GetString(3), figures, rd.GetInt32(11), Utc(rd, 12)));
            }

            return (IReadOnlyList<StoredGstSetoff>)list;
        }, ct);

    public Task<long> InsertSetoffAsync(ICommandTransaction tx, int propertyId, StoredGstSetoff setoff, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(setoff);
        return WithAsync(tx, async (c, t) =>
        {
            var x = setoff.Figures;
            await using var cmd = Cmd("""
                UPDATE acc.gst_setoff SET superseded_utc = @now WHERE property_id = @p AND setoff_month = @m AND superseded_utc IS NULL;
                INSERT acc.gst_setoff(property_id, setoff_month, posted_on, source_key, output_igst_paise, output_cgst_paise, output_sgst_paise, credit_igst_paise,
                    credit_cgst_paise, credit_sgst_paise, cash_available_paise, payable_paise, challan_paise, posted_by, posted_utc)
                OUTPUT INSERTED.setoff_id
                VALUES (@p, @m, @d, @k, @oi, @oc, @os, @ci, @cc, @cs, @cash, @pay, @ch, @by, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@m", SqlDbType.Char, setoff.Month, 7);
            cmd.Parameters.Set("@d", SqlDbType.Date, setoff.PostedOn.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.Set("@k", SqlDbType.VarChar, setoff.SourceKey, 80);
            cmd.Parameters.Set("@oi", SqlDbType.BigInt, x.Output.Igst);
            cmd.Parameters.Set("@oc", SqlDbType.BigInt, x.Output.Cgst);
            cmd.Parameters.Set("@os", SqlDbType.BigInt, x.Output.Sgst);
            cmd.Parameters.Set("@ci", SqlDbType.BigInt, x.Credit.Igst);
            cmd.Parameters.Set("@cc", SqlDbType.BigInt, x.Credit.Cgst);
            cmd.Parameters.Set("@cs", SqlDbType.BigInt, x.Credit.Sgst);
            cmd.Parameters.Set("@cash", SqlDbType.BigInt, x.CashAvailable);
            cmd.Parameters.Set("@pay", SqlDbType.BigInt, x.Payable);
            cmd.Parameters.Set("@ch", SqlDbType.BigInt, x.Challan);
            cmd.Parameters.Set("@by", SqlDbType.Int, setoff.PostedBy);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }, ct);
    }

    public Task<bool> InsertSourceAsync(ICommandTransaction tx, int propertyId, AccountingSource source, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return WithAsync(tx, (c, t) => AccountingVoucherProjector.InsertSourceAsync(c, t!, propertyId, source, "command:" + tx.Context.Operation, null, now, ct), ct);
    }

    public Task<IReadOnlyList<SourceEntry>> SourceEntriesAsync(ICommandTransaction tx, int propertyId, string sourceKey, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT se.map_key, se.debit_paise, se.credit_paise, se.party_id FROM acc.source s JOIN acc.source_entry se ON se.source_id = s.source_id
                WHERE s.property_id = @p AND s.source_key = @k ORDER BY se.line_no;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, sourceKey, 80);
            var list = new List<SourceEntry>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new SourceEntry(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2), AccSql.StrOrNull(rd, 3)));
            return (IReadOnlyList<SourceEntry>)list;
        }, ct);

    public Task<IReadOnlyList<MonthLockRow>> MonthLocksAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT l.month_lock_id, l.lock_month, l.locked_utc, l.locked_by, l.setoff_id, g.source_key, l.unlocked_utc, l.unlocked_by, l.unlock_reason
                FROM acc.month_lock l LEFT JOIN acc.gst_setoff g ON g.setoff_id = l.setoff_id
                WHERE l.property_id = @p ORDER BY l.lock_month, l.month_lock_id;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<MonthLockRow>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new MonthLockRow(rd.GetInt64(0), rd.GetString(1), Utc(rd, 2), rd.GetInt32(3), rd.IsDBNull(4) ? null : rd.GetInt64(4), AccSql.StrOrNull(rd, 5),
                    rd.IsDBNull(6) ? null : Utc(rd, 6), rd.IsDBNull(7) ? null : rd.GetInt32(7), AccSql.StrOrNull(rd, 8)));
            return (IReadOnlyList<MonthLockRow>)list;
        }, ct);

    public Task<long> InsertMonthLockAsync(ICommandTransaction tx, int propertyId, string month, long? setoffId, int by, DateTime now, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                INSERT acc.month_lock(property_id, lock_month, setoff_id, locked_by, locked_utc) OUTPUT INSERTED.month_lock_id VALUES (@p, @m, @s, @by, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@m", SqlDbType.Char, month, 7);
            cmd.Parameters.Set("@s", SqlDbType.BigInt, setoffId);
            cmd.Parameters.Set("@by", SqlDbType.Int, by);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }, ct);

    public Task UnlockMonthAsync(ICommandTransaction tx, long lockId, int by, DateTime now, string reason, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("UPDATE acc.month_lock SET unlocked_by = @by, unlocked_utc = @now, unlock_reason = @r WHERE month_lock_id = @id AND unlocked_utc IS NULL;", c, t);
            cmd.Parameters.Set("@id", SqlDbType.BigInt, lockId);
            cmd.Parameters.Set("@by", SqlDbType.Int, by);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            cmd.Parameters.Set("@r", SqlDbType.NVarChar, reason, 200);
            if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new InvalidOperationException($"Month lock {lockId.ToString(CultureInfo.InvariantCulture)} is not current.");
            return 0;
        }, ct);

    public Task InsertMonthLockRequestAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("INSERT acc.month_lock_request(property_id, lock_month, requested_by, requested_utc) VALUES (@p, @m, @by, @now);", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@m", SqlDbType.Char, month, 7);
            cmd.Parameters.Set("@by", SqlDbType.Int, by);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);

    public Task<IReadOnlyList<string>> OpenMonthLockRequestsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT r.lock_month FROM acc.month_lock_request r WHERE r.property_id = @p
                GROUP BY r.lock_month
                HAVING MAX(r.requested_utc) > ISNULL((SELECT MAX(l.locked_utc) FROM acc.month_lock l WHERE l.property_id = @p AND l.lock_month = r.lock_month), '19000101')
                   AND NOT EXISTS (SELECT 1 FROM acc.month_lock l WHERE l.property_id = @p AND l.lock_month = r.lock_month AND l.unlocked_utc IS NULL)
                ORDER BY r.lock_month;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<string>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(rd.GetString(0));
            return (IReadOnlyList<string>)list;
        }, ct);

    public Task<bool> GstOnAdvancesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT gst_on_advances FROM cmp.profile WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
        }, ct);

    public Task<bool> SetGstOnAdvancesAsync(ICommandTransaction tx, int propertyId, bool on, int by, DateTime now, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("UPDATE cmp.profile SET gst_on_advances = @on, gst_on_advances_by = @by, gst_on_advances_utc = @now WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@on", SqlDbType.Bit, on);
            cmd.Parameters.Set("@by", SqlDbType.Int, by);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        }, ct);

    // ── The deposit rule's reads (projector, ACC-2 gst section) ──

    /// <summary>
    /// The switch and the booked room of a folio (bill.folio → inv.reservation): the GST rate of its first agreed night (inv.reservation_night
    /// tax snapshot), else its nightly rate and arrival for the dated GST master. Null when the switch is off or the folio has no reservation.
    /// </summary>
    internal static async Task<(int? Bps, long RatePaise, DateOnly Arrival)?> AdvanceContextAsync(SqlConnection c, SqlTransaction t, int propertyId, long folioId, CancellationToken ct)
    {
        await using (var sw = new SqlCommand("SELECT gst_on_advances FROM cmp.profile WHERE property_id = @p;", c, t))
        {
            sw.Parameters.Set("@p", SqlDbType.Int, propertyId);
            if (await sw.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) return null;
        }

        await using var cmd = new SqlCommand("""
            SELECT TOP (1) r.arrival, ISNULL(r.nightly_rate_paise, 0),
                   (SELECT TOP (1) n.tax_bps FROM inv.reservation_night n WHERE n.reservation_id = r.reservation_id ORDER BY n.night)
            FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE f.folio_id = @f;
            """, c, t);
        cmd.Parameters.Set("@f", SqlDbType.BigInt, folioId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (rd.IsDBNull(2) ? null : rd.GetInt32(2), rd.GetInt64(1), AccSql.Date(rd, 0));
    }

    /// <summary>Advance GST still held for a folio: Σ booked advance-GST lines − Σ reversals (the 1350 key, by source key prefix).</summary>
    internal static async Task<long> AdvanceGstHeldAsync(SqlConnection c, SqlTransaction t, int propertyId, long folioId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT ISNULL(SUM(se.debit_paise - se.credit_paise), 0) FROM acc.source s JOIN acc.source_entry se ON se.source_id = s.source_id
            WHERE s.property_id = @p AND s.source_key LIKE @k AND se.map_key = @m;
            """, c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@k", SqlDbType.VarChar, "advance-gst:folio:" + folioId.ToString(CultureInfo.InvariantCulture) + "/%", 80);
        cmd.Parameters.Set("@m", SqlDbType.VarChar, GstSetOff.KeyAdvance, 80);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
