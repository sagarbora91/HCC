using System.Data;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Application.Commands;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>AF-VERIFY (wave 4): hostile attempts against PAY-08 (duplicate-receipt guard). Prefix V-PAY-nn = attempt id in af-verify-DONE.md.</summary>
[Trait("Category", "Sql")]
public class AuditFixVerifyPayTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> FolioAsync()
    {
        var (rid, folio) = await db.NewFolioAsync("InHouse", BillingDb.Day, BillingDb.Day.AddDays(3));
        db.Stays[rid] = (folio, 200_000);
        return folio;
    }

    private Task<CommandResult<ReceiptResult>> Rec(long folio, long amount, string tender = "UPI", string kind = "payment", bool confirm = false, string? key = null, string? reference = "DEMO-REF", int? till = null)
        => db.Billing.RecordReceiptAsync(db.As(Who.Cashier), key ?? BillingDb.Key(), new RecordReceiptRequest(folio, kind, tender, amount, tender == "Cash" ? null : reference, till, null, confirm), Ct);

    private Task<long> Count(long folio, string? kind = null) =>
        db.LongAsync("SELECT COUNT(*) FROM pay.receipt WHERE folio_id = @f" + (kind is null ? "" : " AND kind = '" + kind + "'"), ("@f", SqlDbType.BigInt, folio));

    // V-PAY-01 window edges
    [Theory]
    [InlineData(0, 59, true)]   // 0:59
    [InlineData(9, 59, true)]   // 9:59 -> refused
    [InlineData(10, 0, true)]   // exactly 10:00 -> refused (window is inclusive)
    [InlineData(10, 1, false)]  // 10:01 -> allowed
    [InlineData(60, 0, false)]
    public async Task V_PAY_01_window_edges_exact_to_the_second(int minutes, int seconds, bool refused)
    {
        var folio = await FolioAsync();
        await Rec(folio, 100_000);
        db.Clock.Now += TimeSpan.FromSeconds(minutes * 60 + seconds);
        if (refused)
        {
            var ex = await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 100_000));
            Assert.Equal("DUPLICATE_SUSPECTED", ex.Details!["reason"].GetString());
            Assert.Equal(1, await Count(folio));
        }
        else
        {
            await Rec(folio, 100_000);
            Assert.Equal(2, await Count(folio));
        }
    }

    // V-PAY-02 amounts: one paise apart is never flagged, equal is; different folios are independent; the same tender with a different reference is still flagged (by design)
    [Fact]
    public async Task V_PAY_02_amount_folio_and_reference_variants()
    {
        var a = await FolioAsync();
        var b = await FolioAsync();
        await Rec(a, 100_000);
        await Rec(a, 100_001);
        await Rec(a, 99_999);
        await Rec(b, 100_000);                       // another folio, same minute, same amount: not flagged
        Assert.Equal(3, await Count(a));
        await Fails(HccErrorCode.VALIDATION, () => Rec(a, 100_000, reference: "A-DIFFERENT-REF")); // reference does not matter
        await Fails(HccErrorCode.VALIDATION, () => Rec(a, 100_001, "upi"));                        // tender spelling does not matter
        await Rec(a, 100_000, "Card");                 // a different tender is a different payment
    }

    // V-PAY-03 refusal leaves no trace: no receipt, no audit, no outbox, no idempotency row; a plain retry with the same key is refused again; a confirmed resend with the SAME key works
    [Fact]
    public async Task V_PAY_03_refusal_stores_no_idempotency_row_and_the_same_base_key_can_be_confirmed()
    {
        var folio = await FolioAsync();
        await Rec(folio, 100_000);
        var idem = await db.LongAsync("SELECT COUNT(*) FROM plat.idempotency");
        var audit = await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event");
        var key = BillingDb.Key();
        await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 100_000, key: key));
        Assert.Equal(idem, await db.LongAsync("SELECT COUNT(*) FROM plat.idempotency"));
        Assert.Equal(audit, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event"));
        await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 100_000, key: key)); // retry of the refused request: refused again, not replayed
        // the cashier ticks "second, separate payment" and the client resends with the SAME key -> no IDEMPOTENCY_MISMATCH
        var ok = await Rec(folio, 100_000, confirm: true, key: key);
        Assert.False(ok.Replayed);
        Assert.Equal(2, await Count(folio));
        // and a replay of that confirmed request returns the stored result
        var replay = await Rec(folio, 100_000, confirm: true, key: key);
        Assert.True(replay.Replayed);
        Assert.Equal(2, await Count(folio));
    }

    // V-PAY-04 confirm flag on a non-duplicate: posts, and the audit line does NOT claim a second payment
    [Fact]
    public async Task V_PAY_04_confirm_without_a_duplicate_posts_once_and_the_audit_has_no_second_payment_note()
    {
        var folio = await FolioAsync();
        var r = await Rec(folio, 77_700, confirm: true);
        var detail = (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED' AND detail LIKE @d", ("@d", SqlDbType.NVarChar, $"%receipt {r.Value.ReceiptId}%")))!;
        Assert.DoesNotContain("confirmed second payment", detail, StringComparison.Ordinal);
    }

    // V-PAY-05 confirmed duplicates audit the earlier receipt; a third equal one with confirm is again audited against the NEWEST earlier receipt
    [Fact]
    public async Task V_PAY_05_confirmed_duplicates_are_audited_each_time_and_refer_to_the_newest_equal_receipt()
    {
        var folio = await FolioAsync();
        var first = await Rec(folio, 50_000);
        db.Clock.Now += TimeSpan.FromMinutes(2);
        var second = await Rec(folio, 50_000, confirm: true);
        db.Clock.Now += TimeSpan.FromMinutes(1);
        var third = await Rec(folio, 50_000, confirm: true);
        var d3 = (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED' AND detail LIKE @d", ("@d", SqlDbType.NVarChar, $"%receipt {third.Value.ReceiptId} ·%")))!;
        Assert.Contains($"(receipt {second.Value.ReceiptId} 1 min earlier)", d3, StringComparison.Ordinal);
        var d2 = (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED' AND detail LIKE @d", ("@d", SqlDbType.NVarChar, $"%receipt {second.Value.ReceiptId} ·%")))!;
        Assert.Contains($"(receipt {first.Value.ReceiptId} 2 min earlier)", d2, StringComparison.Ordinal);
    }

    // V-PAY-06 deposits and kinds
    [Fact]
    public async Task V_PAY_06_deposit_vs_payment_kinds_are_independent_and_an_equal_deposit_is_flagged()
    {
        var folio = await FolioAsync();
        await Rec(folio, 30_000, kind: "deposit");
        await Rec(folio, 30_000, kind: "payment");
        var ex = await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 30_000, kind: "deposit"));
        Assert.Contains("deposit of", ex.Message, StringComparison.Ordinal);
        await Rec(folio, 30_000, kind: "deposit", confirm: true);
        Assert.Equal(2, await Count(folio, "deposit"));
    }

    // V-PAY-07 split tenders: different tenders of one amount are two payments; the same tender twice (two UPI rows of equal amount) is refused on the second row
    [Fact]
    public async Task V_PAY_07_split_tenders_equal_rows_of_one_tender_hit_the_guard_on_the_second_row()
    {
        var folio = await FolioAsync();
        await Rec(folio, 50_000, "UPI", reference: "UPI-A");
        await Rec(folio, 50_000, "Card", reference: "CARD-A");
        Assert.Equal(2, await Count(folio));
        var ex = await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 50_000, "UPI", reference: "UPI-B")); // second UPI row of an equal split
        Assert.Equal("DUPLICATE_SUSPECTED", ex.Details!["reason"].GetString());
        Assert.Equal(2, await Count(folio));
    }

    // V-PAY-08 two terminals post the same payment at the same instant (different keys): the guard must hold
    [Fact]
    public async Task V_PAY_08_two_simultaneous_equal_payments_on_different_keys_post_exactly_one()
    {
        var folio = await FolioAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await Rec(folio, 123_400); return "ok"; }
            catch (Hcc.Domain.Errors.HccException e) { return e.Code + ":" + (e.Details is { } d && d.TryGetValue("reason", out var r) ? r.GetString() : ""); }
        }));
        Assert.True(await Count(folio) == 1, $"{await Count(folio)} equal receipts posted by 4 simultaneous requests: {string.Join(", ", results)}");
    }

    // V-PAY-09 clock stepping back (NTP / manual change) must not disable the guard for receipts that now lie in the future
    [Fact]
    public async Task V_PAY_09_clock_stepped_back_two_minutes_still_flags_an_equal_receipt()
    {
        var folio = await FolioAsync();
        await Rec(folio, 66_600);
        db.Clock.Now -= TimeSpan.FromMinutes(2);
        try { await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 66_600)); }
        finally { db.Clock.Now += TimeSpan.FromMinutes(2); }
    }

    // V-PAY-10 advance before check-in through the reservation path (folioId 0)
    [Fact]
    public async Task V_PAY_10_advance_on_a_confirmed_booking_is_guarded_too()
    {
        var (rid, _) = await db.NewFolioAsync("Confirmed", BillingDb.Day.AddDays(5), BillingDb.Day.AddDays(7));
        var one = await db.Billing.RecordReceiptAsync(db.As(Who.Cashier), BillingDb.Key(), new RecordReceiptRequest(0, "deposit", "UPI", 40_000, "UPI-ADV", null, rid), Ct);
        var ex = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() =>
            db.Billing.RecordReceiptAsync(db.As(Who.Cashier), BillingDb.Key(), new RecordReceiptRequest(0, "deposit", "UPI", 40_000, "UPI-ADV2", null, rid), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Equal(1, await Count(one.Value.FolioId, "deposit"));
    }

    // V-PAY-11 cash duplicates: the guard also covers Cash with a till, and the confirmed second posts exactly one more drawer movement
    [Fact]
    public async Task V_PAY_11_cash_duplicate_confirmed_posts_exactly_one_more_drawer_movement()
    {
        var (till, _) = await db.OpenTillAsync(100_000);
        var folio = await FolioAsync();
        await Rec(folio, 25_000, "Cash", till: till);
        var moves = await db.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'receipt' AND source_id IN (SELECT receipt_id FROM pay.receipt WHERE folio_id = @f)", ("@f", SqlDbType.BigInt, folio));
        await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 25_000, "Cash", till: till));
        Assert.Equal(moves, await db.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'receipt' AND source_id IN (SELECT receipt_id FROM pay.receipt WHERE folio_id = @f)", ("@f", SqlDbType.BigInt, folio)));
        await Rec(folio, 25_000, "cash", confirm: true, till: till);
        Assert.Equal(moves + 1, await db.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'receipt' AND source_id IN (SELECT receipt_id FROM pay.receipt WHERE folio_id = @f)", ("@f", SqlDbType.BigInt, folio)));
    }

    // V-PAY-12 refusal text sanity: names the tender, amount, IST time, minutes, receipt id
    [Fact]
    public async Task V_PAY_12_refusal_text_names_tender_amount_time_minutes_and_receipt()
    {
        var folio = await FolioAsync();
        var first = await Rec(folio, 150_000);
        db.Clock.Now += TimeSpan.FromMinutes(7);
        var ex = await Fails(HccErrorCode.VALIDATION, () => Rec(folio, 150_000));
        Assert.Contains("UPI payment of", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IST", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(7 minutes ago)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("receipt " + first.Value.ReceiptId, ex.Message, StringComparison.Ordinal);
        Assert.Contains("1,500", ex.Message, StringComparison.Ordinal);
    }
}
