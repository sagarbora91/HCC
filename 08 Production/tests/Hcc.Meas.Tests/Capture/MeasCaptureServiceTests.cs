// F12 f12-int-cap · MeasCaptureService rules with in-memory fakes: role refusals (prototype handler twins), MEAS_OFF through the real
// MeasurementGate over fake settings, Idempotency-Key replay, anonymous pulse, monotonic meter readings, DEC-061 read masks, route table.
using System.Text.Json;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Meas;
using Hcc.Contracts;
using Hcc.Contracts.Meas;
using Hcc.Domain.Errors;
using Hcc.Operations.Meas;

namespace Hcc.Meas.Tests.Capture;

internal sealed class FakeRunner : ICommandRunner
{
    private readonly Dictionary<string, (string Payload, object Result)> _done = new(StringComparer.Ordinal);

    public List<(int Principal, string Action, string Target, string Detail)> Audits { get; } = [];

    public int Runs { get; private set; }

    private sealed class Tx(CommandContext ctx, FakeRunner owner) : ICommandTransaction
    {
        public CommandContext Context { get; } = ctx;

        public Task AuditAsync(string action, string target, string detail, CancellationToken ct = default)
        {
            owner.Audits.Add((Context.PrincipalId, action, target, detail));
            return Task.CompletedTask;
        }

        public Task<long> PublishAsync(string eventType, long aggregateId, object payload, CancellationToken ct = default) => Task.FromResult(0L);
    }

    public async Task<CommandResult<TResult>> RunAsync<TPayload, TResult>(CommandContext context, TPayload payload, Func<ICommandTransaction, CancellationToken, Task<TResult>> handler, CancellationToken ct = default)
    {
        var key = $"{context.PrincipalId}|{context.Operation}|{context.IdempotencyKey}";
        var json = JsonSerializer.Serialize(payload);
        if (_done.TryGetValue(key, out var hit))
            return hit.Payload == json ? new CommandResult<TResult>((TResult)hit.Result, true) : throw new HccException(HccErrorCode.IDEMPOTENCY_MISMATCH, "Different payload.");
        Runs++;
        var r = await handler(new Tx(context, this), ct);
        _done[key] = (json, r!);
        return new CommandResult<TResult>(r, false);
    }
}

internal sealed class FakePolicy : IAccessPolicy
{
    public HashSet<(int, string)> Denied { get; } = [];

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
        Denied.Contains((principalId, permissionId)) ? throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {permissionId}.") : Task.CompletedTask;

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(!Denied.Contains((principalId, permissionId)));
}

/// <summary>In-memory store: only what the tests use; the rest is not needed here (the SQL test covers the real store).</summary>
internal sealed class FakeStore : IMeasCaptureCommon, IMeasDeskStore, IMeasGuestStore, IMeasPeopleStore, IMeasHkStore, IMeasRevenueStore, IMeasCaptureReads
{
    public Dictionary<int, string[]> Roles { get; } = [];
    public DateOnly Today { get; set; } = new(2026, 10, 24);
    public Dictionary<string, (long Id, MeasTurnawayRow Row)> Turnaway { get; } = new(StringComparer.Ordinal);
    public List<MeasMeterView> Meters { get; } = [new("MTR-1", "Main electricity", "electricity", "kWh", true)];
    public List<(string Client, string Meter, DateOnly Date, decimal Value)> Readings { get; } = [];
    public List<(Guid Client, MeasPulseRow Row)> Pulses { get; } = [];
    public MeasCapturesView View { get; set; } = null!;

    public Task<IReadOnlyList<string>> RoleCodesAsync(int propertyId, int principalId, DateTime utcNow, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Roles.TryGetValue(principalId, out var r) ? r : []);
    public Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(Today);

    public Task<(long Id, bool Existed)> InsertTurnawayAsync(ICommandTransaction tx, string clientId, MeasTurnawayRow row, CancellationToken ct = default)
    {
        if (Turnaway.TryGetValue(clientId, out var hit)) return Task.FromResult((hit.Id, true));
        Turnaway[clientId] = (Turnaway.Count + 1, row);
        return Task.FromResult(((long)Turnaway.Count, false));
    }

