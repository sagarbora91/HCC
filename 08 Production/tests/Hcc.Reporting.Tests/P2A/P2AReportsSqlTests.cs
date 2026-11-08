// F8-RPT-P2-A · batch A guests + rooms on a disposable HccTest_* database: reservations register, guest tariff, company business, user-wise activity, service lines,
// room-wise revenue, rooms left, allocation grid. Rule V / M through the engine runner (FakePolicy), ranges, masks, screen = Excel = PDF = print on every result.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.P2A;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.P2A;

[Trait("Category", "Sql")]
public sealed class P2AReportsSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;
    private readonly FakePolicy _policy = Policy();

    private static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-ADM-004"] = ["OWN", "MGR"];
        return p;
    }

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task Seed()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES (@p, 'F1', N'Floor 1', 1, {own}, '2026-01-01');
            DECLARE @fl INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic'), (@p, 'DLX', 4, N'Deluxe');
            DECLARE @cls INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS'), @dlx INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'DLX');
            UPDATE inv.room_type SET rack_rate_paise = 300000 WHERE room_type_id = @dlx;
            INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES (@cls, '2026-01-01', 200000);
            INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, '101', @cls, 'Ready', @fl), (@p, '102', @cls, 'Ready', @fl), (@p, '201', @dlx, 'Ready', @fl),
                   (@p, '202', @dlx, 'Ready', @fl), (@p, '203', @cls, 'Ready', @fl);
            INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc) VALUES
              (@p, N'Ananya Rao', '+919820001041', N'Indian', N'Aadhaar', '4101', N'10 Demo Street, Bengaluru, Karnataka (fictional)', 'manual', '2026-10-01'),
              (@p, N'Claire Dupont', '+33100000001', N'French', N'Passport', '4567', N'1 Rue Demo, Paris', 'manual', '2026-10-01');
            DECLARE @g1 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Ananya Rao'), @g2 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Claire Dupont');
            INSERT gst.guest_agreed_rate(guest_id, room_type_id, rate_paise, set_by, set_utc) VALUES (@g1, @cls, 190000, {own}, '2026-10-01');
            INSERT grp.company_account(property_id, name, created_by, created_utc) VALUES (@p, N'Acme Travels', {own}, '2026-10-01');
            DECLARE @co BIGINT = (SELECT company_id FROM grp.company_account WHERE name = N'Acme Travels');
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101'), @r102 INT = (SELECT room_id FROM inv.room WHERE room_no = '102'), @r201 INT = (SELECT room_id FROM inv.room WHERE room_no = '201'),
                    @r203 INT = (SELECT room_id FROM inv.room WHERE room_no = '203');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source, phone) VALUES
              (@p, 'HCC-1041', N'Ananya Rao', '2026-10-23', '2026-10-26', 'InHouse', @r101, @cls, 1, 0, 'Direct', NULL),
              (@p, 'HCC-1042', N'Claire Dupont', '2026-10-22', '2026-10-26', 'InHouse', @r201, @dlx, 2, 0, 'Direct', NULL),
              (@p, 'HCC-1043', N'Walk In', '2026-10-24', '2026-10-26', 'Confirmed', @r102, @cls, 2, 1, 'Phone', '+919000000001'),
              (@p, 'HCC-1044', N'Cancelled Guest', '2026-10-25', '2026-10-27', 'Cancelled', @r203, @cls, 1, 0, 'Direct', NULL),
              (@p, 'HCC-1045', N'Ananya Rao', '2026-10-18', '2026-10-20', 'Departed', @r101, @cls, 1, 0, 'Direct', NULL);
            UPDATE inv.reservation SET nightly_rate_paise = 200000, company_id = @co WHERE public_no = 'HCC-1041';
            UPDATE inv.reservation SET nightly_rate_paise = 350000 WHERE public_no = 'HCC-1042';
            UPDATE inv.reservation SET nightly_rate_paise = 200000 WHERE public_no IN ('HCC-1043','HCC-1045');
            UPDATE inv.reservation SET rate_source = 'agreed' WHERE public_no = 'HCC-1045';
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-1041','HCC-1042','HCC-1043','HCC-1045');
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by)
              SELECT reservation_id, CASE public_no WHEN 'HCC-1042' THEN @g2 ELSE @g1 END, '2026-10-20', {own} FROM inv.reservation WHERE public_no IN ('HCC-1041','HCC-1042','HCC-1045');
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
              SELECT @p, r.reservation_id, CASE r.public_no WHEN 'HCC-1042' THEN @g2 ELSE @g1 END, f.folio_id,
                     CASE r.public_no WHEN 'HCC-1045' THEN 'CheckedOut' ELSE 'InHouse' END, r.arrival, r.departure, r.arrival,
                     DATEADD(MINUTE, 510, CAST(r.arrival AS DATETIME2)), {own},
                     CASE r.public_no WHEN 'HCC-1045' THEN r.departure END, CASE r.public_no WHEN 'HCC-1045' THEN DATEADD(MINUTE, 330, CAST(r.departure AS DATETIME2)) END, CASE r.public_no WHEN 'HCC-1045' THEN {own} END
                FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id WHERE r.public_no IN ('HCC-1041','HCC-1042','HCC-1045');
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date)
              SELECT s.stay_id, 1, r.room_id, s.arrival, CASE s.status WHEN 'CheckedOut' THEN s.departure END FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id;
            DECLARE @f1 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-1041'),
                    @f2 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-1042'),
                    @f3 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-1043'),
                    @f5 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-1045');
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
              (@f1, 'room', '2026-10-23', N'Room night · 2026-10-23', 100000, 500, 5000), (@f1, 'room', '2026-10-24', N'Room night · 2026-10-24', 100000, 500, 5000),
              (@f1, 'service', '2026-10-24', N'Extra person', 20000, 500, 1000), (@f1, 'discount', '2026-10-24', N'Discount · loyalty', -10000, 500, -500),
              (@f1, 'service', '2026-10-24', N'Laundry · shirts', 30000, 1800, 5400), (@f1, 'service', '2026-10-24', N'Restaurant · bill 9 · 2 items', 50000, 500, 2500),
              (@f1, 'service', '2026-10-24', N'Spa visit', 40000, 1800, 7200), (@f1, 'service', '2026-10-24', N'Mini bar water', 5000, 500, 250),
              (@f2, 'room', '2026-10-22', N'Room night · 2026-10-22', 350000, 1800, 63000), (@f2, 'room', '2026-10-23', N'Room night · 2026-10-23', 350000, 1800, 63000),
              (@f5, 'room', '2026-10-18', N'Room night · 2026-10-18', 200000, 500, 10000), (@f5, 'room', '2026-10-19', N'Room night · 2026-10-19', 200000, 500, 10000);
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id)
              SELECT @f1, 'correction', '2026-10-24', N'Reversal · Mini bar water', -5000, 500, -250, charge_id FROM bill.charge WHERE label = N'Mini bar water';
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f3, 'deposit', 'Cash', 50000, '2026-10-20');
            INSERT inv.room_night(room_id, night, reservation_id)
              SELECT x.room_id, x.night, r.reservation_id FROM inv.reservation r
              JOIN (VALUES ('HCC-1041', @r101, '2026-10-23'), ('HCC-1041', @r101, '2026-10-24'), ('HCC-1041', @r101, '2026-10-25'),
                           ('HCC-1042', @r201, '2026-10-22'), ('HCC-1042', @r201, '2026-10-23'), ('HCC-1042', @r201, '2026-10-24'), ('HCC-1042', @r201, '2026-10-25'),
                           ('HCC-1043', @r102, '2026-10-24'), ('HCC-1043', @r102, '2026-10-25'), ('HCC-1045', @r101, '2026-10-18'), ('HCC-1045', @r101, '2026-10-19')) x(no, room_id, night) ON x.no = r.public_no
              WHERE 1 = 1;
            INSERT inv.group_block(property_id, code, name, arrival, departure, cutoff, status) VALUES (@p, 'GB1', N'Wedding', '2026-10-25', '2026-10-27', '2026-10-24', 'Open');
            INSERT inv.group_block_type(group_block_id, room_type_id, allotted) SELECT group_block_id, @cls, 1 FROM inv.group_block WHERE code = 'GB1';
            INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES (@p, @r203, NULL, NULL, N'Bathroom leak', 'manual', {own}, '2026-10-20');
            SELECT 1;
            """);
    }

    private ReportRunner Runner() => Rig.Runner(_policy, new ReservationsRegisterReport(db.Platform), new GuestTariffReport(db.Platform), new CompanyBusinessReport(db.Platform),
        new UserwiseActivityReport(db.Platform), new ServiceLinesReport(db.Platform), new RoomWiseRevenueReport(db.Platform), new RoomsLeftReport(db.Platform), new AllocationGridReport(db.Platform));

    private async Task<ReportResult> Run(int principal, string id, params (string K, string V)[] raw)
    {
        var d = raw.ToDictionary(x => x.K, x => (string?)x.V);
        var r = await Runner().RunAsync(new Hcc.Application.Billing.Caller(db.PropertyId, principal), id, d, new RunOptions(All: true), Ct);
        RendererTests.ThreeWay(r);
        return r;
    }

    private static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); return null; }
        catch (HccException e) { return e.Code; }
    }

    private static List<ReportRow> Data(ReportResult r) => r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    private static string S(ReportRow r, string k) => r.Cells[k] is DateOnly d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToString(r.Cells[k], CultureInfo.InvariantCulture) ?? "";

    private static long L(ReportRow r, string k) => Convert.ToInt64(r.Cells[k], CultureInfo.InvariantCulture);

    [Fact]
    public async Task Reservations_register_lists_by_arrival_with_filters_totals_and_limits()
    {
        var r = await Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"));
        var d = Data(r);
        Assert.Equal(["HCC-1042", "HCC-1041", "HCC-1043", "HCC-1044"], d.Select(x => S(x, "booking")));
        Assert.Equal(50000L, L(d.Single(x => S(x, "booking") == "HCC-1043"), "advance"));
        Assert.Equal(600000L, L(d.Single(x => S(x, "booking") == "HCC-1041"), "quoted"));
        Assert.Equal("Checked in", S(d[0], "status"));
        var total = r.Rows[^1];
        Assert.Equal(ReportRowKind.Total, total.Kind);
        Assert.Equal(d.Sum(x => L(x, "quoted")), L(total, "quoted"));
        Assert.Equal(4L, L(total, "rooms"));
        Assert.Equal("Confirmed 1 · Checked in 2 · Cancelled 1", S(total, "status"));
        Assert.Equal(["HCC-1044"], Data(await Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"), ("status", "cancelled"))).Select(x => S(x, "booking")));
        Assert.Equal(["HCC-1042"], Data(await Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"), ("search", "claire"))).Select(x => S(x, "booking")));
        Assert.Equal(["HCC-1043"], Data(await Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"), ("search", "9000000001"))).Select(x => S(x, "booking")));
        Assert.Equal(["HCC-1043"], Data(await Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"), ("source", "phone"))).Select(x => S(x, "booking")));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-01"), ("to", "2027-01-05"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, ReservationsRegisterReport.Id, ("from", "2026-10-29"), ("to", "2027-02-01"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, ReservationsRegisterReport.Id, ("status", "maybe"))));
    }

    [Fact]
    public async Task Reservations_register_roles_and_money_removal()
    {
        foreach (var refused in new[] { 3, 4, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, ReservationsRegisterReport.Id)));
        Assert.Null(await Code(() => Run(5, ReservationsRegisterReport.Id)));
        _policy.Denied.Add((5, "PERM-RPT-002"));
        try
        {
            var r = await Run(5, ReservationsRegisterReport.Id, ("from", "2026-10-22"), ("to", "2026-10-26"));
            Assert.DoesNotContain(r.Columns, c => c.Money);
            Assert.Equal(12 - 2, r.Columns.Count);
            Assert.All(r.Rows, x => Assert.False(x.Cells.ContainsKey("quoted")));
        }
        finally { _policy.Denied.Remove((5, "PERM-RPT-002")); }
    }

    [Fact]
    public async Task Guest_tariff_net_tariff_rate_source_and_agreed_rate_per_stay()
    {
        var r = await Run(1, GuestTariffReport.Id, ("party", "ananya"), ("from", "2026-01-01"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(["HCC-1041", "HCC-1045"], d.Select(x => S(x, "stay")));
        var a = d[0];
        Assert.Equal(200000L, L(a, "standard"));
        Assert.Equal("standard", S(a, "rateSource"));
        Assert.Equal(190000L, L(a, "agreed"));
        Assert.Equal(140000L, L(a, "extras"));
        Assert.Equal(10000L, L(a, "discount"));
        Assert.Equal(190000L, L(a, "netTariff"));
        Assert.Equal("agreed", S(d[1], "rateSource"));
        Assert.Equal(400000L, L(d[1], "netTariff"));
        Assert.Equal(590000L, L(r.Rows[^1], "netTariff"));
        Assert.Equal(["HCC-1041"], Data(await Run(1, GuestTariffReport.Id, ("party", "acme"), ("from", "2026-01-01"), ("to", "2026-10-24"))).Select(x => S(x, "stay")));
        Assert.Equal("manual", S(Data(await Run(1, GuestTariffReport.Id, ("party", "claire"), ("from", "2026-01-01"), ("to", "2026-10-24")))[0], "rateSource"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, GuestTariffReport.Id)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(3, GuestTariffReport.Id, ("party", "ananya"))));
    }

    [Fact]
    public async Task Company_business_sums_the_company_stays_in_range()
    {
        var r = await Run(1, CompanyBusinessReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        var d = Data(r);
        var x = Assert.Single(d);
        Assert.Equal("Acme Travels", S(x, "company"));
        Assert.Equal(1L, L(x, "guests"));
        Assert.Equal(2L, L(x, "roomNights"));
        Assert.Equal(200000L, L(x, "tariff"));
        Assert.Equal(130000L, L(x, "other"));
        Assert.Equal(330000L, L(x, "total"));
        Assert.Equal(330000L, L(r.Rows[^1], "total"));
        Assert.Empty(Data(await Run(1, CompanyBusinessReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("company", "nobody"))));
        foreach (var refused in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, CompanyBusinessReport.Id)));
    }

    [Fact]
    public async Task Userwise_activity_counts_check_ins_and_check_outs_and_binds_the_range_to_both()
    {
        var r = await Run(1, UserwiseActivityReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(4, d.Count);
        Assert.Equal(3, d.Count(x => S(x, "action") == "Check-in"));
        Assert.Equal("HCC-1045", S(d.Single(x => S(x, "action") == "Check-out"), "booking"));
        Assert.Equal("2026-10-22 14:00", S(d.Single(x => S(x, "booking") == "HCC-1042" && S(x, "action") == "Check-in"), "at"));
        Assert.Equal("101", S(d.Single(x => S(x, "booking") == "HCC-1041"), "room"));
        Assert.Equal("3 check-ins · 1 check-out", S(r.Rows[^1], "booking"));
        Assert.Contains(r.Rows, x => x.Kind == ReportRowKind.Subtotal);
        var only = Data(await Run(1, UserwiseActivityReport.Id, ("from", "2026-10-21"), ("to", "2026-10-22")));
        Assert.Equal(["HCC-1042"], only.Select(x => S(x, "booking")));
        Assert.Single(Data(await Run(1, UserwiseActivityReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("action", "check-out"))));
        Assert.Empty(Data(await Run(1, UserwiseActivityReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("user", "9999"))));
        Assert.DoesNotContain(r.Columns, c => c.Money);
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, UserwiseActivityReport.Id, ("from", "2026-07-01"), ("to", "2026-10-24"))));
        foreach (var refused in new[] { 3, 4, 5, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, UserwiseActivityReport.Id)));
        Assert.Null(await Code(() => Run(2, UserwiseActivityReport.Id)));
    }

    [Fact]
    public async Task Service_lines_group_leave_out_reversed_and_total()
    {
        var r = await Run(1, ServiceLinesReport.Id, ("from", "2026-10-24"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(4, d.Count);
        Assert.DoesNotContain(d, x => S(x, "item") == "Mini bar water");
        Assert.Equal("Laundry", S(d.Single(x => S(x, "item") == "Laundry · shirts"), "group"));
        var pos = d.Single(x => S(x, "item").StartsWith("Restaurant", StringComparison.Ordinal));
        Assert.Equal("Food", S(pos, "group"));
        Assert.Equal("room-service POS", S(pos, "source"));
        Assert.Equal("Other", S(d.Single(x => S(x, "item") == "Spa visit"), "group"));
        Assert.Equal("101", S(d[0], "room"));
        Assert.Equal(140000L, L(r.Rows[^1], "amount"));
        Assert.Equal(140000L, r.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).Sum(x => L(x, "amount")));
        Assert.Single(Data(await Run(1, ServiceLinesReport.Id, ("from", "2026-10-24"), ("to", "2026-10-24"), ("group", "laundry"))));
        Assert.Single(Data(await Run(1, ServiceLinesReport.Id, ("from", "2026-10-24"), ("to", "2026-10-24"), ("item", "spa"))));
        foreach (var refused in new[] { 3, 4, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, ServiceLinesReport.Id)));
    }

    [Fact]
    public async Task Room_wise_revenue_by_room_type_and_floor_ties_out()
    {
        var byRoom = await Run(1, RoomWiseRevenueReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        var d = Data(byRoom);
        Assert.Equal(["101", "201"], d.Select(x => S(x, "name")));
        var r101 = d[0];
        Assert.Equal(4L, L(r101, "nights"));
        Assert.Equal(600000L, L(r101, "tariff"));
        Assert.Equal(20000L, L(r101, "extra"));
        Assert.Equal(-15000L, L(r101, "other"));
        Assert.Equal(605000L, L(r101, "total"));
        Assert.Equal(150000L, L(r101, "avg"));
        Assert.Equal(350000L, L(d[1], "avg"));
        Assert.Equal(1300000L, L(byRoom.Rows[^1], "tariff"));
        var byType = await Run(1, RoomWiseRevenueReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("groupBy", "room type"));
        Assert.Equal(["Classic", "Deluxe"], Data(byType).Select(x => S(x, "name")));
        Assert.Equal(L(byRoom.Rows[^1], "total"), L(byType.Rows[^1], "total"));
        var byFloor = await Run(1, RoomWiseRevenueReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("groupBy", "floor"));
        Assert.Equal(["Floor 1"], Data(byFloor).Select(x => S(x, "name")));
        Assert.Equal(L(byRoom.Rows[^1], "total"), L(byFloor.Rows[^1], "total"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, RoomWiseRevenueReport.Id, ("groupBy", "colour"))));
        foreach (var refused in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, RoomWiseRevenueReport.Id)));
    }

    [Fact]
    public async Task Rooms_left_counts_occupied_held_and_out_of_order_per_type_and_night()
    {
        var r = await Run(1, RoomsLeftReport.Id, ("from", "2026-10-24"), ("to", "2026-10-25"));
        var d = Data(r);
        Assert.Equal(4, d.Count);
        var cls24 = d.Single(x => S(x, "date") == "2026-10-24" && S(x, "roomType") == "Classic");
        Assert.Equal(3L, L(cls24, "total"));
        Assert.Equal(2L, L(cls24, "occupied"));
        Assert.Equal(0L, L(cls24, "held"));
        Assert.Equal(1L, L(cls24, "ooo"));
        Assert.Equal(0L, L(cls24, "free"));
        var dlx24 = d.Single(x => S(x, "date") == "2026-10-24" && S(x, "roomType") == "Deluxe");
        Assert.Equal(1L, L(dlx24, "occupied"));
        Assert.Equal(1L, L(dlx24, "free"));
        var cls25 = d.Single(x => S(x, "date") == "2026-10-25" && S(x, "roomType") == "Classic");
        Assert.Equal(1L, L(cls25, "held"));
        Assert.Single(Data(await Run(1, RoomsLeftReport.Id, ("from", "2026-10-24"), ("to", "2026-10-24"), ("roomType", "deluxe"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, RoomsLeftReport.Id, ("from", "2026-10-24"), ("to", "2026-11-24"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, RoomsLeftReport.Id, ("from", "2027-01-20"), ("to", "2027-01-25"))));
        Assert.DoesNotContain(r.Columns, c => c.Money);
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(3, RoomsLeftReport.Id)));
    }

    [Fact]
    public async Task Allocation_grid_shows_guest_names_out_of_order_and_nights_occupied()
    {
        var r = await Run(1, AllocationGridReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        Assert.Equal(2 + 3 + 1, r.Columns.Count);
        Assert.Equal("22 Oct", r.Columns[2].Label);
        var d = Data(r);
        Assert.Equal(["101", "102", "203", "201", "202"], d.Select(x => S(x, "room")));
        Assert.Equal(["", "Ananya Rao", "Ananya Rao"], new[] { "d01", "d02", "d03" }.Select(k => S(d[0], k)));
        Assert.Equal(2L, L(d[0], "nights"));
        Assert.Equal("Walk In", S(d[1], "d03"));
        Assert.Equal(["OOO", "OOO", "OOO"], new[] { "d01", "d02", "d03" }.Select(k => S(d[2], k)));
        Assert.Equal(3L, L(d[3], "nights"));
        Assert.Equal(0L, L(d[4], "nights"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, AllocationGridReport.Id, ("from", "2026-09-20"), ("to", "2026-10-24"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, AllocationGridReport.Id, ("from", "2026-10-22"), ("to", "2026-10-25"))));
        Assert.Empty(Data(await Run(1, AllocationGridReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"), ("roomType", "none"))));
        foreach (var refused in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, AllocationGridReport.Id)));
    }
}
