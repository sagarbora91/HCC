// F12-MEAS2-A · SQL round trip of the daily-money fixes on a fresh .\SQLEXPRESS HccTest_* database (Hcc.Inventory.Tests harness, as AnalyticsInputSqlTests):
// M5 flash ADR over charged nights, N3 rate re-posts in the allowances / voids report (written exactly as StayRateCommands writes them), N4 room discrepancy age.
// One fictional property of its own; business date 10 Oct 2026.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Inventory.Tests.Harness;
using Hcc.Reporting.Reports.MeasDaily;

namespace Hcc.Meas.Tests;

public sealed class MeasDailySqlFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public int P { get; private set; }

    public int Owner { get; private set; }

    private async Task<long> IdAsync(string sql) => Convert.ToInt64(await Db.ScalarAsync(sql), CultureInfo.InvariantCulture);

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        P = (int)await IdAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('M2A', N'Meas2 lane A test (fictional)', 'Asia/Kolkata', '2026-10-10');");
        async Task<int> User(string login, string name, string role)
        {
            var id = (int)await IdAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({P}, '{login}', N'{name}', SYSUTCDATETIME());");
            await Db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2020-01-01', SYSUTCDATETIME());");
            return id;
        }

        Owner = await User("m2a.owner", "Owner M2A", "OWN");
        var manager = await User("m2a.manager", "Manager M2A", "MGR");
        var type = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name) OUTPUT INSERTED.room_type_id VALUES ({P}, 'MDLX', 3, N'Deluxe');");
        var rooms = new List<long>();
        foreach (var no in new[] { "101", "102", "103", "104", "105" })
            rooms.Add(await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '{no}', {type});"));

        // four in-house stays 9 → 12 Oct in 101-104 (105 empty): folio, guest, stay, open segment; checked in 3 hours ago
        var folios = new List<long>();
        var stays = new List<(long Res, long Stay)>();
        for (var i = 0; i < 4; i++)
        {
            var res = await IdAsync($"""
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise)
                OUTPUT INSERTED.reservation_id VALUES ({P}, 'M2A-{i + 1}', N'Guest {i + 1} (fictional)', '2026-10-09', '2026-10-12', 'InHouse', {rooms[i]}, {type}, 2, 0, 200000);
                """);
            var g = await IdAsync($"INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({P}, N'Guest {i + 1} (fictional)', '98000100{i}0', N'Indian', 1, 'manual', SYSUTCDATETIME());");
            var f = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res});");
            var s = await IdAsync($"""
                INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
                OUTPUT INSERTED.stay_id VALUES ({P}, {res}, {g}, {f}, 'InHouse', '2026-10-09', '2026-10-12', '2026-10-09', DATEADD(HOUR, -3, SYSUTCDATETIME()), {Owner});
                """);
            await Db.ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({s}, 1, {rooms[i]}, '2026-10-09', NULL);");
            folios.Add(f);
            stays.Add((res, s));
        }

        async Task<long> Charge(long folio, string kind, string date, string label, long amount, long? reverses = null, long? approval = null, int? by = null) => await IdAsync($"""
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id, approval_id, principal_id, posted_utc)
            OUTPUT INSERTED.charge_id VALUES ({folio}, '{kind}', '{date}', N'{label}', {amount}, 0, 0, {N(reverses)}, {N(approval)}, {N(by)}, SYSUTCDATETIME());
            """);
        async Task<(long Rev, long New)> Repost(int i, string night, long oldRate, long newRate, long? approval, int changedBy, int? approvedBy, string reason)
        {
            // StayRateCommands.ApplyAsync: reversal kind 'correction' on the business date with "Reversal · rate change · <label>", new room line on the night
            var orig = await Charge(folios[i], "room", night, $"Room night · {night}", oldRate);
            var rev = await Charge(folios[i], "correction", "2026-10-10", $"Reversal · rate change · Room night · {night}", -oldRate, orig, approval, changedBy);
            await Db.ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {rev} WHERE charge_id = {orig};");
            var line = await Charge(folios[i], "room", night, $"Room night · {night}", newRate, null, approval, changedBy);
            await Db.ExecAsync($$"""
                INSERT gst.stay_rate_change(property_id, stay_id, reservation_id, room_id, business_date, changed_utc, changed_by, approval_id, approved_by, scope, reason,
                                            old_rate_paise, new_rate_paise, old_people, new_people, old_children, new_children, effective_from, reposted_json)
                VALUES ({{P}}, {{stays[i].Stay}}, {{stays[i].Res}}, {{rooms[i]}}, '2026-10-10', SYSUTCDATETIME(), {{changedBy}}, {{N(approval)}}, {{N(approvedBy)}}, 'all', N'{{reason}}',
                        {{oldRate}}, {{newRate}}, 2, 2, 0, 0, '{{night}}', N'[{"reversal":"{{rev}}","line":"{{line}}","night":"{{night}}"}]');
                """);
            return (rev, line);
        }

        // 101: a plain paid night
        await Charge(folios[0], "room", "2026-10-10", "Room night · 2026-10-10", 200000);
        // 102: re-posted 1,800 → 2,000 through an approved 'rate-change' request (Manager asks, Owner decides)
        var a1 = await IdAsync($"""
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc, executed_by, executed_utc)
            OUTPUT INSERTED.approval_id VALUES ({P}, 'rate-change', {folios[1]}, 'folio', {folios[1]}, 200000, N'Corporate rate agreed', 'Executed', {manager}, SYSUTCDATETIME(), DATEADD(DAY, 1, SYSUTCDATETIME()),
                                               {Owner}, SYSUTCDATETIME(), {Owner}, SYSUTCDATETIME());
            """);
        await Repost(1, "2026-10-10", 180000, 200000, a1, manager, Owner, "Corporate rate agreed");
        // 103: re-posted 2,200 → 2,000 directly by the Owner (no approval row)
        await Repost(2, "2026-10-10", 220000, 200000, null, Owner, null, "Owner guest rate");
        // 104: a zero-rate night tonight; yesterday re-posted directly by a Manager (no approval → red); a laundry line voided without an approval (red)
        await Charge(folios[3], "room", "2026-10-10", "Room night · 2026-10-10", 0);
        await Repost(3, "2026-10-09", 150000, 140000, null, manager, null, "Long stay");
        var laundry = await Charge(folios[3], "service", "2026-10-10", "Laundry", 5000);
        var vrev = await Charge(folios[3], "correction", "2026-10-10", "Reversal · Laundry · guest dispute", -5000, laundry);
        await Db.ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {vrev} WHERE charge_id = {laundry};");
        // N4: room 104 marked out of order 61 minutes ago (no block); the stay was checked in 3 hours ago
        await Db.ExecAsync($"INSERT meas.room_offline(property_id, room_id, reason, set_by, set_utc) VALUES ({P}, {rooms[3]}, 'OOO', {Owner}, DATEADD(MINUTE, -61, SYSUTCDATETIME()));");
    }

    private static string N(long? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : "NULL";

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

public sealed class MeasDailySqlTests(MeasDailySqlFixture f) : IClassFixture<MeasDailySqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Bd = new(2026, 10, 10);

    private Caller Owner => new(f.P, f.Owner);

    [Fact]
    public async Task Meas2A_M5_flash_adr_divides_by_charged_nights_and_a_reposted_night_counts_once()
    {
        var r = await new ManagerFlashReport(f.Db.Platform).SelectAsync(Owner, new BoundParams(new Dictionary<string, string>(), Bd, Bd), Ct);
        var occ = r.Rows.Single(x => (string?)x.Cells["kpi"] == "Occupancy %");
        var adr = r.Rows.Single(x => (string?)x.Cells["kpi"] == "ADR");
        var revpar = r.Rows.Single(x => (string?)x.Cells["kpi"] == "RevPAR");
        Assert.Equal(80.0, occ.Cells["pctActual"]);                                  // 4 slept / 5 in service (the zero-rate night is still slept)
        Assert.Equal(200000L, adr.Cells["amtActual"]);                                // 6,000 / 3 charged nights (not / 4, and the re-posts count the new line only)
        Assert.Equal("", adr.Cells["exception"]);                                     // / 4 = 1,500 would be red against the 2,050 default target
        Assert.Equal(120000L, revpar.Cells["amtActual"]);                             // 6,000 / 5 rooms in service
        Assert.Null(adr.Cells["amtLast"]);                                            // 10 Oct 2025 (same calendar date) has no charged night
    }

    [Fact]
    public async Task Meas2A_N3_rate_reposts_are_their_own_kind_with_the_change_approver_and_only_unapproved_entries_are_red()
    {
        var r = await new AllowancesVoidsReport(f.Db.Platform).SelectAsync(Owner, new BoundParams(new Dictionary<string, string>(), Bd, Bd), Ct);
        var rate = r.Rows.Where(x => (string?)x.Cells["kind"] == "rate-change").ToDictionary(x => (string)x.Cells["bill"]!);
        Assert.Equal(3, rate.Count);
        Assert.Equal(("Corporate rate agreed", "Manager M2A", "Owner M2A", "", 180000L),
            ((string?)rate["M2A-2"].Cells["reasonCode"], (string?)rate["M2A-2"].Cells["user"], (string?)rate["M2A-2"].Cells["approver"], (string?)rate["M2A-2"].Cells["exception"], (long?)rate["M2A-2"].Cells["amount"]));
        Assert.Equal(("Owner guest rate", "Owner M2A", "Owner M2A", ""),
            ((string?)rate["M2A-3"].Cells["reasonCode"], (string?)rate["M2A-3"].Cells["user"], (string?)rate["M2A-3"].Cells["approver"], (string?)rate["M2A-3"].Cells["exception"]));
        Assert.Equal(("Long stay", "Manager M2A", "", "No approver"),
            ((string?)rate["M2A-4"].Cells["reasonCode"], (string?)rate["M2A-4"].Cells["user"], (string?)rate["M2A-4"].Cells["approver"], (string?)rate["M2A-4"].Cells["exception"]));
        var v = Assert.Single(r.Rows, x => (string?)x.Cells["kind"] == "void");
        Assert.Equal(("M2A-4", 5000L, "No approver"), ((string?)v.Cells["bill"], (long?)v.Cells["amount"], (string?)v.Cells["exception"]));
        Assert.DoesNotContain(r.Rows, x => (string?)x.Cells["kind"] == "correction");   // no re-post reversal is a correction any more

        var only = await new AllowancesVoidsReport(f.Db.Platform).SelectAsync(Owner, new BoundParams(new Dictionary<string, string> { ["kind"] = "rate-change" }, Bd, Bd), Ct);
        Assert.Equal(3, only.Rows.Count);
    }

    [Fact]
    public async Task Meas2A_N4_offline_set_an_hour_ago_is_one_hour_old_in_local_time_and_not_red()
    {
        var r = await new RoomDiscrepancyReport(f.Db.Platform).SelectAsync(Owner, new BoundParams(new Dictionary<string, string>()), Ct);
        var row = Assert.Single(r.Rows);
        Assert.Equal(("104", "OOO-with-stay", 1L, ""), ((string?)row.Cells["room"], (string?)row.Cells["kind"], (long?)row.Cells["ageHours"], (string?)row.Cells["exception"]));
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var since = DateTime.ParseExact((string)row.Cells["since"]!, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
        Assert.InRange((localNow - since).TotalMinutes, 60, 63);                       // local, not UTC (UTC would be 5 h 30 min off)
    }
}
