using System.Globalization;
using System.Text.Json.Nodes;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8D-TESTS-A] Server tests for the money-review fixes of F8-INT (H6 rate re-post, H7 meal-plan line, H8 explicit postings, H9 Udhaar guest
/// credit) and the report rule R6 (high balance of a split stay), over HTTP with the real composition. Fictional data; company GSTIN
/// 27AAPFU0939F1ZV. Owner-question defaults used: SFQ-6 (night posting never refused), SFQ-20 (mixed-rate adjustment refused).
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8dSplitMoneyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Gstin = "27AAPFU0939F1ZV";

    private sealed record Stay(long ReservationId, CheckInResult CheckIn, long FolioA, long FolioB, long CompanyId);

    private static async Task<long> LongAsync(HccServer s, string sql) => Convert.ToInt64(await s.ScalarAsync(sql, Ct), CultureInfo.InvariantCulture);

    private static async Task<Stay> OpenAsync(HccServer s, EndToEndTests.Staff staff, string guest, string phone, string[] routes, long limit = 5_000_000)
    {
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, phone), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto(guest, phone, Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1401")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", Gstin, City: "Latur", CreditLimitPaise: limit), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, routes, "Company pays"), Ct);
        return new Stay(booking.ReservationId, stay, stay.FolioId, opened.FolioId, company.CompanyId);
    }

    // ── H6: the rate re-post keeps each night on its own folio ───────────────────────────────────────────────────
    [Fact]
    public async Task F8D_H6_rate_repost_of_a_night_routed_to_B_reverses_and_reposts_on_B_nothing_lands_on_A()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var st = await OpenAsync(s, staff, "H6 Guest (fictional)", "9800000401", ["room"]);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(st.CheckIn.StayId, new DateOnly(2026, 10, 24)), Ct);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB} AND kind = 'room'", Ct));

        var r = await Wire.CommandAsync<StayRateChangeResult>(staff.Owner, "ChangeStayRate", new ChangeStayRateRequest(st.CheckIn.StayId, 250000, null, null, "today-next", "Corporate rate (test)"), Ct);
        Assert.Equal("Applied", r.Status);
        Assert.Single(r.Reposted);

        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA}", Ct));
        // B: original night 3,000, its reversal, the new night 2,500 (one live room line, at the new rate)
        Assert.Equal(3, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB}", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB} AND kind = 'room' AND amount_paise = 250000 AND reverses_charge_id IS NULL AND reversed_by_charge_id IS NULL", Ct));
        Assert.Equal(250000L, await LongAsync(s, $"SELECT SUM(amount_paise) FROM bill.charge WHERE folio_id = {st.FolioB}"));
        var summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{st.ReservationId}/summary", Ct);
        var b = summary.Single(f => f.Letter == "B");
        Assert.Equal((250000L, 12500L, 262500L), (b.NetPaise, b.TaxPaise, b.TotalPaise));
        Assert.Equal(0L, summary.Single(f => f.Letter == "A").TotalPaise);
    }

    [Fact]
    public async Task F8D_H6_a_night_posted_on_A_before_the_room_was_routed_is_reposted_on_A_not_on_the_current_route()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var st = await OpenAsync(s, staff, "H6b Guest (fictional)", "9800000402", []);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(st.CheckIn.StayId, new DateOnly(2026, 10, 24)), Ct);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA} AND kind = 'room'", Ct));
        // the room is routed to B only afterwards (routing applies to later postings)
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "SetFolioRouting", new SetFolioRoutingRequest(st.ReservationId, 2, ["room"], "Company pays from now"), Ct);

        var r = await Wire.CommandAsync<StayRateChangeResult>(staff.Owner, "ChangeStayRate", new ChangeStayRateRequest(st.CheckIn.StayId, 250000, null, null, "today-next", "Corporate rate (test)"), Ct);
        Assert.Equal("Applied", r.Status);

        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB}", Ct));
        Assert.Equal(3, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA}", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA} AND kind = 'room' AND amount_paise = 250000 AND reversed_by_charge_id IS NULL", Ct));
        Assert.Equal(250000L, await LongAsync(s, $"SELECT SUM(amount_paise) FROM bill.charge WHERE folio_id = {st.FolioA}"));
    }

    // ── H7: the meal-plan line follows its room night's folio ────────────────────────────────────────────────────
    [Fact]
    public async Task F8D_H7_meal_plan_line_sits_on_the_folio_of_its_room_night_B_not_on_the_extras_folio_A()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var st = await OpenAsync(s, staff, "H7 Guest (fictional)", "9800000403", ["room"]);
        await Wire.CommandAsync<object>(staff.Owner, "SaveMealPlan", new SaveMealPlanRequest(null, "SEPX", "Separate plan (test)", ["B"], "separate", null, [new DatedAmount("2026-10-24", 20000)]), Ct);
        await Wire.CommandAsync<ReservationMealPlanResult>(staff.Owner, "SetReservationMealPlan", new SetReservationMealPlanRequest(st.ReservationId, "SEPX"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(st.CheckIn.StayId, new DateOnly(2026, 10, 24)), Ct);

        var run = await Wire.CommandAsync<MealPlanNightsResult>(staff.Owner, "PostMealPlanNights", new PostMealPlanNightsRequest(), Ct);
        Assert.Equal(1, run.Posted);
        var line = run.ChargeIds[0];
        Assert.Equal(st.FolioB, await LongAsync(s, $"SELECT folio_id FROM bill.charge WHERE charge_id = {line}"));
        Assert.Equal(20000L, await LongAsync(s, $"SELECT amount_paise FROM bill.charge WHERE charge_id = {line}"));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA}", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.meal_plan_night WHERE folio_id = {st.FolioB}", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.meal_plan_night WHERE folio_id = {st.FolioA}", Ct));
    }

    // ── H8: explicit postings are not re-routed ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task F8D_H8_ExplicitFolio_flag_keeps_a_charge_and_a_discount_on_A_while_a_plain_charge_is_routed_to_B()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var st = await OpenAsync(s, staff, "H8 Guest (fictional)", "9800000404", ["room", "extras"]);

        var vb0 = (await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{st.FolioB}", Ct)).Version;
        // no flag: addressed to the primary folio, routed by category (extras to B), and B's version moves (DEC-041)
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(st.FolioA, "Laundry routed", 50000, "laundry"), Ct);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB} AND label = N'Laundry routed'", Ct));
        Assert.NotEqual(vb0, (await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{st.FolioB}", Ct)).Version);
        // flag: posted on A exactly although extras are routed to B
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(st.FolioA, "Laundry explicit", 50000, "laundry", ExplicitFolio: true), Ct);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioA} AND label = N'Laundry explicit'", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB} AND label = N'Laundry explicit'", Ct));

        // a discount on folio A (single-rate folio: 18 % laundry) stays on A although "room" (the discount category) is routed to B
        var va = await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{st.FolioA}", Ct);
        var d = await Wire.CommandAsync<AdjustmentResult>(staff.Owner, "RequestAdjustment", new RequestAdjustmentRequest("discount", st.FolioA, null, 10000, "Goodwill (test)", va.Version), Ct);
        Assert.Equal("Posted", d.Outcome);
        Assert.Equal(st.FolioA, await LongAsync(s, $"SELECT folio_id FROM bill.charge WHERE charge_id = {d.ChargeId}"));
        Assert.Equal(-10000L, await LongAsync(s, $"SELECT amount_paise FROM bill.charge WHERE charge_id = {d.ChargeId}"));
        Assert.Equal(1800L, await LongAsync(s, $"SELECT tax_bps FROM bill.charge WHERE charge_id = {d.ChargeId}"));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {st.FolioB} AND kind = 'discount'", Ct));
    }

    // ── H9: guest credit on an Udhaar folio creates the receivable on the payer ──────────────────────────────────
    private sealed record GuestCreditReply(long ApprovalId, string ApprovalVersion, long AmountPaise, string FolioVersion);

    [Fact]
    public async Task F8D_H9_udhaar_folio_guest_credit_raises_the_receivable_on_the_payer_never_on_the_stay_guest()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-25", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("H9 Stay Guest (fictional)", "9800000405"), room), Ct);
        var det = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, det.Version,
            new GuestDraftDto("H9 Stay Guest (fictional)", "9800000405", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1405")), Ct);
        det = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, det.Version), Ct);

        var payer = Convert.ToInt64(await s.ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, origin, created_utc) VALUES ({s.PropertyId}, N'H9 Udhaar Payer (fictional)', '9800000505', 'manual', SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """, Ct), CultureInfo.InvariantCulture);
        await Wire.CommandAsync<object>(staff.Owner, "SetGuestCreditAccount", new { guestId = payer, limitPaise = 1_000_000L, active = true }, Ct);
        var key = (string)(await s.ScalarAsync($"SELECT customer_key FROM gst.guest_credit_account WHERE guest_id = {payer}", Ct))!;
        var b = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "udhaar", null, key, null, null, ["extras"], "Udhaar customer pays the extras"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(stay.FolioId, "Laundry", 50000, "laundry"), Ct); // routed to B

        var req = await Wire.CommandAsync<GuestCreditReply>(staff.Reception, "RequestGuestCredit", new { stayId = stay.StayId, reason = "Regular, pays monthly", folioId = b.FolioId }, Ct);
        Assert.Equal(59000L, req.AmountPaise);
        await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId, true, "Known customer", req.ApprovalVersion), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, new DateOnly(2026, 10, 24)), Ct);
        await EndToEndTests.CloseDayAsync(staff.Owner, new DateOnly(2026, 10, 24));
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(stay.FolioId, "payment", "UPI", 315000, "UPI-H9-A", null), Ct);
        await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stay.StayId, stay.StayVersion, CreditApprovalIds: [req.ApprovalId]), Ct);

        var stayGuest = await LongAsync(s, $"SELECT guest_id FROM gst.stay WHERE stay_id = {stay.StayId}");
        Assert.NotEqual(payer, stayGuest);
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM ops.udhaar_receivable", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE guest_id = {payer} AND folio_id = {b.FolioId} AND amount_paise = 59000", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE guest_id = {stayGuest}", Ct));
    }

    // ── R6: high-balance report of a split stay ──────────────────────────────────────────────────────────────────
    private static async Task<JsonNode> HighBalanceRowAsync(HttpClient owner, string guest, string query)
    {
        using var r = await owner.GetAsync(ApiConventions.BasePath + "/reports/rpt.money.high-balance?all=true" + query, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, body);
        var rows = JsonNode.Parse(body)!["rows"]!.AsArray().Where(x => (string?)x!["cells"]!["guest"] == guest).ToList();
        return Assert.Single(rows)!["cells"]!;
    }

    [Fact]
    public async Task F8D_R6_high_balance_one_row_per_split_stay_company_folio_against_the_limit_guest_folio_against_the_threshold()
    {
        await using var s = await HccServer.StartAsync(SfGoldenTests.Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        const string guest = "R6 Guest (fictional)";
        var st = await OpenAsync(s, staff, guest, "9800000406", ["room"]);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(st.FolioA, "payment", "UPI", 100000, "UPI-R6", null), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(st.FolioA, "Laundry", 50000, "laundry"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(st.FolioA, "Room service food", 120000, "food"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(st.CheckIn.StayId, new DateOnly(2026, 10, 24)), Ct);
        // A: 1,850 - 1,000 advance = 850; B: one night 3,000 + 150 GST = 3,150
        var sum = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{st.ReservationId}/summary", Ct);
        Assert.Equal((85000L, 315000L), (sum.Single(f => f.Letter == "A").BalancePaise, sum.Single(f => f.Letter == "B").BalancePaise));

        // within both tests: a 5,000-rupee threshold and the 50,000-rupee limit: one row, the stay balance once, no exception
        var c = await HighBalanceRowAsync(staff.Owner, guest, "&threshold=5000");
        Assert.Equal(400000L, c["balance"]!.GetValue<long>());
        Assert.Equal(5_000_000L, c["limit"]!.GetValue<long>());
        Assert.Equal("", (string?)c["exception"] ?? "");

        // guest folio A (850 rupees) above a 100-rupee threshold: red for the guest folio only; over by 750 rupees
        c = await HighBalanceRowAsync(staff.Owner, guest, "&threshold=100");
        Assert.Contains("Guest folio balance above 100 rupees", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.DoesNotContain("Company folio", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.Equal(75000L, c["overBy"]!.GetValue<long>());

        // limit lowered to 3,000 rupees: the company folio (3,150) is over by 150; the deposit on A is not counted
        await Wire.CommandAsync<CompanyResult>(staff.Owner, "SetCompanyLimit", new SetCompanyLimitRequest(st.CompanyId, 300_000, "Lowered (test)"), Ct);
        c = await HighBalanceRowAsync(staff.Owner, guest, "&threshold=5000");
        Assert.Contains("Company folio above its credit limit", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.DoesNotContain("Guest folio", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.Equal(15000L, c["overBy"]!.GetValue<long>());

        // both over: red for either, over-by is the larger excess
        c = await HighBalanceRowAsync(staff.Owner, guest, "&threshold=100");
        Assert.Contains("Company folio above its credit limit", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.Contains("Guest folio balance above", (string)c["exception"]!, StringComparison.Ordinal);
        Assert.Equal(75000L, c["overBy"]!.GetValue<long>());
    }
}
