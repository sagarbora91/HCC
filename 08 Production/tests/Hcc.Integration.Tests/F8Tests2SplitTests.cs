using System.Globalization;
using System.Net;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8-TESTS2] Server side of the split-folio cross-check tests listed in F8-INT-DONE: golden variants (b) and (d), the bill-correction
/// refusal (M3), M1 (a night posted on A, then the room routed to B), the folio-join audit on the real HTTP lists (a split stay is not
/// listed twice) and the DEC-061 walk for Housekeeping, Supervisor and Maintenance. Variant (c) is written against SF-CONTRACT §5.7c and
/// skipped by design: the server has no posting-time D7 check on the company folio (F8-INT-DONE, money review M2). Tests only, no product
/// code. Fictional data; company GSTIN 27AAPFU0939F1ZV (valid check digit). Owner-question defaults used: SFQ-6 (night posting is never
/// refused), SFQ-11 (bill correction refused on a split stay), SFQ-13 (Manager credit limit ₹10,000 per company folio).
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8Tests2SplitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string CompanyGstin = "27AAPFU0939F1ZV";
    private const string FirstPin = "816305";

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p INT = SCOPE_IDENTITY();
        UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = @p;
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 300000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '201', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '2026-10-01', '2027-01-01';
        SELECT @p;
        """;

    private sealed record Split(long ReservationId, long StayId, string StayVersion, long FolioA, long FolioB, long CompanyId);

    /// <summary>Book 3 nights ₹3,000, register, check in, company folio B takes the room (routes set before the nights unless <paramref name="routeNow"/> is false).</summary>
    private static async Task<(Split S, int RoomId)> CheckInSplitAsync(HccServer s, EndToEndTests.Staff staff, bool routeRoom = true, long limitPaise = 5_000_000)
    {
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Tests2 Split Guest (fictional)", "9800000202"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("Tests2 Split Guest (fictional)", "9800000202", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1202")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", CompanyGstin, City: "Latur", CreditLimitPaise: limitPaise), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, routeRoom ? ["room"] : [], "Company pays the room"), Ct);
        return (new Split(booking.ReservationId, stay.StayId, stay.StayVersion, stay.FolioId, opened.FolioId, company.CompanyId), room);
    }

    private static async Task PostNightAsync(EndToEndTests.Staff staff, Split sp, DateOnly d)
    {
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(sp.StayId, d), Ct);
        await EndToEndTests.CloseDayAsync(staff.Owner, d);
    }

    /// <summary>Not-OK reply of a command: status and error body.</summary>
    private static async Task<(HttpStatusCode Status, ErrorResponse Error)> FailAsync(HttpClient c, string name, object body)
    {
        using var r = await Wire.PostAsync(c, "/commands/" + name, body, Wire.NewKey(name), Ct);
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        return (r.StatusCode, JsonSerializer.Deserialize<ErrorResponse>(text, HccServer.Json)!);
    }

    private static string? ReasonOf(ErrorResponse e) => e.Details is not null && e.Details.TryGetValue("reason", out var v) ? v.GetString() : null;

    // ── variant (b): the D7 limit is lowered before checkout ─────────────────────────────────────────────────────
    [Fact]
    public async Task F8_TESTS2_variant_b_limit_lowered_before_checkout_company_credit_cannot_settle_B_guest_folio_unaffected()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (sp, _) = await CheckInSplitAsync(s, staff);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1)) await PostNightAsync(staff, sp, d);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(sp.FolioA, "Laundry", 50000, "laundry"), Ct);

        // The Owner lowers the limit to ₹9,000 (the company folio is ₹9,450).
        await Wire.CommandAsync<CompanyResult>(staff.Owner, "SetCompanyLimit", new SetCompanyLimitRequest(sp.CompanyId, 900_000, "Lowered by the owner (test)"), Ct);

        var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
        await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
        // Guest folio A pays on its own: unaffected by the company limit.
        var summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{sp.ReservationId}/summary", Ct);
        var a = summary.Single(f => f.Letter == "A");
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(sp.FolioA, "payment", "Cash", a.BalancePaise, null, till.TillId), Ct);
        summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{sp.ReservationId}/summary", Ct);
        Assert.Equal(0L, summary.Single(f => f.Letter == "A").BalancePaise);
        Assert.Equal(945000L, summary.Single(f => f.Letter == "B").BalancePaise);

        // Company credit for B: refused at request / approval / checkout (D7 is re-checked at each). At least the whole path must not settle B.
        var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{sp.FolioB}", Ct);
        using var req = await Wire.PostAsync(staff.Reception, "/commands/RequestAdjustment", new RequestAdjustmentRequest("credit", sp.FolioB, null, 945000, "Bill to Deccan (limit lowered)", fb.Version), Wire.NewKey("req"), Ct);
        var settled = false;
        if (req.StatusCode == HttpStatusCode.OK)
        {
            var r = JsonSerializer.Deserialize<AdjustmentResult>(await req.Content.ReadAsStringAsync(Ct), HccServer.Json)!;
            var (st, _) = await FailOrAsync(staff.Owner, "DecideApproval", new DecideApprovalRequest(r.ApprovalId!.Value, true, "Company account", r.ApprovalVersion!));
            if (st == HttpStatusCode.OK)
            {
                var (st2, _) = await FailOrAsync(staff.Reception, "CheckoutStay", new CheckoutStayRequest(sp.StayId, sp.StayVersion, r.ApprovalId.Value));
                settled = st2 == HttpStatusCode.OK;
            }
        }
        Assert.False(settled, "company credit must not settle folio B above the lowered limit");
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {sp.FolioB} AND kind = 'company-credit'", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {sp.FolioB}", Ct));
        // Guest folio A still carries no company money.
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {sp.FolioA} AND kind = 'company-credit'", Ct));
    }

    private static async Task<(HttpStatusCode, string)> FailOrAsync(HttpClient c, string name, object body)
    {
        using var r = await Wire.PostAsync(c, "/commands/" + name, body, Wire.NewKey(name), Ct);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(Ct));
    }

    // ── variant (c): an extra posted to B over the limit (SF-CONTRACT §5.7c) ─────────────────────────────────────
    [Fact(Skip = "BUG (money review M2, F8-INT-DONE 'Open items'): the server has no posting-time D7 check when a charge is posted to the company folio; "
               + "company credit is only re-checked at request / approval / checkout. Remove this Skip when the check exists: the extra ₹600 onto B over the limit must be refused, the same onto A must pass.")]
    public async Task F8_TESTS2_variant_c_extra_onto_B_over_limit_refused_same_onto_A_passes()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (sp, _) = await CheckInSplitAsync(s, staff);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1)) await PostNightAsync(staff, sp, d);
        await Wire.CommandAsync<CompanyResult>(staff.Owner, "SetCompanyLimit", new SetCompanyLimitRequest(sp.CompanyId, 1_000_000, "Tight limit (test)"), Ct);
        var (st, err) = await FailAsync(staff.Owner, "PostCharge", new PostChargeRequest(sp.FolioB, "Banquet extra", 60000, "laundry", ExplicitFolio: true));
        Assert.Equal("INVALID_TRANSITION", err.Code);
        Assert.NotEqual(HttpStatusCode.OK, st);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(sp.FolioA, "Banquet extra", 60000, "laundry", ExplicitFolio: true), Ct);
    }

    // ── M1: a night posted on A, then the room routed to B ───────────────────────────────────────────────────────
    [Fact]
    public async Task F8_TESTS2_M1_night_posted_on_A_then_room_routed_to_B_later_nights_follow_the_route_company_exposure_counts_B_only()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (sp, _) = await CheckInSplitAsync(s, staff, routeRoom: false);
        await PostNightAsync(staff, sp, new DateOnly(2026, 10, 24));                       // routed to A (no route set yet)
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "SetFolioRouting", new SetFolioRoutingRequest(sp.ReservationId, 2, ["room"], "Company pays from tonight"), Ct);
        await PostNightAsync(staff, sp, new DateOnly(2026, 10, 25));                       // routed to B
        await PostNightAsync(staff, sp, new DateOnly(2026, 10, 26));

        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {sp.FolioA} AND kind = 'room'", Ct));
        Assert.Equal(2, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {sp.FolioB} AND kind = 'room'", Ct));
        var summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{sp.ReservationId}/summary", Ct);
        Assert.Equal((300000L, 15000L, 315000L), (summary.Single(f => f.Letter == "A").NetPaise, summary.Single(f => f.Letter == "A").TaxPaise, summary.Single(f => f.Letter == "A").TotalPaise));
        Assert.Equal((600000L, 30000L, 630000L), (summary.Single(f => f.Letter == "B").NetPaise, summary.Single(f => f.Letter == "B").TaxPaise, summary.Single(f => f.Letter == "B").TotalPaise));
        Assert.Equal(945000L, summary.Sum(f => f.TotalPaise));                              // I2: stay = Σ folios, to the paisa
        // I6: no charge ever moved folio; the company's exposure counts the company folio (₹6,300), never the ₹3,150 on the guest folio.
        var book = await Wire.GetAsync<CompanyBookView>(staff.Owner, "/groups/companies", Ct);
        Assert.Equal(630000L, book.Companies.Single(c => c.CompanyId == sp.CompanyId).Credit!.ExposurePaise);
    }

    // ── M3: bill correction on a split stay is refused (SFQ-11) ──────────────────────────────────────────────────
    [Fact]
    public async Task F8_TESTS2_M3_bill_correction_on_a_split_stay_is_refused_use_a_credit_note_per_folio()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (sp, _) = await CheckInSplitAsync(s, staff);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1)) await PostNightAsync(staff, sp, d);
        var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
        await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
        var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{sp.FolioB}", Ct);
        var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", sp.FolioB, null, 945000, "Bill to Deccan Agro (fictional)", fb.Version), Ct);
        await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId!.Value, true, "Company account", req.ApprovalVersion!), Ct);
        var checkout = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(sp.StayId, sp.StayVersion, req.ApprovalId.Value), Ct);
        Assert.Equal(2, checkout.InvoiceNos!.Count);

        foreach (var no in checkout.InvoiceNos)
        {
            var (st, err) = await FailAsync(staff.Owner, "RequestBillCorrection",
                new Hcc.Application.Invoices.RequestBillCorrectionRequest(no, [new Hcc.Application.Invoices.BillChangeIn("add-line", Label: "Late fee", AmountPaise: 10000, GstCategory: "laundry", ServiceDate: new DateOnly(2026, 10, 26))], "Test correction"));
            Assert.True((int)st is >= 400 and < 500, "client error, not a 500: " + st);
            Assert.True(ReasonOf(err) == "SF_SPLIT_BILL_CORRECTION" || err.Message.Contains("credit note", StringComparison.OrdinalIgnoreCase), "refusal names the split rule: " + err.Message);
        }
        Assert.Equal(0, await s.CountAsync("SELECT COUNT(*) FROM ivc.invoice WHERE revision > 1", Ct));
    }

    // ── variant (d): DEC-061 walk ────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task F8_TESTS2_variant_d_DEC061_walk_Housekeeping_Supervisor_Maintenance_see_no_folio_money()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (sp, _) = await CheckInSplitAsync(s, staff);
        await PostNightAsync(staff, sp, new DateOnly(2026, 10, 24));

        var clients = new List<(string Who, HttpClient C)> { ("HKP", staff.Housekeeper) };
        using var sup = await PinClientAsync(s, staff, "sup.neha", "Neha (Supervisor)", "SUP", "829471");
        using var mnt = await PinClientAsync(s, staff, "mnt.joseph", "Joseph (Maintenance)", "MNT", "940263");
        clients.Add(("SUP", sup));
        clients.Add(("MNT", mnt));

        foreach (var (who, c) in clients)
        {
            // the folio summary endpoint grants (the split dialog, the folio switcher, the settle sheet and the phone folio read these)
            foreach (var path in new[] { $"/folios/by-reservation/{sp.ReservationId}/summary", $"/folios/by-reservation/{sp.ReservationId}", $"/folios/{sp.FolioA}", $"/folios/{sp.FolioB}" })
            {
                using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
                Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{who} GET {path}: expected 403, got {(int)r.StatusCode}");
                Assert.DoesNotContain("945000", await r.Content.ReadAsStringAsync(Ct));
            }
            // every split / folio money command
            await Wire.CommandFailsAsync(c, "OpenFolio", new OpenFolioRequest(sp.ReservationId, "guest", null, null, "Second Person", null, [], "x"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "SetFolioRouting", new SetFolioRoutingRequest(sp.ReservationId, 2, [], "No longer pays"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "RemoveFolio", new RemoveFolioRequest(sp.ReservationId, 2), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "RecordReceipt", new RecordReceiptRequest(sp.FolioB, "payment", "UPI", 100, "UPI-X", null), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "PostCharge", new PostChargeRequest(sp.FolioB, "x", 100, "laundry"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "RequestAdjustment", new RequestAdjustmentRequest("credit", sp.FolioB, null, 100, "x", "0"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
            await Wire.CommandFailsAsync(c, "CheckoutStay", new CheckoutStayRequest(sp.StayId, sp.StayVersion), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        }
        // the same reads are open to the Owner (the walk is not refusing everything)
        Assert.Equal(2, (await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{sp.ReservationId}/summary", Ct)).Count);
    }

    private static async Task<HttpClient> PinClientAsync(HccServer s, EndToEndTests.Staff staff, string login, string name, string role, string pin)
    {
        var u = await Wire.CommandAsync<UserVersionResult>(staff.Owner, "CreateUser", new CreateUserRequest(login, name, [new RoleAssignmentInput(role)]), Ct);
        await Wire.CommandAsync<UserVersionResult>(staff.Owner, "SetCredential", new SetCredentialRequest(u.UserId, null, FirstPin, u.Version), Ct);
        var c = s.DeviceClient(staff.DeviceToken);
        using var r = await Wire.PostAsync(c, "/auth/sign-in", new SignInRequest(login, Pin: FirstPin, ReturnToken: true), null, Ct);
        var result = await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct);
        HccServer.UseBearer(c, result.SessionToken!);
        await Wire.CommandAsync<UserVersionResult>(c, "ChangeOwnCredential", new ChangeOwnCredentialRequest(FirstPin, null, pin), Ct);
        return c;
    }

    // ── folio-join audit: a split stay is listed once on the HTTP lists ──────────────────────────────────────────
    [Fact]
    public async Task F8_TESTS2_folio_join_audit_opening_a_second_folio_does_not_duplicate_the_stay_on_any_list()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        // the stay checked in with ONE folio first: take the list sizes, then split it and compare
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Audit Split Guest (fictional)", "9800000203"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("Audit Split Guest (fictional)", "9800000203", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1203")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, new DateOnly(2026, 10, 24)), Ct);

        var lists = new[] { "/reservations", "/reservations?view=inhouse", "/guests", "/stays" };
        async Task<Dictionary<string, int>> CountsAsync()
        {
            var m = new Dictionary<string, int>();
            foreach (var path in lists)
            {
                using var r = await staff.Owner.GetAsync(ApiConventions.BasePath + path, Ct);
                var text = await r.Content.ReadAsStringAsync(Ct);
                m[path] = r.StatusCode == HttpStatusCode.OK ? System.Text.RegularExpressions.Regex.Count(text, "Audit Split Guest") : -1;
            }
            return m;
        }
        var before = await CountsAsync();
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Audit Co (fictional)", CompanyGstin, City: "Latur", CreditLimitPaise: 5_000_000), Ct);
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, ["extras"], "Company pays the extras"), Ct);
        await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(booking.ReservationId, "guest", null, null, "Second Person (fictional)", null, [], "Another person pays"), Ct);
        var after = await CountsAsync();
        foreach (var path in lists) Assert.True(before[path] == after[path], $"{path}: guest listed {before[path]}× before the split and {after[path]}× after");
        Assert.Contains(before.Values, v => v > 0); // at least one list really shows the guest (the audit is not vacuous)
    }
}
