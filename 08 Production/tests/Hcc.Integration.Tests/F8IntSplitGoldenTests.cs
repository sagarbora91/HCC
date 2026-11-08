using System.Globalization;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// [F8-INT] The split-stay golden of SF-CONTRACT §5.7 end to end over HTTP with the real composition: "Deccan Agro pays rooms, guest pays extras".
/// Room Deluxe ₹3,000 × 3 nights at 5 % on the company folio B (routing set by OpenFolio before the nights), laundry ₹500 at 18 % and room-service
/// food ₹1,200 on the guest folio A; ₹1,000 UPI advance and ₹850 cash on A; company credit ₹9,450 on B. CheckoutStay settles both folios and
/// issues two tax invoices in letter order in the same transaction (A B2C ₹1,850, B B2B ₹9,450); Σ = ₹11,300. Fictional data; GSTIN of the
/// company 27AAPFU0939F1ZV (valid check digit, contract deviation 8). Lane sf-tests owns the variants a–d and the DEC-061 walk.
/// </summary>
[Trait("Category", "Sql")]
public sealed class F8IntSplitGoldenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string CompanyGstin = "27AAPFU0939F1ZV";

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

    [Fact]
    public async Task F8_INT_golden_split_stay_two_folios_two_invoices_sum_11300()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var staff = await EndToEndTests.SignInStaffAsync(s);
        var room = await s.CountAsync("SELECT room_id FROM inv.room WHERE room_no = '201'", Ct);

        // Book 3 nights ₹3,000 (5 %: ₹9,450), register, check in.
        var quote = await Wire.CommandAsync<QuoteResult>(staff.Reception, "QuoteStay", new QuoteStayRequest("2026-10-24", "2026-10-27", 1, 0, room), Ct);
        Assert.Equal(945000, quote.TotalPaise);
        var booking = await Wire.CommandAsync<ReservationResult>(staff.Reception, "CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Golden Split Guest (fictional)", "9800000201"), room), Ct);
        var details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        await Wire.CommandAsync<RegisterGuestResult>(staff.Reception, "RegisterGuest", new RegisterGuestRequest(booking.ReservationId, details.Version,
            new GuestDraftDto("Golden Split Guest (fictional)", "9800000201", Address: "4 Ganj Golai, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "1201")), Ct);
        details = await Wire.GetAsync<ReservationDetails>(staff.Reception, $"/reservations/{booking.ReservationId}", Ct);
        var stay = await Wire.CommandAsync<CheckInResult>(staff.Reception, "CheckIn", new CheckInRequest(booking.ReservationId, details.Version), Ct);
        var folioA = stay.FolioId;

        // At check-in: the company folio B takes the room (D7: ₹9,450 estimate within the ₹50,000 limit), then the ₹1,000 UPI advance on A.
        var company = await Wire.CommandAsync<CompanyResult>(staff.Owner, "SaveCompany", new SaveCompanyRequest(null, null, "Deccan Agro Ltd (fictional)", CompanyGstin, City: "Latur", CreditLimitPaise: 5_000_000), Ct);
        var opened = await Wire.CommandAsync<FolioCommandResult>(staff.Owner, "OpenFolio",
            new OpenFolioRequest(booking.ReservationId, "company", company.CompanyId, null, null, null, ["room"], "Company pays the room"), Ct);
        Assert.Equal(("B", 2), (opened.Letter, opened.Seq));
        var folioB = opened.FolioId;
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioA, "payment", "UPI", 100000, "UPI-GOLD-1", null), Ct);

        // Extras on A (category extras: the primary folio), the three nights routed to B by the store; each day closed.
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(folioA, "Laundry", 50000, "laundry"), Ct);
        await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostCharge", new PostChargeRequest(folioA, "Room service food", 120000, "food"), Ct);
        for (var d = new DateOnly(2026, 10, 24); d < new DateOnly(2026, 10, 27); d = d.AddDays(1))
        {
            await Wire.CommandAsync<ChargeResult>(staff.Owner, "PostRoomNight", new PostRoomNightRequest(stay.StayId, d), Ct);
            await EndToEndTests.CloseDayAsync(staff.Owner, d);
        }

        Assert.Equal(3, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folioB} AND kind = 'room'", Ct));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folioA} AND kind = 'room'", Ct));
        var summary = await Wire.GetAsync<List<FolioSummaryDto>>(staff.Owner, $"/folios/by-reservation/{booking.ReservationId}/summary", Ct);
        var a = summary.Single(f => f.Letter == "A");
        var b = summary.Single(f => f.Letter == "B");
        Assert.Equal((170000L, 15000L, 185000L, 85000L), (a.NetPaise, a.TaxPaise, a.TotalPaise, a.BalancePaise));
        Assert.Equal((900000L, 45000L, 945000L, 945000L), (b.NetPaise, b.TaxPaise, b.TotalPaise, b.BalancePaise));

        // Departure day: ₹850 cash on A on Reception's own shift; company credit ₹9,450 on B (requested by Reception, approved by the Owner).
        var till = await Wire.CommandAsync<TillResult>(staff.Owner, "CreateTill", new CreateTillRequest("FD-1", "Front desk"), Ct);
        await Wire.CommandAsync<ShiftResult>(staff.Reception, "OpenShift", new OpenShiftRequest(till.TillId, 500000), Ct);
        await Wire.CommandAsync<ReceiptResult>(staff.Reception, "RecordReceipt", new RecordReceiptRequest(folioA, "payment", "Cash", 85000, null, till.TillId), Ct);
        var fb = await Wire.GetAsync<FolioView>(staff.Reception, $"/folios/{folioB}", Ct);
        var req = await Wire.CommandAsync<AdjustmentResult>(staff.Reception, "RequestAdjustment", new RequestAdjustmentRequest("credit", folioB, null, 945000, "Bill to Deccan Agro (fictional)", fb.Version), Ct);
        Assert.Equal("PendingApproval", req.Outcome);
        await Wire.CommandAsync<ApprovalResult>(staff.Owner, "DecideApproval", new DecideApprovalRequest(req.ApprovalId!.Value, true, "Company account", req.ApprovalVersion!), Ct);

        var checkout = await Wire.CommandAsync<CheckoutStayResult>(staff.Reception, "CheckoutStay", new CheckoutStayRequest(stay.StayId, stay.StayVersion, req.ApprovalId.Value), Ct);
        Assert.NotNull(checkout.InvoiceNos);
        Assert.Equal(2, checkout.InvoiceNos.Count);

        // Two invoices = two GST documents, consecutive in letter order, buyers from the folio payers; Σ = ₹11,300 to the paisa.
        var rows = new List<(string No, int Seq, string Supply, string? Gstin, long Taxable, long Cgst, long Sgst, long Total)>();
        foreach (var f in new[] { folioA, folioB })
        {
            string no = (string)(await s.ScalarAsync($"SELECT invoice_no FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct))!;
            var seq = await s.CountAsync($"SELECT seq FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct);
            string supply = (string)(await s.ScalarAsync($"SELECT supply_type FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct))!;
            var gstin = await s.ScalarAsync($"SELECT buyer_gstin FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct) as string;
            long L(string col) => Convert.ToInt64(s.ScalarAsync($"SELECT {col} FROM ivc.invoice WHERE source_kind = 'folio' AND source_id = {f}", Ct).GetAwaiter().GetResult(), CultureInfo.InvariantCulture);
            rows.Add((no, seq, supply, gstin, L("taxable_paise"), L("cgst_paise"), L("sgst_paise"), L("total_paise")));
        }

        Assert.Equal(checkout.InvoiceNos, rows.Select(r => r.No));
        Assert.Equal(rows[0].Seq + 1, rows[1].Seq);
        Assert.Equal(("B2C", (string?)null, 170000L, 7500L, 7500L, 185000L), (rows[0].Supply, rows[0].Gstin, rows[0].Taxable, rows[0].Cgst, rows[0].Sgst, rows[0].Total));
        Assert.Equal(("B2B", (string?)CompanyGstin, 900000L, 22500L, 22500L, 945000L), (rows[1].Supply, rows[1].Gstin, rows[1].Taxable, rows[1].Cgst, rows[1].Sgst, rows[1].Total));
        Assert.Equal(1130000L, rows.Sum(r => r.Total));

        // Both folios settled; the company-credit receipt sits on B and names Deccan; the guest folio carries no company money.
        var va = await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{folioA}", Ct);
        var vb = await Wire.GetAsync<FolioView>(staff.Owner, $"/folios/{folioB}", Ct);
        Assert.Equal(("Departed", 0L, 0L), (va.ReservationStatus, va.Totals.BalancePaise, va.Totals.CompanyCreditPaise));
        Assert.Equal((0L, 945000L), (vb.Totals.BalancePaise, vb.Totals.CompanyCreditPaise));
        Assert.Equal(company.CompanyId, Convert.ToInt64(await s.ScalarAsync($"SELECT company_id FROM pay.receipt WHERE folio_id = {folioB} AND kind = 'company-credit'", Ct), CultureInfo.InvariantCulture));
        Assert.Equal(0, await s.CountAsync($"SELECT COUNT(*) FROM pay.receipt WHERE folio_id = {folioA} AND kind = 'company-credit'", Ct));

        // D7 after checkout: the company's receivable is the ₹9,450 of folio B only (never the guest folio).
        var book = await Wire.GetAsync<CompanyBookView>(staff.Owner, "/groups/companies", Ct);
        var credit = book.Companies.Single(c => c.CompanyId == company.CompanyId).Credit!;
        Assert.Equal((945000L, 945000L, 0L), (credit.ExposurePaise, credit.ReceivablePaise, credit.OpenStaysPaise));
    }
}
