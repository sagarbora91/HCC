// F12-MEAS2-F · one fresh .\SQLEXPRESS HccTest_* database (Hcc.Inventory.Tests harness, every migration applied) with ONE fictional property
// ("Meas smoke", business date Thu 5 Nov 2026, Asia/Kolkata) seeded for all 32 measurement reports: rooms, stays (three re-posted nights written exactly as
// StayRateCommands.ApplyAsync writes them), folio lines, receipts, a cash shift, hk_task + meas.task_times, room_block + meas.room_offline, meters, stock,
// maintenance, hr staff / attendance / roster, and the meas.* captures. Nothing here is real data. The expected figures live in MeasReportSqlTests.
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Yield;
using Hcc.Infrastructure.SqlServer.Yield;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Meas.Tests.Reports;

/// <summary>Allows everything: the smoke test calls the selectors as the Owner (the pulse and the scorecard ask PERM-HR-002).</summary>
public sealed class OwnerPolicy : IAccessPolicy
{
    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(true);
}

public sealed class MeasReportSqlFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public int P { get; private set; }

    public int Owner { get; private set; }

    public int Manager { get; private set; }

    public int Clerk { get; private set; }

    public IYieldStore Yield => new SqlYieldStore(Db.Platform.Connections);

    private async Task<long> IdAsync(string sql) => Convert.ToInt64(await Db.ScalarAsync(sql), CultureInfo.InvariantCulture);

    private Task ExecAsync(string sql) => Db.ExecAsync(sql);

    private static string N(long? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : "NULL";

    private static string Q(string? v) => v is null ? "NULL" : "'" + v + "'";

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        P = (int)await IdAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('SMK', N'Meas smoke (fictional)', 'Asia/Kolkata', '2026-11-05');");

        async Task<int> User(string login, string name, string role)
        {
            var id = (int)await IdAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({P}, '{login}', N'{name}', '2026-01-01');");
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2020-01-01', '2026-01-01');");
            return id;
        }

        Owner = await User("sm.owner", "Owner SM", "OWN");
        Manager = await User("sm.mgr", "Manager SM", "MGR");
        Clerk = await User("sm.clerk", "Clerk SM", "REC");
        var asha = await User("sm.asha", "Asha SM", "HKP");
        var bina = await User("sm.bina", "Bina SM", "HKP");

        // ── rooms: 5 Deluxe (101-105) + 2 Suite (201-202); the largest type is Deluxe ──
        var dlx = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise) OUTPUT INSERTED.room_type_id VALUES ({P}, 'DLX', 3, N'Deluxe', 250000);");
        var ste = await IdAsync($"INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise) OUTPUT INSERTED.room_type_id VALUES ({P}, 'STE', 4, N'Suite', 400000);");
        var room = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var no in new[] { "101", "102", "103", "104", "105" })
            room[no] = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '{no}', {dlx});");
        foreach (var no in new[] { "201", "202" })
            room[no] = await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id VALUES ({P}, '{no}', {ste});");
        await ExecAsync($"INSERT yld.owned_tariff(room_type_id, effective_from, rate_paise) VALUES ({dlx}, '2026-01-01', 250000), ({ste}, '2026-01-01', 400000);");
        var company = await IdAsync($"INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) OUTPUT INSERTED.company_id VALUES ({P}, N'Acme Fictional Ltd', 300000, {Owner}, '2026-01-01');");

        // ── helpers ──
        async Task<long> Guest(string name, string phone) => await IdAsync($"INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({P}, N'{name}', '{phone}', N'Indian', 1, 'manual', '2026-01-01');");

        async Task<(long Res, long Folio, long Stay)> Book(string no, string name, long? guest, string? roomNo, long type, string arr, string dep, string status, string source, long rate,
            string created = "2026-10-20T06:00:00", long? companyId = null, string? inUtc = null, int? inBy = null, string? outUtc = null, int? outBy = null)
        {
            var rid = roomNo is null ? null : (long?)room[roomNo];
            var res = await IdAsync($"""
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise, source, company_id, created_utc)
                OUTPUT INSERTED.reservation_id VALUES ({P}, '{no}', N'{name}', '{arr}', '{dep}', '{status}', {N(rid)}, {type}, 2, 0, {rate}, N'{source}', {N(companyId)}, '{created}');
                """);
            var folio = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res});");
            long stay = 0;
            if (guest is { } g && inUtc is not null)
            {
                stay = await IdAsync($"""
                    INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
                    OUTPUT INSERTED.stay_id VALUES ({P}, {res}, {g}, {folio}, '{(outUtc is null ? "InHouse" : "CheckedOut")}', '{arr}', '{dep}', '{arr}', '{inUtc}', {inBy}, {(outUtc is null ? "NULL" : "'" + dep + "'")}, {Q(outUtc)}, {N(outBy)});
                    """);
                if (rid is not null) await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({stay}, 1, {rid}, '{arr}', {(outUtc is null ? "NULL" : "'" + dep + "'")});");
            }

            if (rid is not null && status is "Confirmed" or "InHouse" or "Departed")
                for (var d = DateOnly.Parse(arr, CultureInfo.InvariantCulture); d < DateOnly.Parse(dep, CultureInfo.InvariantCulture); d = d.AddDays(1))
                    await ExecAsync($"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({rid}, '{d:yyyy-MM-dd}', {res});");
            return (res, folio, stay);
        }

        async Task<long> Charge(long folio, string kind, string date, string label, long amount, long tax = 0, int bps = 0, long? reverses = null, long? approval = null, int? by = null, string? posted = null)
        {
            var id = await IdAsync($"""
                INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id, approval_id, principal_id, posted_utc)
                OUTPUT INSERTED.charge_id VALUES ({folio}, '{kind}', '{date}', N'{label}', {amount}, {bps}, {tax}, {N(reverses)}, {N(approval)}, {N(by)}, '{posted ?? date + "T06:35:00"}');
                """);
            if (reverses is { } r) await ExecAsync($"UPDATE bill.charge SET reversed_by_charge_id = {id} WHERE charge_id = {r};");
            return id;
        }

        // StayRateCommands.ApplyAsync: the original room line, a reversal of kind 'correction' dated the business date ("Reversal · rate change · <label>") with
        // reverses_charge_id (the original is marked reversed_by), then the new room line on the night; gst.stay_rate_change lists both ids in reposted_json.
        async Task Repost(long folio, long res, long stay, string roomNo, string night, string bd, long oldRate, long newRate, int bps, long? approval, int changedBy, int? approvedBy, string reason, string changedUtc, long tariff)
        {
            long Tax(long a) => a * bps / 10000;
            var rev = await Charge(folio, "correction", bd, $"Reversal · rate change · Room night · {night}", -oldRate, -Tax(oldRate), bps, await OriginalAsync(folio, night), approval, changedBy);
            var line = await Charge(folio, "room", night, $"Room night · {night}", newRate, Tax(newRate), bps, null, approval, changedBy);
            await ExecAsync($$"""
                INSERT gst.stay_rate_change(property_id, stay_id, reservation_id, room_id, business_date, changed_utc, changed_by, approval_id, approved_by, scope, reason,
                                            old_rate_paise, new_rate_paise, old_people, new_people, old_children, new_children, effective_from, reposted_json, tariff_paise)
                VALUES ({{P}}, {{stay}}, {{res}}, {{room[roomNo]}}, '{{bd}}', '{{changedUtc}}', {{changedBy}}, {{N(approval)}}, {{N(approvedBy)}}, 'all', N'{{reason}}',
                        {{oldRate}}, {{newRate}}, 2, 2, 0, 0, '{{night}}', N'[{"reversal":"{{rev}}","line":"{{line}}","night":"{{night}}"}]', {{tariff}});
                """);
        }

        async Task<long> OriginalAsync(long folio, string night) => await IdAsync($"SELECT TOP (1) charge_id FROM bill.charge WHERE folio_id = {folio} AND kind = 'room' AND service_date = '{night}' AND reversed_by_charge_id IS NULL AND reverses_charge_id IS NULL ORDER BY charge_id;");

        async Task<long> Approval(string action, long folio, long amount, string reason, int requestedBy, int decidedBy, int? executedBy, string utc) => await IdAsync($"""
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc, executed_by, executed_utc)
            OUTPUT INSERTED.approval_id VALUES ({P}, '{action}', {folio}, 'folio', {folio}, {amount}, N'{reason}', 'Executed', {requestedBy}, '{utc}', DATEADD(DAY, 1, '{utc}'), {decidedBy}, '{utc}', {N(executedBy)}, '{utc}');
            """);

        // ── guests and stays ──
        var gA = await Guest("Guest A (fictional)", "9811100001");
        var gB = await Guest("Guest B (fictional)", "9811100002");
        var gC = await Guest("Guest C (fictional)", "9811100003");
        var gD = await Guest("Guest D (fictional)", "9811100004");

        // history of guest A (three completed stays make the 3+ loyalty list): September and October, no lines
        await Book("SM-H1", "Guest A (fictional)", gA, null, dlx, "2026-09-10", "2026-09-11", "Departed", "Direct", 250000, "2026-09-01T06:00:00", null, "2026-09-10T06:30:00", Clerk, "2026-09-11T05:00:00", Clerk);
        await Book("SM-H2", "Guest A (fictional)", gA, null, dlx, "2026-10-05", "2026-10-06", "Departed", "Direct", 250000, "2026-10-01T06:00:00", null, "2026-10-05T06:30:00", Clerk, "2026-10-06T05:00:00", Clerk);

        // S1 · SM-1 · Guest A · 101 · 1-3 Nov departed via OTA; night 2 re-posted 2,500 → 2,000 by the Owner on the 2nd; extras; paid in full
        var s1 = await Book("SM-1", "Guest A (fictional)", gA, "101", dlx, "2026-11-01", "2026-11-03", "Departed", "OTA · Booking.com", 250000, "2026-10-20T06:00:00", null, "2026-11-01T06:30:00", Clerk, "2026-11-03T05:00:00", Clerk);
        await Charge(s1.Folio, "room", "2026-11-01", "Room night · 2026-11-01", 250000, 30000, 1200);
        await Charge(s1.Folio, "room", "2026-11-02", "Room night · 2026-11-02", 250000, 30000, 1200);
        await Repost(s1.Folio, s1.Res, s1.Stay, "101", "2026-11-02", "2026-11-02", 250000, 200000, 1200, null, Owner, null, "Loyalty rate", "2026-11-02T07:00:00", 250000);
        await Charge(s1.Folio, "service", "2026-11-02", "Extra bed", 30000);
        await Charge(s1.Folio, "service", "2026-11-02", "Breakfast add-on", 20000);
        await Charge(s1.Folio, "service", "2026-11-03", "LCO1 · Late check-out", 50000, posted: "2026-11-03T04:40:00");
        await ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date, recorded_utc) VALUES ({s1.Folio}, 'payment', N'Cash', 604000, '2026-11-03', '2026-11-03T04:50:00');");
        await ExecAsync($"""
            INSERT ops.key_job(job_id, property_id, kind, event_type, reservation_id, room_id, payload_json, requested_by, requested_utc, state, timeout_utc, finished_utc)
            VALUES (NEWID(), {P}, 'issue_guest', 'issued', {s1.Res}, {room["101"]}, N'[]', {Clerk}, '2026-11-01T06:30:30', 'done', '2026-11-01T06:40:00', '2026-11-01T06:34:00');
            """);

        // S2 · SM-2 · Guest B · 102 · in house 4-7 Nov, walk-in: two nights, dinner, a paid upgrade, a laundry line voided without an approver; nothing paid
        var s2 = await Book("SM-2", "Guest B (fictional)", gB, "102", dlx, "2026-11-04", "2026-11-07", "InHouse", "Direct", 200000, "2026-10-20T06:00:00", null, "2026-11-04T06:30:00", Clerk);
        await Charge(s2.Folio, "room", "2026-11-04", "Room night · 2026-11-04", 200000);
        await Charge(s2.Folio, "room", "2026-11-05", "Room night · 2026-11-05", 200000);
        await Charge(s2.Folio, "food", "2026-11-05", "Restaurant dinner", 150000);
        await Charge(s2.Folio, "service", "2026-11-05", "UPG · Deluxe to Suite", 100000);
        var laundry = await Charge(s2.Folio, "service", "2026-11-05", "Laundry", 5000);
        await Charge(s2.Folio, "correction", "2026-11-05", "Reversal · Laundry · guest dispute", -5000, reverses: laundry);
        await ExecAsync($"INSERT meas.booking_extra(property_id, reservation_id, booked_type, upgrade, changed_by) VALUES ({P}, {s2.Res}, N'Deluxe', 'paid', {Clerk});");

        // S3 · SM-3 · Guest C · 103 · company account (limit 3,000, deposit 1,000) · in house 3-6 Nov; night 4 re-posted 2,200 → 2,000 through an approved request
        var s3 = await Book("SM-3", "Guest C (fictional)", gC, "103", dlx, "2026-11-03", "2026-11-06", "InHouse", "Corporate", 200000, "2026-10-20T06:00:00", company, "2026-11-03T07:10:00", Clerk);
        await Charge(s3.Folio, "room", "2026-11-03", "Room night · 2026-11-03", 200000);
        await Charge(s3.Folio, "room", "2026-11-04", "Room night · 2026-11-04", 220000);
        var aRate = await Approval("rate-change", s3.Folio, 200000, "Corporate rate agreed", Manager, Owner, Owner, "2026-11-05T06:00:00");
        await Repost(s3.Folio, s3.Res, s3.Stay, "103", "2026-11-04", "2026-11-05", 220000, 200000, 0, aRate, Manager, Owner, "Corporate rate agreed", "2026-11-05T06:00:00", 250000);
        await Charge(s3.Folio, "room", "2026-11-05", "Room night · 2026-11-05", 200000);
        var disc = await Charge(s3.Folio, "discount", "2026-11-05", "Discount · loyalty", -20000);
        var aDisc = await Approval("discount", s3.Folio, 20000, "loyalty", Clerk, Manager, Manager, "2026-11-05T07:00:00");
        await ExecAsync($"UPDATE bill.approval SET result_id = {disc} WHERE approval_id = {aDisc};");
        await ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date, recorded_utc) VALUES ({s3.Folio}, 'deposit', N'Cash', 100000, '2026-11-03', '2026-11-03T07:20:00');");
        await ExecAsync($"INSERT meas.booking_extra(property_id, reservation_id, booked_type, upgrade, upgrade_approver, changed_by) VALUES ({P}, {s3.Res}, N'Deluxe', 'free', {Manager}, {Clerk});");

        // S4 · SM-4 · Guest D · 104 · in house 5-7 Nov; night 1 re-posted 2,500 → 2,000 directly by the Owner; room 104 is marked out of order (N4)
        var s4 = await Book("SM-4", "Guest D (fictional)", gD, "104", dlx, "2026-11-05", "2026-11-07", "InHouse", "Direct", 250000, "2026-10-20T06:00:00", null, "2026-11-05T05:00:00", Clerk);
        await Charge(s4.Folio, "room", "2026-11-05", "Room night · 2026-11-05", 250000);
        await Repost(s4.Folio, s4.Res, s4.Stay, "104", "2026-11-05", "2026-11-05", 250000, 200000, 0, null, Owner, null, "Owner guest rate", "2026-11-05T08:00:00", 250000);
        await ExecAsync($"INSERT meas.room_offline(property_id, room_id, reason, set_by, set_utc) VALUES ({P}, {room["104"]}, 'OOO', {Owner}, '2026-11-05T08:30:00');");

        // S5 · confirmed for tonight in Suite 201 (due at noon: sold-but-vacant); S6 no-show; S7 cancelled with a retention waived without an approver
        var s5 = await Book("SM-5", "Guest E (fictional)", null, "201", ste, "2026-11-05", "2026-11-06", "Confirmed", "Direct", 400000);
        await Book("SM-6", "Guest F (fictional)", null, null, dlx, "2026-11-02", "2026-11-03", "NoShow", "OTA · Agoda", 250000);
        var s7 = await Book("SM-7", "Guest G (fictional)", null, null, dlx, "2026-11-03", "2026-11-04", "Cancelled", "Direct", 250000);
        await Charge(s7.Folio, "service", "2026-11-03", "Cancellation retention", 100000);
        await Charge(s7.Folio, "discount", "2026-11-03", "Retention waived · goodwill", -100000);

        // future bookings for the revenue reports
        await Book("SM-F1", "Guest H (fictional)", null, "101", dlx, "2026-11-10", "2026-11-13", "Confirmed", "OTA · MakeMyTrip", 230000);
        await Book("SM-F2", "Guest I (fictional)", null, "201", ste, "2026-11-20", "2026-11-22", "Confirmed", "Direct", 400000, "2026-11-01T06:00:00");
        await Book("SM-F4", "Guest J (fictional)", null, "202", ste, "2026-12-04", "2026-12-07", "Confirmed", "Direct", 400000, "2026-11-02T06:00:00");

        // a refund executed on the 5th for guest A's folio (requested by the clerk, decided by the manager, executed by the owner)
        await Approval("refund", s1.Folio, 30000, "Early checkout refund", Clerk, Manager, Owner, "2026-11-05T05:00:00");

        // service recovery: two cleanliness complaints for room 102 (the second one repeats the first and carries an approved compensation)
        await ExecAsync($"INSERT gsv.complaint_category(property_id, code, name, sort_order, department, sla_hours, updated_by, updated_utc) VALUES ({P}, 'HK-CLEAN', N'Cleanliness', 1, 'Housekeeping', 4, {Owner}, '2026-01-01');");
        await ExecAsync($"INSERT meas.root_cause(property_id, code, label) VALUES ({P}, 'LINEN', N'Linen not changed');");
        var c1 = await IdAsync($"""
            INSERT ops.guest_complaint(property_id, stay_id, room_id, guest_name, category, severity, description, owner_id, status, created_utc, resolved_utc, created_by, category_code)
            OUTPUT INSERTED.complaint_id VALUES ({P}, {s2.Stay}, {room["102"]}, N'Guest B (fictional)', N'Cleanliness', 'Medium', N'Dusty room', {Manager}, 'Resolved', '2026-11-04T05:00:00', '2026-11-04T08:00:00', {Clerk}, 'HK-CLEAN');
            """);
        var aRec = await Approval("credit", s2.Folio, 50000, "Service recovery", Clerk, Manager, Manager, "2026-11-05T09:00:00");
        var c2 = await IdAsync($"""
            INSERT ops.guest_complaint(property_id, stay_id, room_id, guest_name, category, severity, description, owner_id, status, created_utc, resolved_utc, created_by, category_code,
                                       recovery_state, recovery_amount_paise, recovery_folio_id, recovery_approval_id)
            OUTPUT INSERTED.complaint_id VALUES ({P}, {s2.Stay}, {room["102"]}, N'Guest B (fictional)', N'Cleanliness', 'High', N'Dusty again', {Manager}, 'Resolved', '2026-11-05T04:00:00', '2026-11-05T11:00:00', {Clerk}, 'HK-CLEAN',
                    'Recorded', 50000, {s2.Folio}, {aRec});
            """);
        await ExecAsync($"INSERT meas.root_cause_set(property_id, complaint_id, code, set_by) VALUES ({P}, '{c1}', 'LINEN', {Manager}), ({P}, '{c2}', 'LINEN', {Manager});");

        // loyalty tag and an offer sent to guest A (repeat-loyalty)
        await ExecAsync($"INSERT meas.customer_preference(property_id, customer_key, loyalty_tag, changed_by) VALUES ({P}, N'9811100001', N'Gold', {Manager});");
        await ExecAsync($"""
            INSERT msg.message(property_id, reservation_id, guest_id, template_code, channel, recipient_masked, body, queued_utc, outcome, created_utc)
            VALUES ({P}, {s1.Res}, {gA}, NULL, 'SMS', N'98XXXXXX01', N'Offer for a loyal guest (fictional)', '2026-11-04T05:00:00', 'Delivered', '2026-11-04T05:00:00');
            """);

        // ── cash: one till, three closed day shifts (variance 0, -50.00, -200.00) ──
        var till = await IdAsync($"INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES ({P}, 'FD1', N'Front desk', {Owner}, '2026-01-01');");
        async Task Shift(string bd, long expected, long counted) => await ExecAsync($"""
            INSERT pay.shift(till_id, business_date, status, opening_float_paise, opened_by, opened_utc, expected_paise, counted_paise, submitted_by, submitted_utc, closed_by, closed_utc)
            VALUES ({till}, '{bd}', 'Closed', 500000, {Clerk}, '{bd}T02:00:00', {expected}, {counted}, {Clerk}, '{bd}T16:00:00', {Manager}, '{bd}T16:30:00');
            """);
        await Shift("2026-11-03", 1000000, 1000000);
        await Shift("2026-11-04", 800000, 795000);
        await Shift("2026-11-05", 900000, 880000);

        // ── desk captures ──
        async Task Turn(string id, string reason, string arrival, string bd, long? rate) => await ExecAsync($"""
            INSERT meas.turnaway(property_id, client_id, reason, arrival, nights, room_type, rooms, quoted_rate_paise, channel, lead_days, business_date, recorded_utc, recorded_by)
            VALUES ({P}, N'{id}', '{reason}', '{arrival}', 1, N'Deluxe', 1, {N(rate)}, N'Phone', 7, '{bd}', '{bd}T08:00:00', {Clerk});
            """);
        await Turn("t1", "price", "2026-11-12", "2026-11-02", 250000);
        await Turn("t2", "price", "2026-11-12", "2026-11-03", 250000);
        await Turn("t3", "price", "2026-11-12", "2026-11-04", 250000);
        await Turn("t4", "price", "2026-11-12", "2026-11-05", 250000);
        await Turn("t5", "full", "2026-11-08", "2026-11-05", null);
        await ExecAsync($"INSERT meas.walked(property_id, client_id, reservation_id, night, reason, cost_paise, approved_by, recorded_by) VALUES ({P}, N'w1', {s5.Res}, '2026-11-05', N'Oversold (fictional)', 150000, {Manager}, {Clerk});");

        // surveys of November: six with consent (one QR survey with every score at 2, older than 48 hours, no complaint on a booking → unanswered)
        async Task Survey(string id, string at, string via, int overall, int clean, int staff, int value, int? breakfast, int recommend, string? comment, int? by) => await ExecAsync($"""
            INSERT meas.survey(property_id, client_id, at_utc, via, reservation_id, room_type, channel, overall, cleanliness, staff, value_score, breakfast, recommend, comment, consent, entered_by)
            VALUES ({P}, '{id}', '{at}', '{via}', NULL, 'DLX', 'Direct', {overall}, {clean}, {staff}, {value}, {N(breakfast)}, {recommend}, {(comment is null ? "NULL" : "N'" + comment + "'")}, 1, {N(by)});
            """);
        await Survey("sv1", "2026-11-01T08:00:00", "desk", 5, 5, 5, 4, 5, 10, "Lovely stay", Clerk);
        await Survey("sv2", "2026-11-02T08:00:00", "qr", 4, 4, 5, 4, 4, 9, null, null);
        await Survey("sv3", "2026-11-03T08:00:00", "qr", 5, 4, 4, 5, null, 9, "Quiet rooms", null);
        await Survey("sv4", "2026-11-03T09:00:00", "qr", 3, 3, 3, 3, null, 7, null, null);
        await Survey("sv5", "2026-11-04T08:00:00", "qr", 5, 5, 5, 5, 5, 10, null, null);
        await Survey("sv6", "2026-11-02T10:00:00", "qr", 2, 2, 2, 2, null, 3, "Room was not ready", null);
        await ExecAsync($"""
            INSERT meas.review_month(property_id, month, platform, review_count, avg_score, score_12m, responded, themes) VALUES
              ({P}, '2026-10', 'google', 10, 4.20, 4.30, 8, N'clean'), ({P}, '2026-11', 'google', 12, 4.50, 4.40, 11, N'staff'), ({P}, '2026-11', 'booking', 4, 3.50, 3.80, 1, N'noise');
            """);

        // ── housekeeping: three tasks for Asha (the re-let in 201 fails inspection), one for Bina ──
        async Task<long> Task_(long roomId, string kind, int by, string bd, string created) => await IdAsync($"""
            INSERT ops.hk_task(property_id, room_id, kind, status, assignee_id, business_date, created_utc)
            OUTPUT INSERTED.task_id VALUES ({P}, {roomId}, '{kind}', 'Done', {by}, '{bd}', '{created}');
            """);
        async Task Times(long task, string started, string finished, string inspected, string result, string? reject) => await ExecAsync($"""
            INSERT meas.task_times(property_id, task_no, attendant_id, started_utc, finished_utc, inspected_utc, inspect_result, reject_reason)
            VALUES ({P}, '{task}', NULL, '{started}', '{finished}', '{inspected}', '{result}', {(reject is null ? "NULL" : "N'" + reject + "'")});
            """);
        var t1 = await Task_(room["101"], "checkout", asha, "2026-11-03", "2026-11-03T05:00:00");
        await Times(t1, "2026-11-03T05:10:00", "2026-11-03T05:35:00", "2026-11-03T05:50:00", "pass", null);
        var t2 = await Task_(room["102"], "stayover", asha, "2026-11-05", "2026-11-05T03:00:00");
        await Times(t2, "2026-11-05T05:00:00", "2026-11-05T05:20:00", "2026-11-05T05:30:00", "pass", null);
        var t3 = await Task_(room["201"], "checkout", asha, "2026-11-05", "2026-11-05T02:30:00");
        await Times(t3, "2026-11-05T02:40:00", "2026-11-05T03:30:00", "2026-11-05T04:00:00", "fail", "Dusty");
        var t4 = await Task_(room["103"], "stayover", bina, "2026-11-04", "2026-11-04T08:00:00");
        await Times(t4, "2026-11-04T09:00:00", "2026-11-04T09:30:00", "2026-11-04T10:00:00", "pass", null);

        // ── blocks and offline reasons: 105 blocked 25 Oct - 12 Nov (maintenance, no work order); 104 offline only (see S4) ──
        await ExecAsync($"INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES ({P}, {room["105"]}, '2026-10-25', '2026-11-12', N'AC replacement', 'maintenance', {Manager}, '2026-10-25T06:00:00');");
        await ExecAsync($"INSERT meas.room_offline(property_id, room_id, reason, expected_return, set_by, set_utc) VALUES ({P}, {room["105"]}, 'OOO', '2026-11-12', {Manager}, '2026-10-25T06:00:00');");

        // ── maintenance: one AC with a captured life, a preventive task overdue 11 days and three corrective tasks in 62 days ──
        var ac = await IdAsync($"INSERT mnt.asset(property_id, name, kind, room_id, installed_on, status) OUTPUT INSERTED.asset_id VALUES ({P}, N'AC 102', 'AC', {room["102"]}, '2020-01-01', 'Working');");
        await ExecAsync($"INSERT meas.asset_life(property_id, asset_id, expected_life_months, condition_grade, replacement_cost_paise) VALUES ({P}, '{ac}', 60, 'C', 4500000);");
        async Task Wo(string kind, string priority, string status, string created, string? done, string? due) => await ExecAsync($"""
            INSERT mnt.work_order(property_id, kind, title, priority, status, room_id, asset_id, created_utc, created_by, done_utc, due_date)
            VALUES ({P}, '{kind}', N'AC service (fictional)', '{priority}', '{status}', {room["102"]}, {ac}, '{created}', {Manager}, {Q(done)}, {Q(due)});
            """);
        await Wo("Preventive", "P3", "Open", "2026-10-01T04:00:00", null, "2026-10-25");
        await Wo("Corrective", "P1", "Done", "2026-11-02T04:00:00", "2026-11-02T08:00:00", null);
        await Wo("Corrective", "P2", "Done", "2026-10-20T04:00:00", "2026-10-20T10:00:00", null);
        await Wo("Corrective", "P3", "Done", "2026-09-01T04:00:00", "2026-09-01T07:00:00", null);

        // ── register assets (FY 2026-27 capex 600.00; the previous FY pump is not capex) ──
        await ExecAsync($"INSERT acc.ledger(property_id, code, name, group_id, is_system) VALUES ({P}, '1510', N'Furniture (Owner name)', 'fixed-assets', 1);");
        async Task Asset(string no, string ledger, string name, long cost, string date)
        {
            var e = await IdAsync($"""
                INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc)
                OUTPUT INSERTED.expense_id VALUES ({P}, N'Vendor (fictional)', N'Assets', {cost}, 'Cash', '{date}', 'Draft', {Owner}, '{date}');
                """);
            await ExecAsync($"""
                INSERT acc.fixed_asset(property_id, asset_no, ledger, name, cost_paise, purchase_date, vendor, expense_id, created_by, created_utc)
                VALUES ({P}, '{no}', '{ledger}', N'{name}', {cost}, '{date}', N'Vendor (fictional)', {e}, {Owner}, '{date}');
                """);
        }

        await Asset("FA-2627-001", "1510", "Sofa set", 60000, "2026-06-10");
        await Asset("FA-2526-001", "1520", "Water pump", 25000, "2025-12-01");

        // ── utilities: electricity 100,110,120,120,130 kWh on 1-5 Nov (and 100 kWh on 15 Oct on a 10-room night), water 2.5 kL on the 5th, a posted Utilities expense ──
        await ExecAsync($"INSERT meas.meter(property_id, meter_id, name, kind, unit) VALUES ({P}, 'EM1', N'Main electricity', 'electricity', 'kWh'), ({P}, 'WM1', N'Main water', 'water', 'kL');");
        async Task Reading(string meter, string date, decimal value) => await ExecAsync($"INSERT meas.meter_reading(property_id, meter_id, reading_date, value, entered_by) VALUES ({P}, '{meter}', '{date}', {value.ToString(CultureInfo.InvariantCulture)}, {Clerk});");
        foreach (var (d, v) in new[] { ("2026-10-14", 900m), ("2026-10-15", 1000m), ("2026-11-01", 1100m), ("2026-11-02", 1210m), ("2026-11-03", 1330m), ("2026-11-04", 1450m), ("2026-11-05", 1580m) })
            await Reading("EM1", d, v);
        await Reading("WM1", "2026-11-04", 50m);
        await Reading("WM1", "2026-11-05", 52.5m);
        await ExecAsync($"INSERT stk.occupancy_history(property_id, night, rooms) VALUES ({P}, '2026-10-15', 10);");
        await ExecAsync($"""
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc, decided_by, decided_utc)
            VALUES ({P}, N'Power company (fictional)', N'Utilities', 580000, 'Bank', '2026-11-05', 'Posted', {Clerk}, '2026-11-05', {Manager}, '2026-11-05');
            """);

        // ── linen and amenities: par = (4 per Deluxe x 5 + 6 per Suite x 2) x 3 sets = 96 towels ──
        var towel = await IdAsync($"INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, laundry_rate_paise, created_utc) OUTPUT INSERTED.item_id VALUES ({P}, 'TWL', N'Bath towel', 'linen', 'pc', 40000, 10, 20, 1500, '2026-01-01');");
        var soap = await IdAsync($"INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, created_utc) OUTPUT INSERTED.item_id VALUES ({P}, 'SOP', N'Soap', 'amenity', 'pc', 500, 10, 20, '2026-01-01');");
        await ExecAsync($"INSERT stk.item_par(item_id, room_type_id, pieces) VALUES ({towel}, {dlx}, 4), ({towel}, {ste}, 6);");
        await ExecAsync($"INSERT stk.stock(item_id, loc, qty) VALUES ({towel}, 'store', 80), ({soap}, 'store', 200);");
        await ExecAsync($"""
            INSERT stk.loss(property_id, business_date, item_id, loc, qty, reason, actor_id, value_paise, created_utc) VALUES
              ({P}, '2026-11-02', {towel}, 'rooms', 3, N'Missing', {Clerk}, 120000, '2026-11-02'), ({P}, '2026-11-02', {towel}, 'rooms', 2, N'Damaged', {Clerk}, 80000, '2026-11-02');
            """);
        var batch = await IdAsync($"INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, returned_date, actor_id) OUTPUT INSERTED.batch_id VALUES ({P}, N'Laundry vendor (fictional)', '2026-11-03', '2026-11-03T06:00:00', 'Returned', '2026-11-04', {Clerk});");
        await ExecAsync($"INSERT stk.laundry_line(batch_id, item_id, sent, received, damaged) VALUES ({batch}, {towel}, 50, 49, 1);");
        await ExecAsync($"INSERT stk.move(property_id, business_date, at_utc, kind, item_id, qty, actor_id) VALUES ({P}, '2026-11-03', '2026-11-03T06:00:00', 'use', {soap}, 40, {Clerk});");
        await ExecAsync($"INSERT meas.discard(property_id, item, qty, reason, discard_date, entered_by) VALUES ({P}, N'Bath towel', 2, N'Stained beyond use', '2026-11-04', {Clerk});");

        // ── people: Front Office (Ravi, Sita) and Housekeeping (Asha, Bina, and Chandu who left on 20 Oct) ──
        await ExecAsync($"INSERT hr.department(property_id, dept_code, name) VALUES ({P}, 'FO', N'Front Office'), ({P}, 'HK', N'Housekeeping');");
        var phone = 9000000000L;
        async Task<long> Staff(string name, string dept, string joined, string? exited = null) => await IdAsync($"""
            INSERT hr.staff(property_id, name, dept_code, designation, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, exited_on, active, phone, created_by, created_utc)
            OUTPUT INSERTED.staff_id VALUES ({P}, N'{name}', '{dept}', N'Staff', 'monthly', 2000000, 0, 'M', 0, '{joined}', {Q(exited)}, {(exited is null ? 1 : 0)}, '{phone++}', {Owner}, '2026-01-01');
            """);
        var ravi = await Staff("Ravi (fictional)", "FO", "2024-01-10");
        var sita = await Staff("Sita (fictional)", "FO", "2026-09-01");
        var ashaS = await Staff("Asha (fictional)", "HK", "2023-05-01");
        var binaS = await Staff("Bina (fictional)", "HK", "2025-02-01");
        var chandu = await Staff("Chandu (fictional)", "HK", "2024-06-01", "2026-10-20");
        await ExecAsync($"INSERT meas.staff_exit(property_id, staff_id, reason, exit_date, set_by) VALUES ({P}, {chandu}, N'Better pay', '2026-10-20', {Manager});");
        async Task Att(long staff, string date, string status, int ot) => await ExecAsync($"INSERT hr.attendance(staff_id, work_date, status, ot_minutes, marked_by, marked_utc) VALUES ({staff}, '{date}', '{status}', {ot}, {Manager}, '{date}T12:00:00');");
        await Att(ravi, "2026-11-05", "P", 60);
        await Att(sita, "2026-11-03", "L", 0);
        await Att(ashaS, "2026-11-02", "A", 0);
        await Att(ashaS, "2026-11-03", "L", 0);
        await Att(ashaS, "2026-11-04", "P", 30);
        await ExecAsync($"INSERT hr.roster(staff_id, work_date, shift_code, set_by, set_utc) VALUES ({binaS}, '2026-11-05', 'M', {Manager}, '2026-11-01');");
        async Task Train(long staff, string course, string on, decimal hours, string? validTo, bool mandatory) => await ExecAsync($"""
            INSERT meas.training(property_id, client_id, staff_id, course, trained_on, hours, valid_to, mandatory, created_by)
            VALUES ({P}, NEWID(), {staff}, N'{course}', '{on}', {hours.ToString(CultureInfo.InvariantCulture)}, {Q(validTo)}, {(mandatory ? 1 : 0)}, {Manager});
            """);
        await Train(ashaS, "Fire safety", "2025-01-10", 2m, "2026-01-09", true);
        await Train(ravi, "First aid", "2026-07-01", 3m, "2027-07-01", true);
        // staff pulse: five answers for Housekeeping in FY 2026-27 Q3 (eNPS +40), three for Front Office (suppressed)
        foreach (var rec in new[] { 10, 9, 9, 8, 3 })
            await ExecAsync($"INSERT meas.pulse(property_id, client_id, quarter_key, dept_code, workload, respect, tools, pay_fairness, recommend) VALUES ({P}, NEWID(), '2026-27-Q3', 'HK', 4, 4, 3, 3, {rec});");
        foreach (var rec in new[] { 9, 8, 2 })
            await ExecAsync($"INSERT meas.pulse(property_id, client_id, quarter_key, dept_code, workload, respect, tools, pay_fairness, recommend) VALUES ({P}, NEWID(), '2026-27-Q3', 'FO', 3, 3, 3, 3, {rec});");

        // ── revenue captures: an event, competitors with shops, a connected channel, a minimum-stay restriction with counters ──
        await ExecAsync($"INSERT meas.event(property_id, event_id, name, from_date, to_date, kind, impact_pct) VALUES ({P}, 'ev1', N'Festival (fictional)', '2026-11-08', '2026-11-10', 'holiday', 20);");
        await ExecAsync($"INSERT meas.competitor(property_id, competitor_id, name) VALUES ({P}, 'c1', N'Hotel Alpha (fictional)'), ({P}, 'c2', N'Hotel Beta (fictional)');");
        await ExecAsync($"""
            INSERT meas.rate_shop(property_id, client_id, shop_date, stay_date, competitor_id, room_type_id, rate_paise, with_breakfast, source, entered_by) VALUES
              ({P}, 'rs1', '2026-11-04', '2026-11-05', 'c1', {dlx}, 200000, 0, 'manual', {Clerk}), ({P}, 'rs2', '2026-11-04', '2026-11-05', 'c2', {dlx}, 240000, 0, 'manual', {Clerk});
            """);
        var ch = await IdAsync($"INSERT ota.channel(property_id, code, name, status, commission_bps, markup_bps) OUTPUT INSERTED.channel_id VALUES ({P}, 'MMT', N'MakeMyTrip (fictional)', 'Connected', 1500, 1000);");
        await ExecAsync($"INSERT ota.channel_room_map(channel_id, room_type_id, channel_code, stop_sell) VALUES ({ch}, {dlx}, 'MMT-DLX', 0);");
        await ExecAsync($"INSERT yld.restriction(property_id, code, arrival_from, arrival_to, min_stay, closed_to_arrival) VALUES ({P}, 'LOS2', '2026-11-03', '2026-11-04', 2, 0);");
        await ExecAsync($"INSERT meas.restriction_counter(property_id, night, kind, refused, reshaped) VALUES ({P}, '2026-11-03', 'MIN_STAY', 2, 1);");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}
