using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// The ONE adapter that reads a charge's stored CGST / SGST for vouchers (lead, 25 Oct 2026: never recompute tax): agent C's per-charge
/// GST record <c>tax.charge_gst</c> (migration 0600, written by Billing and the POS folio port). A line without a record (posted before
/// DEC-069 or seeded by tests) falls back to splitting the tax STORED on the charge with the same rule (SGST = half toward zero).
/// </summary>
public interface IChargeGstReader
{
    Task<(long Cgst, long Sgst)> ReadAsync(SqlConnection connection, SqlTransaction transaction, long chargeId, long storedTaxPaise, CancellationToken ct);
}

public sealed class StoredChargeGstReader : IChargeGstReader
{
    public async Task<(long Cgst, long Sgst)> ReadAsync(SqlConnection connection, SqlTransaction transaction, long chargeId, long storedTaxPaise, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT cgst_paise, sgst_paise FROM tax.charge_gst WHERE charge_id = @c;", connection, transaction);
        cmd.Parameters.Set("@c", SqlDbType.BigInt, chargeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var (cgst, sgst) = (rd.GetInt64(0), rd.GetInt64(1));
            if (cgst + sgst != storedTaxPaise)
                throw new InvalidOperationException($"Charge {chargeId}: stored CGST + SGST ({cgst} + {sgst}) differs from the charge tax {storedTaxPaise}.");
            return (cgst, sgst);
        }

        return GstSplit.Of(storedTaxPaise);
    }
}

/// <summary>
/// Accounting's outbox consumer (<c>accounting-vouchers</c>): every consumed event becomes at most one source (unique key per
/// property, check-then-insert under lock), in the same transaction as the consumer-dedup row (frozen <see cref="OutboxConsumer"/>).
/// Gaps in today's P4 events are bridged read-only: an executed correction (ApprovalDecided) is read from <c>bill.charge</c>, and
/// accepted cash differences (no event yet; requested <c>ShiftDifferenceDecided</c>) are swept from <c>pay.shift</c>.
/// </summary>
public sealed class AccountingVoucherProjector(SqlConnectionFactory connections, OutboxConsumer outbox, IChargeGstReader gst, TimeProvider clock)
{
    public const string ConsumerName = "accounting-vouchers";

    private const string SweepWhere = """
        s.status = 'Closed' AND a.action = 'cash-difference' AND a.status = 'Approved' AND s.counted_paise <> s.expected_paise
          AND NOT EXISTS (SELECT 1 FROM acc.source x WHERE x.property_id = t.property_id
                          AND x.source_key = CONCAT('shift:', CAST(s.shift_id AS varchar(20)), '/cash-difference'))
        """;

    /// <summary>Unprocessed consumed events + unswept accepted cash differences for a property (blocks locking).</summary>
    public const string PendingSql = """
        SELECT (SELECT COUNT(*) FROM plat.outbox o
                WHERE o.property_id = @p AND o.event_type IN (SELECT value FROM OPENJSON(@types))
                  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = o.outbox_id))
             + (SELECT COUNT(*) FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id JOIN bill.approval a ON a.approval_id = s.approval_id
                WHERE t.property_id = @p AND
        """ + " " + SweepWhere + ");";

    private static readonly JsonSerializerOptions Json = CanonicalJson.SerializerOptions;

