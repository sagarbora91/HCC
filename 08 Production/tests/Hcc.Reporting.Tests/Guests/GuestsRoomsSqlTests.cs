// F7-R3 · batch 1 guests + rooms on a disposable HccTest_* database: movement register, guest register (incl. a legacy stay), Form C register, rate changes,
// room status, the Lodge layouts. Rule V / M through the engine runner (FakePolicy), masks, ranges, and screen = Excel = PDF = print on every result.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Guests;
using Hcc.Reporting.Reports.Rooms;
using Hcc.Reporting.Sql;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.Guests;

[Trait("Category", "Sql")]
public sealed class GuestsRoomsSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;
    private readonly FakePolicy _policy = Policy();

    private static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-STY-001"] = ["OWN", "MGR", "REC"];
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
        var mgr = db.Users["MGR"];
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES (@p, 'F1', N'Floor 1', 1, {own}, '2026-01-01');
            DECLARE @fl INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic'), (@p, 'DLX', 4, N'Deluxe');
            DECLARE @cls INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS'), @dlx INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'DLX');
            INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, '101', @cls, 'Ready', @fl), (@p, '102', @cls, 'Ready', @fl), (@p, '201', @dlx, 'Ready', @fl),
                   (@p, '202', @dlx, 'Dirty', @fl), (@p, '203', @cls, 'Ready', @fl);
            INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc) VALUES
              (@p, N'Ananya Rao', '+919820001041', N'Indian', N'Aadhaar', '4101', N'10 Demo Street, Bengaluru, Karnataka (fictional)', 'manual', '2026-10-01'),
              (@p, N'Claire Dupont', '+33100000001', N'French', N'Passport', '4567', N'1 Rue Demo, Paris', 'manual', '2026-10-01'),
              (@p, N'Hans Meyer', '+49100000001', N'German', N'Passport', '8899', N'2 Demo Allee, Berlin', 'manual', '2026-10-01');
            DECLARE @g1 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Ananya Rao'), @g2 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Claire Dupont'),
                    @g3 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Hans Meyer');
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101'), @r102 INT = (SELECT room_id FROM inv.room WHERE room_no = '102'), @r201 INT = (SELECT room_id FROM inv.room WHERE room_no = '201'),
                    @r202 INT = (SELECT room_id FROM inv.room WHERE room_no = '202'), @r203 INT = (SELECT room_id FROM inv.room WHERE room_no = '203');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source, purpose_code, vehicle_no) VALUES
              (@p, 'HCC-1041', N'Ananya Rao', '2026-10-23', '2026-10-26', 'InHouse', @r101, @cls, 1, 0, 'Direct', 'BUSINESS', 'MH12AB1234'),
              (@p, 'HCC-1042', N'Claire Dupont', '2026-10-22', '2026-10-24', 'InHouse', @r201, @dlx, 2, 0, 'Direct', 'TOURISM', NULL),
              (@p, 'HCC-1043', N'Walk In', '2026-10-24', '2026-10-26', 'Confirmed', @r102, @cls, 2, 1, 'Phone', NULL, NULL),
              (@p, 'HCC-1044', N'Cancelled Guest', '2026-10-25', '2026-10-27', 'Cancelled', @r203, @cls, 1, 0, 'Direct', NULL, NULL),
              (@p, 'HCC-1045', N'Hans Meyer', '2026-10-21', '2026-10-27', 'InHouse', @r202, @dlx, 1, 0, 'Direct', NULL, NULL),
              (@p, 'HCC-1046', N'Ananya Rao', '2026-10-18', '2026-10-20', 'Departed', @r101, @cls, 1, 0, 'Direct', NULL, NULL);
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-1041','HCC-1042','HCC-1045','HCC-1046');
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by)
              SELECT reservation_id, CASE public_no WHEN 'HCC-1042' THEN @g2 WHEN 'HCC-1045' THEN @g3 ELSE @g1 END, '2026-10-20', {own} FROM inv.reservation WHERE public_no IN ('HCC-1041','HCC-1042','HCC-1045','HCC-1046');
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
              SELECT @p, r.reservation_id, CASE r.public_no WHEN 'HCC-1042' THEN @g2 WHEN 'HCC-1045' THEN @g3 ELSE @g1 END, f.folio_id,
                     CASE r.public_no WHEN 'HCC-1046' THEN 'CheckedOut' ELSE 'InHouse' END, r.arrival, r.departure, r.arrival,
                     DATEADD(MINUTE, 510, CAST(r.arrival AS DATETIME2)), {own},
                     CASE r.public_no WHEN 'HCC-1046' THEN r.departure END, CASE r.public_no WHEN 'HCC-1046' THEN DATEADD(MINUTE, 330, CAST(r.departure AS DATETIME2)) END, CASE r.public_no WHEN 'HCC-1046' THEN {own} END
                FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id;
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date)
              SELECT s.stay_id, 1, r.room_id, s.arrival, CASE s.status WHEN 'CheckedOut' THEN s.departure END FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id;
            DECLARE @f1 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-1041');
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
              (@f1, 'room', '2026-10-23', N'Room night · 2026-10-23', 100000, 500, 5000), (@f1, 'service', '2026-10-24', N'Late check-out · half day', 50000, 1800, 9000);
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f1, 'payment', 'Cash', 80000, '2026-10-23');
            DECLARE @s2 BIGINT = (SELECT s.stay_id FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id WHERE r.public_no = 'HCC-1042');
            INSERT gst.stay_form_c(stay_id, property_id, passport_place, passport_issued, passport_expiry, visa_no, visa_type, visa_place, visa_issued, visa_expiry, arrived_from, arrival_in_india, intended_stay_days,
                                   next_destination, employed_in_india, address_abroad, sex, updated_by, updated_utc)
              VALUES (@s2, @p, N'Paris', '2022-01-10', '2032-01-09', N'V99887766', N'Tourist', N'Paris', '2026-09-01', '2027-03-01', N'Delhi', '2026-10-20', 10, N'Goa', 0, N'1 Rue Demo, Paris', 'F', {own}, '2026-10-22');
            DECLARE @s1 BIGINT = (SELECT s.stay_id FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id WHERE r.public_no = 'HCC-1041'), @res1 BIGINT = (SELECT reservation_id FROM inv.reservation WHERE public_no = 'HCC-1041');
            INSERT gst.stay_rate_change(property_id, stay_id, reservation_id, room_id, business_date, changed_utc, changed_by, scope, reason, old_rate_paise, new_rate_paise, old_people, new_people, old_children, new_children, effective_from)
              VALUES (@p, @s1, @res1, @r101, '2026-10-23', '2026-10-23T10:00:00', {mgr}, 'next', N'Loyal guest', 200000, 180000, 1, 2, 0, 0, '2026-10-24'),
                     (@p, @s1, @res1, @r101, '2026-10-24', '2026-10-24T03:30:00', {own}, 'all', N'Extra bed', 180000, 210000, 2, 2, 0, 1, '2026-10-24');
            INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES (@p, @r203, NULL, NULL, N'Bathroom leak', 'manual', {own}, '2026-10-20');
            INSERT lgc.stay(property_id, legacy_fy, legacy_booking_id, legacy_guest_id, customer_key, guest_name_snapshot, arrival_date, arrival_utc, departure_date, departure_utc, nights, adults, children, room_no, room_count, purpose, status, legacy_bill_no)
              VALUES (@p, '2025-26', 777, 5001, 'k1', N'Legacy Guest', '2026-03-15', '2026-03-15T08:00:00', '2026-03-17', '2026-03-17T05:30:00', 2, 2, 0, '305', 1, N'Tourism', 'CheckedOut', 4321);
            INSERT lgc.guest_map(property_id, legacy_guest_id, guest_id, customer_key, phone_valid, name_snapshot, name_fold, deleted, source_fy) VALUES (@p, 5001, @g1, 'k1', 1, N'Legacy Guest', N'legacy guest', 0, '2025-26');
            SELECT 1;
            """);
    }

    private ReportRunner Runner() => Rig.Runner(_policy, new MovementRegisterReport(db.Platform), new GuestRegisterReport(db.Platform), new FormCRegisterReport(db.Platform),
        new RateChangesReport(db.Platform), new RoomStatusReport(db.Platform, _policy));

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

    [Fact]
    public async Task Movement_register_lists_arrivals_and_departures_with_day_totals_late_charge_and_balance()
    {
        var r = await Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2026-10-27"));
        var d = Data(r);
        Assert.Equal(["HCC-1043", "HCC-1042", "HCC-1044", "HCC-1041", "HCC-1043", "HCC-1045"], d.Select(x => S(x, "booking")));
        var one = d.Single(x => S(x, "booking") == "HCC-1041");
        Assert.Equal(59000L, one.Cells["lateCharge"]);
        Assert.Equal(84000L, one.Cells["balanceDue"]);
        Assert.Equal("2026-10-23 14:00", S(one, "checkInTime"));
        Assert.Equal("MH12AB1234", S(one, "vehicle"));
        Assert.Equal("Fixed time", S(one, "basis"));
        Assert.Equal("Checked in", S(one, "status"));
        var sub = r.Rows.Where(x => x.Kind == ReportRowKind.Subtotal).ToList();
        Assert.Equal(4, sub.Count);
        Assert.Equal("Day total · 2 rooms", S(sub[0], "booking"));
        var total = r.Rows[^1];
        Assert.Equal(ReportRowKind.Total, total.Kind);
        Assert.Equal(d.Sum(x => (long)x.Cells["pax"]!), (long)total.Cells["pax"]!);
        Assert.Equal(d.Sum(x => (long)x.Cells["balanceDue"]!), (long)total.Cells["balanceDue"]!);
    }

    [Fact]
    public async Task Movement_register_filters_ranges_and_roles()
    {
        var arrivals = await Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2026-10-27"), ("type", "arrivals"), ("status", "cancelled"));
        Assert.Equal(["HCC-1044"], Data(arrivals).Select(x => S(x, "booking")));
        var byType = await Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2026-10-27"), ("roomType", "deluxe"), ("source", "direct"));
        Assert.All(Data(byType), x => Assert.Equal("Deluxe", S(x, "roomType")));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2027-01-24"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2027-01-23"))));
        Assert.Null(await Code(() => Run(1, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2027-01-22"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, MovementRegisterReport.Id, ("type", "sideways"))));
        foreach (var refused in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, MovementRegisterReport.Id)));
        Assert.Null(await Code(() => Run(5, MovementRegisterReport.Id)));
        var legacy = await Run(1, MovementRegisterReport.Id, ("from", "2026-03-01"), ("to", "2026-03-10"));
        Assert.True(legacy.About.Legacy);
        Assert.Empty(Data(legacy));
    }

    [Fact]
    public async Task Movement_register_without_the_finance_right_loses_both_money_columns()
    {
        _policy.Denied.Add((5, "PERM-RPT-002"));
        try
        {
            var r = await Run(5, MovementRegisterReport.Id, ("from", "2026-10-24"), ("to", "2026-10-27"));
            Assert.DoesNotContain(r.Columns, c => c.Money);
            Assert.Equal(["lateCharge", "balanceDue"], r.Removed!.OrderBy(x => x == "balanceDue"));
            Assert.Equal(16 - 2, r.Columns.Count);
            Assert.All(r.Rows, x => Assert.False(x.Cells.ContainsKey("balanceDue")));
        }
        finally { _policy.Denied.Remove((5, "PERM-RPT-002")); }
    }

    [Fact]
    public async Task Guest_register_masks_ids_lists_the_stays_and_a_legacy_stay_before_April_2026()
    {
        var r = await Run(1, GuestRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(["HCC-1041", "HCC-1042", "HCC-1045", "HCC-1046"], d.Select(x => S(x, "regNo")));
        var one = d[0];
        Assert.Equal("XXXX-XXXX-4101", S(one, "idNumber"));
        Assert.Equal("Bengaluru", S(one, "city"));
        Assert.Equal("2026-10-23 14:00", S(one, "checkIn"));
        Assert.Equal("", S(one, "checkOut"));
        Assert.Equal("BUSINESS", S(one, "purpose"));
        Assert.Equal("MH12AB1234", S(one, "vehicle"));
        Assert.Equal("Delhi", S(d[1], "comingFrom"));
        Assert.Equal("Goa", S(d[1], "goingTo"));
        Assert.Equal("2026-10-20 11:00", S(d[3], "checkOut"));
        Assert.Equal("4 stays", S(r.Rows[^1], "regNo"));
        Assert.All(d, x => Assert.Matches("^XXXX-XXXX-\\w{4}$", S(x, "idNumber")));
        var inHouse = await Run(1, GuestRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("status", "in house"));
        Assert.Equal(3, Data(inHouse).Count);
        var legacy = await Run(1, GuestRegisterReport.Id, ("from", "2026-03-01"), ("to", "2026-03-31"));
        Assert.True(legacy.About.Legacy);
        Assert.Equal(["L2025-26-777"], Data(legacy).Select(x => S(x, "regNo")));
        Assert.Equal("L-4321", S(Data(legacy)[0], "billNo"));
        Assert.Equal("XXXX-XXXX-4101", S(Data(legacy)[0], "idNumber"));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(1, GuestRegisterReport.Id, ("from", "2026-07-01"), ("to", "2026-10-24"))));
    }

    [Fact]
    public async Task Guest_register_roles_follow_the_desk_right()
    {
        Assert.Null(await Code(() => Run(3, GuestRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"))));
        foreach (var refused in new[] { 4, 7, 8 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, GuestRegisterReport.Id)));
        var rec = await Run(3, GuestRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        Assert.DoesNotContain("•", S(Data(rec)[0], "guest"));
        _policy.Denied.Add((3, "PERM-RES-001"));
        _policy.Denied.Add((3, "PERM-FOL-001"));
        try
        {
            var masked = await Run(3, GuestRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
            Assert.Equal("A••• R•••", S(Data(masked)[0], "guest"));
        }
        finally { _policy.Denied.Remove((3, "PERM-RES-001")); _policy.Denied.Remove((3, "PERM-FOL-001")); }
    }

    [Fact]
    public async Task Form_c_register_masks_passport_and_visa_and_counts_the_unsubmitted()
    {
        var r = await Run(1, FormCRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"));
        Assert.Equal(ReportRowKind.Subtotal, r.Rows[0].Kind);
        Assert.Equal("To submit: 1", S(r.Rows[0], "nationality"));
        var d = Data(r);
        Assert.Equal(["German", "French"], d.Select(x => S(x, "nationality")));
        var fr = d[1];
        Assert.Equal("XXXX-XXXX-4567", S(fr, "passportNo"));
        Assert.Equal("••••7766 · Tourist", S(fr, "visa"));
        Assert.Equal("2022-01-10 · Paris", S(fr, "passportIssue"));
        Assert.Equal(true, fr.Cells["submitted"]);
        Assert.Equal(false, d[0].Cells["submitted"]);
        Assert.Equal("2 foreign stays", S(r.Rows[^1], "nationality"));
        Assert.Single(Data(await Run(1, FormCRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("submitted", "no"))));
        Assert.Single(Data(await Run(1, FormCRegisterReport.Id, ("from", "2026-10-18"), ("to", "2026-10-24"), ("submitted", "yes"))));
        Assert.Null(await Code(() => Run(3, FormCRegisterReport.Id)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(4, FormCRegisterReport.Id)));
    }

    [Fact]
    public async Task Rate_changes_show_old_new_guests_authoriser_and_the_net_change()
    {
        var r = await Run(1, RateChangesReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(2, d.Count);
        Assert.Equal("2026-10-23 15:30", S(d[0], "changedAt"));
        Assert.Equal(200000L, d[0].Cells["oldRate"]);
        Assert.Equal(180000L, d[0].Cells["newRate"]);
        Assert.Equal(1L, d[0].Cells["oldGuests"]);
        Assert.Equal(2L, d[0].Cells["newGuests"]);
        Assert.Equal("MGR", S(d[0], "changedBy"));
        Assert.Equal(S(d[1], "changedBy"), S(d[1], "authorisedBy"));
        var total = r.Rows[^1];
        Assert.Equal(-20000L + 30000L, (long)total.Cells["newRate"]!);
        Assert.Equal("Total · 2 changes", S(total, "changedAt"));
        Assert.Single(Data(await Run(1, RateChangesReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("room", "101"), ("changedBy", "mgr"))));
        Assert.Empty(Data(await Run(1, RateChangesReport.Id, ("from", "2026-10-20"), ("to", "2026-10-24"), ("room", "102"))));
        foreach (var refused in new[] { 3, 4, 7 }) Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(refused, RateChangesReport.Id)));
        Assert.Null(await Code(() => Run(5, RateChangesReport.Id)));
    }

    [Fact]
    public async Task Room_status_shows_every_room_counts_filters_and_hides_guest_names_without_the_reservation_right()
    {
        var r = await Run(1, RoomStatusReport.Id);
        var d = Data(r);
        Assert.Equal(["101", "102", "201", "202", "203"], d.Select(x => S(x, "room")));
        var one = d[0];
        Assert.Equal("Ananya Rao", S(one, "guest"));
        Assert.Equal("Occupied", S(one, "status"));
        Assert.Equal("Floor 1", S(one, "floor"));
        Assert.Equal("Classic", S(one, "type"));
        Assert.Equal("2026-10-23", S(one, "checkIn"));
        Assert.Equal("2026-10-26", S(one, "checkOut"));
        Assert.Equal("2026-10-24", S(d[1], "nextArrival"));
        Assert.Equal("Vacant", S(d[1], "status"));
        Assert.Equal("Out of order", S(d[4], "status"));
        Assert.Equal("Bathroom leak", S(d[4], "notes"));
        var tot = r.Rows[^1];
        Assert.Equal("5 rooms", S(tot, "room"));
        Assert.Equal("Occupied 3 · Vacant 1 · Out of order 1", S(tot, "status"));
        Assert.Equal("Check-ins today 1 · Check-outs today 1", S(tot, "notes"));
        Assert.Equal(["201"], Data(await Run(1, RoomStatusReport.Id, ("filter", "due out"))).Select(x => S(x, "room")));
        Assert.Equal(["202"], Data(await Run(1, RoomStatusReport.Id, ("filter", "dirty"))).Select(x => S(x, "room")));
        Assert.Equal(["203"], Data(await Run(1, RoomStatusReport.Id, ("filter", "out of order"))).Select(x => S(x, "room")));
        var hk = await Run(7, RoomStatusReport.Id, ("filter", "occupied"));
        Assert.Equal(3, Data(hk).Count);
        Assert.All(Data(hk), x => Assert.Equal("", S(x, "guest")));
        Assert.Null(await Code(() => Run(3, RoomStatusReport.Id)));
        Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(4, RoomStatusReport.Id)));
    }

    [Fact]
    public async Task Lodge_layouts_carry_the_letterhead_a_masked_id_and_only_allowed_money()
    {
        var card = await LodgeLayouts.RegistrationCardAsync(db.Platform, new Hcc.Application.Billing.Caller(db.PropertyId, 1), "HCC-1041", Ct);
        Assert.Contains(LodgeLayouts.Letterhead, card.Body);
        Assert.Contains("Guest registration card", card.Body);
        Assert.Contains("XXXX-XXXX-4101", card.Body);
        Assert.Contains("Test Hotel", card.Body);
        Assert.Equal(("Registration no", "HCC-1041"), card.Pairs[0]);
        Assert.Equal(("Room", "101"), card.Pairs[1]);
        Assert.DoesNotContain("#", card.Css.Replace("&#", ""));
        Assert.Equal(HccErrorCode.NOT_FOUND, await Code(() => LodgeLayouts.RegistrationCardAsync(db.Platform, new Hcc.Application.Billing.Caller(db.PropertyId, 1), "NOPE", Ct)));
        var withMoney = LodgeLayouts.DayEnd("Test Hotel", new DateOnly(2026, 10, 24), "2026-10-24T08:00", [("Arrivals", "2")], [("Net revenue", "Rs. 10.00")]);
        var without = LodgeLayouts.DayEnd("Test Hotel", new DateOnly(2026, 10, 24), "2026-10-24T08:00", [("Arrivals", "2")], null);
        Assert.Contains("B · Money", withMoney.Body);
        Assert.DoesNotContain("B · Money", without.Body);
        Assert.Equal("Day end report 2026-10-24", without.Title);
    }

    [Fact]
    public void City_of_an_address_and_the_visa_mask()
    {
        Assert.Equal("Bengaluru", GuestsSql_City("10 Demo Street, Bengaluru, Karnataka (fictional)"));
        Assert.Equal("Pune", GuestsSql_City("Pune, Maharashtra"));
        Assert.Equal("", GuestsSql_City(""));
    }

    private static string GuestsSql_City(string a) => (string)typeof(GuestRegisterReport).Assembly.GetType("Hcc.Reporting.Reports.Guests.GuestsSql")!.GetMethod("CityOfAddress")!.Invoke(null, [a])!;
}
