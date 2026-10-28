using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Application.Groups;
using Hcc.Application.Ota;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-2 flows (lane acc2-flows; 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §7, §8): the SQL side of the cash guards (cash already received from /
/// paid to a party), the OTA channel of a folio, OTA payouts (migration 4251), Udhaar payment reversals (4252) and company retire (4253). Writers take
/// the command transaction; readers run inside it (or on their own connection for the payout list).
/// </summary>
public sealed class SqlFlowsStore(SqlConnectionFactory connections) : ICashFlowReader, ICashDeductionMarker, IFolioChannelSource, IOtaPayoutStore, IUdhaarReversalStore, ICompanyRetireStore
{
    private static SqlCommandTransaction Tx(ICommandTransaction tx)
        => tx as SqlCommandTransaction ?? throw new InvalidOperationException("The ACC-2 flows store needs a SQL command transaction.");

    private static SqlCommand Cmd(ICommandTransaction tx, string sql)
    {
        var s = Tx(tx);
        return new SqlCommand(sql, s.Connection, s.Transaction);
    }

    private static async Task<long> LongAsync(SqlCommand cmd, CancellationToken ct) => Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L, System.Globalization.CultureInfo.InvariantCulture);

    // ── ICashFlowReader (B9 / B10) ──

    public async Task<(long Day, long Scope)> ReceiptTotalsAsync(ICommandTransaction tx, int propertyId, CashParty party, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(party);
        var sql = party.Kind switch
        {
            // A guest's booking: cash payments and deposits on its folio (cash = taken into a shift).
            CashParty.Folio => """
                SELECT ISNULL(SUM(CASE WHEN r.business_date = @d THEN r.amount_paise ELSE 0 END), 0), ISNULL(SUM(r.amount_paise), 0)
                FROM pay.receipt r WHERE r.folio_id = @id AND r.kind IN ('payment','deposit') AND r.shift_id IS NOT NULL;
                """,
            // An Udhaar credit: its cash desk settlements (a reversal is negative).
            CashParty.Udhaar => """
                SELECT ISNULL(SUM(CASE WHEN s.settle_date = @d THEN s.amount_paise ELSE 0 END), 0), ISNULL(SUM(s.amount_paise), 0)
                FROM ops.udhaar_settlement s JOIN ops.udhaar_receivable u ON u.receivable_id = s.receivable_id
                WHERE s.receivable_id = @id AND u.property_id = @p AND s.till_id IS NOT NULL;
                """,
            _ => null,
        };
        if (sql is null) return (0, 0);
        await using var cmd = Cmd(tx, sql);
        cmd.Parameters.Set("@id", SqlDbType.BigInt, party.Id);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetInt64(0), rd.GetInt64(1)) : (0, 0);
    }

    public async Task<long> PaymentDayTotalAsync(ICommandTransaction tx, int propertyId, CashParty party, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(party);
        if (party.Kind != CashParty.Vendor || string.IsNullOrWhiteSpace(party.Text)) return 0;
        // Posted drawer-cash expenses to the same vendor on the date (reversal records are negative and posted).
        await using var cmd = Cmd(tx, """
            SELECT ISNULL(SUM(amount_paise), 0) FROM ops.expense
            WHERE property_id = @p AND business_date = @d AND status IN ('Posted','Reversed') AND till_id IS NOT NULL AND vendor = @v;
            """);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Set("@v", SqlDbType.NVarChar, party.Text.Trim(), 120);
        return await LongAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task MarkDeductionLostAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "UPDATE ops.expense SET cash_deduction_lost = 1 WHERE expense_id = @e;");
        cmd.Parameters.Set("@e", SqlDbType.BigInt, expenseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── IFolioChannelSource (A13) ──

    public async Task<FolioChannel?> ChannelOfFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT TOP (1) ch.channel_id, ch.code, ch.name, ch.status FROM bill.folio f
            JOIN ota.message m ON m.reservation_id = f.reservation_id AND m.kind = 'New' JOIN ota.channel ch ON ch.channel_id = m.channel_id
            WHERE f.folio_id = @f ORDER BY m.message_id;
            """);
        cmd.Parameters.Set("@f", SqlDbType.BigInt, folioId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new FolioChannel(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3)) : null;
    }

    // ── IOtaPayoutStore (A13 / DEC-150) ──

    private const string ChannelFolios = """
        SELECT f.folio_id FROM ota.message m JOIN bill.folio f ON f.reservation_id = m.reservation_id
        WHERE m.channel_id = @c AND m.kind = 'New' AND m.reservation_id IS NOT NULL
        """;

    public async Task<PayoutChannel?> LockChannelAsync(ICommandTransaction tx, int propertyId, int channelId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT channel_id, code, name, status, itc_allowed FROM ota.channel WITH (UPDLOCK, HOLDLOCK) WHERE channel_id = @c AND property_id = @p;");
        cmd.Parameters.Set("@c", SqlDbType.Int, channelId);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new PayoutChannel(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4)) : null;
    }

    public async Task<long> OpenReceivableAsync(ICommandTransaction tx, int channelId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, $"""
            SELECT ISNULL((SELECT SUM(r.amount_paise) FROM pay.receipt r WHERE r.kind = 'ota-credit' AND r.folio_id IN ({ChannelFolios})), 0)
                 - ISNULL((SELECT SUM(gross_paise) FROM ota.payout WHERE channel_id = @c), 0);
            """);
        cmd.Parameters.Set("@c", SqlDbType.Int, channelId);
        return await LongAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<long>> CreditedReservationsAsync(ICommandTransaction tx, int channelId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT DISTINCT f.reservation_id FROM pay.receipt r JOIN bill.folio f ON f.folio_id = r.folio_id
            JOIN ota.message m ON m.reservation_id = f.reservation_id AND m.kind = 'New' WHERE r.kind = 'ota-credit' AND m.channel_id = @c;
            """);
        cmd.Parameters.Set("@c", SqlDbType.Int, channelId);
        var set = new HashSet<long>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetInt64(0));
        return set;
    }

    public async Task<(string Name, bool Active)?> BankAccountAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT name, active FROM mst.bank_account WHERE property_id = @p AND kind = 'bank' AND code = @code;");
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@code", SqlDbType.VarChar, code, 20);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? (rd.GetString(0), rd.GetBoolean(1)) : null;
    }

    public async Task<bool> StatementTakenAsync(ICommandTransaction tx, int channelId, string statementRef, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM ota.payout WITH (UPDLOCK, HOLDLOCK) WHERE channel_id = @c AND statement_ref = @r;");
        cmd.Parameters.Set("@c", SqlDbType.Int, channelId);
        cmd.Parameters.Set("@r", SqlDbType.NVarChar, statementRef, 40);
        return await LongAsync(cmd, ct).ConfigureAwait(false) > 0;
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        return DateOnly.FromDateTime((DateTime)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Property not found.")));
    }

    public async Task<long> InsertPayoutAsync(ICommandTransaction tx, NewOtaPayout payout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payout);
        long id;
        await using (var cmd = Cmd(tx, """
            INSERT ota.payout(property_id, channel_id, business_date, statement_ref, gross_paise, commission_paise, commission_gst_paise, tcs_paise, net_paise,
              bank_account, itc_allowed, recorded_by, recorded_utc)
            OUTPUT INSERTED.payout_id VALUES (@p, @c, @d, @r, @g, @cm, @cg, @t, @n, @b, @itc, @by, @now);
            """))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, payout.PropertyId);
            cmd.Parameters.Set("@c", SqlDbType.Int, payout.ChannelId);
            cmd.Parameters.Set("@d", SqlDbType.Date, payout.BusinessDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.Set("@r", SqlDbType.NVarChar, payout.StatementRef, 40);
            cmd.Parameters.Set("@g", SqlDbType.BigInt, payout.GrossPaise);
            cmd.Parameters.Set("@cm", SqlDbType.BigInt, payout.CommissionPaise);
            cmd.Parameters.Set("@cg", SqlDbType.BigInt, payout.CommissionGstPaise);
            cmd.Parameters.Set("@t", SqlDbType.BigInt, payout.TcsPaise);
            cmd.Parameters.Set("@n", SqlDbType.BigInt, payout.NetPaise);
            cmd.Parameters.Set("@b", SqlDbType.VarChar, payout.BankAccount, 20);
            cmd.Parameters.Set("@itc", SqlDbType.Bit, payout.ItcAllowed);
            cmd.Parameters.Set("@by", SqlDbType.Int, payout.RecordedBy);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, payout.RecordedUtc);
            id = await LongAsync(cmd, ct).ConfigureAwait(false);
        }

        foreach (var r in payout.ReservationIds)
        {
            await using var b = Cmd(tx, "INSERT ota.payout_booking(payout_id, reservation_id) VALUES (@id, @r);");
            b.Parameters.Set("@id", SqlDbType.BigInt, id);
            b.Parameters.Set("@r", SqlDbType.BigInt, r);
            await b.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return id;
    }

    public async Task SetItcAllowedAsync(ICommandTransaction tx, int channelId, bool itcAllowed, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "UPDATE ota.channel SET itc_allowed = @i WHERE channel_id = @c;");
        cmd.Parameters.Set("@i", SqlDbType.Bit, itcAllowed);
        cmd.Parameters.Set("@c", SqlDbType.Int, channelId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OtaPayoutView>> ListPayoutsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT p.payout_id, p.channel_id, p.business_date, p.statement_ref, p.gross_paise, p.commission_paise, p.commission_gst_paise, p.tcs_paise, p.net_paise,
              p.bank_account, p.recorded_by, p.recorded_utc,
              (SELECT STRING_AGG(CAST(b.reservation_id AS varchar(20)), ',') FROM ota.payout_booking b WHERE b.payout_id = p.payout_id)
            FROM ota.payout p WHERE p.property_id = @p ORDER BY p.payout_id;
            """, conn, null);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        var list = new List<OtaPayoutView>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var ids = rd.IsDBNull(12) ? [] : rd.GetString(12).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => long.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();
            list.Add(new OtaPayoutView(rd.GetInt64(0), rd.GetInt32(1), AccSql.Date(rd, 2), rd.GetString(3), rd.GetInt64(4), rd.GetInt64(5), rd.GetInt64(6), rd.GetInt64(7), rd.GetInt64(8),
                rd.GetString(9), ids, rd.GetInt32(10), DateTime.SpecifyKind(rd.GetDateTime(11), DateTimeKind.Utc)));
        }

        return list;
    }

    // ── IUdhaarReversalStore (A17b) ──

    public async Task<SettlementReversalState?> StateAsync(ICommandTransaction tx, long settlementId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT s.settlement_id, s.reversal_of, (SELECT TOP (1) x.settlement_id FROM ops.udhaar_settlement x WITH (UPDLOCK, HOLDLOCK) WHERE x.reversal_of = s.settlement_id)
            FROM ops.udhaar_settlement s WITH (UPDLOCK, HOLDLOCK) WHERE s.settlement_id = @s;
            """);
        cmd.Parameters.Set("@s", SqlDbType.BigInt, settlementId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false)
            ? new SettlementReversalState(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetInt64(1), rd.IsDBNull(2) ? null : rd.GetInt64(2))
            : null;
    }

    public async Task<long> InsertReversalAsync(ICommandTransaction tx, NewUdhaarReversal r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var cmd = Cmd(tx, """
            INSERT ops.udhaar_settlement(receivable_id, amount_paise, settle_date, mode, recorded_by, recorded_utc, desk, reference, till_id, reversal_of, reversal_reason)
            OUTPUT INSERTED.settlement_id VALUES (@id, @a, @d, @m, @by, @now, @desk, @ref, @till, @of, @why);
            """);
        cmd.Parameters.Set("@id", SqlDbType.BigInt, r.ReceivableId);
        cmd.Parameters.Set("@a", SqlDbType.BigInt, r.AmountPaise);
        cmd.Parameters.Set("@d", SqlDbType.Date, r.Date.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Set("@m", SqlDbType.NVarChar, r.Mode, 60);
        cmd.Parameters.Set("@by", SqlDbType.Int, r.RecordedBy);
        cmd.Parameters.Set("@now", SqlDbType.DateTime2, r.RecordedUtc);
        cmd.Parameters.Set("@desk", SqlDbType.Bit, r.Desk);
        cmd.Parameters.Set("@ref", SqlDbType.NVarChar, r.Reference, 120);
        cmd.Parameters.Set("@till", SqlDbType.Int, r.TillId);
        cmd.Parameters.Set("@of", SqlDbType.BigInt, r.ReversalOf);
        cmd.Parameters.Set("@why", SqlDbType.NVarChar, r.Reason, 200);
        return await LongAsync(cmd, ct).ConfigureAwait(false);
    }

    // ── ICompanyRetireStore (contract §8, DEC-157) ──

    public async Task SetActiveAsync(ICommandTransaction tx, long companyId, bool active, int principalId, DateTime utc, string? reason, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, active
            ? "UPDATE grp.company_account SET active = 1, retired_by = NULL, retired_utc = NULL, retire_reason = NULL WHERE company_id = @c;"
            : "UPDATE grp.company_account SET active = 0, retired_by = @by, retired_utc = @now, retire_reason = @why WHERE company_id = @c;");
        cmd.Parameters.Set("@c", SqlDbType.BigInt, companyId);
        cmd.Parameters.Set("@by", SqlDbType.Int, principalId);
        cmd.Parameters.Set("@now", SqlDbType.DateTime2, utc);
        cmd.Parameters.Set("@why", SqlDbType.NVarChar, reason, 200);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
