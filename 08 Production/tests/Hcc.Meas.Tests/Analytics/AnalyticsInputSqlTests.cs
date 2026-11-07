// F12 f12-int-an · SQL round trip of the analytics loader (SqlAnalyticsInputSource) and the pack facts (SqlStudyPackFacts) on a fresh .\SQLEXPRESS HccTest_*
// database with every migration applied (Hcc.Inventory.Tests harness). One fictional property of its own: live reservations / folio lines / receipts and lgc rows.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Reporting;
using Hcc.Inventory.Tests.Harness;
using Hcc.Reporting.Analytics;
using Hcc.Reporting.Reports.MeasDigest;
using Hcc.Reporting.Reports.StudyPack;

namespace Hcc.Meas.Tests.Analytics;

public sealed class AnalyticsInputSqlFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public int P { get; private set; }

    public long LegacyBillId { get; private set; }

    private async Task<long> IdAsync(string sql) => Convert.ToInt64(await Db.ScalarAsync(sql), CultureInfo.InvariantCulture);

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        P = (int)await IdAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('ANT', N'Analytics test (fictional)', 'Asia/Kolkata', '2026-11-05');");
        var dlx = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name) OUTPUT INSERTED.room_type_id VALUES ({P}, 'ADLX', 3, N'Deluxe');");
        var hall = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name) OUTPUT INSERTED.room_type_id VALUES ({P}, 'AHALL', 10, N'Conference Hall');");
        var r101 = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '101', {dlx});");
        var r211 = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '211', {dlx});");
        await Db.ExecAsync($"INSERT inv.room(property_id, room_no, room_type_id) VALUES ({P}, 'H1', {hall});");

        // live T-1: departed 1 → 3 Nov in 101 via OTA; nights 1 and 2 posted, night 2 reversed (original + reversal never count as charged nights)
        var res1 = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise, source, company_name)
            OUTPUT INSERTED.reservation_id VALUES ({P}, 'T-1', N'Guest One (fictional)', '2026-11-01', '2026-11-03', 'Departed', {r101}, {dlx}, 2, 1, 250000, N'OTA · Booking.com', N'Acme Fictional Ltd');
            """);
        var g1 = await IdAsync($"INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({P}, N'Guest One (fictional)', '9800000001', N'Indian', 1, 'manual', SYSUTCDATETIME());");
        var f1 = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res1});");
        var s1 = await IdAsync($"""
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            OUTPUT INSERTED.stay_id VALUES ({P}, {res1}, {g1}, {f1}, 'CheckedOut', '2026-11-01', '2026-11-03', '2026-11-01', '2026-11-01T08:00:00', 1, '2026-11-03', '2026-11-03T06:00:00', 1);
            """);
        await Db.ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({s1}, 1, {r101}, '2026-11-01', '2026-11-03');");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f1}, 'room', '2026-11-01', N'Room night', 250000, 1200, 30000);");
        var orig = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) OUTPUT INSERTED.charge_id VALUES ({f1}, 'room', '2026-11-02', N'Room night', 250000, 1200, 30000);");
        // F12-MEAS2-E N1: the reversal is written as production writes it (BillingService void / StayRateCommands re-post): kind 'correction', reverses_charge_id set
        var rev = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id) OUTPUT INSERTED.charge_id VALUES ({f1}, 'correction', '2026-11-02', N'Reversal · Room night · left early', -250000, 1200, -30000, {orig});");
        await Db.ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {rev} WHERE charge_id = {orig};");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f1}, 'food', '2026-11-02', N'Dinner', 50000, 500, 2500);");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f1}, 'discount', '2026-11-02', N'Discount', -20000, 0, 0);");
        await Db.ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES ({f1}, 'payment', 'Cash', 300000, '2026-11-03');");

        // live T-2: confirmed for later in 211, folio without lines → stay, no bill; T-3: cancelled → stay (not slept), no bill
        var res2 = await IdAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, nightly_rate_paise) OUTPUT INSERTED.reservation_id VALUES ({P}, 'T-2', N'Guest Two', '2026-11-10', '2026-11-12', 'Confirmed', {r211}, {dlx}, 300000);");
        await Db.ExecAsync($"INSERT bill.folio(reservation_id) VALUES ({res2});");
        await Db.ExecAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id) VALUES ({P}, 'T-3', N'Guest Three', '2026-11-04', '2026-11-05', 'Cancelled', {r101}, {dlx});");

        // legacy: a two-room booking (101 + 105), a duplicate, a hall booking and a stay open at the cutover
        async Task<long> LStay(long booking, string room, int rooms, string arrival, string? departure, string? expected, int nights, string status) => await IdAsync($"""
            INSERT lgc.stay(property_id, legacy_fy, legacy_booking_id, legacy_guest_id, customer_key, guest_name_snapshot, arrival_date, arrival_utc, departure_date, departure_utc,
                            expected_departure_date, nights, adults, children, room_no, room_count, status, source_tag, purpose)
            OUTPUT INSERTED.stay_id VALUES ({P}, '2025-26', {booking}, {8000 + booking}, 'CK{booking}', N'Legacy guest (fictional)', '{arrival}', '{arrival}', {Q(departure)}, {Q(departure)}, {Q(expected)},
                            {nights}, 2, 0, '{room}', {rooms}, '{status}', {(booking == 7 ? "'OTA:AGODA'" : "NULL")}, N'Official');
            """);
        var l7 = await LStay(7, "101", 2, "2025-06-01", "2025-06-03", null, 2, "CheckedOut");
        await Db.ExecAsync($"INSERT lgc.room_segment(stay_id, seq, room_no, room_type_code, rate_paise, kind) VALUES ({l7}, 1, '101', 'DELUXE', 180000, 'booked'), ({l7}, 2, '105', 'TWIN', 150000, 'booked');");
        var l8 = await LStay(8, "101", 1, "2025-06-01", "2025-06-02", null, 1, "Duplicate");
        var l9 = await LStay(9, "CONAC", 1, "2025-07-01", "2025-07-02", null, 1, "CheckedOut");
        await Db.ExecAsync($"INSERT lgc.room_segment(stay_id, seq, room_no, room_type_code, rate_paise, kind) VALUES ({l9}, 1, 'CONAC', 'CONF', 900000, 'booked');");
        await LStay(10, "101", 1, "2026-10-29", null, "2026-11-04", 6, "InHouseAtCutover");
        async Task<long> LBill(long stay, int no, string room, int nights, long roomCharge, long net, long balance) => await IdAsync($"""
            INSERT lgc.bill(property_id, legacy_fy, legacy_bill_no, legacy_transaction_id, stay_id, legacy_guest_id, customer_key, bill_date, bill_utc, room_no, nights,
                            room_charge_paise, extra_paise, fb_paise, laundry_paise, other_paise, discount_paise, room_gst_bps, room_gst_paise, fb_gst_paise, laundry_gst_paise,
                            other_gst_paise, advance_paise, net_paise, balance_paise, refund_paise, data_flag, paid_flag, b2b, out_of_fy)
            OUTPUT INSERTED.bill_id VALUES ({P}, '2025-26', {no}, {no}, {stay}, 8007, 'CK7', '2025-06-03', '2025-06-03', '{room}', {nights}, {roomCharge}, 10000, 20000, 5000, 0, 15000, 1200,
                            79200, 1000, 0, 0, 0, {net}, {balance}, 0, 'C', 'Y', 0, 0);
            """);
        LegacyBillId = await LBill(l7, 501, "101,105", 2, 660000, 760200, 100000);
        await LBill(l8, 502, "101", 1, 180000, 200000, 0);
        await LBill(l9, 503, "CONAC", 1, 900000, 1000000, 0);
        await Db.ExecAsync($"""
            INSERT lgc.receipt(property_id, legacy_fy, legacy_source, legacy_row_id, stay_id, bill_id, received_date, received_utc, kind, method, amount_paise)
            VALUES ({P}, '2025-26', 'deposit', 1, {l7}, {LegacyBillId}, '2025-06-03', '2025-06-03', 'payment', 'CASH', 500000),
                   ({P}, '2025-26', 'refund', 2, {l7}, {LegacyBillId}, '2025-06-03', '2025-06-03', 'refund', 'CASH', 1000);
            """);

        // F12-MEAS2-E N1 / M5: a property of its own with one live stay whose night 2 was re-posted exactly as StayRateCommands.ApplyAsync does it
        // (reversal kind 'correction' dated the business date with reverses_charge_id, original marked reversed_by, then the new room line), and a zero-rate night 3.
        R = (int)await IdAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('ANR', N'Analytics re-post test (fictional)', 'Asia/Kolkata', '2026-11-05');");
        var rdlx = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name) OUTPUT INSERTED.room_type_id VALUES ({R}, 'RDLX', 2, N'Deluxe');");
        var r301 = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({R}, '301', {rdlx});");
        var res9 = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES ({R}, 'T-9', N'Guest Nine (fictional)', '2026-11-01', '2026-11-04', 'Departed', {r301}, {rdlx}, 2, 0, 200000);
            """);
        var f9 = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res9});");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f9}, 'room', '2026-11-01', N'Room night', 250000, 1200, 30000);");
        var o9 = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) OUTPUT INSERTED.charge_id VALUES ({f9}, 'room', '2026-11-02', N'Room night', 250000, 1200, 30000);");
        var v9 = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id) OUTPUT INSERTED.charge_id VALUES ({f9}, 'correction', '2026-11-02', N'Reversal · rate change · Room night', -250000, 1200, -30000, {o9});");
        await Db.ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {v9} WHERE charge_id = {o9};");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f9}, 'room', '2026-11-02', N'Room night', 200000, 1200, 24000);");
        await Db.ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({f9}, 'room', '2026-11-03', N'Room night', 0, 0, 0);");
    }

    /// <summary>F12-MEAS2-E: the re-post property (one stay, night 2 re-posted, night 3 at zero).</summary>
    public int R { get; private set; }

    private static string Q(string? d) => d is null ? "NULL" : "'" + d + "'";

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

