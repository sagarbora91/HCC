using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Groups.Tests.Sql;

/// <summary>SQL integration on a disposable HccTest_* database per test (real RBAC, GST master, business-day guard and Billing store).</summary>
[Trait("Category", "Sql")]
public sealed class GroupsSqlTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<long> PostLaundryAsync(string code, long rupees = 500)
        => (await _w.Billing.PostChargeAsync(_w.Caller("reception"), Guid.NewGuid().ToString("N"), new PostChargeRequest(_w.Folio[code], "Laundry · shirts", rupees * 100, "laundry"))).Value.ChargeId;

    // ── Golden: the prototype master folio of GB-1 ──

    [Fact]
    public async Task Statement_reproduces_the_prototype_GB1_master_folio()
    {
        var g = await _w.CreateGb1Async();
        Assert.StartsWith("GRP-", g.Code, StringComparison.Ordinal);
        var s = await _w.StatementAsync(g.GroupId);
        Assert.Equal(2, s.Members.Count);
        Assert.Equal("27ZZFIC0001F1Z5", s.Gstin);
        foreach (var m in s.Members)
        {
            Assert.Equal((200000L, 10000L, 210000L, 200000L, 10000L, 430000L), (m.NetPaise, m.TaxPaise, m.TotalPaise, m.FolioPaidPaise, m.FolioBalancePaise, m.EstimatePaise));
            var night = Assert.Single(m.Nights);
            Assert.Equal(new DateOnly(2026, 10, 23), night.Night);
            var line = Assert.Single(night.Lines);
            Assert.Equal((500, 10000L, 5000L, 5000L, "room"), (line.TaxBps, line.TaxPaise, line.CgstPaise, line.SgstPaise, line.GstCategory));
            Assert.Equal(("master", "master", false, 210000L), (line.NetTarget, line.TaxTarget, line.Routed, line.MasterPaise)); // preview until routed
        }

        Assert.Equal(new[] { "Arjun Kapoor", "Dev Malhotra" }, s.Members.Select(m => m.Guest));
        Assert.Equal(new[] { "201", "302" }, s.Members.Select(m => m.Room));
        Assert.Equal((400000L, 860000L, 2), (s.Totals.MembersPaidPaise, s.Totals.MembersEstimatePaise, s.Totals.UnroutedLines));
        var p = Assert.Single(s.Pickup);
        Assert.Equal((4, 2, 2, 0, 50), (p.Allotted, p.Picked, p.Released, p.Remaining, p.PickedPercent));
        Assert.Equal("Released", s.BlockStatus);
        Assert.Equal((420000L, 20000L, 10000L, 10000L, 420000L), (s.Totals.MasterNetPaise + s.Totals.MasterTaxPaise, s.Totals.MasterTaxPaise, s.Totals.MasterCgstPaise,
            s.Totals.MasterSgstPaise, s.Totals.MasterBalancePaise));
    }

    // ── Routing and pickup billing ──

    [Fact]
    public async Task Pickup_billing_routes_posted_lines_by_the_rules_and_is_idempotent()
    {
        var g = await _w.CreateGb1Async();
        await PostLaundryAsync("HCC-1044");
        var r1 = (await _w.Commands.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId))).Value;
        Assert.Equal((3, 0), (r1.Routed, r1.AlreadyRouted));
        var r2 = (await _w.Commands.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId))).Value;
        Assert.Equal((0, 3), (r2.Routed, r2.AlreadyRouted));

        var s = await _w.StatementAsync(g.GroupId);
        var arjun = s.Members.Single(m => m.PublicNo == "HCC-1044");
        Assert.Equal((210000L, 59000L), (arjun.MasterSharePaise, arjun.GuestSharePaise)); // laundry Rs 500 + 18 % stays with the guest
        Assert.Equal(0, s.Totals.UnroutedLines);
        Assert.Equal(420000, s.Totals.MasterChargesPaise);

        // A rule change applies to lines not yet routed only: extras → master for HCC-1048; a routed room night keeps its route.
        await _w.Commands.SetRoutingAsync(_w.Ctx("reception"), new SetGroupRoutingRequest(g.GroupId, await _w.VersionAsync(g.GroupId), _w.Res["HCC-1048"], "extras", "master"));
        await _w.Commands.SetRoutingAsync(_w.Ctx("reception"), new SetGroupRoutingRequest(g.GroupId, await _w.VersionAsync(g.GroupId), null, "room", "guest"));
        var laundry = await PostLaundryAsync("HCC-1048", 200);
        s = await _w.StatementAsync(g.GroupId);
        var dev = s.Members.Single(m => m.PublicNo == "HCC-1048");
        var line = dev.Nights.SelectMany(n => n.Lines).Single(l => l.ChargeId == laundry);
        Assert.Equal(("master", "master", false), (line.NetTarget, line.TaxTarget, line.Routed));
        Assert.Equal("master", dev.Nights.SelectMany(n => n.Lines).Single(l => l.ChargeId == _w.Night23["HCC-1048"]).NetTarget);
        Assert.Equal(1, s.Totals.UnroutedLines);
        Assert.Equal(1, (await _w.Commands.ApplyRoutingAsync(_w.Ctx("manager"), new ApplyGroupRoutingRequest(g.GroupId))).Value.Routed);
        Assert.Equal(210000 + 23600, (await _w.StatementAsync(g.GroupId)).Members.Single(m => m.PublicNo == "HCC-1048").MasterSharePaise);
    }

    [Fact]
    public async Task Rerouting_a_posted_line_appends_a_route_with_audit_and_never_edits_the_charge()
    {
        var g = await _w.CreateGb1Async();
        await _w.Commands.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId));
        var charge = _w.Night23["HCC-1048"];
        var v = await _w.VersionAsync(g.GroupId);
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.RerouteAsync(_w.Ctx("reception"), new RerouteGroupChargeRequest(g.GroupId, v, charge, "guest", "guest", " ")));
        var r = (await _w.Commands.RerouteAsync(_w.Ctx("reception"), new RerouteGroupChargeRequest(g.GroupId, v, charge, "guest", "guest", "Guest pays own room (organiser e-mail)"))).Value;
        Assert.Equal(2, r.Seq);
        await Expect.Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.RerouteAsync(_w.Ctx("reception"), new RerouteGroupChargeRequest(g.GroupId, v, charge, "master", "master", "Back")));

        Assert.Equal(2, await _w.LongAsync($"SELECT COUNT(*) FROM grp.line_route WHERE charge_id = {charge};"));
        Assert.Equal(200000, await _w.LongAsync($"SELECT amount_paise FROM bill.charge WHERE charge_id = {charge};"));
        Assert.Equal(1, await _w.LongAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {_w.Folio["HCC-1048"]};"));
        Assert.Equal(1, await _w.LongAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'GROUP_CHARGE_REROUTED' AND target = 'charge:{charge}' AND detail LIKE N'%organiser e-mail%';"));
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'GroupChargeRerouted';"));
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync($"UPDATE grp.line_route SET net_target = 'master' WHERE charge_id = {charge};"));
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync($"DELETE grp.line_route WHERE charge_id = {charge};"));

        var s = await _w.StatementAsync(g.GroupId);
        var dev = s.Members.Single(m => m.PublicNo == "HCC-1048");
        Assert.Equal((0L, 210000L), (dev.MasterSharePaise, dev.GuestSharePaise));
        var line = dev.Nights.Single().Lines.Single();
        Assert.Equal(("guest", 2, true), (line.NetTarget, line.RouteSeq, line.Routed));
    }

    [Fact]
    public async Task Transfer_moves_the_master_share_off_the_guest_folio_as_company_credit()
    {
        var g = await _w.CreateGb1Async();
        var t = (await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"]))).Value; // routes first
        Assert.Equal(210000, t.AmountPaise);
        var folio = await _w.Folios.GetFolioAsync(_w.Caller("reception"), _w.Folio["HCC-1044"]);
        Assert.Equal((210000L, -200000L), (folio.Totals.CompanyCreditPaise, folio.Totals.BalancePaise)); // the guest's own Rs 2,000 is now a credit to refund
        var receipt = folio.Receipts.Single(x => x.ReceiptId == t.FolioReceiptId);
        Assert.Equal(("company-credit", "Company credit"), (receipt.Kind, receipt.Tender));
        Assert.Contains(g.Code, receipt.Reference, StringComparison.Ordinal);
        Assert.Equal(1, await _w.LongAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReceiptRecorded' AND aggregate_id = {_w.Folio["HCC-1044"]};"));

        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"])));
        // Re-routing below the transferred share is refused; more lines to the master can follow and be transferred.
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.RerouteAsync(_w.Ctx("reception"),
            new RerouteGroupChargeRequest(g.GroupId, t.Version, _w.Night23["HCC-1044"], "guest", "guest", "Guest pays")));
        var laundry = await PostLaundryAsync("HCC-1044");
        await _w.Commands.RerouteAsync(_w.Ctx("reception"), new RerouteGroupChargeRequest(g.GroupId, await _w.VersionAsync(g.GroupId), laundry, "master", "master", "Company pays laundry"));
        Assert.Equal(59000, (await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"]))).Value.AmountPaise);
        var s = await _w.StatementAsync(g.GroupId);
        Assert.Equal((269000L, 0L), (s.Members.Single(m => m.PublicNo == "HCC-1044").TransferredPaise, s.Members.Single(m => m.PublicNo == "HCC-1044").PendingTransferPaise));
        Assert.Equal(210000, s.Totals.PendingTransferPaise); // HCC-1048 still on its guest folio
    }

    // ── Master folio receipts ──

    [Fact]
    public async Task Master_receipts_are_non_cash_capped_and_permissioned()
    {
        var g = await _w.CreateGb1Async();
        RecordGroupReceiptRequest R(string kind, string tender, long amount, long? of = null) => new(g.GroupId, kind, tender, amount, "NEFT-778812", of);
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.RecordReceiptAsync(_w.Ctx("reception"), R("payment", "Cash", 100000)));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.RecordReceiptAsync(_w.Ctx("reception"), new RecordGroupReceiptRequest(g.GroupId, "payment", "UPI", 100000, "")));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.RecordReceiptAsync(_w.Ctx("housekeeper"), R("deposit", "UPI", 100000)));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), R("payment", "Bank transfer", 420001)));
        var dep = (await _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), R("deposit", "Bank transfer", 500000))).Value; // advance above the charges
        Assert.Equal(new DateOnly(2026, 10, 24), dep.BusinessDate);
        var s = await _w.StatementAsync(g.GroupId);
        Assert.Equal((500000L, -80000L), (s.Totals.DepositsPaise, s.Totals.MasterBalancePaise));
        await Expect.Fails(HccErrorCode.APPROVAL_REQUIRED, () => _w.Commands.RecordReceiptAsync(_w.Ctx("reception"), R("refund", "Bank transfer", 80000, dep.EntryId)));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.RecordReceiptAsync(_w.Ctx("owner"), R("refund", "Bank transfer", 90000, dep.EntryId)));
        await _w.Commands.RecordReceiptAsync(_w.Ctx("manager"), R("refund", "Bank transfer", 80000, dep.EntryId));
        Assert.Equal(0, (await _w.StatementAsync(g.GroupId)).Totals.MasterBalancePaise);
        await Assert.ThrowsAsync<SqlException>(() => _w.ExecAsync("UPDATE grp.master_entry SET amount_paise = 1;"));
    }

    // ── Attrition ──

    [Fact]
    public async Task Attrition_after_cut_off_needs_a_different_approver_and_posts_GST_per_line()
    {
        var g = await _w.CreateGb1Async(allowanceBps: 0, attritionBps: 5000);
        var a = (await _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(g.GroupId, g.Version, "Contract clause 7: 50 % retention"))).Value;
        Assert.Equal(("Pending", 525000L, 26250L), (a.Status, a.NetPaise, a.TaxPaise)); // 2 rooms × Rs 875 × 3 nights; 5 % (Rs 875 per unit-night ≤ Rs 7,500)
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, async () => await _w.Commands.RequestAttritionAsync(_w.Ctx("manager"), new RequestGroupAttritionRequest(g.GroupId, await _w.VersionAsync(g.GroupId), "Again")));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.DecideAttritionAsync(_w.Ctx("reception"), new DecideGroupAttritionRequest(a.AttritionId, true)));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.DecideAttritionAsync(_w.Ctx("housekeeper"), new DecideGroupAttritionRequest(a.AttritionId, true)));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.DecideAttritionAsync(_w.Ctx("manager"), new DecideGroupAttritionRequest(a.AttritionId, false)));
        var d = (await _w.Commands.DecideAttritionAsync(_w.Ctx("manager"), new DecideGroupAttritionRequest(a.AttritionId, true, "Agreed with organiser"))).Value; // Rs 5,512.50 ≤ Manager limit Rs 10,000
        Assert.Equal(("Posted", 3), (d.Status, d.EntryIds.Count));

        var s = await _w.StatementAsync(g.GroupId);
        var lines = s.Master.Where(m => m.Kind == "attrition").ToList();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal((175000L, 500, 8750L, 4375L, 4375L), (l.AmountPaise, l.TaxBps!.Value, l.TaxPaise!.Value, l.CgstPaise!.Value, l.SgstPaise!.Value)));
        Assert.Equal(new[] { 23, 24, 25 }, lines.Select(l => l.ServiceDate!.Value.Day));
        Assert.Equal(420000 + 551250, s.Totals.MasterChargesPaise);
        Assert.Equal(1, await _w.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'GroupAttritionPosted';"));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, async () => await _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(g.GroupId, await _w.VersionAsync(g.GroupId), "Again")));
    }

    [Fact]
    public async Task Attrition_is_refused_while_pickup_is_open_and_above_the_manager_limit_needs_the_owner()
    {
        var open = (await _w.Commands.CreateAsync(_w.Ctx("reception"), new CreateGroupRequest("Godavari Agro dealers meet", _w.Gb2, "Sunita Patil", "Godavari Agro Inputs (fictional)",
            "Sunita Patil", AttritionRateBps: 10000))).Value;
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(open.GroupId, open.Version, "Clause")));

        // Past the cut-off (business date 28 Oct): GB-2 is derived Released; 6 Classic × Rs 1,800 + 3 Deluxe × Rs 2,700, 2 nights at 100 %.
        await _w.ExecAsync($"UPDATE inv.property SET business_date = '2026-10-28' WHERE property_id = {_w.PropertyId};");
        var a = (await _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(open.GroupId, await _w.VersionAsync(open.GroupId), "Clause 4"))).Value;
        Assert.Equal(2 * (6 * 180000 + 3 * 270000), a.NetPaise);
        await Expect.Fails(HccErrorCode.APPROVAL_REQUIRED, () => _w.Commands.DecideAttritionAsync(_w.Ctx("manager"), new DecideGroupAttritionRequest(a.AttritionId, true)));
        Assert.Equal("Rejected", (await _w.Commands.DecideAttritionAsync(_w.Ctx("owner"), new DecideGroupAttritionRequest(a.AttritionId, false, "Waived: repeat client"))).Value.Status);
        Assert.Empty((await _w.StatementAsync(open.GroupId)).Master);
    }

    // ── Day closed, permissions, membership, settlement ──

    [Fact]
    public async Task Group_writes_are_refused_on_a_closed_business_day()
    {
        var g = await _w.CreateGb1Async();
        var run = await _w.ScalarAsync($"""
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            OUTPUT INSERTED.run_id VALUES ({_w.PropertyId}, '2026-10-24', 'r', 'r', 'Closed', 'close', 1, {_w.Users["owner"]}, '2026-10-24T20:00:00', {_w.Users["owner"]}, '2026-10-24T20:00:00');
            """);
        await _w.ExecAsync($"INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES ({_w.PropertyId}, '2026-10-24', {run}, {_w.Users["owner"]}, '2026-10-24T20:00:00');");
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => _w.Commands.CreateAsync(_w.Ctx("owner"), new CreateGroupRequest("Late group", null, "A", "B", "C")));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => _w.Commands.ApplyRoutingAsync(_w.Ctx("owner"), new ApplyGroupRoutingRequest(g.GroupId)));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => _w.Commands.RerouteAsync(_w.Ctx("owner"), new RerouteGroupChargeRequest(g.GroupId, g.Version, _w.Night23["HCC-1044"], "guest", "guest", "Late")));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => _w.Commands.TransferAsync(_w.Ctx("owner"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"])));
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => _w.Commands.RecordReceiptAsync(_w.Ctx("owner"), new RecordGroupReceiptRequest(g.GroupId, "deposit", "UPI", 1000, "UPI-1")));
        Assert.Equal(0, await _w.LongAsync("SELECT COUNT(*) FROM grp.line_route;"));
    }

    [Fact]
    public async Task Permissions_follow_the_group_rows_of_the_role_matrix()
    {
        foreach (var u in new[] { "housekeeper", "accountant", "cashier" })
            await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.CreateAsync(_w.Ctx(u), new CreateGroupRequest("X group", null, "A", "B", "C")));
        var g = await _w.CreateGb1Async("reception");
        Assert.NotNull(await _w.StatementAsync(g.GroupId, "accountant")); // view only
        Assert.NotNull(await _w.StatementAsync(g.GroupId, "cashier"));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.StatementAsync(g.GroupId, "housekeeper"));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.ListAsync(_w.PropertyId, _w.Users["housekeeper"]));
        Assert.Single(await _w.Queries.ListAsync(_w.PropertyId, _w.Users["accountant"]));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.ApplyRoutingAsync(_w.Ctx("accountant"), new ApplyGroupRoutingRequest(g.GroupId)));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Commands.CreateAsync(_w.Ctx("owner"), new CreateGroupRequest("Bad GSTIN", null, "A", "B", "C", Gstin: "12345")));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.CreateAsync(_w.Ctx("owner"), new CreateGroupRequest("Second master", _w.Gb1, "A", "B", "C")));
    }

    [Fact]
    public async Task Members_attach_and_detach_outside_the_block_and_idempotency_replays()
    {
        var key = Guid.NewGuid().ToString("N");
        var req = new CreateGroupRequest("Rotary district meet", null, "Mr Deshmukh", "Rotary Club Latur (fictional)", "Mr Deshmukh",
            Rules: [new RoutingRuleInput("room", "guest"), new RoutingRuleInput("extras", "guest")]);
        var first = await _w.Commands.CreateAsync(_w.Ctx("reception", key), req);
        var again = await _w.Commands.CreateAsync(_w.Ctx("reception", key), req);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.GroupId, again.Value.GroupId);
        var gid = first.Value.GroupId;
        await _w.Commands.AttachMemberAsync(_w.Ctx("reception"), new GroupMemberRequest(gid, first.Value.Version, _w.Res["HCC-2001"]));
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, async () => await _w.Commands.AttachMemberAsync(_w.Ctx("reception"),
            new GroupMemberRequest(gid, await _w.VersionAsync(gid), _w.Res["HCC-1044"]))); // a pickup of block GB-1 belongs to that block's group
        var s = await _w.StatementAsync(gid);
        Assert.Equal(("HCC-2001", "attached"), (s.Members.Single().PublicNo, s.Members.Single().Source));
        Assert.Equal(new[] { "guest", "guest", "follow" }, s.Rules.OrderBy(r => r.Category, StringComparer.Ordinal).Select(r => r.Target));
        await _w.Commands.DetachMemberAsync(_w.Ctx("reception"), new GroupMemberRequest(gid, await _w.VersionAsync(gid), _w.Res["HCC-2001"]));
        Assert.Empty((await _w.StatementAsync(gid)).Members);
    }

    [Fact]
    public async Task Settlement_requires_checkout_full_transfer_and_a_zero_master_balance()
    {
        var g = await _w.CreateGb1Async();
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.SettleAsync(_w.Ctx("reception"), new SettleGroupRequest(g.GroupId, g.Version))); // in house
        await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"]));
        await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1048"]));
        await _w.ExecAsync($"UPDATE inv.reservation SET status = 'Departed' WHERE property_id = {_w.PropertyId} AND group_block_id = {_w.Gb1};"); // checked out (Stays)
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, async () => await _w.Commands.SettleAsync(_w.Ctx("reception"), new SettleGroupRequest(g.GroupId, await _w.VersionAsync(g.GroupId))));
        await _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), new RecordGroupReceiptRequest(g.GroupId, "payment", "Bank transfer", 420000, "NEFT-9912"));
        var settled = (await _w.Commands.SettleAsync(_w.Ctx("reception"), new SettleGroupRequest(g.GroupId, await _w.VersionAsync(g.GroupId)))).Value;
        Assert.Equal("Settled", settled.Status);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), new RecordGroupReceiptRequest(g.GroupId, "deposit", "UPI", 100, "UPI-1")));
        var s = await _w.StatementAsync(g.GroupId);
        Assert.Equal(("Settled", 420000L, 0L, 420000L), (s.Status, s.Totals.TransferredPaise, s.Totals.MasterBalancePaise, s.Totals.ReceivedPaise));
    }

    // ── Read port for W3-6 ──

    [Fact]
    public async Task Invoice_port_exposes_routed_master_lines_and_attrition_with_the_GST_split()
    {
        var g = await _w.CreateGb1Async(attritionBps: 5000);
        await PostLaundryAsync("HCC-1044");
        Assert.Equal(3, (await _w.Invoices.GetAsync(null, _w.PropertyId, g.GroupId))!.UnroutedLines);
        await _w.Commands.ApplyRoutingAsync(_w.Ctx("reception"), new ApplyGroupRoutingRequest(g.GroupId));
        var a = (await _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(g.GroupId, await _w.VersionAsync(g.GroupId), "Clause 7"))).Value;
        await _w.Commands.DecideAttritionAsync(_w.Ctx("owner"), new DecideGroupAttritionRequest(a.AttritionId, true));

        var inv = (await _w.Invoices.GetAsync(null, _w.PropertyId, g.GroupId))!;
        Assert.Equal(("Deccan Pharma Distributors (fictional)", "27ZZFIC0001F1Z5", 0), (inv.Party.BillingName, inv.Party.Gstin, inv.UnroutedLines));
        Assert.Equal(2, inv.Lines.Count(l => l.Source == "folio-charge"));
        Assert.Equal(3, inv.Lines.Count(l => l.Source == "attrition"));
        Assert.Equal((925000L, 46250L, 23125L, 23125L, 971250L), (inv.MasterNetPaise, inv.MasterTaxPaise, inv.MasterCgstPaise, inv.MasterSgstPaise, inv.MasterChargesPaise));
        var shares = await _w.Invoices.MasterSharesForFolioAsync(null, _w.PropertyId, _w.Folio["HCC-1044"]);
        var share = Assert.Single(shares); // the laundry line stays on the guest folio invoice
        Assert.Equal((_w.Night23["HCC-1044"], 210000L, true, true), (share.ChargeId, share.MasterPaise, share.NetToMaster, share.TaxToMaster));
    }
}
