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

    /// <summary>
    /// Unprocessed consumed events + unswept accepted cash differences for a property (blocks locking), as two columns: still being
    /// processed, and parked (Wave C3: "stuck — see System health"). A08-01: events above the consumer's watermark are counted with one
    /// IX_outbox_type seek per type (types as a typed row source, not OPENJSON inside the scan); at or below the watermark only parked
    /// events can still be unprocessed (A08-02: parked = not booked, still blocks). Parked events are counted wherever they are.
    /// </summary>
    public const string PendingSql = """
        DECLARE @low BIGINT = ISNULL((SELECT low_outbox_id FROM plat.outbox_consumer WHERE consumer = @c), 0);
        SELECT (SELECT COUNT(*) FROM OPENJSON(@types) WITH (event_type VARCHAR(60) '$') t
                CROSS APPLY (SELECT o.outbox_id FROM plat.outbox o WITH (INDEX(IX_outbox_type))
                             WHERE o.event_type = t.event_type AND o.outbox_id > @low AND o.property_id = @p
                               AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = o.outbox_id)
                               AND NOT EXISTS (SELECT 1 FROM plat.consumer_failure f WHERE f.consumer = @c AND f.outbox_id = o.outbox_id AND f.parked_utc IS NOT NULL)) x)
             + (SELECT COUNT(*) FROM pay.shift s JOIN pay.till t ON t.till_id = s.till_id JOIN bill.approval a ON a.approval_id = s.approval_id
                WHERE t.property_id = @p AND
        """ + " " + SweepWhere + """
        ),
               (SELECT COUNT(*) FROM plat.consumer_failure f JOIN plat.outbox o ON o.outbox_id = f.outbox_id
                WHERE f.consumer = @c AND f.parked_utc IS NOT NULL AND o.property_id = @p
                  AND o.event_type IN (SELECT event_type FROM OPENJSON(@types) WITH (event_type VARCHAR(60) '$'))
                  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = f.outbox_id));
        """;

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

        // DEC-074 (wave B3): group master folio, advances, full and final (one event may book two vouchers) — see BooksSourcesAsync.
        if (await BooksSourcesAsync(evt, connection, transaction, propertyId, ct).ConfigureAwait(false) is { } books)
        {
            foreach (var b in books.Where(b => !b.IsZero))
                await InsertSourceAsync(connection, transaction, propertyId, b, evt.EventType, evt.OutboxId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
            return;
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
                var company = e.Kind == "company-credit" ? await ReceiptCompanyAsync(connection, transaction, e.ReceiptId, ct).ConfigureAwait(false) : null;
                var channel = e.Kind == "ota-credit" ? await FolioChannelAsync(connection, transaction, e.FolioId, ct).ConfigureAwait(false) : null; // ACC-2 flows (A13)
                source = AccountingEventMapper.Receipt(e.FolioId, e.ReceiptId, e.Kind, e.Tender, e.BusinessDate, e.AmountPaise,
                    new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false), Company: company, Channel: channel));
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
                    source = AccountingEventMapper.CashDifference(e.ShiftId, await TillOfAsync(connection, transaction, e.ShiftId, ct).ConfigureAwait(false), e.BusinessDate, e.ExpectedPaise, e.CountedPaise,
                        await CashPartiesAsync(connection, transaction, propertyId, ct).ConfigureAwait(false));
                break;
            }

            case AccountingEvents.PosBillSettled:
            {
                // A room-posted bill is one folio service line: booked once from its ChargePosted (guest ledger), never here as well.
                var e = Parse<PosBillSettledEvent>(evt);
                if (e.Mode != "folio") source = AccountingEventMapper.PosBill(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false)));
                break;
            }

            case AccountingEvents.PosBillVoided:
            {
                var e = Parse<PosBillSettledEvent>(evt);
                source = AccountingEventMapper.PosBill(e with
                {
                    Kind = "void", TotalPaise = -Math.Abs(e.TotalPaise), NetPaise = -Math.Abs(e.NetPaise), CgstPaise = -Math.Abs(e.CgstPaise), SgstPaise = -Math.Abs(e.SgstPaise),
                }, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false)));
                break;
            }
            case AccountingEvents.ExpensePosted:
            case AccountingEvents.ExpenseReversed:
            {
                var e = Parse<ExpensePostedEvent>(evt);
                source = AccountingEventMapper.Expense(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Funding, ct).ConfigureAwait(false),
                    Vendor: await VendorPartyAsync(connection, transaction, propertyId, e.Vendor, ct).ConfigureAwait(false)));
                break;
            }
            case AccountingEvents.PayrollPosted:
                source = AccountingEventMapper.PayRun(Parse<PayrollPostedEvent>(evt));
                break;
            case AccountingEvents.PayrollPaid:
            {
                var e = Parse<PayrollPaidEvent>(evt);
                source = AccountingEventMapper.PayRunPaid(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Funding, ct).ConfigureAwait(false)));
                break;
            }
            case AccountingEvents.PayRunUnlocked:
                source = await PayRunReversalAsync(connection, transaction, propertyId, Parse<PayRunUnlockedEvent>(evt), ct).ConfigureAwait(false);
                break;

            // ── Wave B1 (DEC-073, A03b-01): cash leaving the drawer (a till transfer books nothing: cash to cash) ──
            case AccountingEvents.CashBanked:
            case AccountingEvents.CashBankingRejected:
            {
                var e = Parse<CashBankingEvent>(evt);
                var cash = await CashPartiesAsync(connection, transaction, propertyId, ct).ConfigureAwait(false);
                source = AccountingEventMapper.Banking(e, rejected: evt.EventType == AccountingEvents.CashBankingRejected,
                    cash with { Bank = await BankPartyAsync(connection, transaction, propertyId, e.Bank, ct).ConfigureAwait(false) });
                break;
            }

            case AccountingEvents.OwnerDrawingRecorded:
                source = AccountingEventMapper.OwnerDrawing(Parse<OwnerDrawingEvent>(evt), await CashPartiesAsync(connection, transaction, propertyId, ct).ConfigureAwait(false));
                break;
            // ── end Wave B1 ──
            case AccountingEvents.CompanyPaymentRecorded:
            {
                var e = Parse<CompanyPaymentRecordedEvent>(evt);
                source = AccountingEventMapper.CompanyPayment(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false)));
                break;
            }

            case AccountingEvents.UdhaarPaymentRecorded: // DEC-130
            {
                var e = Parse<UdhaarPaymentRecordedEvent>(evt);
                source = AccountingEventMapper.UdhaarPayment(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false),
                    Udhaar: await UdhaarPartyAsync(connection, transaction, e.ReceivableId, e.Customer, ct).ConfigureAwait(false)));
                break;
            }

            case AccountingEvents.UdhaarCreditRaised: // ACC-1 (contract §4, A17)
                source = AccountingEventMapper.UdhaarCredit(Parse<UdhaarCreditRaisedEvent>(evt));
                break;

            // ── ACC-2 flows (contract §4, §8): the OTA payout (A13) and a mistaken Udhaar payment reversed (A17b) ──
            case AccountingEvents.OtaPayoutRecorded:
                source = AccountingEventMapper.OtaPayout(Parse<OtaPayoutRecordedEvent>(evt));
                break;
            case AccountingEvents.UdhaarPaymentReversed:
            {
                var e = Parse<UdhaarPaymentReversedEvent>(evt);
                source = AccountingEventMapper.UdhaarPaymentReversed(e, new PartyContext(Tender: await TenderPartyAsync(connection, transaction, propertyId, e.Tender, ct).ConfigureAwait(false),
                    Udhaar: await UdhaarPartyAsync(connection, transaction, e.ReceivableId, e.Customer, ct).ConfigureAwait(false)));
                break;
            }
            // ── end ACC-2 flows ──
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
            SELECT se.map_key, se.debit_paise, se.credit_paise, se.party_id FROM acc.source s JOIN acc.source_entry se ON se.source_id = s.source_id
            WHERE s.property_id = @p AND s.source_key = @k ORDER BY se.line_no;
            """, c, t))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, key, 80);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) entries.Add(new SourceEntry(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2), rd.IsDBNull(3) ? null : rd.GetString(3)));
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

    // ── DEC-074 (wave B3) · group master folio, staff advances, full and final ──────────────────────────────────────────────────────

    /// <summary>
    /// The sources of a DEC-074 event, or null for any other event type. A figure a payload published before DEC-074 lacks is read
    /// back (read-only) from the append-only master folio (<c>grp.master_entry</c>): the kind of a refunded receipt, the deposits held at
    /// settlement; a missing date is the property's business date (as for <c>PayRunUnlocked</c>).
    /// </summary>
    private async Task<IReadOnlyList<AccountingSource>?> BooksSourcesAsync(OutboxEvent evt, SqlConnection c, SqlTransaction t, int propertyId, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case AccountingEvents.GroupReceiptRecorded:
            {
                var e = Parse<GroupReceiptRecordedEvent>(evt);
                var of = e.Kind == "refund" ? e.RefundOfKind ?? await RefundedKindAsync(c, t, e.EntryId, ct).ConfigureAwait(false) : null;
                return [AccountingEventMapper.GroupReceipt(e, of, new PartyContext(Tender: await TenderPartyAsync(c, t, propertyId, e.Tender, ct).ConfigureAwait(false),
                    Company: await GroupCompanyAsync(c, t, e.GroupId, ct).ConfigureAwait(false)))];
            }

            case AccountingEvents.GroupAttritionPosted:
            {
                var e = Parse<GroupAttritionPostedEvent>(evt);
                return [AccountingEventMapper.GroupAttrition(e, new PartyContext(Company: await GroupCompanyAsync(c, t, e.GroupId, ct).ConfigureAwait(false)))];
            }

            case AccountingEvents.GroupSettled:
            {
                var e = Parse<GroupSettledEvent>(evt);
                var held = e.DepositsHeldPaise ?? await DepositsHeldAsync(c, t, e.GroupId, ct).ConfigureAwait(false);
                return [AccountingEventMapper.GroupSettlement(e, held, e.BusinessDate ?? await BusinessDateAsync(c, t, propertyId, ct).ConfigureAwait(false),
                    new PartyContext(Company: await GroupCompanyAsync(c, t, e.GroupId, ct).ConfigureAwait(false)))];
            }

            case AccountingEvents.AdvanceGiven:
            {
                var e = Parse<AdvanceGivenEvent>(evt);
                return [AccountingEventMapper.Advance(e, new PartyContext(Tender: await TenderPartyAsync(c, t, propertyId, string.IsNullOrEmpty(e.Funding) ? "Bank transfer" : e.Funding, ct).ConfigureAwait(false)))];
            }

            case AccountingEvents.FnfSettled:
            {
                var e = Parse<FnfSettledEvent>(evt);
                return AccountingEventMapper.Fnf(e, e.BusinessDate ?? await BusinessDateAsync(c, t, propertyId, ct).ConfigureAwait(false),
                    new PartyContext(Tender: await TenderPartyAsync(c, t, propertyId, string.IsNullOrEmpty(e.Funding) ? "Bank transfer" : e.Funding, ct).ConfigureAwait(false)));
            }

            default:
                return null;
        }
    }

    private static async Task<string?> RefundedKindAsync(SqlConnection c, SqlTransaction t, long entryId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT p.kind FROM grp.master_entry e JOIN grp.master_entry p ON p.entry_id = e.refund_of WHERE e.entry_id = @e;", c, t);
        cmd.Parameters.Set("@e", SqlDbType.BigInt, entryId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private static async Task<long> DepositsHeldAsync(SqlConnection c, SqlTransaction t, long groupId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT ISNULL(SUM(CASE WHEN e.kind = 'deposit' THEN e.amount_paise WHEN e.kind = 'refund' AND p.kind = 'deposit' THEN -e.amount_paise ELSE 0 END), 0)
            FROM grp.master_entry e LEFT JOIN grp.master_entry p ON p.entry_id = e.refund_of WHERE e.group_id = @g;
            """, c, t);
        cmd.Parameters.Set("@g", SqlDbType.BigInt, groupId);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<DateOnly> BusinessDateAsync(SqlConnection c, SqlTransaction t, int propertyId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT business_date FROM inv.property WHERE property_id = @p;", c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        return DateOnly.FromDateTime((DateTime)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    // ── end DEC-074 (wave B3) ──

    // ── ACC-1 (acc1-parties, contract §3) · the parties an event names, read when it is consumed (read-only; null = not named) ──

    /// <summary>The bank account / till code a payment method settles to (mst.payment_method by name or code → mst.bank_account).</summary>
    private static async Task<string?> TenderPartyAsync(SqlConnection c, SqlTransaction t, int propertyId, string? method, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(method)) return null;
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) b.code FROM mst.payment_method m JOIN mst.bank_account b ON b.bank_account_id = m.settles_to_id
            WHERE m.property_id = @p AND (m.name = @n OR m.code = @n) ORDER BY CASE WHEN m.name = @n THEN 0 ELSE 1 END, m.payment_method_id;
            """, c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@n", SqlDbType.NVarChar, method.Trim(), 60);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    /// <summary>Cash leaving / entering a drawer: the till the Cash method settles to.</summary>
    private static async Task<PartyContext> CashPartiesAsync(SqlConnection c, SqlTransaction t, int propertyId, CancellationToken ct)
        => new(Tender: await TenderPartyAsync(c, t, propertyId, "Cash", ct).ConfigureAwait(false));

    /// <summary>DEC-132: the vendor list row of an expense's vendor text (ops.expense_vendor by name or code).</summary>
    private static async Task<string?> VendorPartyAsync(SqlConnection c, SqlTransaction t, int propertyId, string? vendor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vendor)) return null;
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) code FROM ops.expense_vendor WHERE property_id = @p AND (name = @n OR code = @n)
            ORDER BY CASE WHEN name = @n THEN 0 ELSE 1 END, expense_vendor_id;
            """, c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Set("@n", SqlDbType.NVarChar, vendor.Trim(), 60);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    /// <summary>The bank account a deposit slip names (code, name or bank, case-insensitive), else the property's only bank account.</summary>
    private static async Task<string?> BankPartyAsync(SqlConnection c, SqlTransaction t, int propertyId, string? text, CancellationToken ct)
    {
        var rows = new List<(string Code, string Name, string? Bank, bool Active)>();
        await using (var cmd = new SqlCommand("SELECT code, name, bank, active FROM mst.bank_account WHERE property_id = @p AND kind = 'bank' ORDER BY sort_order, bank_account_id;", c, t))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetBoolean(3)));
        }

        var x = (text ?? "").Trim();
        if (x.Length > 0)
        {
            var hit = rows.FirstOrDefault(r => string.Equals(r.Code, x, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Name, x, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Bank, x, StringComparison.OrdinalIgnoreCase));
            hit = hit.Code is not null ? hit : rows.FirstOrDefault(r => x.Contains(r.Name, StringComparison.OrdinalIgnoreCase) || x.Contains(r.Code, StringComparison.OrdinalIgnoreCase));
            if (hit.Code is not null) return hit.Code;
        }

        var active = rows.Where(r => r.Active).ToList();
        return active.Count == 1 ? active[0].Code : rows.Count == 1 ? rows[0].Code : null;
    }

    /// <summary>The company a company-credit receipt is owed by (pay.receipt.company_id, migration 3500).</summary>
    private static async Task<string?> ReceiptCompanyAsync(SqlConnection c, SqlTransaction t, long receiptId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT company_id FROM pay.receipt WHERE receipt_id = @r;", c, t);
        cmd.Parameters.Set("@r", SqlDbType.BigInt, receiptId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? PartyLedgers.Company(id) : null;
    }

    /// <summary>The company a group master folio is billed to (grp.group_account.company_id; none → the control).</summary>
    private static async Task<string?> GroupCompanyAsync(SqlConnection c, SqlTransaction t, long groupId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT company_id FROM grp.group_account WHERE group_id = @g;", c, t);
        cmd.Parameters.Set("@g", SqlDbType.BigInt, groupId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? PartyLedgers.Company(id) : null;
    }

    /// <summary>The Udhaar customer of a credit (ops.udhaar_receivable name + mobile; the event's name when the row is not found).</summary>
    private static async Task<string> UdhaarPartyAsync(SqlConnection c, SqlTransaction t, long receivableId, string customer, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT customer_name, mobile FROM ops.udhaar_receivable WHERE receivable_id = @r;", c, t);
        cmd.Parameters.Set("@r", SqlDbType.BigInt, receivableId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? PartyLedgers.Udhaar(rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1)) : PartyLedgers.Udhaar(customer, null);
    }

    // ── end ACC-1 ──

    /// <summary>ACC-2 flows (A13): the channel code of a folio's reservation (the New message that created it), the 1115 party of an OTA credit.</summary>
    private static async Task<string?> FolioChannelAsync(SqlConnection c, SqlTransaction t, long folioId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) ch.code FROM bill.folio f JOIN ota.message m ON m.reservation_id = f.reservation_id AND m.kind = 'New'
            JOIN ota.channel ch ON ch.channel_id = m.channel_id WHERE f.folio_id = @f ORDER BY m.message_id;
            """, c, t);
        cmd.Parameters.Set("@f", SqlDbType.BigInt, folioId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
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
            await using var ins = new SqlCommand("INSERT acc.source_entry(source_id, line_no, map_key, debit_paise, credit_paise, party_id) VALUES (@s, @i, @k, @d, @c, @pa);", c, t);
            ins.Parameters.Set("@s", SqlDbType.BigInt, id);
            ins.Parameters.Set("@i", SqlDbType.TinyInt, (byte)i);
            ins.Parameters.Set("@k", SqlDbType.VarChar, e.MapKey.Length <= 80 ? e.MapKey : e.MapKey[..80], 80);
            ins.Parameters.Set("@d", SqlDbType.BigInt, e.DebitPaise);
            ins.Parameters.Set("@c", SqlDbType.BigInt, e.CreditPaise);
            ins.Parameters.Set("@pa", SqlDbType.VarChar, PartyLedgers.Clip(e.Party), PartyLedgers.MaxPartyLength); // ACC-1 (migration 4210)
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    internal static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