    public Task<IReadOnlyList<MeasMeterView>> MetersAsync(ICommandTransaction tx, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MeasMeterView>>(Meters);
    public Task<IReadOnlyList<(DateOnly Date, decimal Value)>> ReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(DateOnly, decimal)>>(Readings.Where(r => r.Meter == meterId).Select(r => (r.Date, r.Value)).ToList());
    public Task<(long Id, bool Existed)> InsertReadingAsync(ICommandTransaction tx, string clientId, string meterId, DateOnly date, decimal value, string? photoRef, CancellationToken ct = default)
    {
        var i = Readings.FindIndex(r => r.Client == clientId);
        if (i >= 0) return Task.FromResult(((long)i + 1, true));
        Readings.Add((clientId, meterId, date, value));
        return Task.FromResult(((long)Readings.Count, false));
    }

    public Task<bool> DepartmentExistsAsync(int propertyId, string deptCode, CancellationToken ct = default) => Task.FromResult(deptCode is "HK" or "FO");
    public Task<(long Id, bool Existed)> AddPulseAsync(int propertyId, Guid clientId, MeasPulseRow row, DateTime utc, CancellationToken ct = default)
    {
        var i = Pulses.FindIndex(p => p.Client == clientId);
        if (i >= 0) return Task.FromResult(((long)i + 1, true));
        Pulses.Add((clientId, row));
        return Task.FromResult(((long)Pulses.Count, false));
    }

    public Task<MeasCapturesView> CapturesAsync(int propertyId, CancellationToken ct = default) => Task.FromResult(View);

    // ── not used by these tests ──
    private static T No<T>() => throw new NotSupportedException("Not used by the unit tests.");
    public Task<bool> ReservationExistsAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default) => No<Task<bool>>();
    public Task<string?> ReservationRoomTypeAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default) => No<Task<string?>>();
    public Task<bool> RoomTypeExistsAsync(ICommandTransaction tx, string code, CancellationToken ct = default) => No<Task<bool>>();
    public Task<(long Id, bool Existed)> InsertWalkedAsync(ICommandTransaction tx, string clientId, long reservationId, DateOnly night, string reason, long? costPaise, int approvedBy, CancellationToken ct = default) => No<Task<(long, bool)>>();
    public Task<MeasAllowanceReasonView?> AllowanceReasonAsync(ICommandTransaction tx, string code, CancellationToken ct = default) => No<Task<MeasAllowanceReasonView?>>();
    public Task UpsertAllowanceReasonAsync(ICommandTransaction tx, string code, string label, bool active, CancellationToken ct = default) => No<Task>();
    public Task<MeasBookingExtraView?> BookingExtraAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default) => No<Task<MeasBookingExtraView?>>();
    public Task UpsertBookingExtraAsync(ICommandTransaction tx, long reservationId, string? bookedType, string? upgrade, int? approver, CancellationToken ct = default) => No<Task>();
    public Task<bool> InsertBookingExtraLogAsync(ICommandTransaction tx, string clientId, long reservationId, string? item, bool waived, string? waiveReason, CancellationToken ct = default) => No<Task<bool>>();
    public Task<MeasPreferenceRow?> PreferenceAsync(ICommandTransaction tx, string customerKey, CancellationToken ct = default) => No<Task<MeasPreferenceRow?>>();
    public Task UpsertPreferenceAsync(ICommandTransaction tx, string customerKey, MeasPreferenceRow row, CancellationToken ct = default) => No<Task>();
    public Task<bool> InsertPreferenceLogAsync(ICommandTransaction tx, string clientId, string customerKey, string action, string? detail, int? approvedBy, CancellationToken ct = default) => No<Task<bool>>();
    public Task<IReadOnlyList<string>> ApproverRolesAsync(ICommandTransaction tx, int userId, DateTime utcNow, CancellationToken ct = default) => No<Task<IReadOnlyList<string>>>();
    public Task<bool> SurveyExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default) => No<Task<bool>>();
    public Task<IReadOnlyList<DateTime>> QrSurveyTimesAsync(ICommandTransaction tx, DateTime sinceUtc, CancellationToken ct = default) => No<Task<IReadOnlyList<DateTime>>>();
    public Task<(bool Found, string? RoomType, string? Channel)> SurveyBookingAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default) => No<Task<(bool, string?, string?)>>();
    public Task InsertSurveyAsync(ICommandTransaction tx, MeasSurveyRow row, CancellationToken ct = default) => No<Task>();
    public Task<bool> UpsertReviewAsync(ICommandTransaction tx, MeasReviewView row, int by, CancellationToken ct = default) => No<Task<bool>>();
    public Task<IReadOnlyList<MeasRootCauseView>> RootCausesAsync(ICommandTransaction tx, CancellationToken ct = default) => No<Task<IReadOnlyList<MeasRootCauseView>>>();
    public Task<IReadOnlyList<string>> UsedRootCausesAsync(ICommandTransaction tx, CancellationToken ct = default) => No<Task<IReadOnlyList<string>>>();
    public Task ReplaceRootCausesAsync(ICommandTransaction tx, IReadOnlyList<MeasRootCauseView> rows, CancellationToken ct = default) => No<Task>();
    public Task<bool> ComplaintExistsAsync(ICommandTransaction tx, long complaintId, CancellationToken ct = default) => No<Task<bool>>();
    public Task<string?> RootCauseOfAsync(ICommandTransaction tx, string complaintId, CancellationToken ct = default) => No<Task<string?>>();
    public Task SetRootCauseAsync(ICommandTransaction tx, string complaintId, string? code, int by, CancellationToken ct = default) => No<Task>();
    public Task SetLoyaltyTagAsync(ICommandTransaction tx, string customerKey, string? tag, CancellationToken ct = default) => No<Task>();
    public Task<DateOnly?> StaffJoinedAsync(ICommandTransaction tx, long staffId, CancellationToken ct = default) => No<Task<DateOnly?>>();
    public Task<long?> TrainingStaffAsync(ICommandTransaction tx, long trainingId, CancellationToken ct = default) => No<Task<long?>>();
    public Task<(long Id, bool Existed)> InsertTrainingAsync(ICommandTransaction tx, Guid clientId, MeasTrainingView row, CancellationToken ct = default) => No<Task<(long, bool)>>();
    public Task UpdateTrainingAsync(ICommandTransaction tx, long trainingId, MeasTrainingView row, CancellationToken ct = default) => No<Task>();
    public Task<bool> SetExitAsync(ICommandTransaction tx, long staffId, string? reason, DateOnly? exitDate, CancellationToken ct = default) => No<Task<bool>>();
    public Task<int?> RoomIdAsync(ICommandTransaction tx, string roomNo, CancellationToken ct = default) => No<Task<int?>>();
    public Task<bool> WorkOrderExistsAsync(ICommandTransaction tx, long workOrderId, CancellationToken ct = default) => No<Task<bool>>();
    public Task<bool> OfflineExistsAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default) => No<Task<bool>>();
    public Task SetOfflineAsync(ICommandTransaction tx, int roomId, string clientId, string reason, DateOnly? expectedReturn, string? workOrder, string? note, CancellationToken ct = default) => No<Task>();
    public Task ClearOfflineAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default) => No<Task>();
    public Task<bool> MeterHasReadingsAsync(ICommandTransaction tx, string meterId, CancellationToken ct = default) => No<Task<bool>>();
    public Task UpsertMeterAsync(ICommandTransaction tx, MeasMeterView meter, string clientId, CancellationToken ct = default) => No<Task>();
    public Task<bool> AssetExistsAsync(ICommandTransaction tx, int assetId, CancellationToken ct = default) => No<Task<bool>>();
    public Task UpsertAssetLifeAsync(ICommandTransaction tx, string assetId, int? lifeMonths, string? grade, long? costPaise, string clientId, CancellationToken ct = default) => No<Task>();
    public Task<(long Id, bool Existed)> InsertDiscardAsync(ICommandTransaction tx, string clientId, string item, int qty, string reason, DateOnly date, CancellationToken ct = default) => No<Task<(long, bool)>>();
    public Task<(MeasEventView Event, bool Deleted)?> EventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default) => No<Task<(MeasEventView, bool)?>>();
    public Task UpsertEventAsync(ICommandTransaction tx, MeasEventView e, CancellationToken ct = default) => No<Task>();
    public Task DeleteEventAsync(ICommandTransaction tx, string eventId, CancellationToken ct = default) => No<Task>();
    public Task<int> SnapshotOtbAsync(ICommandTransaction tx, DateOnly takenOn, int nights, CancellationToken ct = default) => No<Task<int>>();
    public Task PruneOtbAsync(ICommandTransaction tx, int keep, CancellationToken ct = default) => No<Task>();
    public Task<IReadOnlyList<MeasCompetitorView>> CompetitorsAsync(ICommandTransaction tx, CancellationToken ct = default) => No<Task<IReadOnlyList<MeasCompetitorView>>>();
    public Task UpsertCompetitorAsync(ICommandTransaction tx, MeasCompetitorView c, CancellationToken ct = default) => No<Task>();
    public Task<bool> RateShopExistsAsync(ICommandTransaction tx, string clientId, CancellationToken ct = default) => No<Task<bool>>();
    public Task<int?> RoomTypeIdAsync(ICommandTransaction tx, string code, CancellationToken ct = default) => No<Task<int?>>();
    public Task InsertRateShopAsync(ICommandTransaction tx, string clientId, DateOnly shopDate, DateOnly stayDate, string competitorId, int roomTypeId, long ratePaise, bool withBreakfast, string source, CancellationToken ct = default) => No<Task>();
}