    public async Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var n = await outbox.ConsumeAsync(ConsumerName, AccountingEvents.Consumed, ApplyAsync, batchSize, ct).ConfigureAwait(false);
        return n + await SweepCashDifferencesAsync(batchSize, ct).ConfigureAwait(false);
    }

    private sealed record ChargePostedPayload(long FolioId, long ChargeId, string Kind, DateOnly ServiceDate, long AmountPaise, long TaxPaise);

    private sealed record ReceiptRecordedPayload(long FolioId, long ReceiptId, string Kind, string Tender, long AmountPaise, DateOnly BusinessDate);

    private sealed record ApprovalDecidedPayload(long ApprovalId, string Action, string Status, long? FolioId);

    private T Parse<T>(OutboxEvent evt) => JsonSerializer.Deserialize<T>(evt.PayloadJson, Json)
        ?? throw new InvalidOperationException($"Outbox {evt.OutboxId}: empty {evt.EventType} payload.");

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        int propertyId;
        await using (var p = new SqlCommand("SELECT property_id FROM plat.outbox WHERE outbox_id = @o;", connection, transaction))
        {
            p.Parameters.Set("@o", SqlDbType.BigInt, evt.OutboxId);
            if (await p.ExecuteScalarAsync(ct).ConfigureAwait(false) is not int pid) return; // no property: not a posting event
            propertyId = pid;
        }

        AccountingSource? source = null;
        switch (evt.EventType)
        {
            case AccountingEvents.ChargePosted:
            {
                var e = Parse<ChargePostedPayload>(evt);
                var (cgst, sgst) = await gst.ReadAsync(connection, transaction, e.ChargeId, e.TaxPaise, ct).ConfigureAwait(false);
                source = AccountingEventMapper.Charge(e.FolioId, e.ChargeId, e.Kind, e.ServiceDate, e.AmountPaise, cgst, sgst);
                break;
            }

            case AccountingEvents.ReceiptRecorded:
            {
                var e = Parse<ReceiptRecordedPayload>(evt);
                source = AccountingEventMapper.Receipt(e.FolioId, e.ReceiptId, e.Kind, e.Tender, e.BusinessDate, e.AmountPaise);
                break;
            }

            case AccountingEvents.ApprovalDecided:
            {
                var e = Parse<ApprovalDecidedPayload>(evt);
                if (e.Action == "correction" && e.Status == "Executed") source = await CorrectionLineAsync(connection, transaction, e.ApprovalId, ct).ConfigureAwait(false);
                break;
            }

            case AccountingEvents.ShiftDifferenceDecided:
            {
                var e = Parse<ShiftDifferenceDecidedEvent>(evt);
                if (e.Status == "Approved" && e.CountedPaise != e.ExpectedPaise)
                    source = AccountingEventMapper.CashDifference(e.ShiftId, await TillOfAsync(connection, transaction, e.ShiftId, ct).ConfigureAwait(false), e.BusinessDate, e.ExpectedPaise, e.CountedPaise);
                break;
            }

            case AccountingEvents.PosBillSettled:
            {
                // A room-posted bill is one folio service line: booked once from its ChargePosted (guest ledger), never here as well.
                var e = Parse<PosBillSettledEvent>(evt);
                if (e.Mode != "folio") source = AccountingEventMapper.PosBill(e);
                break;
            }

            case AccountingEvents.PosBillVoided:
            {
                var e = Parse<PosBillSettledEvent>(evt);
                source = AccountingEventMapper.PosBill(e with
                {
                    Kind = "void", TotalPaise = -Math.Abs(e.TotalPaise), NetPaise = -Math.Abs(e.NetPaise), CgstPaise = -Math.Abs(e.CgstPaise), SgstPaise = -Math.Abs(e.SgstPaise),
                });
                break;
            }
            case AccountingEvents.ExpensePosted:
            case AccountingEvents.ExpenseReversed:
                source = AccountingEventMapper.Expense(Parse<ExpensePostedEvent>(evt));
                break;
            case AccountingEvents.PayrollPosted:
                source = AccountingEventMapper.PayRun(Parse<PayrollPostedEvent>(evt));
                break;
            case AccountingEvents.PayrollPaid:
                source = AccountingEventMapper.PayRunPaid(Parse<PayrollPaidEvent>(evt));
                break;
            case AccountingEvents.PayRunUnlocked:
                source = await PayRunReversalAsync(connection, transaction, propertyId, Parse<PayRunUnlockedEvent>(evt), ct).ConfigureAwait(false);
                break;
        }

        if (source is not null && !source.IsZero)
            await InsertSourceAsync(connection, transaction, propertyId, source, evt.EventType, evt.OutboxId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
    }

    private async Task<AccountingSource?> CorrectionLineAsync(SqlConnection c, SqlTransaction t, long approvalId, CancellationToken ct)
    {
        long chargeId, folioId, amount, tax;
        DateOnly date;
        await using (var cmd = new SqlCommand("SELECT TOP (1) charge_id, folio_id, service_date, amount_paise, tax_paise FROM bill.charge WHERE approval_id = @a AND kind = 'correction' ORDER BY charge_id;", c, t))
        {
            cmd.Parameters.Set("@a", SqlDbType.BigInt, approvalId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            (chargeId, folioId, date, amount, tax) = (rd.GetInt64(0), rd.GetInt64(1), AccSql.Date(rd, 2), rd.GetInt64(3), rd.GetInt64(4));
        }

        var (cgst, sgst) = await gst.ReadAsync(c, t, chargeId, tax, ct).ConfigureAwait(false);
        return AccountingEventMapper.Charge(folioId, chargeId, "correction", date, amount, cgst, sgst);
    }

    /// <summary>
    /// W3-b: the reversal of the pay-run voucher booked for that lock (read back from <c>acc.source</c>, so it is the exact negative of what was
    /// booked, whatever the figures). Nothing was booked for the lock (zero run) → no reversal. Dated the unlock's business date.
    /// </summary>
    private static async Task<AccountingSource?> PayRunReversalAsync(SqlConnection c, SqlTransaction t, int propertyId, PayRunUnlockedEvent e, CancellationToken ct)
    {
        var key = AccountingEventMapper.PayRunKey(e.RunId, e.LockSeq);
        var entries = new List<SourceEntry>();
        await using (var cmd = new SqlCommand("""
            SELECT se.map_key, se.debit_paise, se.credit_paise FROM acc.source s JOIN acc.source_entry se ON se.source_id = s.source_id
            WHERE s.property_id = @p AND s.source_key = @k ORDER BY se.line_no;
            """, c, t))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, key, 80);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) entries.Add(new SourceEntry(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2)));
        }

        if (entries.Count == 0) return null;
        bool paid;
        await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM acc.source WHERE property_id = @p AND source_key = @k;", c, t))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, AccountingEventMapper.PayRunPaidKey(e.RunId, e.LockSeq), 80);
            paid = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
        }

        DateOnly date;
        if (e.BusinessDate is { } d)
        {
            date = d;
        }
        else
        {
            await using var cmd = new SqlCommand("SELECT business_date FROM inv.property WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            date = DateOnly.FromDateTime((DateTime)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
        }

        return AccountingEventMapper.PayRunReversal(e, date, key, entries, paid);
    }

    private static async Task<int> TillOfAsync(SqlConnection c, SqlTransaction t, long shiftId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT till_id FROM pay.shift WHERE shift_id = @s;", c, t);
        cmd.Parameters.Set("@s", SqlDbType.BigInt, shiftId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id ? id : 0;
    }

    /// <summary>Accepted cash differences not yet recorded (P4 publishes no event for the decision today).</summary>
    public async Task<int> SweepCashDifferencesAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var todo = new List<(long Shift, int Till, int Property, DateOnly Date, long Expected, long Counted)>();
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
        await using (var cmd = connections.Command($"""
            SELECT TOP (@n) s.shift_id, s.till_id, t.property_id, s.business_date, s.expected_paise, s.counted_paise
            FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id JOIN bill.approval a ON a.approval_id = s.approval_id
            WHERE {SweepWhere} ORDER BY s.shift_id;
            """, conn))
        {
            cmd.Parameters.Set("@n", SqlDbType.Int, batchSize);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                todo.Add((rd.GetInt64(0), rd.GetInt32(1), rd.GetInt32(2), AccSql.Date(rd, 3), rd.GetInt64(4), rd.GetInt64(5)));
        }

        var n = 0;
        foreach (var x in todo)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            var src = AccountingEventMapper.CashDifference(x.Shift, x.Till, x.Date, x.Expected, x.Counted);
            if (await InsertSourceAsync(conn, tx, x.Property, src, "sweep:pay.shift", null, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false)) n++;
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        return n;
    }

    /// <summary>Inserts the source unless its key exists for the property (check-then-insert under UPDLOCK + HOLDLOCK). True when inserted.</summary>
    public static async Task<bool> InsertSourceAsync(SqlConnection c, SqlTransaction t, int propertyId, AccountingSource s, string eventType, long? outboxId, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        await using (var chk = new SqlCommand("SELECT COUNT(*) FROM acc.source WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND source_key = @k;", c, t))
        {
            chk.Parameters.Set("@p", SqlDbType.Int, propertyId);
            chk.Parameters.Set("@k", SqlDbType.VarChar, s.SourceKey, 80);
            if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) return false;
        }

        long id;
        await using (var ins = new SqlCommand("""
            INSERT acc.source(property_id, source_key, source_kind, sub, voucher_type, business_date, narration, event_type, outbox_id, received_utc)
            OUTPUT INSERTED.source_id VALUES (@p, @k, @kind, @sub, @ty, @d, @na, @ev, @o, @now);
            """, c, t))
        {
            ins.Parameters.Set("@p", SqlDbType.Int, propertyId);
            ins.Parameters.Set("@k", SqlDbType.VarChar, s.SourceKey, 80);
            ins.Parameters.Set("@kind", SqlDbType.VarChar, s.Kind, 12);
            ins.Parameters.Set("@sub", SqlDbType.VarChar, s.Sub.Length <= 30 ? s.Sub : s.Sub[..30], 30);
            ins.Parameters.Set("@ty", SqlDbType.VarChar, s.Type.ToString(), 8);
            ins.Parameters.Set("@d", SqlDbType.Date, s.BusinessDate.ToDateTime(TimeOnly.MinValue));
            ins.Parameters.Set("@na", SqlDbType.NVarChar, s.Narration.Length <= 400 ? s.Narration : s.Narration[..400], 400);
            ins.Parameters.Set("@ev", SqlDbType.VarChar, eventType, 60);
            ins.Parameters.Set("@o", SqlDbType.BigInt, outboxId);
            ins.Parameters.Set("@now", SqlDbType.DateTime2, now);
            id = (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        for (var i = 0; i < s.Entries.Count; i++)
        {
            var e = s.Entries[i];
            await using var ins = new SqlCommand("INSERT acc.source_entry(source_id, line_no, map_key, debit_paise, credit_paise) VALUES (@s, @i, @k, @d, @c);", c, t);
            ins.Parameters.Set("@s", SqlDbType.BigInt, id);
            ins.Parameters.Set("@i", SqlDbType.TinyInt, (byte)i);
            ins.Parameters.Set("@k", SqlDbType.VarChar, e.MapKey.Length <= 80 ? e.MapKey : e.MapKey[..80], 80);
            ins.Parameters.Set("@d", SqlDbType.BigInt, e.DebitPaise);
            ins.Parameters.Set("@c", SqlDbType.BigInt, e.CreditPaise);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    internal static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
