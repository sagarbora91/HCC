// F12 f12-int-cap · one SQL round trip of the capture module against the real meas tables: every migration applied on a fresh .\SQLEXPRESS
// HccTest_* database (the Hcc.Inventory.Tests harness, as Hcc.Yield.Tests does), the real command runner, access policy and SqlMeasCaptureStore.
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Contracts.Meas;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Meas;
using Hcc.Inventory.Tests.Harness;
using Hcc.Operations.Meas;

namespace Hcc.Meas.Tests.Capture;

public sealed class MeasCaptureSqlFixture : IAsyncLifetime
{
    private InventoryDatabase? _db;

    public InventoryDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public MeasCaptureService Service { get; private set; } = null!;

    public Dictionary<string, int> Users { get; } = [];

    public string RoomNo { get; private set; } = string.Empty;

    public string TypeCode { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _db = await InventoryDatabase.CreateAsync();
        var golden = GoldenInventory.Load();
        await golden.SeedAsync(_db);
        var clock = new TestClock(new DateTimeOffset(GoldenInventory.LocalToUtc(golden.Raw.Clock), TimeSpan.Zero));
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("housekeeper", "HKP"), ("maintenance", "MNT") })
        {
            var id = Convert.ToInt32(await _db.ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (1, '{login}', N'User {login}', '2026-01-01');"), CultureInfo.InvariantCulture);
            await _db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
            Users[role] = id;
        }
        await _db.ExecAsync("IF NOT EXISTS (SELECT 1 FROM hr.department WHERE property_id = 1 AND dept_code = 'HK') INSERT hr.department(property_id, dept_code, name) VALUES (1, 'HK', N'Housekeeping');");
        RoomNo = (string)(await _db.ScalarAsync("SELECT TOP (1) room_no FROM inv.room ORDER BY room_no;"))!;
        TypeCode = (string)(await _db.ScalarAsync("SELECT TOP (1) code FROM inv.room_type ORDER BY code;"))!;
        var conns = _db.Platform.Connections;
        var store = new SqlMeasCaptureStore(conns);
        Service = new MeasCaptureService(_db.Platform.Commands, new SqlAccessPolicy(conns, clock, []), new MeasurementGate(new FakeSettings(), new FakeModules()),
            store, store, store, store, store, store, store, clock);
    }

    public CommandContext Ctx(string role, string key) => new(1, Users[role], "x", key);

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

