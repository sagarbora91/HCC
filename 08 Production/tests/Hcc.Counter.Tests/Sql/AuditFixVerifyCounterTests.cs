using Hcc.Application.Commands;
using Hcc.Application.Counter;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>
/// AF-VERIFY (wave 4): hostile attempts against BIL-01 (server half), BIL-02, CON-01 / HIS-01 and the counter price path. Prefix V-CTR-nn = attempt id in
/// af-verify-DONE.md. A passing test = the system refused / behaved correctly; a failing test is a finding.
/// </summary>
public sealed class AuditFixVerifyCounterTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] Tables = ["ctr.bill", "ctr.bill_line", "pay.cash_movement", "ivc.invoice", "plat.outbox", "ops.udhaar_receivable"];

    private static async Task<Hotel> NewAsync()
    {
        var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        return h;
    }

    private static async Task<long[]> CountsAsync(Hotel h) => [.. await Task.WhenAll(Tables.Select(t => h.LongAsync($"SELECT COUNT(*) FROM {t}", Ct)))];

    private static IssueCounterBillRequest Paid(string? method, string? reference = null, int? till = null, long unit = 2500, int qty = 2, long discount = 0) =>
        new(new("Walk-in (fictional)"), [new("Tea", qty, unit, "food")], new("paid", method, reference, till), DiscountPaise: discount, DiscountReason: discount > 0 ? "regular" : null);

    private static Task<CommandResult<CounterBillResult>> Issue(Hotel h, IssueCounterBillRequest r, int? who = null, string? key = null) =>
        h.Get<CounterBillService>().IssueAsync(h.Ctx(who ?? h.Reception, CounterBillService.OpIssue, key), r, Ct);

    private static async Task<HccException> Refused(Hotel h, IssueCounterBillRequest r, HccErrorCode? code = null)
    {
        var before = await CountsAsync(h);
        var ex = await Assert.ThrowsAsync<HccException>(() => Issue(h, r));
        if (code is { } c) Assert.Equal(c, ex.Code);
        Assert.Equal(before, await CountsAsync(h)); // nothing written anywhere
        return ex;
    }

    // V-CTR-01 misspelt / odd tenders
    [Theory]
    [InlineData("Cashh", "R1")]
    [InlineData("Cash h", "R1")]
    [InlineData("Cas", "R1")]
    [InlineData("C4sh", "R1")]
    [InlineData("Cash;", "R1")]
    [InlineData("UP1", "R1")]
    [InlineData("Company credit", "R1")]
    [InlineData("company credit", "R1")]
    [InlineData("COMPANY", "R1")]
    [InlineData("Vendor credit", "R1")]
    [InlineData("VCREDIT", "R1")]
    [InlineData("Udhaar", "R1")]
    [InlineData("", "R1")]
    [InlineData("   ", "R1")]
    [InlineData(null, "R1")]
    public async Task V_CTR_01_misspelt_or_non_front_desk_tenders_are_refused_with_zero_writes(string? method, string reference)
    {
        await using var h = await NewAsync();
        var (till, _) = await h.OpenTillAsync(0, Ct); // an open shift exists, so only the method can be the reason
        var ex = await Refused(h, Paid(method, reference, till), HccErrorCode.VALIDATION);
        Assert.StartsWith("Select a payment method (", ex.Message);
        Assert.DoesNotContain("Company credit", ex.Message); // front-desk list does not offer it
    }

    [Theory]
    [InlineData("cash ")]
    [InlineData(" CASH")]
    [InlineData("CASH")]
    [InlineData("cash")]
    public async Task V_CTR_02_cash_spellings_resolve_to_Cash_and_without_a_till_are_refused_with_zero_writes(string method)
    {
        await using var h = await NewAsync();
        var ex = await Refused(h, Paid(method), HccErrorCode.VALIDATION);
        Assert.Equal("Choose the till that receives the cash.", ex.Message);
    }

    [Theory]
    [InlineData("cash ")]
    [InlineData("CASH")]
    public async Task V_CTR_03_cash_spellings_with_an_open_till_post_one_bill_one_movement_stored_as_Cash(string method)
    {
        await using var h = await NewAsync();
        var (till, _) = await h.OpenTillAsync(0, Ct);
        var before = await CountsAsync(h);
        var r = await Issue(h, Paid(method, null, till));
        var after = await CountsAsync(h);
        Assert.Equal(before[0] + 1, after[0]); // bill
        Assert.Equal(before[2] + 1, after[2]); // one drawer movement
        Assert.Equal(before[3] + 1, after[3]); // one invoice
        Assert.Equal("Cash", await h.ScalarAsync($"SELECT method FROM ctr.bill WHERE bill_id = {r.Value.BillId}", Ct));
        Assert.Equal(r.Value.TotalPaise, await h.LongAsync($"SELECT amount_paise FROM pay.cash_movement WHERE source_type = 'counter-bill' AND source_id = {r.Value.BillId}", Ct));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CounterBillIssued' AND JSON_VALUE(payload, '$.tender') = 'Cash'", Ct));
    }

    // V-CTR-04 references
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task V_CTR_04_UPI_card_and_bank_without_a_real_reference_are_refused(string? reference)
    {
        await using var h = await NewAsync();
        foreach (var m in new[] { "UPI", "upi", "Card", "Bank transfer" })
            Assert.Equal("Enter the transaction reference for a non-cash payment.", (await Refused(h, Paid(m, reference), HccErrorCode.VALIDATION)).Message);
    }

    // V-CTR-05 cash / drawer abuse: a till id that is not an open shift's, an unknown till, a negative till, a UPI bill that sneaks a till in
    [Fact]
    public async Task V_CTR_05_cash_with_an_unknown_or_closed_till_is_refused_and_a_non_cash_bill_ignores_the_till()
    {
        await using var h = await NewAsync();
        await Refused(h, Paid("Cash", null, 999_999));
        await Refused(h, Paid("Cash", null, -1));
        await Refused(h, Paid("Cash", null, 0));
        var (till, shift) = await h.OpenTillAsync(0, Ct);
        // UPI + a till id: no drawer movement
        var r = await Issue(h, Paid("UPI", "REF-1", till));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM pay.cash_movement WHERE source_id = {r.Value.BillId} AND source_type = 'counter-bill'", Ct));
        Assert.Equal(DBNull.Value, await h.ScalarAsync($"SELECT receipt_id FROM ctr.bill WHERE bill_id = {r.Value.BillId}", Ct));
        await h.CountExactAsync(shift.ShiftId, Ct); // submit the count -> no open shift on this till any more
        var state = await h.ScalarAsync($"SELECT TOP 1 status FROM pay.shift WHERE till_id = {till} ORDER BY shift_id DESC", Ct);
        var closed = await Assert.ThrowsAsync<HccException>(() => Issue(h, Paid("Cash", null, till)));
        Assert.True(closed.Code is HccErrorCode.VALIDATION or HccErrorCode.INVALID_TRANSITION or HccErrorCode.NOT_FOUND, $"shift status {state}: {closed.Code} {closed.Message}");
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM ctr.bill", Ct)); // only the UPI bill
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'counter-bill'", Ct));
    }

    // V-CTR-06 the cash receipt limit still fires, also with a spelling variant
    [Fact]
    public async Task V_CTR_06_cash_receipt_limit_fires_for_cash_spellings_and_leaves_nothing_behind()
    {
        await using var h = await NewAsync();
        var (till, _) = await h.OpenTillAsync(0, Ct);
        foreach (var m in new[] { "Cash", "cash ", "CASH" })
        {
            var ex = await Refused(h, new(new("Walk-in (fictional)", Address: "1 Test Road, Latur (fictional)"), [new("Banquet hall · wedding", 1, 19_000_000, "banquet")], new("paid", m, null, till)), HccErrorCode.VALIDATION);
            Assert.Equal("CASH_LIMIT", ex.Details!["reason"].GetString());
        }

        // the same amount by UPI is not a cash receipt
        Assert.True((await Issue(h, new(new("Walk-in (fictional)", Address: "1 Test Road, Latur (fictional)"), [new("Banquet hall · wedding", 1, 19_000_000, "banquet")], new("paid", "UPI", "BIG-1")))).Value.TotalPaise > 19_000_000);
    }

    // V-CTR-07 replay and two keys
    [Fact]
    public async Task V_CTR_07_same_key_same_payload_replays_two_keys_post_two_bills_same_key_different_payload_is_a_mismatch()
    {
        await using var h = await NewAsync();
        var (till, _) = await h.OpenTillAsync(0, Ct);
        var key = Hotel.Key();
        var a = await Issue(h, Paid("Cash", null, till), key: key);
        var before = await CountsAsync(h);
        var same = await Issue(h, Paid("Cash", null, till), key: key);
        Assert.True(same.Replayed);
        Assert.Equal(a.Value.BillId, same.Value.BillId);
        // a different spelling under the same key is a different payload -> IDEMPOTENCY_MISMATCH (a silent replay or a second bill would both be wrong)
        var ex = await Assert.ThrowsAsync<HccException>(() => Issue(h, Paid("cash", null, till), key: key));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, ex.Code);
        Assert.Equal(before, await CountsAsync(h));
    }

    [Fact]
    public async Task V_CTR_07b_same_key_different_amount_is_IDEMPOTENCY_MISMATCH_and_two_keys_same_payload_are_two_bills()
    {
        await using var h = await NewAsync();
        var key = Hotel.Key();
        await Issue(h, Paid("UPI", "R1"), key: key);
        var before = await CountsAsync(h);
        var ex = await Assert.ThrowsAsync<HccException>(() => Issue(h, Paid("UPI", "R1", unit: 9999), key: key));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, ex.Code);
        Assert.Equal(before, await CountsAsync(h));
        var x = await Issue(h, Paid("UPI", "R1"));
        var y = await Issue(h, Paid("UPI", "R1"));
        Assert.NotEqual(x.Value.BillId, y.Value.BillId);
        Assert.NotEqual(x.Value.InvoiceNo, y.Value.InvoiceNo);
    }

    // V-CTR-08 settlement modes with a stray method
    [Fact]
    public async Task V_CTR_08_udhaar_and_company_modes_ignore_a_stray_method_and_never_touch_the_drawer()
    {
        await using var h = await NewAsync();
        var (till, _) = await h.OpenTillAsync(0, Ct);
        var u = await Issue(h, new(new("Walk-in C (fictional)", "9820005555"), [new("Dinner", 2, 40000, "food")], new("udhaar", "Cashh", "ZZ", till)));
        Assert.NotNull(u.Value.UdhaarId);
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'counter-bill'", Ct));
        Assert.True(await h.ScalarAsync($"SELECT method FROM ctr.bill WHERE bill_id = {u.Value.BillId}", Ct) is DBNull or null);
        // company mode on an unknown company: refused with zero extra writes
        var before = await CountsAsync(h);
        var ex = await Assert.ThrowsAsync<HccException>(() => Issue(h, new(new("Walk-in D (fictional)"), [new("Dinner", 1, 40000, "food")], new("company", "Cash", null, till, 987654))));
        Assert.Equal(before, await CountsAsync(h));
        Assert.True(ex.Code is HccErrorCode.VALIDATION or HccErrorCode.NOT_FOUND, ex.Code + " " + ex.Message);
        // company mode with a real company and the method "Cash" + a till: no drawer movement, method not stored as a tender
        await h.ScalarAsync($"INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) VALUES ({h.PropertyId}, N'Fictional Travels', 100000000, {h.Owner}, SYSUTCDATETIME());", Ct);
        var cid = await h.LongAsync($"SELECT company_id FROM grp.company_account WHERE property_id = {h.PropertyId}", Ct);
        var c = await Issue(h, new(new("Walk-in E (fictional)"), [new("Dinner", 1, 40000, "food")], new("company", "Cash", null, till, cid)));
        Assert.Equal(0, await h.LongAsync($"SELECT COUNT(*) FROM pay.cash_movement WHERE source_id = {c.Value.BillId} AND source_type = 'counter-bill'", Ct));
        Assert.True(await h.ScalarAsync($"SELECT method FROM ctr.bill WHERE bill_id = {c.Value.BillId}", Ct) is DBNull or null);
    }

    // V-CTR-09 mode text
    [Theory]
    [InlineData("PAID")]
    [InlineData(" paid ")]
    public async Task V_CTR_09_mode_is_case_and_space_insensitive_and_an_unknown_mode_is_refused(string mode)
    {
        await using var h = await NewAsync();
        var ok = await Issue(h, new(new("W (fictional)"), [new("Tea", 1, 2500, "food")], new(mode, "UPI", "R9")));
        Assert.Equal("paid", ok.Value.Settlement);
        await Refused(h, new(new("W (fictional)"), [new("Tea", 1, 2500, "food")], new("cheque", "UPI", "R9")), HccErrorCode.VALIDATION);
        await Refused(h, new(new("W (fictional)"), [new("Tea", 1, 2500, "food")], new("", "UPI", "R9")), HccErrorCode.VALIDATION);
    }

    // V-CTR-10 price edits (DEC-241 is pending: free price is current behaviour; the hostile part is the arithmetic and the bounds)
    [Fact]
    public async Task V_CTR_10_extreme_prices_quantities_and_discounts_are_refused_not_crashed_and_write_nothing()
    {
        await using var h = await NewAsync();
        await h.ScalarAsync($"INSERT bill.extra_item(property_id, code, name, category, gst_category, unit, department, sort_order, active, updated_by, updated_utc) VALUES ({h.PropertyId}, 'DRYCLEAN', N'Dry cleaning', 'laundry', 'laundry', 'piece', 'laundry', 20, 1, 0, SYSUTCDATETIME()); "
            + $"INSERT bill.extra_item_price(extra_item_id, effective_from, amount_paise) SELECT extra_item_id, '2025-04-01', 25000 FROM bill.extra_item WHERE property_id = {h.PropertyId} AND code = 'DRYCLEAN';", Ct);
        // negative / zero / overflow-sized values
        foreach (var unit in new long[] { -1, 0, long.MinValue })
            await Refused(h, Paid("UPI", "R", unit: unit), HccErrorCode.VALIDATION);
        foreach (var qty in new[] { 0, -1, 100, int.MaxValue, int.MinValue })
            await Refused(h, Paid("UPI", "R", qty: qty), HccErrorCode.VALIDATION);
        // discount larger than the bill / equal to the bill / negative
        await Refused(h, Paid("UPI", "R", unit: 10000, qty: 1, discount: 10001), HccErrorCode.VALIDATION);
        await Refused(h, Paid("UPI", "R", unit: 10000, qty: 1, discount: -5), HccErrorCode.VALIDATION);
        var owner = await Assert.ThrowsAsync<HccException>(() => Issue(h, Paid("UPI", "R", unit: 10000, qty: 1, discount: 10000), h.Owner));
        Assert.Equal(HccErrorCode.VALIDATION, owner.Code); // a zero-total bill
        // a price lower than the catalogue price is currently accepted (DEC-241 pending): record the observable fact, keep the money consistent
        var cheap = await Issue(h, new(new("W (fictional)"), [new("Dry cleaning", 1, 1, ExtraId: "DRYCLEAN")], new("paid", "UPI", "R-cheap")));
        Assert.Equal(1, cheap.Value.NetPaise);
        Assert.Equal(cheap.Value.NetPaise + cheap.Value.TaxPaise, cheap.Value.TotalPaise);
        // retired / unknown extra id and menu item id
        await Refused(h, new(new("W"), [new("", 1, 0, ExtraId: "NOSUCH")], new("paid", "UPI", "R")), HccErrorCode.VALIDATION);
        await Refused(h, new(new("W"), [new("", 1, 0, MenuItemId: "abc")], new("paid", "UPI", "R")), HccErrorCode.VALIDATION);
        await Refused(h, new(new("W"), [new("", 1, 0, MenuItemId: "999999")], new("paid", "UPI", "R")), HccErrorCode.VALIDATION);
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MaxValue / 2)]
    [InlineData(long.MaxValue / 90)]
    [InlineData(10_000_000_000_000_000L)]
    [InlineData(1_000_000_000_000_000L)]
    [InlineData(100_000_000_000_000L)]
    [InlineData(10_000_000_000_000L)]
    [InlineData(1_000_000_000_000L)]
    public async Task V_CTR_10b_overflow_sized_line_is_refused_VALIDATION_not_an_unhandled_exception(long unit)
    {
        await using var h = await NewAsync();
        var before = await CountsAsync(h);
        Exception? caught = null;
        try { await Issue(h, new(new("W (fictional)", Address: "1 Test Road, Latur (fictional)"), [new("Tea", 99, unit, "food")], new("paid", "UPI", "R"))); }
        catch (Exception e) { caught = e; }
        Assert.True(caught is HccException { Code: HccErrorCode.VALIDATION }, $"unit {unit} x 99: expected VALIDATION, got {caught?.GetType().Name}: {caught?.Message}");
        Assert.Equal(before, await CountsAsync(h));
    }

    // V-CTR-11 totals are paise-exact for odd values (tax + net = total, cgst + sgst = tax)
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 33333)]
    [InlineData(99, 12345)]
    [InlineData(7, 2501)]
    public async Task V_CTR_11_totals_reconcile_to_the_paise_for_odd_lines(int qty, long unit)
    {
        await using var h = await NewAsync();
        foreach (var cat in new[] { "food", "laundry", "banquet" })
        {
            var r = (await Issue(h, new(new("W (fictional)"), [new("Item", qty, unit, cat)], new("paid", "UPI", "R-" + cat)))).Value;
            Assert.Equal(qty * unit, r.NetPaise);
            Assert.Equal(r.NetPaise + r.TaxPaise, r.TotalPaise);
            Assert.Equal(r.TaxPaise, r.CgstPaise + r.SgstPaise);
        }
    }

    // V-CTR-12 older-bill lookup bounds (CON-01 / HIS-01)
    [Fact]
    public async Task V_CTR_12_lookup_bounds_wildcards_injection_permissions_and_take_clamping()
    {
        await using var h = await NewAsync();
        var nos = new List<string>();
        for (var i = 0; i < 4; i++) nos.Add((await Issue(h, Paid("UPI", "R" + i, unit: 1000 + i))).Value.InvoiceNo);
        var reads = h.Get<LfMoneyReadService>();
        foreach (var take in new int?[] { 0, -5, int.MinValue })
            Assert.Single(await reads.CounterBillsAsync(h.PropertyId, h.Reception, take: take, ct: Ct)); // clamped to 1, never 0 / negative / error
        Assert.Equal(4, (await reads.CounterBillsAsync(h.PropertyId, h.Reception, take: 100_000, ct: Ct)).Count); // clamped to 500
        // wildcard / injection / overlong / whitespace-only
        foreach (var bad in new[] { "%", "_", "INV/%", "' OR 1=1 --", "'; DROP TABLE ctr.bill; --", "[a-z]%", "\0" })
            Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: bad, ct: Ct));
        Assert.Equal(4, (await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: "   ", ct: Ct)).Count); // blank = no filter
        var long41 = new string('9', 41);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: long41, ct: Ct))).Code);
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: new string('9', 40), ct: Ct));
        Assert.Equal(4, await h.LongAsync("SELECT COUNT(*) FROM ctr.bill", Ct)); // the injection string did nothing
        // exact match only: lower-case / partial / neighbour
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: nos[0].ToLowerInvariant() == nos[0] ? nos[0] + "x" : nos[0].ToLowerInvariant(), ct: Ct)
            .ContinueWith(t => t.Result.Where(r => r.InvoiceNo != nos[0]).ToList(), Ct));
        // dates: equal bounds include the day, far bounds, MinValue / MaxValue
        var day = (await reads.CounterBillsAsync(h.PropertyId, h.Reception, ct: Ct))[0].BusinessDate;
        Assert.Equal(4, (await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: day, to: day, ct: Ct)).Count);
        Assert.Equal(4, (await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: DateOnly.MinValue, to: DateOnly.MaxValue, ct: Ct)).Count);
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: day.AddDays(1), ct: Ct));
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, to: day.AddDays(-1), ct: Ct));
        // permissions: money roles only; the accountant has none, housekeeping none
        foreach (var who in new[] { h.Accountant })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => reads.CounterBillsAsync(h.PropertyId, who, invoiceNo: nos[0], ct: Ct))).Code);
        // a lookup never returns another property's bill
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId + 1000, h.Reception, ct: Ct).ContinueWith(t => t.IsFaulted ? new List<CounterBillListRow>() : t.Result.ToList(), Ct));
    }

    // V-CTR-13 the older-bill lookup feeds the credit-note request: a bill id of another kind / unknown is refused
    [Fact]
    public async Task V_CTR_13_void_request_for_unknown_bill_id_is_NOT_FOUND_and_a_found_old_bill_can_be_requested_once()
    {
        await using var h = await NewAsync();
        var r = (await Issue(h, Paid("UPI", "R1"))).Value;
        var svc = h.Get<CounterBillService>();
        foreach (var id in new long[] { 0, -1, 987654321, long.MaxValue })
        {
            var ex = await Assert.ThrowsAsync<HccException>(() => svc.RequestVoidAsync(h.Ctx(h.Reception, CounterBillService.OpRequestVoid), new(id, "Billed twice"), Ct));
            Assert.Equal(HccErrorCode.NOT_FOUND, ex.Code);
        }

        var req = await svc.RequestVoidAsync(h.Ctx(h.Reception, CounterBillService.OpRequestVoid), new(r.BillId, "Billed twice"), Ct);
        Assert.True(req.Value.ApprovalId > 0);
    }
}
