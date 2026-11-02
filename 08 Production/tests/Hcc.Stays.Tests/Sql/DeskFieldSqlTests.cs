using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>LF lf-desk through SQL: purpose / vehicle at check-in, Form C, the late check-out decision and the Form C register (migrations 4695–4699).</summary>
[Trait("Category", "Sql")]
public class DeskFieldSqlTests : IAsyncLifetime
{
    private P3World _w = null!;
    private StaysCommands _stays = null!;
    private DeskFieldService _desk = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Policy : IAccessPolicy
    {
        // Reception: check-in / check-out / ID details; Manager adds the approval right that waives a late charge.
        public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
            Has(principalId, permissionId) ? Task.CompletedTask : throw new Hcc.Domain.Errors.HccException(HccErrorCode.FORBIDDEN, "Access denied: " + permissionId);

        public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(Has(principalId, permissionId));

        private static bool Has(int principal, string perm) => perm != "PERM-APR-001" ? principal is Users.Reception or Users.Manager or Users.Owner : principal is Users.Manager or Users.Owner;
    }

    private sealed class NoSettings : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(ConfigValue.Null);

        public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => Task.FromResult(ConfigValue.Null);
    }

    public async ValueTask InitializeAsync()
    {
        _w = await P3World.CreateAsync(Ct);
        var conns = _w.Db.Platform.Connections;
        var billingStore = new SqlBillingStore(conns);
        var port = new BillingStayFolioPort(billingStore, _w.Billing);
        var policy = new Policy();
        _desk = new DeskFieldService(new SqlDeskFieldStore(conns), new NoSettings(), policy, port);
        _stays = new StaysCommands(_w.Db.Platform.Commands, policy, new SqlStaysStore(), port, _w.Clock, null, _desk);
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static GuestDraftDto Guest(string nationality, string idType, string? dob = null) => new("Desk Guest", "+91 98200 07777", "desk@example.test", "7 Demo Lane (fictional)", nationality, idType, "K777", Dob: dob);

    private static readonly FormCDto Form = new("Berlin", "2022-03-01", "2032-02-28", "V9988776", "Tourist", "Berlin", "2026-09-01", "2027-03-01", "Frankfurt", "2026-10-20", 10, "Goa", false, null,
        "1 Example Str, Berlin (fictional)", null, "F");

    private async Task<(long Res, string Ver)> RegisteredAsync(string room, string arrival, string departure, GuestDraftDto guest)
    {
        var res = await _w.Seed.ReservationAsync(room, arrival, departure, "Desk Guest", ct: Ct);
        await _w.Db.ExecAsync($"UPDATE inv.reservation SET nightly_rate_paise = 200000 WHERE reservation_id = {res};", Ct);
        await _stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "reg-" + res), new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), guest), Ct);
        return (res, await _w.Seed.ReservationVersionAsync(res, Ct));
    }

    private static CommandContext Ctx(P3World w, int user, string key) => w.Ctx(user, key);

    [Fact]
    public async Task Check_in_from_a_desk_form_needs_a_purpose_and_stores_purpose_vehicle_and_basis()
    {
        var (res, ver) = await RegisteredAsync("105", "2026-10-24", "2026-10-25", Guest("Indian", "Aadhaar (demo)"));
        var e = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-a"), new CheckInRequest(res, ver, Desk: true), Ct));
        Assert.Equal("Choose the purpose of visit.", e.Message);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-b"), new CheckInRequest(res, ver, "NOPE", Desk: true), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-c"), new CheckInRequest(res, ver, "TOURISM", "!!"), Ct));
        var ci = await _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-d"), new CheckInRequest(res, ver, "tourism", "mh24 ab  1234", "car", "24h", Desk: true), Ct);
        Assert.Equal("TOURISM|MH24 AB 1234|Car|24h", await _w.Db.StringAsync($"SELECT CONCAT(purpose_code, '|', vehicle_no, '|', vehicle_type, '|', RTRIM(checkout_basis)) FROM inv.reservation WHERE reservation_id = {res};", Ct));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_form_c WHERE stay_id = {ci.Value.StayId};", Ct));
    }

    [Fact]
    public async Task A_booking_without_desk_fields_still_checks_in_as_before()
    {
        var (res, ver) = await RegisteredAsync("106", "2026-10-24", "2026-10-25", Guest("Indian", "Aadhaar (demo)"));
        var ci = await _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-plain"), new CheckInRequest(res, ver), Ct);
        Assert.True(ci.Value.StayId > 0);
    }

    [Fact]
    public async Task A_foreign_guest_needs_a_valid_form_c_and_the_register_masks_the_visa_number()
    {
        var (res, ver) = await RegisteredAsync("107", "2026-10-24", "2026-10-25", Guest("German", "Passport (demo)", "1990-05-05"));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-f1"), new CheckInRequest(res, ver, "TOURISM"), Ct));
        var bad = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-f2"), new CheckInRequest(res, ver, "TOURISM", FormC: Form with { VisaExpiry = "2026-10-20" }), Ct));
        Assert.Contains("visa expires on or before", bad.Message);
        var ci = await _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-f3"), new CheckInRequest(res, ver, "TOURISM", FormC: Form), Ct);
        Assert.Equal("V9988776", await _w.Db.StringAsync($"SELECT visa_no FROM gst.stay_form_c WHERE stay_id = {ci.Value.StayId};", Ct));
        Assert.DoesNotContain("V9988776", await _w.Db.StringAsync("SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), detail), '|') FROM plat.audit_event;", Ct) ?? string.Empty);
        var reg = await _desk.RegisterAsync(_w.PropertyId, Users.Reception, "2026-10-01", "2026-10-31", Ct);
        var row = Assert.Single(reg.Rows, r => r.StayId == ci.Value.StayId);
        Assert.Equal("V9988776", row.FormC!.VisaNo); // Reception holds PERM-DOC-004 in this stand-in policy
        Assert.False(row.Missing);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _desk.RegisterAsync(_w.PropertyId, Users.Cashier, "2026-10-01", "2026-10-31", Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _desk.RegisterAsync(_w.PropertyId, Users.Reception, "soon", "2026-10-31", Ct));
    }

    [Fact]
    public async Task A_late_checkout_needs_a_decision_half_day_posts_a_service_line_and_waiving_needs_the_approval_right()
    {
        var (res, ver) = await RegisteredAsync("105", "2026-10-24", "2026-10-25", Guest("Indian", "Aadhaar (demo)"));
        var ci = await _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-l"), new CheckInRequest(res, ver, "TOURISM"), Ct);
        var stay = ci.Value.StayId;
        _w.Clock.Now = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero); // 13:30 IST on the departure day, due out 11:00 + 1 h grace
        var sv = await _w.Seed.StayVersionAsync(stay, Ct);
        var refused = await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _stays.CheckoutStayAsync(Ctx(_w, Users.Reception, "co-l1"), new CheckoutStayRequest(stay, sv), Ct));
        Assert.StartsWith("Guest is past the due-out time by 2:30", refused.Message);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _stays.PostLateCheckoutChargeAsync(Ctx(_w, Users.Reception, "lc-w"), new LateCheckoutChargeRequest(stay, sv, "waived", "Goodwill"), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _stays.PostLateCheckoutChargeAsync(Ctx(_w, Users.Manager, "lc-w2"), new LateCheckoutChargeRequest(stay, sv, "waived", "x"), Ct));
        var half = await _stays.PostLateCheckoutChargeAsync(Ctx(_w, Users.Reception, "lc-h"), new LateCheckoutChargeRequest(stay, sv, "half-day"), Ct);
        Assert.Equal("half-day", half.Value.Decision);
        Assert.Equal(100000, half.Value.AmountPaise); // 50 % of the ₹2,000 night
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM bill.charge c JOIN gst.stay s ON s.folio_id = c.folio_id WHERE s.stay_id = {stay} AND c.label = N'Late check-out · half day' AND c.kind = 'service' AND c.tax_bps = 500;", Ct));
        Assert.Equal("half-day", await _w.Db.StringAsync($"SELECT late_checkout_decision FROM gst.stay WHERE stay_id = {stay};", Ct));
        sv = await _w.Seed.StayVersionAsync(stay, Ct); // the decision changed the stay row
        await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _stays.PostLateCheckoutChargeAsync(Ctx(_w, Users.Reception, "lc-h2"), new LateCheckoutChargeRequest(stay, sv, "half-day"), Ct));
        // decided: the checkout now fails on the folio (unpaid nights), not on the late rule
        var next = await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _stays.CheckoutStayAsync(Ctx(_w, Users.Reception, "co-l2"), new CheckoutStayRequest(stay, sv), Ct));
        Assert.DoesNotContain("past the due-out", next.Message);
    }

    [Fact]
    public async Task A_manager_can_waive_the_late_charge_with_a_reason()
    {
        var (res, ver) = await RegisteredAsync("105", "2026-10-24", "2026-10-25", Guest("Indian", "Aadhaar (demo)"));
        var ci = await _stays.CheckInAsync(Ctx(_w, Users.Reception, "ci-w"), new CheckInRequest(res, ver, "TOURISM"), Ct);
        _w.Clock.Now = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
        var sv = await _w.Seed.StayVersionAsync(ci.Value.StayId, Ct);
        var w = await _stays.PostLateCheckoutChargeAsync(Ctx(_w, Users.Manager, "lc-ok"), new LateCheckoutChargeRequest(ci.Value.StayId, sv, "waived", "Flight delayed"), Ct);
        Assert.Equal(("waived", 0), (w.Value.Decision, w.Value.AmountPaise));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM bill.charge c JOIN gst.stay s ON s.folio_id = c.folio_id WHERE s.stay_id = {ci.Value.StayId} AND c.label LIKE N'Late check-out%';", Ct));
    }
}
