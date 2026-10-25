using System.Net;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// Wave 1 end to end over HTTP with the real composition (no fakes): DEC-062 sign-in (Owner password in a cookie session;
/// Reception and Housekeeper PIN on a registered device, bearer token) → book (P2) → register guest → check in (P3) → post
/// room nights → cash receipt on Reception's own shift (P4) → checkout → StayDeparted → housekeeping task (projector worker)
/// → assigned clean → inspect → Ready. Prototype room 105 figures: 2 nights × ₹2,000 + 10 % tax = ₹4,400 incl. ₹400 tax.
/// Business dates are advanced with SQL (day close is P6).
/// </summary>
[Trait("Category", "Sql")]
public sealed class EndToEndTests
{
    private const string OwnerLogin = "owner.sagar";
    private const string OwnerPassword = "Maroon&Sand#Ledger26";
    private const string ReceptionPin = "472913";
    private const string HousekeeperPin = "583104";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p INT = SCOPE_IDENTITY();
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 200000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '105', @t), (@p, '106', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '2026-10-01', '2027-01-01';
        SELECT @p;
        """;

    private sealed record Staff(HttpClient Owner, HttpClient Reception, HttpClient Housekeeper, int OwnerId, int ReceptionId, int HousekeeperId, string DeviceToken) : IDisposable
    {
        public void Dispose()
        {
            Owner.Dispose();
            Reception.Dispose();
            Housekeeper.Dispose();
        }
    }

    /// <summary>First Owner on the server PC, then the Owner creates Reception + Housekeeper (PIN) and registers the shared tablet.</summary>
    private static async Task<Staff> SignInStaffAsync(HccServer s)
    {
        var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Sagar (Owner)", OwnerPassword), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);

        // Owner: password only (DEC-062); a PIN is refused for the Owner role.
        using (var pin = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Pin: "472913"), null, Ct))
            Assert.Equal(HttpStatusCode.Forbidden, pin.StatusCode);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: OwnerPassword), null, Ct))
        {
            var r = await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
            Assert.Equal("PASSWORD", r.CredentialKind);
            Assert.Null(r.SessionToken); // browser: HttpOnly cookie only
        }

        var ownerId = (await Wire.GetAsync<CapabilitiesResult>(owner, "/me/capabilities", Ct)).UserId;
        async Task<int> CreatePinUserAsync(string login, string name, string role, string pin)
        {
            var u = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest(login, name, [new RoleAssignmentInput(role)]), Ct);
            await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(u.UserId, null, pin, u.Version), Ct);
            return u.UserId;
        }

        var receptionId = await CreatePinUserAsync("rec.asha", "Asha (Reception)", "REC", ReceptionPin);
        var housekeeperId = await CreatePinUserAsync("hkp.ravi", "Ravi (Housekeeper)", "HKP", HousekeeperPin);
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Front desk tablet"), Ct);
        Assert.False(string.IsNullOrEmpty(device.DeviceToken));

        // PIN sign-in only on a registered device.
        using (var noDevice = new HttpClient { BaseAddress = s.BaseAddress })
        using (var refused = await Wire.PostAsync(noDevice, "/auth/sign-in", new SignInRequest("rec.asha", Pin: ReceptionPin, ReturnToken: true), null, Ct))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        async Task<HttpClient> PinClientAsync(string login, string pin)
        {
            var c = s.DeviceClient(device.DeviceToken!);
            using var r = await Wire.PostAsync(c, "/auth/sign-in", new SignInRequest(login, Pin: pin, ReturnToken: true), null, Ct);
            var result = await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct);
            Assert.Equal("PIN", result.CredentialKind);
            HccServer.UseBearer(c, result.SessionToken!);
            return c;
        }

        return new Staff(owner, await PinClientAsync("rec.asha", ReceptionPin), await PinClientAsync("hkp.ravi", HousekeeperPin), ownerId, receptionId, housekeeperId, device.DeviceToken!);
    }

    private static async Task<(long ReservationId, long StayId, long FolioId, string StayVersion)> BookAndCheckInAsync(HttpClient reception, int roomId, string arrival, string departure, string guest, long expectedTotal)
    {
        var avail = await Wire.GetAsync<AvailabilityResult>(reception, $"/availability?arrival={arrival}&departure={departure}&adults=2", Ct);
        Assert.True(avail.Rooms.Single(r => r.RoomId == roomId).Available);
        var quote = await Wire.CommandAsync<QuoteResult>(reception, "QuoteStay", new QuoteStayRequest(arrival, departure, 2, 0, roomId), Ct);
        Assert.Equal(expectedTotal, quote.TotalPaise);
        var booking = await Wire.CommandAsync<ReservationResult>(reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, "9800000105"), roomId), Ct);
        Assert.Equal("Confirmed", booking.Status);

        var details = await Wire.GetAsync<ReservationDetails>(reception, $"/reservations/{booking.ReservationId}", Ct);
        var registered = await Wire.CommandAsync<RegisterGuestResult>(reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto(guest, "9800000105", Address: "12 MG Road, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "4321")), Ct);
        Assert.True(registered.Verified);
        details = await Wire.GetAsync<ReservationDetails>(reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        return (booking.ReservationId, stay.StayId, stay.FolioId, stay.StayVersion);
    }

    [Fact]
    public async Task Sign_in_book_check_in_post_nights_pay_check_out_clean_and_inspect_room_105()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await SignInStaffAsync(s);
        var room105 = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '105'", Ct);

        // Capabilities come from the DEC-063 seed: Reception records payments on its own shift only.
        var caps = await Wire.GetAsync<CapabilitiesResult>(staff.Reception, "/me/capabilities", Ct);
        Assert.Equal(["REC"], caps.Roles);
        Assert.Equal("S", caps.Permissions.Single(p => p.PermissionId == "PERM-PAY-001").Scope);

        var (reservationId, stayId, folioId, stayVersion) = await BookAndCheckInAsync(staff.Reception, room105, "2026-10-24", "2026-10-26", "Meera Iyer (fictional)", 440000);

        // Night audit posting is Owner/Manager only (PERM-CLS-002).
        await Wire.CommandFailsAsync(staff.Reception, "PostRoomNight", new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 24)), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        var n1 = await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 24)), Ct);
        Assert.Equal((200000L, 1000, 20000L), (n1.AmountPaise, n1.TaxBps, n1.TaxPaise));
        await s.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-25' WHERE property_id = {s.PropertyId};", Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 25)), Ct);
        await s.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-26' WHERE property_id = {s.PropertyId};", Ct);

        // Shared front-desk till (DEC-052): Owner creates it, Reception opens its own shift and takes the cash.
        var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
        var shift = await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
        var receipt = await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioId, "payment", "Cash", 440000, null, till.TillId), Ct);
        Assert.Equal(shift.ShiftId, receipt.ShiftId);

        var folio = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{folioId}", Ct);
        Assert.Equal(new FolioTotalsView(400000, 40000, 440000, 440000, 0, 0, 0), folio.Totals);
        Assert.Equal(["Room night · 2026-10-24", "Room night · 2026-10-25"], folio.Lines.Select(l => l.Label));

        var checkout = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stayId, stayVersion), Ct);
        Assert.Equal(("105", "Dirty", "2026-10-26"), (checkout.RoomNo, checkout.RoomHk, checkout.BusinessDate));
        Assert.Equal("Departed", (await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{reservationId}", Ct)).Status);

        // StayDeparted → the hosted projector creates the checkout clean.
        HousekeepingTaskView? task = null;
        for (var i = 0; i < 50 && task is null; i++)
        {
            task = (await Wire.GetAsync<HousekeepingTaskPage>(staff.Owner, "/housekeeping/tasks", Ct)).Rows.SingleOrDefault(t => t.RoomId == room105 && t.Kind == "checkout");
            if (task is null) await Task.Delay(200, Ct);
        }

        Assert.NotNull(task);
        Assert.Equal("ToDo", task.Status);

        // Assigned scope: the housekeeper sees and works only tasks assigned to them; inspection is a supervisor/owner step.
        var mine = await Wire.GetAsync<HousekeepingTaskPage>(staff.Housekeeper, "/housekeeping/tasks", Ct);
        Assert.True(mine.AssignedOnly);
        Assert.Empty(mine.Rows);
        await Wire.CommandFailsAsync(staff.Housekeeper, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(task.TaskId, task.Version, "start"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        var t = (await Wire.CommandAsync<TransitionHousekeepingTaskResult>(staff.Owner, "TransitionHousekeepingTask",
            new TransitionHousekeepingTaskRequest(task.TaskId, task.Version, "assign", staff.HousekeeperId), Ct)).Task;
        var step = await Wire.CommandAsync<TransitionHousekeepingTaskResult>(staff.Housekeeper, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(t.TaskId, t.Version, "start"), Ct);
        Assert.Equal("Cleaning", step.RoomHk);
        for (var i = 0; i < 3; i++)
            step = await Wire.CommandAsync<TransitionHousekeepingTaskResult>(staff.Housekeeper, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(t.TaskId, step.Task.Version, "check", Index: i), Ct);
        step = await Wire.CommandAsync<TransitionHousekeepingTaskResult>(staff.Housekeeper, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(t.TaskId, step.Task.Version, "complete"), Ct);
        Assert.Equal("Inspection", step.RoomHk);
        await Wire.CommandFailsAsync(staff.Housekeeper, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(t.TaskId, step.Task.Version, "inspect"), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        step = await Wire.CommandAsync<TransitionHousekeepingTaskResult>(staff.Owner, "TransitionHousekeepingTask", new TransitionHousekeepingTaskRequest(t.TaskId, step.Task.Version, "inspect"), Ct);
        Assert.Equal(("Ready", "Done"), (step.RoomHk, step.Task.Status));

        var board = await Wire.GetAsync<RoomBoardPage>(staff.Owner, "/rooms/board", Ct);
        Assert.Equal("Ready", board.Rows.Single(r => r.RoomNo == "105").Status);
        using (var hkBoard = await staff.Housekeeper.GetAsync(ApiConventions.BasePath + "/rooms/board", Ct))
            Assert.DoesNotContain("paise", await hkBoard.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        // The actor of every write is the session principal (never the body), and the cash landed once in the drawer.
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CHECKOUT' AND principal_id = {staff.ReceptionId}", Ct));
        Assert.Equal(2, await s.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ROOM_NIGHT_POSTED' AND principal_id = {staff.OwnerId}", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM pay.cash_movement WHERE shift_id = {shift.ShiftId} AND amount_paise = 440000", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {stayId}", Ct));

        // Sign-out ends the session: the next request is 401 UNAUTHENTICATED.
        using (var signOut = await Wire.PostAsync(staff.Reception, "/auth/sign-out", new { }, null, Ct))
            Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        using var after = await staff.Reception.GetAsync(ApiConventions.BasePath + "/me/capabilities", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal("UNAUTHENTICATED", JsonSerializer.Deserialize<ErrorResponse>(await after.Content.ReadAsStringAsync(Ct), HccServer.Json)!.Code);
    }

    [Fact]
    public async Task Company_credit_is_executed_inside_checkout_and_rolls_back_with_it()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await SignInStaffAsync(s);
        var room106 = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '106'", Ct);
        var (_, stayId, folioId, stayVersion) = await BookAndCheckInAsync(staff.Reception, room106, "2026-10-24", "2026-10-25", "Acme Travels guest (fictional)", 220000);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stayId, new DateOnly(2026, 10, 24)), Ct);
        await s.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-25' WHERE property_id = {s.PropertyId};", Ct);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioId, "deposit", "UPI", 50000, "UPI-DEP-106", null), Ct);

        async Task<long> ApprovedCreditAsync()
        {
            var f = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{folioId}", Ct);
            var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", folioId, null, 220000, "Bill to Acme Travels (fictional)", f.Version), Ct);
            Assert.Equal("PendingApproval", req.Outcome);
            var decided = await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId!.Value, true, "Corporate account", req.ApprovalVersion!), Ct);
            Assert.Equal("Approved", decided.Status);
            return req.ApprovalId.Value;
        }

        // 1) Credit approved but the deposit is still held: checkout fails and the credit execution rolls back with it.
        var first = await ApprovedCreditAsync();
        await Wire.CommandFailsAsync(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stayId, stayVersion, first), HttpStatusCode.Conflict, "INVALID_TRANSITION", Ct);
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {folioId} AND kind = 'company-credit'", Ct));
        Assert.Equal("Approved", (string?)await s.ScalarAsync($"SELECT status FROM bill.approval WHERE approval_id = {first}", Ct));

        // 2) Returning the deposit changes the folio, so the old approval no longer binds (DEC-041) …
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioId, "deposit-return", "UPI", 50000, "UPI-RET-106", null), Ct);
        await Wire.CommandFailsAsync(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stayId, stayVersion, first), HttpStatusCode.Conflict, "STALE_VERSION", Ct);

        // 3) … a fresh approval settles the folio and the stay departs in the same transaction.
        var second = await ApprovedCreditAsync();
        var checkout = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stayId, stayVersion, second), Ct);
        Assert.Equal("Dirty", checkout.RoomHk);
        var folio = await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{folioId}", Ct);
        Assert.Equal(("Departed", 220000L, 0L, 0L), (folio.ReservationStatus, folio.Totals.CompanyCreditPaise, folio.Totals.BalancePaise, folio.Totals.DepositPaise));
        Assert.Equal("Executed", (string?)await s.ScalarAsync($"SELECT status FROM bill.approval WHERE approval_id = {second}", Ct));
        Assert.Equal(staff.ReceptionId, await s.CountAsync($"SELECT executed_by FROM bill.approval WHERE approval_id = {second}", Ct));
    }
}
