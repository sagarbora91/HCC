using System.Globalization;
using System.Net;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8-FIX] Split-folio money fixes over HTTP with the real composition (fictional data):
/// M2 — D7 at posting on the company folio (SF-CONTRACT §7.3 variant 5.7c, SFQ-7); the cancel guard reads every folio of the booking;
/// guest credit requested on an Udhaar folio (SF-CONTRACT §2.2 / §3, SFQ-16) settles at checkout on the Udhaar payer's receivable.
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8FixTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private sealed record GuestCreditReply(long ApprovalId, string ApprovalVersion, long AmountPaise, string FolioVersion);

    /// <summary>Book one night ₹3,000 (5 %: ₹3,150) in room 201; optionally register and check in.</summary>
    private static async Task<(long ReservationId, CheckInResult? Stay)> BookAsync(HccServer s, EndToEndTests.Staff staff, string guest, string phone, bool checkIn)
    {
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-25", 1, 0, room), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, phone), room), Ct);
        if (!checkIn) return (booking.ReservationId, null);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto(guest, phone, Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1301")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        return (booking.ReservationId, stay);
    }

    [Fact]
    public async Task F8_FIX_M2_extra_onto_the_company_folio_over_the_limit_is_refused_and_the_same_onto_A_passes()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (reservationId, stay) = await BookAsync(s, staff, "Split D7 Guest (fictional)", "9800000211", checkIn: true);
        var folioA = stay!.FolioId;

        // Limit ₹10,000; the company folio B takes the room: estimate ₹3,150 fits.
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", "27AAPFU0939F1ZV", City: "Latur", CreditLimitPaise: 1_000_000), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(reservationId, "company", company.CompanyId, null, null, null, ["room"], "Company pays the room"), Ct);
        var folioB = opened.FolioId;

        // Variant (c): ₹6,000 laundry at 18 % (₹7,080) onto B: ₹3,150 + ₹7,080 = ₹10,230 > ₹10,000 → refused with the D7 text, nothing posted.
        var refused = await Wire.CommandFailsAsync(staff.Reception, "PostCharge", new PostChargeRequest(folioB, "Laundry", 600000, "laundry", ExplicitFolio: true),
            HttpStatusCode.Conflict, "INVALID_TRANSITION", Ct);
        Assert.Contains("Credit limit exceeded for Deccan Agro Ltd (fictional)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("split folio", refused.Message, StringComparison.Ordinal);
        Assert.Equal("CREDIT_LIMIT", refused.Details!["reason"].GetString());
        Assert.Equal(1_023_000L, refused.Details["neededPaise"].GetInt64());
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folioB}", Ct));

        // The same line onto the guest folio A passes (SFQ-7: the desk may post it to the guest's bill), and so does a line routed by category (extras → A).
        await Wire.CommandAsync<ChargeResult>(staff.Reception, "PostCharge", new PostChargeRequest(folioA, "Laundry", 600000, "laundry", ExplicitFolio: true), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Reception, "PostCharge", new PostChargeRequest(folioA, "Laundry 2", 600000, "laundry"), Ct);
        Assert.Equal(2, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folioA}", Ct));

        // A smaller extra onto B that fits (₹3,150 + ₹590 ≤ ₹10,000) passes; the night itself is never refused (it posts to its routed folio B).
        await Wire.CommandAsync<ChargeResult>(staff.Reception, "PostCharge", new PostChargeRequest(folioB, "Laundry", 50000, "laundry", ExplicitFolio: true), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, new DateOnly(2026, 10, 24)), Ct);
        Assert.Equal(2, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folioB}", Ct));
    }

    [Fact]
    public async Task F8_FIX_cancel_guard_sees_an_advance_paid_on_folio_B_before_arrival()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (reservationId, _) = await BookAsync(s, staff, "Split Cancel Guest (fictional)", "9800000221", checkIn: false);

        // A confirmed booking split before arrival: a friend pays folio B, and pays a ₹1,000 UPI advance on it.
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(reservationId, "guest", null, null, "Friend Payer (fictional)", null, ["extras"], "Friend pays the extras"), Ct);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(opened.FolioId, "payment", "UPI", 100000, "UPI-ADV-B", null), Ct);
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt r JOIN bill.folio f ON f.folio_id = r.folio_id WHERE f.reservation_id = {reservationId} AND f.folio_seq = 1", Ct));

        // Before the fix the guard read folio A only (no money) and let the booking be cancelled with ₹1,000 held on B.
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{reservationId}", Ct);
        var refused = await Wire.CommandFailsAsync(staff.Reception, "CancelReservation", new CancelReservationRequest(reservationId, details.Version, "Guest cancelled"),
            HttpStatusCode.Conflict, "INVALID_TRANSITION", Ct);
        Assert.Contains("Refund the advance", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Confirmed", await s.ScalarAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {reservationId}", Ct));
    }

    [Fact]
    public async Task F8_FIX_guest_credit_on_an_udhaar_folio_settles_on_the_payers_receivable()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var (reservationId, stay) = await BookAsync(s, staff, "Split Udhaar Guest (fictional)", "9800000231", checkIn: true);
        var folioA = stay!.FolioId;

        // The Udhaar payer: a local customer known to the hotel with an active credit account (₹10,000).
        var payer = Convert.ToInt64(await s.ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, origin, created_utc) VALUES ({s.PropertyId}, N'Udhaar Payer (fictional)', '9800000331', 'manual', SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """, Ct), CultureInfo.InvariantCulture);
        await Wire.CommandAsync<object>(staff.Owner, "SetGuestCreditAccount", new { guestId = payer, limitPaise = 1_000_000L, active = true }, Ct);
        var key = (string)(await s.ScalarAsync($"SELECT customer_key FROM gst.guest_credit_account WHERE guest_id = {payer}", Ct))!;

        // Folio B (Udhaar) takes the extras; folio C (another guest payer) is opened empty to prove the receipt-folio rule.
        var b = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(reservationId, "udhaar", null, key, null, null, ["extras"], "Udhaar customer pays the extras"), Ct);
        var c = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio", new OpenFolioRequest(reservationId, "guest", null, null, "Other Person (fictional)", null, [], null), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Reception, "PostCharge", new PostChargeRequest(folioA, "Laundry", 50000, "laundry"), Ct); // routed: extras → B
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {b.FolioId}", Ct));

        // Guest credit on a plain guest folio C is refused (SF-CONTRACT §3: A or an Udhaar folio); on the Udhaar folio B it is requested for B's balance.
        var wrong = await Wire.CommandFailsAsync(staff.Reception, "RequestGuestCredit", new { stayId = stay.StayId, reason = "Wrong folio", folioId = c.FolioId },
            HttpStatusCode.UnprocessableEntity, "VALIDATION", Ct);
        Assert.Equal("SF_PAYER_KIND", wrong.Details!["reason"].GetString());
        var req = await Wire.CommandAsync<GuestCreditReply>(staff.Reception, "RequestGuestCredit", new { stayId = stay.StayId, reason = "Regular, pays monthly", folioId = b.FolioId }, Ct);
        Assert.Equal(59000L, req.AmountPaise); // ₹500 + 18 %
        Assert.Equal(b.FolioId, Convert.ToInt64(await s.ScalarAsync($"SELECT folio_id FROM bill.approval WHERE approval_id = {req.ApprovalId}", Ct), CultureInfo.InvariantCulture));
        await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId, true, "Known customer", req.ApprovalVersion), Ct);

        // Night on A, day closed, A paid; checkout settles B by the guest credit: receipt on B, receivable on the Udhaar payer (never the stay guest).
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, new DateOnly(2026, 10, 24)), Ct);
        await EndToEndTests.CloseDayAsync(staff.Owner, new DateOnly(2026, 10, 24));
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioA, "payment", "UPI", 315000, "UPI-UD-A", null), Ct);
        await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stay.StayId, stay.StayVersion, CreditApprovalIds: [req.ApprovalId]), Ct);

        var receipt = Convert.ToInt64(await s.ScalarAsync($"SELECT receipt_id FROM pay.receipt WHERE folio_id = {b.FolioId} AND kind = 'guest-credit' AND amount_paise = 59000", Ct), CultureInfo.InvariantCulture);
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM ops.udhaar_receivable WHERE receipt_id = {receipt} AND guest_id = {payer} AND folio_id = {b.FolioId} AND amount_paise = 59000", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {folioA} AND kind = 'guest-credit'", Ct));
    }
}