public sealed class MeasCaptureSqlTests(MeasCaptureSqlFixture f) : IClassFixture<MeasCaptureSqlFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task F12_INT_CAP_SQL_round_trip_captures_audits_masks_and_the_anonymous_pulse()
    {
        var s = f.Service;
        // desk
        var t1 = await s.LogTurnawayAsync(f.Ctx("REC", "sql-ta"), new LogTurnawayRequest("full", "2026-10-30", 2, f.TypeCode, 1, 250000, "Walk-in", null), Ct);
        var t2 = await s.LogTurnawayAsync(f.Ctx("REC", "sql-ta"), new LogTurnawayRequest("full", "2026-10-30", 2, f.TypeCode, 1, 250000, "Walk-in", null), Ct);
        Assert.Equal((false, true, t1.Value.Id), (t1.Replayed, t2.Replayed, t2.Value.Id));
        await s.SaveAllowanceReasonAsync(f.Ctx("OWN", "sql-al"), new SaveAllowanceReasonRequest("late bill", "Late bill correction", true), Ct);
        await s.SetCustomerPreferenceAsync(f.Ctx("MGR", "sql-pref"), new SetCustomerPreferenceRequest("G-1", "High", null, null, "Soft", null, true, null, null, null, null, null), Ct);
        await s.LoyaltySetAsync(f.Ctx("REC", "sql-loy"), new MeasLoyaltySetRequest("G-1", "Gold"), Ct);
        // housekeeping / engineering
        await s.OfflineSetAsync(f.Ctx("MGR", "sql-off"), new MeasOfflineSetRequest(f.RoomNo, "OOO", "2026-10-27", null, "AC repair", null), Ct);
        var meter = await s.MeterSaveAsync(f.Ctx("OWN", "sql-mtr"), new MeasMeterSaveRequest(null, "Main electricity", "electricity", null, null), Ct);
        await s.ReadingAddAsync(f.Ctx("MNT", "sql-rd1"), new MeasReadingAddRequest(meter.Value.Id, 1000.5m, "2026-10-23", null), Ct);
        var low = await Assert.ThrowsAsync<HccException>(() => s.ReadingAddAsync(f.Ctx("MNT", "sql-rd2"), new MeasReadingAddRequest(meter.Value.Id, 999m, "2026-10-24", null), Ct));
        Assert.Equal("MEAS_READING_NOT_MONOTONIC", low.Details!["reason"].GetString());
        await s.DiscardAddAsync(f.Ctx("MGR", "sql-dsc"), new MeasDiscardAddRequest("Bath towel", 3, "Torn", null), Ct);
        // guest
        await s.SurveyAddAsync(f.Ctx("REC", "sql-srv"), new MeasSurveyAddRequest(null, "desk", null, 5, 4, 5, 4, null, 9, "Lovely stay", true), Ct);
        await s.ReviewSaveAsync(f.Ctx("MGR", "sql-rev"), new MeasReviewSaveRequest("2026-09", "google", 12, 4.4m, 4.2m, 10, "staff, breakfast"), Ct);
        await s.RootCauseListSaveAsync(f.Ctx("MGR", "sql-rcl"), new MeasRootCauseListSaveRequest([new("NOISE", "Noise", true), new("CLEAN", "Cleanliness", true)]), Ct);
        // people (anonymous pulse)
        var pulse = await s.AddPulseAsync(f.Ctx("HKP", "sql-pulse"), new AddMeasPulseRequest("HK", 4, 5, 3, 3, 8), Ct);
        var pulseAgain = await s.AddPulseAsync(f.Ctx("HKP", "sql-pulse"), new AddMeasPulseRequest("HK", 4, 5, 3, 3, 8), Ct);
        Assert.Equal(pulse.Id, pulseAgain.Id);
        // revenue
        await s.EventSaveAsync(f.Ctx("MGR", "sql-evt"), new MeasEventSaveRequest(null, "Wedding season", "2026-11-20", "2026-12-10", "wedding-season", 20, null), Ct);
        var comp = await s.CompetitorSaveAsync(f.Ctx("MGR", "sql-cmp"), new MeasCompetitorSaveRequest(null, "Hotel Rival (fictional)", true), Ct);
        await s.RateShopAddAsync(f.Ctx("MGR", "sql-rs"), new MeasRateShopAddRequest(null, "2026-11-01", comp.Value.Id, f.TypeCode, 240000, true, null), Ct);
        await s.OtbSnapshotAsync(f.Ctx("OWN", "sql-otb"), new MeasOtbSnapshotRequest(null, null), Ct);

        var owner = await s.CapturesAsync(1, f.Users["OWN"], Ct);
        Assert.Equal((1, 250000L), (owner.Turnaway.Count, owner.Turnaway[0].QuotedRate!.Value));
        Assert.Equal("LATE-BILL", Assert.Single(owner.AllowanceReasons).Code);
        var pref = Assert.Single(owner.Preference);
        Assert.Equal(("High", "Soft", true, "Gold"), (pref.Floor, pref.Pillow, pref.DoNotDisturb, pref.LoyaltyTag));
        Assert.Equal((f.RoomNo, "OOO", "2026-10-27"), (owner.Offline[0].Room, owner.Offline[0].Reason, owner.Offline[0].ExpectedReturn));
        Assert.Equal(1000.5m, Assert.Single(owner.Readings).Value);
        Assert.Single(owner.Discards);
        Assert.Equal(("desk", f.Users["REC"].ToString(CultureInfo.InvariantCulture)), (Assert.Single(owner.Surveys).Via, owner.Surveys[0].EnteredBy));
        Assert.Equal("2026-09-google", Assert.Single(owner.Reviews).Id);
        Assert.Equal(2, owner.RootCauseList.Count);
        Assert.Equal(new MeasPulseView(pulse.Id, "2026-27-Q3", "HK", 4, 5, 3, 3, 8), Assert.Single(owner.Pulse));
        Assert.Single(owner.Events);
        Assert.Equal(240000L, Assert.Single(owner.Rateshop).RatePaise);
        Assert.All(owner.Otb, o => Assert.True(o.Rooms > 0));

        var hk = await s.CapturesAsync(1, f.Users["HKP"], Ct);
        Assert.Null(hk.Turnaway[0].QuotedRate);
        Assert.Null(hk.Rateshop[0].RatePaise);
        Assert.Null(hk.Surveys[0].EnteredBy);

        // Audits: one per captured row; the pulse writes no audit row (its time could name the sender, review H4); meas.pulse has no identity
        // column and no time finer than the quarter (migration 4860).
        Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MEAS_TURNAWAY_LOG';", Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MEAS_PULSE_ADD';", Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('meas.pulse') AND t.name IN ('datetime','datetime2','datetimeoffset','smalldatetime','time','date');", Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM plat.idempotency WHERE operation = 'AddMeasPulse';", Ct));
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('meas.pulse') AND (name LIKE '%user%' OR name LIKE '%staff%' OR name LIKE '%by%');", Ct));
        Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM meas.pulse;", Ct));

        // Roles: Housekeeping cannot log a turn-away; Reception cannot enter a reading.
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.LogTurnawayAsync(f.Ctx("HKP", "sql-ta2"), new LogTurnawayRequest("full", "2026-10-30", 2, f.TypeCode, 1, null, "Walk-in", null), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ReadingAddAsync(f.Ctx("REC", "sql-rd3"), new MeasReadingAddRequest(meter.Value.Id, 1001m, null, null), Ct))).Code);
    }
}
