using System.Data;
using Hcc.Application.Commands;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>PAY-08 / AF-DEC-1: an equal payment or deposit on the same folio within 10 minutes needs an explicit confirmation (a fresh dialog key after a lost response must not post twice).</summary>
[Trait("Category", "Sql")]
public class DuplicateReceiptTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> FolioAsync()
    {
        var (rid, folio) = await db.NewFolioAsync("InHouse", BillingDb.Day, BillingDb.Day.AddDays(3));
        db.Stays[rid] = (folio, 200_000);
        return folio;
    }

    private Task<CommandResult<ReceiptResult>> RecordAsync(long folio, long amount, string tender = "UPI", string kind = "payment", bool confirm = false, string? key = null)
        => db.Billing.RecordReceiptAsync(db.As(Who.Cashier), key ?? BillingDb.Key(), new RecordReceiptRequest(folio, kind, tender, amount, "DEMO-REF", null, null, confirm), Ct);

    private Task<long> ReceiptCountAsync(long folio) => db.LongAsync("SELECT COUNT(*) FROM pay.receipt WHERE folio_id = @f", ("@f", SqlDbType.BigInt, folio));

    [Fact]
    public async Task Equal_receipt_within_window_without_confirm_is_refused_DUPLICATE_SUSPECTED_and_writes_nothing()
    {
        var folio = await FolioAsync();
        var first = await RecordAsync(folio, 100_000);
        db.Clock.Now += TimeSpan.FromMinutes(4);
        var audits = await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED'");
        var outbox = await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReceiptRecorded'");

        var ex = await Fails(HccErrorCode.VALIDATION, () => RecordAsync(folio, 100_000));
        Assert.Equal(DuplicateReceiptGuard_Reason, ex.Details!["reason"].GetString());
        Assert.Equal(first.Value.ReceiptId, ex.Details["receiptId"].GetInt64());
        Assert.Contains("UPI payment of", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(4 minutes ago)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("If this is a second, separate payment, confirm it.", ex.Message, StringComparison.Ordinal);

        Assert.Equal(1, await ReceiptCountAsync(folio));
        Assert.Equal(audits, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED'"));
        Assert.Equal(outbox, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReceiptRecorded'"));
    }

    [Fact]
    public async Task Equal_receipt_with_confirmDuplicate_posts_a_second_receipt_and_audits_it()
    {
        var folio = await FolioAsync();
        var first = await RecordAsync(folio, 100_000);
        db.Clock.Now += TimeSpan.FromMinutes(3);
        var second = await RecordAsync(folio, 100_000, confirm: true);
        Assert.NotEqual(first.Value.ReceiptId, second.Value.ReceiptId);
        Assert.Equal(2, await ReceiptCountAsync(folio));
        var detail = (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED' AND detail LIKE @d",
            ("@d", SqlDbType.NVarChar, $"%receipt {second.Value.ReceiptId} ·%")))!;
        Assert.Contains($"confirmed second payment (receipt {first.Value.ReceiptId} 3 min earlier)", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Different_amount_within_window_is_not_flagged()
    {
        var folio = await FolioAsync();
        await RecordAsync(folio, 100_000);
        await RecordAsync(folio, 100_001);
        Assert.Equal(2, await ReceiptCountAsync(folio));
    }

    [Fact]
    public async Task Different_tender_within_window_is_not_flagged()
    {
        var folio = await FolioAsync();
        await RecordAsync(folio, 100_000, "UPI");
        await RecordAsync(folio, 100_000, "Card");
        Assert.Equal(2, await ReceiptCountAsync(folio));
    }

    [Fact]
    public async Task Deposit_and_payment_of_equal_amount_are_not_flagged()
    {
        var folio = await FolioAsync();
        await RecordAsync(folio, 50_000, "UPI", "payment");
        await RecordAsync(folio, 50_000, "UPI", "deposit");
        Assert.Equal(2, await ReceiptCountAsync(folio));
        await Fails(HccErrorCode.VALIDATION, () => RecordAsync(folio, 50_000, "UPI", "deposit")); // an equal deposit is flagged
    }

    [Fact]
    public async Task Equal_receipt_after_the_window_is_not_flagged()
    {
        var folio = await FolioAsync();
        await RecordAsync(folio, 100_000);
        db.Clock.Now += TimeSpan.FromMinutes(11);
        await RecordAsync(folio, 100_000);
        Assert.Equal(2, await ReceiptCountAsync(folio));
    }

    [Fact]
    public async Task Same_key_replay_returns_the_stored_result_and_never_reaches_the_guard()
    {
        var folio = await FolioAsync();
        var key = BillingDb.Key();
        var first = await RecordAsync(folio, 100_000, key: key);
        var replay = await RecordAsync(folio, 100_000, key: key); // the equal receipt now exists, but the stored result is returned
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, await ReceiptCountAsync(folio));
    }

    [Fact]
    public async Task Refund_execution_is_not_affected()
    {
        var (till, _) = await db.OpenTillAsync(100_000);
        var folio = await FolioAsync();
        var payment = await db.PayAsync(folio, 100_000, "Cash", till: till);
        var version = payment.Value.FolioVersion;
        for (var i = 0; i < 2; i++) // two equal refunds back to back, inside the window, both execute
        {
            var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new RequestAdjustmentRequest("refund", folio, payment.Value.ReceiptId, 30_000, "Early departure", version), Ct);
            var id = req.Value.ApprovalId!.Value;
            var approved = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(id, true, null, req.Value.ApprovalVersion!), Ct);
            var done = await db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier2), BillingDb.Key(), new ExecuteAdjustmentRequest(id, approved.Value.Version, null, null, till), Ct);
            Assert.Equal("Executed", done.Value.Status);
            version = await db.FolioVersionAsync(folio);
        }

        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM pay.receipt WHERE folio_id = @f AND kind = 'refund'", ("@f", SqlDbType.BigInt, folio)));
    }

    private const string DuplicateReceiptGuard_Reason = "DUPLICATE_SUSPECTED";
}