public sealed class MeasCaptureServiceTests
{
    private const int Owner = 1, Reception = 2, Housekeeper = 3, Maintenance = 4, Manager = 5;

    private readonly FakeStore _store = new();
    private readonly FakeRunner _runner = new();
    private readonly FakePolicy _policy = new();
    private readonly FakeSettings _settings = new();
    private readonly MeasCaptureService _svc;

    public MeasCaptureServiceTests()
    {
        _store.Roles[Owner] = ["OWN"]; _store.Roles[Reception] = ["REC"]; _store.Roles[Housekeeper] = ["HKP"]; _store.Roles[Maintenance] = ["MNT"]; _store.Roles[Manager] = ["MGR"];
        _svc = new MeasCaptureService(_runner, _policy, new MeasurementGate(_settings, new FakeModules()), _store, _store, _store, _store, _store, _store, _store, TimeProvider.System);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandContext Ctx(int who, string key = "k-1") => new(1, who, "x", key);

    private static LogTurnawayRequest Turnaway() => new("full", "2026-10-30", 2, "Deluxe", 1, 250000, "Walk-in", null);

    [Fact]
    public async Task F12_INT_CAP_role_rules_mirror_the_prototype_handlers()
    {
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _svc.LogTurnawayAsync(Ctx(Housekeeper), Turnaway(), Ct))).Code);
        Assert.Equal("1", (await _svc.LogTurnawayAsync(Ctx(Reception), Turnaway(), Ct)).Value.Id);
        // Meter readings: Owner, Manager, Maintenance (PERM-MEAS-002); Reception is refused.
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Reception), new MeasReadingAddRequest("MTR-1", 10, null, null), Ct))).Code);
        Assert.False((await _svc.ReadingAddAsync(Ctx(Maintenance), new MeasReadingAddRequest("MTR-1", 10, null, null), Ct)).Replayed);
        // The role alone is not enough: PERM-MEAS-002 is still checked.
        _policy.Denied.Add((Manager, MeasCaptureService.BehalfPermission));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Manager, "k-2"), new MeasReadingAddRequest("MTR-1", 11, null, null), Ct))).Code);
    }

    [Fact]
    public async Task F12_INT_CAP_a_switched_off_item_refuses_first_with_MEAS_OFF_and_writes_nothing()
    {
        _settings.Values[MeasCaptureService.KeyTurnaway] = false;
        var ex = await Assert.ThrowsAsync<HccException>(() => _svc.LogTurnawayAsync(Ctx(Housekeeper), Turnaway(), Ct)); // gate before roles
        Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        Assert.Equal("MEAS_OFF", ex.Details!["reason"].GetString());
        Assert.Equal(MeasCaptureService.KeyTurnaway, ex.Details!["key"].GetString());
        _settings.Values["meas.pack.people"] = false; // pack off switches the pulse off too
        Assert.Equal("MEAS_OFF", (await Assert.ThrowsAsync<HccException>(() => _svc.AddPulseAsync(Ctx(Owner), new AddMeasPulseRequest("HK", 3, 3, 3, 3, 7), Ct))).Details!["reason"].GetString());
        Assert.Empty(_store.Turnaway);
        Assert.Empty(_store.Pulses);
        Assert.Equal(0, _runner.Runs);
    }

    [Fact]
    public async Task F12_INT_CAP_the_same_Idempotency_Key_replays_one_row_and_one_audit()
    {
        var a = await _svc.LogTurnawayAsync(Ctx(Owner, "same"), Turnaway(), Ct);
        var b = await _svc.LogTurnawayAsync(Ctx(Owner, "same"), Turnaway(), Ct);
        Assert.Equal((false, true, a.Value), (a.Replayed, b.Replayed, b.Value));
        Assert.Single(_store.Turnaway);
        Assert.Single(_runner.Audits, x => x.Action == "MEAS_TURNAWAY_LOG");
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, (await Assert.ThrowsAsync<HccException>(() => _svc.LogTurnawayAsync(Ctx(Owner, "same"), Turnaway() with { Nights = 3 }, Ct))).Code);
    }

    [Fact]
    public async Task F12_INT_CAP_pulse_is_anonymous_no_runner_no_principal_in_the_row_or_the_contracts()
    {
        var r = await _svc.AddPulseAsync(Ctx(Housekeeper, "p-1"), new AddMeasPulseRequest("hk", 4, 5, 3, 3, 8), Ct);
        var again = await _svc.AddPulseAsync(Ctx(Housekeeper, "p-1"), new AddMeasPulseRequest("hk", 4, 5, 3, 3, 8), Ct);
        Assert.Equal(r.Id, again.Id);
        var row = Assert.Single(_store.Pulses).Row;
        Assert.Equal(new MeasPulseRow("2026-27-Q3", "HK", 4, 5, 3, 3, 8), row);
        Assert.Equal(0, _runner.Runs); // no idempotency row keyed by the principal, no audit row naming the user
        Assert.Empty(_runner.Audits);
        foreach (var t in new[] { typeof(AddMeasPulseRequest), typeof(MeasPulseRow), typeof(MeasPulseView) })
            Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("User", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Staff", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Principal", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("By", StringComparison.Ordinal) || p.Name.Contains("Actor", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _svc.AddPulseAsync(Ctx(Owner, "p-2"), new AddMeasPulseRequest("ZZ", 4, 5, 3, 3, 8), Ct))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _svc.AddPulseAsync(Ctx(Owner, "p-3"), new AddMeasPulseRequest("HK", 6, 5, 3, 3, 8), Ct))).Code);
    }

    [Fact]
    public async Task F12_INT_CAP_meter_readings_are_monotonic_per_meter()
    {
        _store.Readings.Add(("a", "MTR-1", new DateOnly(2026, 10, 20), 100m));
        _store.Readings.Add(("b", "MTR-1", new DateOnly(2026, 10, 22), 120m));
        var low = await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Owner, "r-1"), new MeasReadingAddRequest("MTR-1", 110m, "2026-10-24", null), Ct));
        Assert.Equal((HccErrorCode.VALIDATION, MeasCaptureService.NotMonotonicReason), (low.Code, low.Details!["reason"].GetString()));
        var backDated = await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Owner, "r-2"), new MeasReadingAddRequest("MTR-1", 125m, "2026-10-21", null), Ct));
        Assert.Equal(MeasCaptureService.NotMonotonicReason, backDated.Details!["reason"].GetString()); // above a later reading
        Assert.False((await _svc.ReadingAddAsync(Ctx(Owner, "r-3"), new MeasReadingAddRequest("MTR-1", 120m, "2026-10-24", null), Ct)).Replayed); // equal is allowed
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Owner, "r-4"), new MeasReadingAddRequest("MTR-1", 130m, "2026-10-25", null), Ct))).Code); // future date
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Assert.ThrowsAsync<HccException>(() => _svc.ReadingAddAsync(Ctx(Owner, "r-5"), new MeasReadingAddRequest("MTR-9", 1m, null, null), Ct))).Code);
        Assert.Equal(3, _store.Readings.Count);
    }

    [Fact]
    public async Task F12_INT_CAP_read_masks_DEC_061_money_and_HR_rows()
    {
        _store.View = new MeasCapturesView(
            [new("1", "2026-10-24T04:30:00.000Z", "2", "full", "2026-10-30", 2, "Deluxe", 1, 250000, "Walk-in", 6, null)],
            [new("1", "2026-10-24T04:30:00.000Z", "1", "77", "2026-10-24", "Overbooked", 150000, "1")], [], [], [], [], [], [], [],
            [new("12", 96, "B", 4500000)], [],
            [new("s1", "2026-10-24T04:30:00.000Z", "77", "Deluxe", "Direct", 5, 5, 5, 4, null, 9, null, true, "desk", "2")], [], [], [],
            [new("1", "9", "Fire safety", "2026-10-01", 2m, null, null, null, true)], [new("9", "Resigned", "2026-10-20")],
            [new("1", "2026-27-Q3", "HK", 4, 5, 3, 3, 8)], [], [new("OTB-1", "2026-10-24", "2026-10-25", 10, 2000000)], [],
            [new("RS-1", "2026-10-24", "2026-10-25", "CMP-1", "Deluxe", 240000, true, "manual", "1")], []);
        var hk = await _svc.CapturesAsync(1, Housekeeper, Ct);
        Assert.Null(hk.Turnaway[0].QuotedRate);
        Assert.Null(hk.Walked[0].CostPaise);
        Assert.Null(hk.AssetLife[0].ReplacementCostPaise);
        Assert.Null(hk.Otb[0].RevenuePaise);
        Assert.Null(hk.Rateshop[0].RatePaise);
        Assert.Equal((null, null), (hk.Surveys[0].Booking, hk.Surveys[0].EnteredBy));
        Assert.Empty(hk.Training);
        Assert.Empty(hk.Exit);
        Assert.Single(hk.Pulse);
        var json = JsonSerializer.Serialize(hk, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("quotedRate", json, StringComparison.Ordinal); // removed, not blanked
        Assert.DoesNotContain("replacementCostPaise", json, StringComparison.Ordinal);
        var owner = await _svc.CapturesAsync(1, Owner, Ct);
        Assert.Equal((250000L, 4500000L, 1, 1), (owner.Turnaway[0].QuotedRate!.Value, owner.AssetLife[0].ReplacementCostPaise!.Value, owner.Training.Count, owner.Exit.Count));
        var reception = await _svc.CapturesAsync(1, Reception, Ct);
        Assert.Null(reception.AssetLife[0].ReplacementCostPaise); // HkRules.CanSeeAssetCost: Owner, Manager, Accountant
        Assert.Empty(reception.Training);
    }

    [Fact]
    public void F12_INT_CAP_every_capture_route_has_a_known_measurement_key()
    {
        Assert.Equal(23, MeasCaptureEndpoints.Routes.Select(r => r.Command).Distinct().Count());
        foreach (var (command, key) in MeasCaptureEndpoints.Routes) Assert.True(MeasurementCatalogue.Item(key) is not null, $"{command} → {key}");
        Assert.Contains((MeasCaptureService.OpWalked, "meas.rev.overbooking"), MeasCaptureEndpoints.Routes); // the prototype's own guard key (turnaway.ts MD_KEY)
        Assert.Equal(["2026-09-07", "2026-09-14", "2026-09-21"], MeasCaptureService.Mondays(new(2026, 9, 1), new(2026, 9, 23)).Select(d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.NotEqual(MeasCaptureService.KeyGuid("pulse", "a"), MeasCaptureService.KeyGuid("pulse", "b"));
        Assert.Equal(64, MeasCaptureService.ClientId(new string('x', 90), 64).Length);
    }
}