public sealed class AnalyticsInputSqlTests(AnalyticsInputSqlFixture f) : IClassFixture<AnalyticsInputSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task F12_INT_AN_loader_reads_live_and_lgc_with_the_prototype_rules()
    {
        var src = new SqlAnalyticsInputSource(f.Db.Platform);
        var inp = await src.LoadAsync(f.P, new DateOnly(2026, 12, 31), Ct);
        Assert.Equal("2026-11-05", inp.AsOf);                                             // never past the business date
        Assert.Equal(("2026-10-30", "2020-03-01", "2021-03-31"), (inp.LegacyUntil, inp.CovidFrom, inp.CovidTo));

        // rooms: live master without the hall; 211 = Suite; legacy-only 105 kept; in service from the first legacy sale
        Assert.Equal(["101", "211", "105"], inp.Rooms.Select(r => r.Id));
        Assert.Equal(("Deluxe", false, "2025-06-01"), (inp.Rooms[0].Type, inp.Rooms[0].Suite, inp.Rooms[0].InServiceFrom));
        Assert.Equal(("Suite", true, "2000-01-01"), (inp.Rooms[1].Type, inp.Rooms[1].Suite, inp.Rooms[1].InServiceFrom));
        Assert.Equal(("Twin", "2025-06-01"), (inp.Rooms[2].Type, inp.Rooms[2].InServiceFrom));

        // stays: duplicate and hall out; one AnStay per booked room; legacy marker; open legacy stay cut at LegacyUntil
        Assert.Equal(["L2025-26-7", "L2025-26-7/105", "L2025-26-10", "T-1", "T-3", "T-2"], inp.Stays.Select(s => s.Id));
        var l7 = inp.Stays[0];
        Assert.Equal(("101", 180000L, 2, "Departed", true, "OTA", "Agoda", "Official"), (l7.Room, l7.RatePaise, l7.Nights, l7.Status, l7.Legacy, l7.Source, l7.Channel, l7.Purpose));
        Assert.Equal(("105", 150000L), (inp.Stays[1].Room, inp.Stays[1].RatePaise));
        Assert.Equal(("2026-10-29", "2026-10-31", 2, "In house"), (inp.Stays[2].From, inp.Stays[2].To, inp.Stays[2].Nights, inp.Stays[2].Status));
        var t1 = inp.Stays[3];
        Assert.Equal(("101", 2, 250000L, "Departed", false, "OTA", "Booking.com", "Acme Fictional Ltd", 3), (t1.Room, t1.Nights, t1.RatePaise, t1.Status, t1.Legacy, t1.Source, t1.Channel, t1.Company, t1.People));
        Assert.StartsWith("G", t1.GuestKey, StringComparison.Ordinal);
        Assert.Equal(("Cancelled", "Confirmed", "211"), (inp.Stays[4].Status, inp.Stays[5].Status, inp.Stays[5].Room));
        Assert.All(inp.Stays, s => Assert.False(s.Complimentary));                       // DEC-266: no complimentary type built yet

        // bills in paise: legacy (hall bill out; duplicate stay's bill kept as in the workbooks) and live (bill date = departure, reversed night out)
        var lb = Assert.Single(inp.Bills, b => b.Id == "LB" + f.LegacyBillId.ToString(CultureInfo.InvariantCulture));
        // paid: the workbook receivables rule (f12-int golden): a bill not flagged unpaid (paid_flag Y) is paid in full whatever its stored balance
        Assert.Equal(("L2025-26-7", "2025-06-03", 660000L, 20000L, 15000L, 15000L, 80200L, 760200L, 4, 760200L, true),
            (lb.Stay, lb.Date, lb.RoomChargePaise, lb.FnbPaise, lb.OtherPaise, lb.DiscountPaise, lb.GstPaise, lb.TotalPaise, lb.ChargedNights, lb.PaidPaise, lb.Legacy));
        Assert.DoesNotContain(inp.Bills, b => b.RoomChargePaise == 900000);              // CONAC hall bill
        Assert.Equal(2, inp.Bills.Count(b => b.Legacy));
        var live = Assert.Single(inp.Bills, b => !b.Legacy);
        Assert.Equal(("B-T-1", "T-1", "2026-11-03", 250000L, 50000L, 0L, 20000L, 32500L, 312500L, 1, 300000L),
            (live.Id, live.Stay, live.Date, live.RoomChargePaise, live.FnbPaise, live.OtherPaise, live.DiscountPaise, live.GstPaise, live.TotalPaise, live.ChargedNights, live.PaidPaise));

        // receipts: payments only (legacy refund out), legacy method normalised
        Assert.Equal(2, inp.Receipts.Count);
        var lr = Assert.Single(inp.Receipts, r => r.Legacy);
        Assert.Equal((lb.Id, "2025-06-03", "Cash", 500000L), (lr.Bill, lr.Date, lr.Method, lr.AmountPaise));
        var vr = Assert.Single(inp.Receipts, r => !r.Legacy);
        Assert.Equal(("B-T-1", "2026-11-03", "Cash", 300000L), (vr.Bill, vr.Date, vr.Method, vr.AmountPaise));

        // the study selectors run on it (study-b view through its own interface; legacy flag carried)
        var (inB, extras) = await ((Hcc.Reporting.Reports.MeasStudyB.IAnalyticsInputSource)src).LoadAsync(new Hcc.Application.Billing.Caller(f.P, 1),
            new BoundParams(new Dictionary<string, string>()), Ct);
        Assert.Same(inp, inB);                                                           // cached per property + as-of date
        Assert.Empty(extras.Expenses);
        var kpi = BeyondRevenue.RevenueKpi(inB, new AnParams());
        Assert.True(kpi.Legacy);
    }

    [Fact]
    public async Task F12_INT_AN_pack_facts_count_rooms_in_service_and_live_room_lines_by_night()
    {
        var facts = new SqlStudyPackFacts(f.Db.Platform);
        Assert.Equal(2, await facts.RoomsInServiceAsync(f.P, Ct));                        // the hall is not a room in service
        var nights = await facts.RoomNightsAsync(f.P, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), Ct);
        var n = Assert.Single(nights);                                                    // the reversed night and its reversal are not room-nights
        Assert.Equal(("101", new DateOnly(2026, 11, 1), 250000L), (n.Room, n.Night, n.AmountPaise));
    }

    [Fact]
    public async Task MEAS2_E_N1_a_reposted_night_counts_once_at_the_new_rate_and_zero_nights_are_not_charged()
    {
        var inp = await new SqlAnalyticsInputSource(f.Db.Platform).LoadAsync(f.R, new DateOnly(2026, 12, 31), Ct);
        var b = Assert.Single(inp.Bills);
        // room = 250000 (night 1) + 200000 (night 2 at the new rate) + 0 (night 3); the 'correction' reversal nets the original, not "other"
        Assert.Equal(("B-T-9", 450000L, 0L, 0L, 0L), (b.Id, b.RoomChargePaise, b.FnbPaise, b.OtherPaise, b.DiscountPaise));
        Assert.Equal((54000L, 504000L), (b.GstPaise, b.TotalPaise));                    // bill total unchanged by the netting
        Assert.Equal(2, b.ChargedNights);                                               // M5/N1: night 1 + the new night 2; the zero-rate night 3 is not charged

        var nights = await new SqlStudyPackFacts(f.Db.Platform).RoomNightsAsync(f.R, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), Ct);
        Assert.Equal(200000L, Assert.Single(nights, n => n.Night == new DateOnly(2026, 11, 2)).AmountPaise);
    }

    [Fact]
    public void F12_INT_AN_digest_red_rows_and_bodies()
    {
        ReportColumn T(string k) => new(k, k, ReportColumnType.Text, false);
        var cols = new List<ReportColumn> { T("room"), new("guest", "Guest", ReportColumnType.Text, false, ReportMask.GuestName), new("balance", "Balance", ReportColumnType.Money, true), T("exception") };
        ReportRow R(string room, string exc, string? group = null) => new(ReportRowKind.Row, new Dictionary<string, object?> { ["room"] = room, ["guest"] = "Secret", ["balance"] = 123456L, ["exception"] = exc }, group);
        var rows = Enumerable.Range(1, 7).Select(i => R("10" + i.ToString(CultureInfo.InvariantCulture), "above limit")).Append(R("201", "")).Append(R("202", "", "red")).ToList();
        var result = new ReportResult("rpt.money.high-balance", "High balances", new BoundParams(new Dictionary<string, string>()), cols, rows, new ReportAbout(DateTimeOffset.MinValue, "", "", false, false));
        var red = MeasReportDigestSource.RedRows("meas.daily.high-balance", result, 5);
        Assert.Equal(6, red.Count);
        Assert.DoesNotContain(red, r => r.Text.Contains("Secret", StringComparison.Ordinal));  // masked columns never in the digest
        Assert.StartsWith("101 · ", red[0].Text, StringComparison.Ordinal);
        Assert.EndsWith(" — above limit", red[0].Text, StringComparison.Ordinal);
        Assert.Equal("… and 3 more in High balances", red[5].Text);                        // 7 exception rows + the red-group row = 8
        Assert.False(MeasReportDigestSource.Evaluated(new ReportDefinition("rpt.study.x", "x", "study", "P2", "MEAS", false, null, [], [], "", null, [], [], "", [], ReportPhone.No), "meas.study.x"));

        var block = MeasurementDigest.Compose(new DateOnly(2026, 11, 5), new Dictionary<string, IReadOnlyList<MeasRedRow>> { ["meas.daily.high-balance"] = red }, _ => true);
        Assert.Equal(["Measurement exceptions: 6"], MeasurementDigest.EmailLines(block).Take(1));
        Assert.Equal(["Measurement exceptions 6"], MeasurementDigest.WhatsAppLines(block));
    }
}
