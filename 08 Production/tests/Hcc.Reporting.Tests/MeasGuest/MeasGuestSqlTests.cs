// MEAS2-B · the six guest measurement reports through SelectAsync on a disposable HccTest_* database (ReportDatabase): M1 red rows at each
// boundary (48:00 / 47:59 unanswered, drop 0.30 / 0.29, response 79 / 80 %, repeat 90 / 91 days, unapproved compensation, desk 9 / 8 and 7 / 6
// minutes, upsell target and free upgrade without approver, repeat share falling), the 5-answer floor (owner 7 Nov: may still turn red on the
// unanswered count), N5 satisfaction denominator by the assigned room type, M3 step (a) upsell lines (service only, half-day late check-out,
// reversed lines out) and L7 (meas.target.upsellPerArrival read with the Rs 50 default). Twin of 03 Prototype/tests/stream-meas-rpt-guest.mjs [MEAS2-B].
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Reporting.Reports.MeasGuest;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.MeasGuest;

[Trait("Category", "Sql")]
public sealed class MeasGuestSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;
    private static readonly Dictionary<string, long> Folio = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> StayId = new(StringComparer.Ordinal);

    /// <summary>Survey instant of the 48-hour tests: 2026-10-20 09:00 IST.</summary>
    private static readonly DateTimeOffset SurveyAt = new(2026, 10, 20, 3, 30, 0, TimeSpan.Zero);

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Settings(Dictionary<string, long> values) : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(key, out var v) ? ConfigValue.Of(v) : ConfigValue.Null);

        public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => GetAsync(propertyId, key, on, ct);
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

    private async Task<long> Scalar(string sql) => Convert.ToInt64(await db.ScalarAsync($"DECLARE @p INT = {db.PropertyId};\n" + sql), CultureInfo.InvariantCulture);

    private async Task Stay(string no, long guest, string room, string booked, string arrival, string departure, string inUtc, int inBy, string? outUtc = null, int? outBy = null, string source = "Direct")
    {
        var outDate = outUtc is null ? "NULL" : $"'{departure}'";
        var ids = (string)(await db.ScalarAsync($"""
            DECLARE @p INT = {db.PropertyId}, @r BIGINT, @f BIGINT, @s BIGINT;
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source)
              VALUES (@p, '{no}', N'Guest {no}', '{arrival}', '{departure}', '{(outUtc is null ? "InHouse" : "Departed")}',
                      (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = '{room}'), (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = '{booked}'), 1, 0, '{source}');
            SET @r = SCOPE_IDENTITY();
            INSERT bill.folio(reservation_id) VALUES (@r);
            SET @f = SCOPE_IDENTITY();
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
              VALUES (@p, @r, {guest}, @f, '{(outUtc is null ? "InHouse" : "CheckedOut")}', '{arrival}', '{departure}', '{arrival}', '{inUtc}', {inBy}, {outDate},
                      {(outUtc is null ? "NULL" : $"'{outUtc}'")}, {(outBy is null ? "NULL" : outBy.Value.ToString(CultureInfo.InvariantCulture))});
            SET @s = SCOPE_IDENTITY();
            SELECT CONCAT(@s, ',', @f);
            """))!;
        StayId[no] = long.Parse(ids.Split(',')[0], CultureInfo.InvariantCulture);
        Folio[no] = long.Parse(ids.Split(',')[1], CultureInfo.InvariantCulture);
    }

    private async Task<long> Guest(string name, string phone) => await Scalar($"""
        INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc)
          OUTPUT INSERTED.guest_id VALUES (@p, N'{name}', '{phone}', N'Indian', N'Aadhaar', '0000', N'1 Demo Road, Pune, Maharashtra (fictional)', 'manual', '2026-01-01');
        """);

    private async Task<long> Charge(string stay, string kind, string label, long amount, string? posted = null, long? reverses = null) => await Scalar($"""
        INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id{(posted is null ? "" : ", posted_utc")})
          OUTPUT INSERTED.charge_id VALUES ({Folio[stay]}, '{kind}', '2026-10-23', N'{label}', {amount}, 1800, 0, {(reverses is null ? "NULL" : reverses.Value.ToString(CultureInfo.InvariantCulture))}{(posted is null ? "" : $", '{posted}'")});
        """);

    private async Task Survey(string client, string at, int staff, int recommend, string? stay, string? roomType, string channel, string via = "desk")
    {
        var res = stay is null ? "NULL" : $"(SELECT reservation_id FROM gst.stay WHERE stay_id = {StayId[stay]})";
        await Scalar($"""
            INSERT meas.survey(property_id, client_id, at_utc, via, reservation_id, room_type, channel, overall, cleanliness, staff, value_score, breakfast, recommend, comment, consent)
              VALUES (@p, '{client}', '{at}', '{via}', {res}, {(roomType is null ? "NULL" : $"'{roomType}'")}, '{channel}', 5, 5, {staff}, 5, NULL, {recommend}, NULL, 1);
            SELECT 1;
            """);
    }

    private async Task Complaint(string? stay, string room, string category, string createdUtc, long? recovery = null, long? approval = null) => await Scalar($"""
        INSERT ops.guest_complaint(property_id, stay_id, room_id, guest_name, category, severity, description, owner_id, status, created_utc, created_by, recovery_amount_paise, recovery_approval_id)
          VALUES (@p, {(stay is null ? "NULL" : StayId[stay].ToString(CultureInfo.InvariantCulture))}, (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = '{room}'), N'G', N'{category}', 'Low', N'd',
                  {db.Users["MGR"]}, 'Open', '{createdUtc}', {db.Users["REC"]}, {(recovery is null ? "NULL" : recovery.Value.ToString(CultureInfo.InvariantCulture))}, {(approval is null ? "NULL" : approval.Value.ToString(CultureInfo.InvariantCulture))});
        SELECT 1;
        """);

    private const string EmptyJson = "{}";

    private async Task Seed()
    {
        int own = db.Users["OWN"], mgr = db.Users["MGR"];
        await Scalar("""
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic'), (@p, 'DLX', 4, N'Deluxe'), (@p, 'STD', 2, N'Standard');
            INSERT inv.room(property_id, room_no, room_type_id, hk_status)
              SELECT @p, v.no, t.room_type_id, 'Ready' FROM (VALUES ('101','CLS'), ('102','CLS'), ('201','DLX'), ('301','STD'), ('302','STD'), ('303','STD'), ('304','STD')) v(no, code)
                JOIN inv.room_type t ON t.property_id = @p AND t.code = v.code;
            SELECT 1;
            """);
        var g0 = await Guest("Desk Guest", "+919800000000");
        // R1: booked Classic, slept in Deluxe 201 (N5), checked out 22 Oct 11:00 IST by the Owner; R4 checked out 22 Oct 11:00 by the Manager.
        await Stay("R1", g0, "201", "CLS", "2026-10-20", "2026-10-22", "2026-10-20T08:30:00", own, "2026-10-22T05:30:00", own);
        await Stay("R4", g0, "102", "CLS", "2026-10-21", "2026-10-22", "2026-10-21T08:30:00", mgr, "2026-10-22T05:30:00", mgr);
        // R2 (Owner) and R3 (Manager): arrivals of 23 Oct 14:00 IST.
        await Stay("R2", g0, "101", "CLS", "2026-10-23", "2026-10-25", "2026-10-23T08:30:00", own);
        await Stay("R3", g0, "102", "CLS", "2026-10-23", "2026-10-25", "2026-10-23T08:30:00", mgr, source: "OTA");

        // Desk timing: bill-to-checkout 7 minutes (R1) and 6 minutes (R4); arrival-to-key 9 minutes (R2) and 8 minutes (R3).
        await Charge("R1", "service", "Laundry", 10000, "2026-10-22T05:23:00");
        await Charge("R4", "service", "Laundry", 10000, "2026-10-22T05:24:00");
        foreach (var (stay, room, min) in new[] { ("R2", "101", 9), ("R3", "102", 8) })
            await Scalar($"""
                INSERT ops.key_job(job_id, property_id, kind, event_type, reservation_id, room_id, payload_json, requested_by, requested_utc, state, timeout_utc, finished_utc)
                  VALUES (NEWID(), @p, 'issue_guest', 'issued', (SELECT reservation_id FROM gst.stay WHERE stay_id = {StayId[stay]}), (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = '{room}'),
                          N'{EmptyJson}', {own}, '2026-10-23T08:30:00', 'done', '2026-10-23T09:30:00', DATEADD(MINUTE, {min}, CAST('2026-10-23T08:30:00' AS DATETIME2(3))));
                SELECT 1;
                """);

        // Upsell (M3 a) on R2: live UPG, a restaurant breakfast (food), the desk half-day late check-out, a reversed UPG and its correction.
        await Charge("R2", "service", "UPG · Room upgrade Classic to Deluxe", 100000);
        await Charge("R2", "food", "Breakfast buffet", 30000);
        await Charge("R2", "service", "Late check-out · half day", 50000);
        var old = await Charge("R2", "service", "UPG · Room upgrade old", 80000);
        var rev = await Charge("R2", "correction", "Reversal · UPG · Room upgrade old", -80000, reverses: old);
        await Scalar($"UPDATE bill.charge SET reversed_by_charge_id = {rev} WHERE charge_id = {old}; SELECT 1;");
        await Scalar($"""
            INSERT meas.booking_extra(property_id, reservation_id, booked_type, upgrade, upgrade_approver, changed_by)
              VALUES (@p, (SELECT reservation_id FROM gst.stay WHERE stay_id = {StayId["R2"]}), N'CLS', 'paid', NULL, {own}),
                     (@p, (SELECT reservation_id FROM gst.stay WHERE stay_id = {StayId["R3"]}), N'CLS', 'free', NULL, {mgr});
            SELECT 1;
            """);

        // Satisfaction: one low survey per channel at 20 Oct 09:00 IST.
        await Survey("s-direct", "2026-10-20T03:30:00", 2, 10, "R2", "CLS", "Direct");
        await Complaint("R2", "101", "Other", "2026-10-20T03:29:00"); // raised before the survey: does not answer it
        await Survey("s-ota", "2026-10-20T03:30:00", 2, 10, "R3", "CLS", "OTA");
        await Complaint("R3", "102", "Other", "2026-10-20T03:30:00"); // raised at the survey minute: answers it
        await Survey("s-qr", "2026-10-20T03:30:00", 2, 10, null, null, "QR", "qr");
        for (var i = 0; i < 5; i++) await Survey("s-five-" + i, "2026-10-20T03:30:00", i == 0 ? 2 : 5, 10, null, null, "Five", "link");
        await Survey("s-dlx", "2026-10-20T03:30:00", 5, 10, null, "DLX", "Lobby");

        // Reputation: March is read only as the previous month of April.
        await Scalar("""
            INSERT meas.review_month(property_id, month, platform, review_count, avg_score, responded) VALUES
              (@p, '2026-03', 'google', 10, 4.50, 10), (@p, '2026-04', 'google', 10, 4.20, 10), (@p, '2026-05', 'google', 10, 3.91, 10),
              (@p, '2026-06', 'agoda', 100, 4.00, 79), (@p, '2026-07', 'agoda', 100, 4.00, 80);
            SELECT 1;
            """);

        // Service recovery: 101 Noise 90 days apart (red), 102 Noise 91 days apart, 201 compensation without / with approval.
        await Complaint(null, "101", "Noise", "2026-07-23T05:00:00");
        await Complaint(null, "101", "Noise", "2026-10-21T05:00:00");
        await Complaint(null, "102", "Noise", "2026-07-22T05:00:00");
        await Complaint(null, "102", "Noise", "2026-10-21T05:00:00");
        await Complaint(null, "201", "Other", "2026-10-21T05:00:00", recovery: 10000);
        var ap = await Scalar($"""
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              OUTPUT INSERTED.approval_id VALUES (@p, 'discount', {Folio["R1"]}, 'folio', {Folio["R1"]}, 10000, N'goodwill', 'Executed', {db.Users["REC"]}, '2026-10-21T05:00:00', '2026-10-22T05:00:00', {mgr}, '2026-10-21T05:10:00');
            """);
        await Complaint(null, "201", "Billing", "2026-10-21T05:00:00", recovery: 10000, approval: ap);

        // Repeat loyalty: P1..P4 in July; the share falls 75 % (Aug) → 50 % (Sep) → 20 % in October (the four October desk stays add one new customer).
        var people = new Dictionary<string, long>(StringComparer.Ordinal);
        var n = 0;
        async Task<long> Who(string k) => people.TryGetValue(k, out var id) ? id : people[k] = await Guest("Repeat " + k, "+9198100000" + (++n).ToString("00", CultureInfo.InvariantCulture));
        var plan = new (string Month, string[] Keys)[]
        {
            ("2026-07", ["P1", "P2", "P3", "P4"]), ("2026-08", ["P1", "P2", "P3", "N1"]), ("2026-09", ["P1", "P2", "N2", "N3"]), ("2026-10", ["P1", "N4", "N5", "N6"]),
        };
        foreach (var (month, keys) in plan)
            for (var i = 0; i < keys.Length; i++)
                await Stay($"L-{month}-{keys[i]}", await Who(keys[i]), "30" + (i + 1).ToString(CultureInfo.InvariantCulture), "STD", month + "-10", month + "-11", month + "-10T08:30:00", own, month + "-11T05:30:00", own);
    }

    private static BoundParams P(DateOnly? from, DateOnly? to, params (string K, string V)[] values) =>
        new(values.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal), from, to);

    private static readonly DateOnly Oct1 = new(2026, 10, 1), Oct31 = new(2026, 10, 31);

    private Caller Owner => new(db.PropertyId, db.Users["OWN"]);

    private static List<ReportRow> Data(ReportResult r) => r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    private static string S(ReportRow r, string k) => Convert.ToString(r.Cells[k], CultureInfo.InvariantCulture) ?? "";

    private async Task<ReportRow> Sat(DateTimeOffset now, string channel, string roomType = "")
    {
        var r = await new SatisfactionReport(db.Platform, null, new At(now)).SelectAsync(Owner, P(Oct1, Oct31, ("month", "2026-10"), ("channel", channel), ("roomType", roomType)), Ct);
        return Assert.Single(Data(r));
    }

    [Fact]
    public async Task Satisfaction_turns_red_at_48_hours_unanswered_below_the_floor_and_a_complaint_on_the_booking_at_or_after_the_survey_answers_it()
    {
        var at48 = SurveyAt.AddMinutes(MeasGuestRules.UnansweredMinutes);
        var before = await Sat(at48.AddMinutes(-1), "Direct");
        Assert.Null(before.Group);
        var red = await Sat(at48, "Direct");
        Assert.Equal("red", red.Group);
        Assert.Null(red.Cells["overall"]); // owner 7 Nov: count only below the floor, never a score
        Assert.Null(red.Cells["nps"]);
        Assert.Equal(1L, red.Cells["responses"]);
        Assert.Null((await Sat(at48, "OTA")).Group); // complaint raised at the survey minute on the same booking
        Assert.Equal("red", (await Sat(at48, "QR")).Group); // a QR survey has no booking: never answered

        var r = await new SatisfactionReport(db.Platform, null, new At(at48)).SelectAsync(Owner, P(Oct1, Oct31, ("month", "2026-10"), ("channel", "Direct")), Ct);
        Assert.EndsWith(SatisfactionReport.UnansweredNote(1), r.About.DateBasis, StringComparison.Ordinal);
        Assert.Contains("Fewer than 5 responses", r.About.DateBasis, StringComparison.Ordinal);
        var quiet = await new SatisfactionReport(db.Platform, null, new At(at48.AddMinutes(-1))).SelectAsync(Owner, P(Oct1, Oct31, ("month", "2026-10"), ("channel", "Direct")), Ct);
        Assert.DoesNotContain("no complaint raised", quiet.About.DateBasis, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Satisfaction_with_five_answers_is_red_on_the_unanswered_score_even_when_NPS_meets_the_target()
    {
        var at48 = SurveyAt.AddMinutes(MeasGuestRules.UnansweredMinutes);
        var row = await Sat(at48, "Five");
        Assert.Equal(5L, row.Cells["responses"]);
        Assert.Equal(100L, row.Cells["nps"]);
        Assert.Equal("red", row.Group);
        Assert.Null((await Sat(at48.AddMinutes(-1), "Five")).Group);
    }

    [Fact]
    public async Task Satisfaction_response_rate_counts_stays_by_the_assigned_room_type_like_the_capture_and_a_defaulted_month_does_not_throw()
    {
        var row = await Sat(SurveyAt, "", "DLX"); // R1 booked Classic, slept in Deluxe 201
        Assert.Equal(1L, row.Cells["responses"]);
        Assert.Equal(100.0, row.Cells["responseRate"]);
        var r = await new SatisfactionReport(db.Platform, null, new At(SurveyAt)).SelectAsync(Owner, P(null, null, ("month", "2026-10"), ("roomType", "DLX")), Ct);
        Assert.Equal(100.0, Assert.Single(Data(r)).Cells["responseRate"]);
    }

    [Fact]
    public async Task Reputation_rows_are_red_at_a_drop_of_0_30_and_a_response_rate_under_80_percent_and_April_compares_with_March()
    {
        var r = await new ReputationIndexReport(db.Platform).SelectAsync(Owner, P(new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)), Ct);
        var g = Data(r).ToDictionary(x => S(x, "month") + " " + S(x, "platform"), x => x.Group);
        Assert.Equal(["2026-04 google", "2026-05 google", "2026-06 agoda", "2026-07 agoda"], g.Keys);
        Assert.Equal("red", g["2026-04 google"]);
        Assert.Null(g["2026-05 google"]);
        Assert.Equal("red", g["2026-06 agoda"]);
        Assert.Null(g["2026-07 agoda"]);
    }

    [Fact]
    public async Task Service_recovery_groups_are_red_on_a_repeat_within_90_days_or_a_compensation_without_approval()
    {
        var r = await new ServiceRecoveryReport(db.Platform).SelectAsync(Owner, P(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 24)), Ct);
        var g = Data(r).ToDictionary(x => S(x, "room") + " " + S(x, "category"), x => x.Group);
        Assert.Equal("red", g["101 Noise"]);
        Assert.Null(g["102 Noise"]);
        Assert.Equal("red", g["201 Other"]);
        Assert.Null(g["201 Billing"]);
    }

    [Fact]
    public async Task Desk_timing_rows_are_red_above_8_minutes_at_check_in_and_6_at_check_out_and_follow_a_stored_standard()
    {
        async Task<Dictionary<string, string?>> Run(ISettingsReader? s, DateOnly d)
        {
            var r = await new DeskTimingReport(db.Platform, s).SelectAsync(Owner, P(d, d), Ct);
            return Data(r).ToDictionary(x => S(x, "hour") + " " + S(x, "deskUser"), x => x.Group);
        }

        var ins = await Run(null, new DateOnly(2026, 10, 23));
        Assert.Equal("red", ins["14:00 OWN"]); // 9 minutes arrival-to-key
        Assert.Null(ins["14:00 MGR"]);        // 8 minutes
        var outs = await Run(null, new DateOnly(2026, 10, 22));
        Assert.Equal("red", outs["11:00 OWN"]); // 7 minutes bill-to-checkout
        Assert.Null(outs["11:00 MGR"]);        // 6 minutes
        var looser = new Settings(new() { ["meas.target.checkinMinutes"] = 9, ["meas.target.checkoutMinutes"] = 7 });
        Assert.Null((await Run(looser, new DateOnly(2026, 10, 23)))["14:00 OWN"]);
        Assert.Null((await Run(looser, new DateOnly(2026, 10, 22)))["11:00 OWN"]);
    }

    private async Task<ReportRow> Up(string deskUser, ISettingsReader? settings = null)
    {
        var d = new DateOnly(2026, 10, 23);
        var r = await new UpsellReport(db.Platform, settings).SelectAsync(Owner, P(d, d, ("deskUser", db.Users[deskUser].ToString(CultureInfo.InvariantCulture))), Ct);
        return Assert.Single(Data(r));
    }

    [Fact]
    public async Task Upsell_counts_service_lines_only_with_the_half_day_late_check_out_and_without_reversed_lines()
    {
        var row = await Up("OWN");
        Assert.Equal(1L, row.Cells["arrivals"]);
        Assert.Equal(0L, row.Cells["breakfast"]);        // the restaurant "Breakfast buffet" is a food line
        Assert.Equal(50000L, row.Cells["lateCheckout"]); // Late check-out · half day (DeskFieldService)
        Assert.Equal(150000L, row.Cells["revenuePerArrival"]); // live UPG 100000 + late 50000; the reversed UPG and its correction are out
        Assert.Null(row.Group);                          // 150000 >= the Rs 50 default
    }

    [Fact]
    public async Task Upsell_red_follows_the_Owner_target_and_a_free_upgrade_without_an_approver()
    {
        Assert.Null((await Up("OWN", new Settings(new() { [UpsellReport.TargetKey] = 150000 }))).Group);
        Assert.Equal("red", (await Up("OWN", new Settings(new() { [UpsellReport.TargetKey] = 150001 }))).Group);
        Assert.Equal("red", (await Up("MGR", new Settings(new() { [UpsellReport.TargetKey] = 0 }))).Group); // free upgrade, no approver
    }

    [Fact]
    public async Task Repeat_loyalty_summary_row_is_red_when_the_share_falls_two_months_running()
    {
        async Task<ReportRow> Summary(string month) =>
            Data(await new RepeatLoyaltyReport(db.Platform).SelectAsync(Owner, P(null, null, ("month", month)), Ct))[0];
        var oct = await Summary("2026-10");
        Assert.Equal(20.0, oct.Cells["repeatShare"]);
        Assert.Equal("red", oct.Group);
        var sep = await Summary("2026-09");
        Assert.Equal(50.0, sep.Cells["repeatShare"]);
        Assert.Null(sep.Group); // 50 < 75 but July was 0
    }
}
