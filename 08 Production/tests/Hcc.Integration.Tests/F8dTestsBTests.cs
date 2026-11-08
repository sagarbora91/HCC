using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcc.Application.Closing;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8D-TESTS-B] Server tests owed from F8-TESTS2 "Not written" (test files only, no product change): the per-folio DocumentSql print, the night-audit
/// endpoint with its key-card and backup signals, the no-show-due endpoint, and the folio-join audit on the movement register, the desk folio list and
/// the MEAS daily reports (a split stay is listed and counted once, SF-CONTRACT R9). Fictional data; company GSTIN 27AAPFU0939F1ZV (valid check digit).
/// Owner-question defaults used: SFQ-6 (night posting is never refused), QWQ-1 (night audit is show only), QWQ-7 (no-show-due is read only).
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8dTestsBTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string CompanyGstin = "27AAPFU0939F1ZV";

    private static string SeedFor(string businessDate, string rebuildFrom, string rebuildTo) => $"""
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '{businessDate}');
        DECLARE @p INT = SCOPE_IDENTITY();
        UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = @p;
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 300000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '201', @t), (@p, '202', @t), (@p, '203', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '{rebuildFrom}', '{rebuildTo}';
        SELECT @p;
        """;

    private static readonly string Seed = SeedFor("2026-10-24", "2026-10-01", "2027-01-01");

    private sealed record Booked(long ReservationId, string PublicNo, string Version, int RoomId);

    private static async Task<int> RoomAsync(HccServer s, string no) => await s.CountAsync($"SELECT room_id FROM inv.room WHERE room_no = '{no}'", Ct);

    private static async Task<Booked> BookAsync(HccServer s, EndToEndTests.Staff staff, string room, string arrival, string departure, string guest, string phone)
    {
        var roomId = await RoomAsync(s, room);
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest(arrival, departure, 1, 0, roomId), Ct);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, phone), roomId), Ct);
        return new Booked(booking.ReservationId, booking.PublicNo, booking.Version, roomId);
    }

    private sealed record InHouse(Booked B, long StayId, string StayVersion, long FolioA, long FolioB, long CompanyId);

    /// <summary>Book 3 nights of room 201, register, check in, open company folio B routed for the room (limit ₹50,000).</summary>
    private static async Task<InHouse> SplitInHouseAsync(HccServer s, EndToEndTests.Staff staff, string guest = "F8D Split Guest (fictional)", string phone = "9800000301", bool split = true)
    {
        var b = await BookAsync(s, staff, "201", "2026-10-24", "2026-10-27", guest, phone);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{b.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(b.ReservationId, details.Version,
            new GuestDraftDto(guest, phone, Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1301")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{b.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(b.ReservationId, details.Version), Ct);
        if (!split) return new InHouse(b, stay.StayId, stay.StayVersion, stay.FolioId, 0, 0);
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", CompanyGstin, City: "Latur", CreditLimitPaise: 5_000_000), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(b.ReservationId, "company", company.CompanyId, null, null, null, ["room"], "Company pays the room"), Ct);
        return new InHouse(b, stay.StayId, stay.StayVersion, stay.FolioId, opened.FolioId, company.CompanyId);
    }

    private static async Task PostNightAndCloseAsync(EndToEndTests.Staff staff, InHouse sp, DateOnly d)
    {
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(sp.StayId, d), Ct);
        await EndToEndTests.CloseDayAsync(staff.Owner, d);
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetTextAsync(HttpClient c, string path)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient c, string path)
    {
        var (st, body) = await GetTextAsync(c, path);
        Assert.True(st == HttpStatusCode.OK, $"GET {path}: {(int)st} {body}");
        return JsonSerializer.Deserialize<T>(body, HccServer.Json)!;
    }

    private static int Occurrences(string text, string needle) => System.Text.RegularExpressions.Regex.Count(text, System.Text.RegularExpressions.Regex.Escape(needle));

    // ── per-folio DocumentSql print ──────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task F8D_B_per_folio_print_each_folio_prints_only_its_own_lines_and_totals()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff);
        await PostNightAndCloseAsync(staff, sp, new DateOnly(2026, 10, 24));
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(sp.FolioA, "Laundry", 50000, "laundry"), Ct);

        var (stA, a) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioA}?format=html");
        var (stB, b) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioB}?format=html");
        Assert.Equal(HttpStatusCode.OK, stA);
        Assert.Equal(HttpStatusCode.OK, stB);
        // The room night is routed to the company folio B, the laundry sits on the guest folio A: no line is printed on both.
        Assert.Contains("Laundry", a);
        Assert.DoesNotContain("Room night", a);
        Assert.Contains("Room night", b);
        Assert.DoesNotContain("Laundry", b);
        Assert.Equal(1, Occurrences(b, "Room night"));
        // Same header facts (booking, room, dates) on both prints; the booking number is the stay's.
        Assert.Contains(sp.B.PublicNo, a);
        Assert.Contains(sp.B.PublicNo, b);
        // Totals: A = ₹500 + 18 % = ₹590, B = ₹3,000 + 5 % = ₹3,150. Neither print carries the other's total.
        Assert.Contains("590", a);
        Assert.DoesNotContain("3,150", a);
        Assert.Contains("3,150", b);
        Assert.DoesNotContain("590", b);
        // The export log records both prints (rule V.4).
        Assert.Equal(2, await s.CountAsync("SELECT COUNT(*) FROM rpt.export_log WHERE report_id = 'doc.folio'", Ct));
    }

    [Fact]
    public async Task F8D_B_per_folio_print_of_a_foreign_or_unknown_folio_is_refused()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff);
        var (st, _) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioB + 9999}?format=html");
        Assert.True((int)st is >= 400 and < 500, "unknown folio is a client error: " + st);
        // Housekeeping holds no folio right (DEC-061): neither folio prints.
        foreach (var f in new[] { sp.FolioA, sp.FolioB })
            Assert.Equal(HttpStatusCode.Forbidden, (await GetTextAsync(staff.Housekeeper, $"/documents/folio/{f}?format=html")).Status);
    }

    /// <summary>
    /// The print of the company folio B names the payer (Bill to = Deccan Agro Ltd, Buyer GSTIN), not the stay's guest; the guest folio A keeps the guest
    /// and no GSTIN. Bug B1 (F8D-TESTS-B-DONE), fixed in lane f8d-b1: <c>DocumentSql.FolioHeaderAsync</c> reads the folio payer and its issued invoice.
    /// </summary>
    [Fact]
    public async Task F8D_B_per_folio_print_company_folio_names_the_company_as_payer()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff);
        await PostNightAndCloseAsync(staff, sp, new DateOnly(2026, 10, 24));
        var (_, b) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioB}?format=html");
        Assert.Contains("Deccan Agro Ltd", b);
        Assert.Contains(CompanyGstin, b);
        Assert.Contains("Not yet issued", b);
        var (_, a) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioA}?format=html");
        Assert.Contains("F8D Split Guest", a);
        Assert.DoesNotContain("Deccan Agro Ltd", a);
        Assert.DoesNotContain(CompanyGstin, a);
    }

    /// <summary>[F8D-B1] After check-out each folio prints its own issued tax invoice number (one invoice per non-zero folio, letter order), not "Not yet issued".</summary>
    [Fact]
    public async Task F8D_B1_per_folio_print_shows_each_folio_issued_invoice_number()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(sp.FolioA, "Laundry", 50000, "laundry"), Ct);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1)) await PostNightAndCloseAsync(staff, sp, d);

        // Guest pays A (₹590) in cash on Reception's shift; Deccan Agro takes B (₹9,450) on company credit approved by the Owner.
        var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
        await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(sp.FolioA, "payment", "Cash", 59000, null, till.TillId), Ct);
        var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{sp.FolioB}", Ct);
        var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", sp.FolioB, null, 945000, "Bill to Deccan Agro (fictional)", fb.Version), Ct);
        await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId!.Value, true, "Company account", req.ApprovalVersion!), Ct);
        var checkout = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(sp.StayId, sp.StayVersion, req.ApprovalId.Value), Ct);
        Assert.NotNull(checkout.InvoiceNos);
        Assert.Equal(2, checkout.InvoiceNos.Count);

        string NoOf(long f) => (string)s.ScalarAsync($"SELECT invoice_no FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct).GetAwaiter().GetResult()!;
        var (noA, noB) = (NoOf(sp.FolioA), NoOf(sp.FolioB));
        Assert.Equal(new[] { noA, noB }, checkout.InvoiceNos);

        var (_, a) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioA}?format=html");
        var (_, b) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioB}?format=html");
        Assert.Contains(noA, a);
        Assert.DoesNotContain(noB, a);
        Assert.DoesNotContain("Not yet issued", a);
        Assert.DoesNotContain(CompanyGstin, a);
        Assert.Contains(noB, b);
        Assert.DoesNotContain(noA, b);
        Assert.DoesNotContain("Not yet issued", b);
        Assert.Contains("Deccan Agro Ltd", b);
        Assert.Contains(CompanyGstin, b);
    }

    /// <summary>[F8D-B1] A single-folio stay prints as before: Bill to = the guest, no buyer GSTIN, "Not yet issued" until an invoice exists.</summary>
    [Fact]
    public async Task F8D_B1_single_folio_print_is_unchanged()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff, "F8D Single Guest (fictional)", "9800000302", split: false);
        await PostNightAndCloseAsync(staff, sp, new DateOnly(2026, 10, 24));
        var (st, a) = await GetTextAsync(staff.Owner, $"/documents/folio/{sp.FolioA}?format=html");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.True(Occurrences(a, "F8D Single Guest (fictional)") >= 2, "Guest and Bill to both name the guest"); // Guest + Bill to
        Assert.Contains("Not yet issued", a);
        Assert.DoesNotContain(CompanyGstin, a);
        Assert.Contains("Room night", a);
    }

    // ── night-audit endpoint and its key-card / backup signals ───────────────────────────────────────────────────
    private static async Task<NightAuditResult> AuditAsync(HttpClient c) => await GetJsonAsync<NightAuditResult>(c, "/closing/night-audit");

    [Fact]
    public async Task F8D_B_night_audit_lists_the_eight_lines_in_order_show_only_and_follows_the_nights()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff, split: false);

        var audit = await AuditAsync(staff.Owner);
        Assert.Equal(new DateOnly(2026, 10, 24), audit.BusinessDate);
        Assert.False(audit.Closed);
        Assert.Equal(["na.arrivals", "na.departures", "na.nights", "na.approvals", "na.pos", "na.cash", "na.keys", "na.backup"], audit.Lines.Select(l => l.Key));
        // Blocking only where the close preflight already blocks (QWQ-1): keys and backup never block.
        Assert.Equal([true, true, true, true, true, true, false, false], audit.Lines.Select(l => l.Blocking));
        Assert.All(audit.Lines, l => Assert.Equal("operational", l.Group));
        // Checked in tonight but the room night is not posted yet.
        var nights = audit.Lines.Single(l => l.Key == "na.nights");
        Assert.False(nights.Ok);
        Assert.Contains("1 stay", nights.Detail);
        Assert.True(audit.Lines.Single(l => l.Key == "na.arrivals").Ok);

        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(sp.StayId, new DateOnly(2026, 10, 24)), Ct);
        audit = await AuditAsync(staff.Owner);
        Assert.True(audit.Lines.Single(l => l.Key == "na.nights").Ok);
        // Show only: reading the audit changed nothing, the day is still open and the close still runs.
        Assert.False(audit.Closed);
        await EndToEndTests.CloseDayAsync(staff.Owner, new DateOnly(2026, 10, 24));
        Assert.True((await AuditAsync(staff.Owner)).BusinessDate == new DateOnly(2026, 10, 25));
    }

    [Fact]
    public async Task F8D_B_night_audit_is_open_to_close_viewers_and_refused_to_reception_and_housekeeping()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        Assert.Equal(HttpStatusCode.OK, (await GetTextAsync(staff.Owner, "/closing/night-audit")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetTextAsync(staff.Owner, "/closing/night-audit?date=2026-10-24")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetTextAsync(staff.Reception, "/closing/night-audit")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetTextAsync(staff.Housekeeper, "/closing/night-audit")).Status);
        using var anon = s.CookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetTextAsync(anon, "/closing/night-audit")).Status);
    }

    [Fact]
    public async Task F8D_B_night_audit_key_card_signal_counts_rooms_of_departed_stays_with_an_active_or_manual_card()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff, split: false);
        Assert.True((await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.keys").Ok);

        // The guest leaves today (the stay row is moved to CheckedOut on the business date; the checkout command itself is covered elsewhere).
        await s.ScalarAsync($"UPDATE gst.stay SET status = 'CheckedOut', checked_out_date = '2026-10-24', checked_out_utc = SYSUTCDATETIME(), checked_out_by = 1 WHERE stay_id = {sp.StayId}; SELECT 1;", Ct);
        string Card(string status) => $"""
            INSERT ops.key_card(property_id, event_type, card_no, reservation_id, room_id, begin_at, end_at, issued_by, issued_utc, status)
            VALUES ({s.PropertyId}, 'issued', 'C-{status}', {sp.B.ReservationId}, {sp.B.RoomId}, '2026-10-24 12:00', '2026-10-27 11:00', 1, SYSUTCDATETIME(), '{status}'); SELECT 1;
            """;
        // Superseded, checked-out and lost cards are history: not counted.
        foreach (var done in new[] { "superseded", "checked_out", "lost" }) await s.ScalarAsync(Card(done), Ct);
        var keys = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.keys");
        Assert.True(keys.Ok, keys.Detail);

        await s.ScalarAsync(Card("active"), Ct);
        await s.ScalarAsync(Card("manual"), Ct);   // a second live card on the same room is still one room
        keys = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.keys");
        Assert.False(keys.Ok);
        Assert.False(keys.Blocking);
        Assert.Equal("1 room still with an active card", keys.Detail);
    }

    [Fact]
    public async Task F8D_B_night_audit_backup_signal_reads_the_last_good_backup_in_hotel_time_and_ignores_failed_runs_and_restore_drills()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var backup = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.backup");
        Assert.False(backup.Ok);
        Assert.Equal("No backup recorded", backup.Detail);

        string Run(string kind, int ok, string utc) =>
            $"INSERT plat.backup_run(kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason, host_name) VALUES ('{kind}', {ok}, '{utc}', '{utc}', N'x.bak', 1000, NULL, N'TEST'); SELECT 1;";
        // A failed Full today and a successful restore drill today do not count.
        await s.ScalarAsync(Run("Full", 0, "2026-10-24 06:30:00"), Ct);
        await s.ScalarAsync(Run("RestoreDrill", 1, "2026-10-24 07:00:00"), Ct);
        backup = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.backup");
        Assert.False(backup.Ok);
        Assert.Equal("No backup recorded", backup.Detail);

        // Yesterday's good backup (10-23 23:30 UTC = 10-24 05:00 IST) is today in hotel time; 10-23 12:00 UTC is 17:30 IST on the 23rd and is not.
        await s.ScalarAsync(Run("Full", 1, "2026-10-23 12:00:00"), Ct);
        backup = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.backup");
        Assert.False(backup.Ok);
        Assert.Equal("Last good backup 2026-10-23 17:30", backup.Detail);
        Assert.False(backup.Blocking);

        await s.ScalarAsync(Run("Differential", 1, "2026-10-23 23:30:00"), Ct);
        backup = (await AuditAsync(staff.Owner)).Lines.Single(l => l.Key == "na.backup");
        Assert.True(backup.Ok, backup.Detail);
        Assert.Equal("Backup at 05:00", backup.Detail);
    }

    // ── no-show-due endpoint ─────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task F8D_B_no_show_due_lists_unarrived_confirmed_arrivals_of_the_business_date_read_only()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);   // business date 2026-10-24: the wall clock is later, so the 23:00 cut-off has passed
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var waiting = await BookAsync(s, staff, "202", "2026-10-24", "2026-10-26", "Waiting Guest (fictional)", "9800000311");
        var tomorrow = await BookAsync(s, staff, "203", "2026-10-25", "2026-10-26", "Tomorrow Guest (fictional)", "9800000312");
        var arrived = await SplitInHouseAsync(s, staff, "Arrived Guest (fictional)", "9800000313", split: false);   // room 201, checked in

        var due = await GetJsonAsync<NoShowDueView>(staff.Reception, "/reservations/no-show-due");
        Assert.Equal("2026-10-24", due.BusinessDate);
        Assert.Equal("23:00", due.Cutoff);
        Assert.True(due.PastCutoff);
        Assert.Equal([waiting.PublicNo], due.Items.Select(i => i.PublicNo));
        var item = due.Items.Single();
        Assert.Equal(("Waiting Guest (fictional)", "2026-10-24", waiting.ReservationId), (item.GuestName, item.Arrival, item.ReservationId));
        Assert.DoesNotContain(due.Items, i => i.PublicNo == tomorrow.PublicNo || i.PublicNo == arrived.B.PublicNo);
        Assert.EndsWith("— not posted", item.ChargeText);   // DEC-MA-5b: shown, never posted

        // Read only (QWQ-7): nothing was marked, nothing was posted.
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM inv.reservation WHERE public_no = '{waiting.PublicNo}' AND status <> 'Confirmed'", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id WHERE f.reservation_id = {waiting.ReservationId}", Ct));

        // Marking goes through the existing command; the row then leaves the list.
        await Wire.CommandAsync<ReservationResult>(staff.Owner, "MarkNoShow", new CancelReservationRequest(waiting.ReservationId, item.Version, "Did not arrive (test)"), Ct);
        Assert.Empty((await GetJsonAsync<NoShowDueView>(staff.Reception, "/reservations/no-show-due")).Items);
    }

    [Fact]
    public async Task F8D_B_no_show_due_is_empty_before_the_cutoff_and_closed_to_housekeeping_and_anonymous_callers()
    {
        // A business date far in the future: the wall clock is before the cut-off of that day, so nothing is due even with a confirmed arrival.
        await using var s = await HccServer.StartAsync(SeedFor("2099-01-01", "2098-12-01", "2099-03-01"), Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        await BookAsync(s, staff, "202", "2099-01-01", "2099-01-02", "Future Guest (fictional)", "9800000321");
        var due = await GetJsonAsync<NoShowDueView>(staff.Reception, "/reservations/no-show-due");
        Assert.Equal("2099-01-01", due.BusinessDate);
        Assert.False(due.PastCutoff);
        Assert.Empty(due.Items);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetTextAsync(staff.Housekeeper, "/reservations/no-show-due")).Status);
        using var anon = s.CookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetTextAsync(anon, "/reservations/no-show-due")).Status);
    }

    // ── folio-join audit: a split stay is listed and counted once (R9) ───────────────────────────────────────────
    private static async Task<List<JsonNode>> RunReportAsync(HccClient c, string id, string query)
    {
        var (st, body) = await GetTextAsync(c.Client, $"/reports/{id}?{query}&all=true");
        Assert.True(st == HttpStatusCode.OK, $"{id}: {(int)st} {body}");
        return JsonNode.Parse(body)!["rows"]!.AsArray().Where(r => r is not null).Select(r => r!).Where(r => r["kind"] is null || r["kind"]!.ToString() is "Row" or "row" || r["kind"]!.GetValueKind() == JsonValueKind.Number && r["kind"]!.GetValue<int>() == 0).ToList();
    }

    private sealed record HccClient(HttpClient Client);

    [Fact]
    public async Task F8D_B_folio_join_audit_movement_register_desk_folios_and_meas_daily_count_a_split_stay_once()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var sp = await SplitInHouseAsync(s, staff);
        await PostNightAndCloseAsync(staff, sp, new DateOnly(2026, 10, 24));   // ₹3,000 + 5 % on company folio B; business date is now 10-25
        var owner = new HccClient(staff.Owner);

        // Movement register: one arrival row (24th) and one departure row (27th); the stay-level balance due is B's ₹3,150, not doubled and not ₹0.
        var rows = (await RunReportAsync(owner, "rpt.guests.movement-register", "from=2026-10-24&to=2026-10-27")).Where(r => r["cells"]?["booking"]?.ToString() == sp.B.PublicNo).ToList();
        Assert.Equal(["2026-10-24", "2026-10-27"], rows.Select(r => r["cells"]!["date"]!.ToString()[..10]).Order(StringComparer.Ordinal));
        var balances = rows.Select(r => r["cells"]!["balanceDue"]).Where(v => v is not null).Select(v => v!.GetValue<long>()).ToList();
        Assert.NotEmpty(balances);
        Assert.All(balances, v => Assert.Equal(315000L, v));

        // Desk folio list: one row per reservation, balance of its primary folio.
        var desk = await GetJsonAsync<DeskFolioListView>(staff.Owner, "/cashier/folios");
        Assert.Single(desk.Items, i => i.PublicNo == sp.B.PublicNo);

        // MEAS daily: high-balance lists the stay once at its stay-level balance; the manager's flash counts it once on the arrival day and charges ₹3,000 once.
        var high = (await RunReportAsync(owner, "rpt.money.high-balance", "threshold=1")).Where(r => r["cells"]?["guest"]?.ToString().Contains("F8D Split Guest") == true).ToList();
        Assert.Single(high);
        Assert.Equal(315000L, high[0]["cells"]!["balance"]!.GetValue<long>());
        var flash = await RunReportAsync(owner, "rpt.guests.manager-flash", "from=2026-10-24");
        long Count(string kpi) => flash.Single(r => r["cells"]!["kpi"]!.ToString() == kpi)["cells"]!["cntActual"]!.GetValue<long>();
        Assert.Equal(1L, Count("Arrivals"));
        Assert.Equal(1L, Count("In-house"));
        Assert.Equal(300000L, flash.Single(r => r["cells"]!["kpi"]!.ToString() == "ADR")["cells"]!["amtActual"]!.GetValue<long>());
    }

}
