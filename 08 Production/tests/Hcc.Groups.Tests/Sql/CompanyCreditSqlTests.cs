using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// DEC-076 on a disposable HccTest_* database with the real RBAC seed (2201 + 3501), GST master and Billing store: the company credit limit is
/// Owner only (A05-01) and enforced (A03a-05). World: HCC-1044 / HCC-1048 in house 23–26 Oct at Rs 2,000 a night, the 23 Oct night posted at 5 %
/// (Rs 2,000 + Rs 100 GST), Rs 2,000 paid each, 24 and 25 Oct agreed (Rs 2,000 + Rs 100 each) but not posted; business date 24 Oct.
/// One stay's estimate = 2,100 + 2 × 2,100 − 2,000 = Rs 4,300 (430000); folio balance = 2,100 − 2,000 = Rs 100 (10000).
/// </summary>
[Trait("Category", "Sql")]
public sealed class CompanyCreditSqlTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private const string Deccan = "Deccan Pharma Distributors (fictional)";

    private static string Key() => Guid.NewGuid().ToString("N");

    private static SaveCompanyRequest New(string name, long limit) => new(null, null, name, CreditLimitPaise: limit);

    private async Task<long> CompanyAsync(string name, long limitPaise) => (await _w.Companies.SaveAsync(_w.Ctx("owner"), New(name, limitPaise))).Value.CompanyId;

    /// <summary>The booking is billed to the company account (the Reservations path with its own check is in Hcc.Inventory.Tests).</summary>
    private Task LinkAsync(string code, long companyId)
        => _w.ExecAsync($"UPDATE inv.reservation SET company_id = {companyId}, company_name = N'{Deccan}' WHERE reservation_id = {_w.Res[code]};");

    private async Task<string> FolioVersionAsync(string code) => (await _w.Folios.GetFolioAsync(_w.Caller("owner"), _w.Folio[code])).Version;

    private async Task<AdjustmentResult> RequestCreditAsync(string code, long amount)
        => (await _w.CreditBilling.RequestAdjustmentAsync(_w.Caller("reception"), Key(), new RequestAdjustmentRequest("credit", _w.Folio[code], null, amount,
            "Bill to " + Deccan, await FolioVersionAsync(code)))).Value;

    private async Task<CompanyCreditView> CreditAsync(long companyId)
        => (await _w.CreditBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies.Single(c => c.CompanyId == companyId).Credit!;

    private async Task<long> LaundryAsync(string code, long paise)
        => (await _w.CreditBilling.PostChargeAsync(_w.Caller("reception"), Key(), new PostChargeRequest(_w.Folio[code], "Laundry · shirts", paise, "laundry"))).Value.ChargeId;

    // ── A05-01: the limit is the Owner's ──

    [Fact]
    public async Task Manager_only_requests_a_limit_and_the_Owner_approves_or_rejects_it()
    {
        // Before DEC-076 the Manager's PERM-GRP-003 amount limit (Rs 10,000) let a Manager SET a limit up to Rs 10,000 directly; Rs 5,000 is inside it.
        var id = await CompanyAsync(Deccan, 0);
        var req = (await _w.Companies.SetLimitAsync(_w.Ctx("manager"), new SetCompanyLimitRequest(id, 500_000, "Monthly corporate stays"))).Value;
        Assert.Equal("requested", req.LimitStatus);
        var opening = (await _w.Companies.SaveAsync(_w.Ctx("manager"), New("Godavari Agro Inputs (fictional)", 500_000))).Value;
        Assert.Equal("requested", opening.LimitStatus);
        Assert.All((await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies, c => Assert.Equal(0, c.CreditLimitPaise));

        // Nobody but the Owner decides: the Manager (another person's request) and Reception are refused.
        var third = await CompanyAsync("Sahyadri Infra Projects (fictional)", 0);
        var byReception = (await _w.Companies.SetLimitAsync(_w.Ctx("reception"), new SetCompanyLimitRequest(third, 100_000, "Site team"))).Value;
        var ex = await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Companies.DecideAsync(_w.Ctx("manager"), new DecideCompanyLimitRequest(byReception.RequestId!.Value, true)));
        Assert.Contains("Only the Owner", ex.Message, StringComparison.Ordinal);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Companies.DecideAsync(_w.Ctx("reception"), new DecideCompanyLimitRequest(req.RequestId!.Value, true)));

        // The Owner approves → the limit changes; the Owner rejects → it stays.
        Assert.Equal("set", (await _w.Companies.DecideAsync(_w.Ctx("owner"), new DecideCompanyLimitRequest(req.RequestId!.Value, true))).Value.LimitStatus);
        Assert.Equal("none", (await _w.Companies.DecideAsync(_w.Ctx("owner"), new DecideCompanyLimitRequest(opening.RequestId!.Value, false, "Not yet"))).Value.LimitStatus);
        var book = await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"]);
        Assert.Equal(500_000L, book.Companies.Single(c => c.CompanyId == id).CreditLimitPaise);
        Assert.Equal(0L, book.Companies.Single(c => c.CompanyId == opening.CompanyId).CreditLimitPaise);
        Assert.Equal(["Approved", "Pending", "Rejected"], book.Requests.Select(r => r.Status).Order(StringComparer.Ordinal));

        // The Owner sets a limit directly (the pending Reception request is superseded).
        Assert.Equal("set", (await _w.Companies.SetLimitAsync(_w.Ctx("owner"), new SetCompanyLimitRequest(third, 200_000))).Value.LimitStatus);

        // The seed cells of migration 3501: PERM-GRP-006 = OWN A, MGR R, REC R and nobody else.
        Assert.Equal("MGR:R,OWN:A,REC:R", (string?)await _w.ScalarAsync(
            "SELECT STRING_AGG(CONCAT(role_code, ':', mode), ',') WITHIN GROUP (ORDER BY role_code) FROM plat.role_grant WHERE permission_id = 'PERM-GRP-006';"));
    }

    // ── A03a-05: exposure ──

    [Fact]
    public async Task Exposure_figures_worked_by_hand_and_a_limit_of_zero_means_no_credit()
    {
        var id = await CompanyAsync(Deccan, 1_000_000); // Rs 10,000
        var c0 = await CreditAsync(id);
        Assert.Equal((0L, 1_000_000L), (c0.ExposurePaise, c0.RemainingPaise));
        await LinkAsync("HCC-1044", id);
        // HCC-1044: Rs 4,300 (see the class summary). Remaining = 10,000 − 4,300 = Rs 5,700.
        var c1 = await CreditAsync(id);
        Assert.Equal((430_000L, 0L, 430_000L, 0L, 570_000L), (c1.ExposurePaise, c1.ReceivablePaise, c1.OpenStaysPaise, c1.GroupMastersPaise, c1.RemainingPaise));
        var item = Assert.Single(c1.Items);
        Assert.Equal(("stay", _w.Res["HCC-1044"], "HCC-1044", 430_000L), (item.Kind, item.Id, item.Reference, item.AmountPaise));
        // Posting the 24 Oct night does not change the estimate (it was already counted as an agreed night).
        await _w.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, principal_id, posted_utc) VALUES ({_w.Folio["HCC-1044"]}, 'room', '2026-10-24', N'Room night · 2026-10-24', 200000, 500, 10000, {_w.Users["owner"]}, '2026-10-24T18:00:00');");
        Assert.Equal(430_000L, (await CreditAsync(id)).ExposurePaise);
        // A second stay billed to the company adds its own Rs 4,300: 8,600; a cancelled booking counts nothing.
        await LinkAsync("HCC-1048", id);
        Assert.Equal(860_000L, (await CreditAsync(id)).ExposurePaise);
        await _w.ExecAsync($"UPDATE inv.reservation SET status = 'Cancelled' WHERE reservation_id = {_w.Res["HCC-1048"]};");
        Assert.Equal(430_000L, (await CreditAsync(id)).ExposurePaise);
        // The accountant (PERM-GRP-004 V) reads the figures; the payments list comes with them.
        var acc = await _w.CreditBook.BookAsync(_w.PropertyId, _w.Users["accountant"]);
        Assert.Equal(430_000L, acc.Companies.Single().Credit!.ExposurePaise);
        Assert.NotNull(acc.Payments);

        // Limit 0 = no credit: even the Rs 100 balance cannot go on company credit.
        var none = await CompanyAsync("Zero Credit Traders (fictional)", 0);
        await _w.ExecAsync($"UPDATE inv.reservation SET company_id = {none}, status = 'InHouse' WHERE reservation_id = {_w.Res["HCC-1048"]};");
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => RequestCreditAsync("HCC-1048", 10_000));
        Assert.Equal("Credit limit exceeded for Zero Credit Traders (fictional): limit ₹0 (no credit), used ₹0, remaining ₹0; this stay on company credit needs ₹100. "
            + "Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).", ex.Message);
    }

    // ── A03a-05: company credit refused beyond the limit; a payment frees credit ──

    [Fact]
    public async Task Company_credit_beyond_the_limit_is_refused_with_limit_used_and_remaining_and_a_company_payment_frees_credit()
    {
        var id = await CompanyAsync(Deccan, 1_000_000);
        await LinkAsync("HCC-1044", id);
        await LinkAsync("HCC-1048", id);
        Assert.Equal(860_000L, (await CreditAsync(id)).ExposurePaise); // 2 × Rs 4,300
        await _w.Companies.SetLimitAsync(_w.Ctx("owner"), new SetCompanyLimitRequest(id, 440_000, "Lowered after review")); // Rs 4,400

        // 1) HCC-1048's Rs 100 balance: used = 8,600 − 4,300 (the stay itself) = Rs 4,300; 4,300 + 100 = 4,400 ≤ 4,400 → allowed.
        var r1 = await RequestCreditAsync("HCC-1048", 10_000);
        Assert.Equal("PendingApproval", r1.Outcome);
        var d1 = (await _w.CreditBilling.DecideApprovalAsync(_w.Caller("owner"), Key(), new DecideApprovalRequest(r1.ApprovalId!.Value, true, null, r1.ApprovalVersion!))).Value;
        var x1 = (await _w.CreditBilling.ExecuteAdjustmentAsync(_w.Caller("reception"), Key(), new ExecuteAdjustmentRequest(r1.ApprovalId.Value, d1.Version, null, null, null))).Value;
        // The receipt names the company: its receivable is Rs 100; HCC-1048's estimate drops to 4,300 − 100 = Rs 4,200. Total unchanged at Rs 8,600.
        Assert.Equal(id, await _w.LongAsync($"SELECT company_id FROM pay.receipt WHERE receipt_id = {x1.ResultId};"));
        var c1 = await CreditAsync(id);
        Assert.Equal((860_000L, 10_000L, 850_000L, 0L), (c1.ExposurePaise, c1.ReceivablePaise, c1.OpenStaysPaise, c1.RemainingPaise));

        // 2) Rs 50 laundry at 18 % on HCC-1044 (Rs 50 + Rs 9 = Rs 59) → balance Rs 100 + Rs 59 = Rs 159. used = receivable 100 + HCC-1048 4,200 = Rs 4,300;
        //    4,300 + 159 = 4,459 > 4,400 → refused, remaining Rs 100.
        await LaundryAsync("HCC-1044", 5_000);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => RequestCreditAsync("HCC-1044", 15_900));
        Assert.Equal($"Credit limit exceeded for {Deccan}: limit ₹4,400, used ₹4,300, remaining ₹100; this stay on company credit needs ₹159. "
            + "Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).", ex.Message);
        Assert.Equal(("CREDIT_LIMIT", 440_000L, 430_000L, 10_000L, 15_900L), (ex.Details!["reason"].GetString(), ex.Details["limitPaise"].GetInt64(),
            ex.Details["usedPaise"].GetInt64(), ex.Details["remainingPaise"].GetInt64(), ex.Details["neededPaise"].GetInt64()));
        Assert.Equal(0L, await _w.LongAsync($"SELECT COUNT(*) FROM bill.approval WHERE folio_id = {_w.Folio["HCC-1044"]} AND action = 'credit';"));

        // 3) The company pays the Rs 100 invoice: never cash, never more than it owes.
        var pay = new RecordCompanyPaymentRequest(id, "Bank transfer", 10_000, "NEFT-DEMO-0001", "inv/2627/0001");
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Tender = "Cash" }));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { AmountPaise = 10_001 }));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Credit.RecordPaymentAsync(_w.Ctx("housekeeper"), pay));
        var paid = (await _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay)).Value;
        // Receivable 0; used = 8,659 − 100 = Rs 8,559 (HCC-1044 4,359 + HCC-1048 4,200); over the lowered limit, so nothing remains overall.
        Assert.Equal((0L, 855_900L, 0L), (paid.ReceivablePaise, paid.UsedPaise, paid.RemainingPaise));
        Assert.Equal("INV/2627/0001", (string?)await _w.ScalarAsync($"SELECT invoice_no FROM grp.company_payment WHERE payment_id = {paid.PaymentId};"));
        Assert.Equal(1L, await _w.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CompanyPaymentRecorded';"));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Reference = "NEFT-DEMO-0002" })); // owes nothing

        // 4) The payment freed Rs 100: used for HCC-1044 = HCC-1048 Rs 4,200; 4,200 + 159 = 4,359 ≤ 4,400 → allowed now.
        var r2 = await RequestCreditAsync("HCC-1044", 15_900);
        Assert.Equal("PendingApproval", r2.Outcome);

        // 5) The approval re-checks: Rs 100 laundry at 18 % (Rs 118) on HCC-1048 raises its estimate to Rs 4,318; 4,318 + 159 = 4,477 > 4,400 → refused.
        await LaundryAsync("HCC-1048", 10_000);
        var late = await Expect.Fails(HccErrorCode.INVALID_TRANSITION,
            () => _w.CreditBilling.DecideApprovalAsync(_w.Caller("owner"), Key(), new DecideApprovalRequest(r2.ApprovalId!.Value, true, null, r2.ApprovalVersion!)));
        Assert.Contains("used ₹4,318, remaining ₹82; this stay on company credit needs ₹159", late.Message, StringComparison.Ordinal);
        var book = await _w.CreditBook.BookAsync(_w.PropertyId, _w.Users["owner"]);
        Assert.Equal(paid.PaymentId, Assert.Single(book.Payments!).PaymentId);
    }

    [Fact]
    public async Task Company_credit_needs_a_booking_billed_to_a_company_account()
    {
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => RequestCreditAsync("HCC-1044", 10_000));
        Assert.Contains("not billed to a company account", ex.Message, StringComparison.Ordinal);
        // Without the guard (test wiring of the Billing module alone) the old behaviour stays.
        var plain = await _w.Billing.RequestAdjustmentAsync(_w.Caller("reception"), Key(), new RequestAdjustmentRequest("credit", _w.Folio["HCC-1044"], null, 10_000, "x",
            await FolioVersionAsync("HCC-1044")));
        Assert.Equal("PendingApproval", plain.Value.Outcome);
    }

    // ── A03a-05: charge routing to a company-billed group master ──

    [Fact]
    public async Task Routing_lines_to_a_company_master_beyond_the_limit_is_refused_and_master_receipts_free_credit()
    {
        var id = await CompanyAsync(Deccan, 100_000); // Rs 1,000
        await LinkAsync("HCC-1044", id); // a member billed to the same company counts once, on the master
        var g = (await _w.CreditGroups.CreateAsync(_w.Ctx("reception"), new CreateGroupRequest("Deccan Pharma regional review", _w.Gb1, "Rahul Joshi", Deccan,
            "Rahul Joshi", CompanyId: id))).Value;
        Assert.Equal(id, (await _w.StatementAsync(g.GroupId)).CompanyId);
        Assert.Equal((0L, 0L, 0L), ((await CreditAsync(id)).ExposurePaise, (await CreditAsync(id)).OpenStaysPaise, (await CreditAsync(id)).GroupMastersPaise));

        // Room + its tax → master: the two 23 Oct nights = 2 × Rs 2,100 = Rs 4,200 > Rs 1,000 → refused, nothing routed.
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.CreditGroups.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId)));
        Assert.Equal($"Credit limit exceeded for {Deccan}: limit ₹1,000, used ₹0, remaining ₹1,000; this routing to group master {g.Code} needs ₹4,200. "
            + "Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).", ex.Message);
        Assert.Equal(0L, await _w.LongAsync($"SELECT COUNT(*) FROM grp.line_route WHERE group_id = {g.GroupId};"));

        // The Owner raises the limit to Rs 5,000: routing passes; exposure = the master's Rs 4,200, remaining Rs 800.
        await _w.Companies.SetLimitAsync(_w.Ctx("owner"), new SetCompanyLimitRequest(id, 500_000));
        Assert.Equal(2, (await _w.CreditGroups.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId))).Value.Routed);
        var c1 = await CreditAsync(id);
        Assert.Equal((420_000L, 0L, 420_000L, 80_000L), (c1.ExposurePaise, c1.OpenStaysPaise, c1.GroupMastersPaise, c1.RemainingPaise));
        Assert.Equal(("group", g.GroupId, g.Code), (c1.Items.Single().Kind, c1.Items.Single().Id, c1.Items.Single().Reference));

        // A Rs 1,000 master payment frees Rs 1,000: master exposure 4,200 − 1,000 = Rs 3,200.
        await _w.CreditGroups.RecordReceiptAsync(_w.Ctx("reception"), new RecordGroupReceiptRequest(g.GroupId, "payment", "Bank transfer", 100_000, "NEFT-GRP-1"));
        Assert.Equal((320_000L, 180_000L), ((await CreditAsync(id)).ExposurePaise, (await CreditAsync(id)).RemainingPaise));

        // Re-routing a Rs 500 laundry line (18 %: Rs 590) from the guest to the master: 3,200 + 590 = 3,790 ≤ 5,000 → allowed.
        var laundry = await LaundryAsync("HCC-1048", 50_000);
        await _w.CreditGroups.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId)); // extras → guest: no change
        Assert.Equal(320_000L, (await CreditAsync(id)).ExposurePaise);
        await _w.CreditGroups.RerouteAsync(_w.Ctx("reception"), new RerouteGroupChargeRequest(g.GroupId, await _w.VersionAsync(g.GroupId), laundry, "master", "master", "Company pays laundry"));
        Assert.Equal(379_000L, (await CreditAsync(id)).ExposurePaise);
    }
}
