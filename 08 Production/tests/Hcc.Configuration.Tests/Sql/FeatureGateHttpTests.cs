using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Configuration;
using Hcc.Configuration.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Closing;
using Hcc.Contracts.Customers;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.Reminders;
using Hcc.Domain.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>
/// ADM-1 [ADM1] sub-function switches on the server (DEC-214 / DEC-215 / DEC-218), whole composition over real HTTP: a switched-off
/// feature's command routes answer FORBIDDEN with the prototype's sentence, <c>details.reason</c> MODULE_OFF and <c>details.featureId</c>;
/// reads keep answering; switching a feature off takes its dependants down (Expenses → Purchases) and switching it on does not bring them
/// back; core features have no switch; the Tech admin may switch (non-money); the route metadata pins which commands are gated and no read is.
/// </summary>
[Trait("Category", "Sql")]
public sealed class FeatureGateHttpTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Command routes gated per feature on the server today (the rest of the catalogue is gated in the prototype; ADM-2 extends this).</summary>
    private static readonly Dictionary<string, string[]> Gated = new(StringComparer.Ordinal)
    {
        [FeatureIds.Reservations] = ["AmendReservation", "SetReservationCompany", "CancelReservation", "MarkNoShow", "ConfirmHold"], // CreateReservation: inside ReservationService (walk-in exempt)
        [FeatureIds.Alerts] = ["SaveReminder", "MarkReminderDone", "DismissReminder", "SnoozeWakeUp"],
        [FeatureIds.Keys] = ["RequestKey", "CancelKey", "ReadKey", "RecordManualKey", "SaveKeyLock", "RegisterKeyEncoder"],
        [FeatureIds.CashBook] = ["LockCashBookDay", "ReopenCashBookDay"],
        [FeatureIds.CounterBills] = ["IssueCounterBill", "RequestCounterBillVoid"],
        [FeatureIds.Handover] = ["SaveHandoverTemplate", "ToggleHandoverTemplate", "TickChecklistItem", "SubmitChecklistRun", "ReopenChecklistRun", "SubmitHandoverNote", "AcknowledgeHandoverNote", "CompleteCarryItem"],
        [FeatureIds.Crm] = ["ProposeCustomerMerge", "DecideCustomerMerge", "AddCustomerTag", "RemoveCustomerTag", "AddCustomerNote", "SetCustomerPreferences", "SetCustomerConsent"],
        [FeatureIds.Yield] = ["SaveYieldPlan", "SaveYieldSeason", "SetYieldDayOfWeek", "SaveYieldOccupancyRule", "SetYieldGuardrails", "SaveYieldRestriction", "SetYieldOverride", "SetYieldForecast", "PublishYieldTariffs", "ProposeYieldPublish", "DecideYieldProposal"],
        [FeatureIds.Purchases] = ["RecordVendorPayment", "VoidVendorPayment", "RecordExpenseReturn", "ReallocateVendorPayment"],
        [FeatureIds.AssetRegister] = ["SetAssetPutToUse", "DisposeFixedAsset"],
        [FeatureIds.Vouchers] = ["SaveManualVoucher", "DecideManualVoucher", "VoidManualVoucher", "AddLoan"],
        [FeatureIds.Gst] = ["PostGstSetoff", "RecordGstPayment", "RecordStatutoryRemittance", "LockAccountingMonth", "RequestAccountingMonthLock", "UnlockAccountingMonth", "SetGstOnAdvances"],
        [FeatureIds.Tds] = ["SetTdsSettings", "SetTdsRate", "RecordTdsRemittance", "SetTdsForm168"],
        [FeatureIds.Compliance] = ["MarkComplianceDone", "MarkComplianceNotApplicable", "ReopenComplianceItem", "SetComplianceFy", "SaveComplianceProfile", "ToggleComplianceArchive",
            "ToggleComplianceCategoryArchive", "SaveCustomObligation", "DeleteCustomObligation", "AddComplianceEvidence", "DeleteComplianceEvidence", "SaveComplianceSettings", "GenerateCaPack"],
        [FeatureIds.LegacyImport] = ["legacy.import"],
        // ADM follow-up (3 Nov): guest services, leave, POS menu, templates, maintenance assets, plans & extras, masters. Menu / templates / assets also sit under their module gate.
        [FeatureIds.GuestRequests] = ["LogServiceRequest", "AssignServiceRequest", "StartServiceRequest", "CompleteServiceRequest", "CancelServiceRequest", "SaveGuestServiceSettings"],
        [FeatureIds.Complaints] = ["LogComplaint", "UpdateComplaint", "ResolveComplaint", "RecordComplaintFollowUp", "OfferRecoveryDiscount"],
        [FeatureIds.LostFound] = ["LogLostItem", "ClaimLostItem", "HandOverLostItem", "DisposeLostItem"],
        [FeatureIds.Leave] = ["RequestLeave", "DecideLeave", "CancelLeave", "ClearLeave", "SetLeavePolicy", "SetLeaveEntOverride", "AddLeaveBlackout", "RemoveLeaveBlackout", "AddLeaveAgenda", "RemoveLeaveAgenda", "ImportLeave"],
        [FeatureIds.PosMenu] = ["SaveMenuItem", "DecideMenuChange", "SetMenuItemAvailability"],
        [FeatureIds.MessageTemplates] = ["UpdateMessageTemplate", "UpdateMessageRule"],
        [FeatureIds.MaintenanceAssets] = ["SavePmPlan", "SaveMaintenanceAsset", "SaveAmc", "GeneratePmWorkOrders"],
        [FeatureIds.PlansExtras] = ["SaveMealPlan", "RetireMealPlan", "ReactivateMealPlan", "DeleteMealPlan", "SaveExtraItem", "RetireExtraItem", "ReactivateExtraItem", "DeleteExtraItem", "ReorderMealPlans", "ReorderExtraItems"],
        [FeatureIds.Masters] = ["SaveFloor", "RetireFloor", "ReactivateFloor", "DeleteFloor", "ReorderFloors", "SaveRoomType", "RetireRoomType", "ReactivateRoomType", "DeleteRoomType", "ReorderRoomTypes", "SaveAmenity", "RetireAmenity", "ReactivateAmenity", "DeleteAmenity", "ReorderAmenities", "SaveOccupancyCharge", "RetireOccupancyCharge", "ReactivateOccupancyCharge", "DeleteOccupancyCharge", "ReorderOccupancyCharges", "SavePaymentMethod", "RetirePaymentMethod", "ReactivatePaymentMethod", "DeletePaymentMethod", "ReorderPaymentMethods", "SaveBankAccount", "RetireBankAccount", "ReactivateBankAccount", "DeleteBankAccount", "ReorderBankAccounts", "SaveExpenseCategory", "RetireExpenseCategory", "ReactivateExpenseCategory", "DeleteExpenseCategory", "ReorderExpenseCategories", "SaveExpenseVendor", "RetireExpenseVendor", "ReactivateExpenseVendor", "DeleteExpenseVendor", "ReorderExpenseVendors", "SaveDiscountReason", "RetireDiscountReason", "ReactivateDiscountReason", "DeleteDiscountReason", "ReorderDiscountReasons", "SaveHkTaskType", "RetireHkTaskType", "ReactivateHkTaskType", "DeleteHkTaskType", "ReorderHkTaskTypes", "SaveHkZone", "RetireHkZone", "ReactivateHkZone", "DeleteHkZone", "ReorderHkZones", "SaveComplaintCategory", "RetireComplaintCategory", "ReactivateComplaintCategory", "DeleteComplaintCategory", "ReorderComplaintCategories", "SaveLostFoundCategory", "RetireLostFoundCategory", "ReactivateLostFoundCategory", "DeleteLostFoundCategory", "ReorderLostFoundCategories", "SaveBookingSource", "RetireBookingSource", "ReactivateBookingSource", "DeleteBookingSource", "ReorderBookingSources", "SaveIdType", "RetireIdType", "ReactivateIdType", "DeleteIdType", "ReorderIdTypes", "SaveNumberSeries", "RetireNumberSeries", "ReactivateNumberSeries", "DeleteNumberSeries", "ReorderNumberSeries", "SavePurpose", "RetirePurpose", "ReactivatePurpose", "DeletePurpose", "ReorderPurposes", "SaveVehicleType", "RetireVehicleType", "ReactivateVehicleType", "DeleteVehicleType", "ReorderVehicleTypes", "SaveBookingPolicy", "RetireBookingPolicy", "ReactivateBookingPolicy", "DeleteBookingPolicy", "ReorderBookingPolicies", "SaveDesignation", "RetireDesignation", "ReactivateDesignation", "DeleteDesignation", "ReorderDesignations", "SaveShiftPattern", "RetireShiftPattern", "ReactivateShiftPattern", "DeleteShiftPattern", "ReorderShiftPatterns", "SaveLeaveType", "RetireLeaveType", "ReactivateLeaveType", "DeleteLeaveType", "ReorderLeaveTypes", "SaveHoliday", "RetireHoliday", "ReactivateHoliday", "DeleteHoliday", "ReorderHolidays", "AddRooms", "SaveRoom", "RenumberRoom", "RetireRoom", "ReactivateRoom", "DeleteRoom"],
    };

    private WebApplication _app = null!;
    private Uri _base = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHcc(builder.Configuration, db.Platform);
        _app = builder.Build();
        _app.MapHcc();
        await _app.StartAsync(Ct);
        _base = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    private async Task<HttpClient> SignInAsync(int userId)
    {
        var login = await db.PasswordAsync(userId, "Sand-and-Maroon-26");
        var c = new HttpClient { BaseAddress = _base };
        using var r = await c.PostAsJsonAsync(ApiConventions.BasePath + "/auth/sign-in", new SignInRequest(login, Password: "Sand-and-Maroon-26", ReturnToken: true), Json, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, body);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JsonSerializer.Deserialize<SignInResult>(body, Json)!.SessionToken);
        return c;
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(HttpClient c, string path, object body)
    {
        using var m = new HttpRequestMessage(HttpMethod.Post, ApiConventions.BasePath + path) { Content = JsonContent.Create(body, options: Json) };
        m.Headers.Add(ApiConventions.IdempotencyKeyHeader, ConfigDatabase.Key());
        using var r = await c.SendAsync(m, Ct);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient c, string path)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
        return r.StatusCode;
    }

    private static bool IsFeatureOff((HttpStatusCode Status, string Body) r) =>
        r.Status == HttpStatusCode.Forbidden && r.Body.Contains("\"featureId\"", StringComparison.Ordinal);

    private static void AssertFeatureOff((HttpStatusCode Status, string Body) r, string featureId, string what)
    {
        Assert.True(r.Status == HttpStatusCode.Forbidden, $"{what}: {(int)r.Status} {r.Body}");
        var e = JsonSerializer.Deserialize<ErrorResponse>(r.Body, Json)!;
        Assert.Equal("FORBIDDEN", e.Code);
        Assert.Equal(FeatureCatalog.FeatureOffMessage(FeatureCatalog.Feature(featureId)!), e.Message);
        Assert.NotNull(e.Details);
        Assert.Equal(ConfigCatalog.ModuleOffReason, e.Details!["reason"].GetString());
        Assert.Equal(featureId, e.Details["featureId"].GetString());
    }

    [Fact]
    public void Gated_command_routes_are_exactly_the_listed_ones_core_features_gate_nothing_and_no_read_is_gated()
    {
        var endpoints = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
        static string Name(RouteEndpoint e) => e.RoutePattern.RawText!.TrimEnd('/').Split('/')[^1];
        static bool IsGet(RouteEndpoint e) => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true;
        var commands = endpoints.Where(e => e.RoutePattern.RawText!.Contains("/commands/", StringComparison.Ordinal) && !IsGet(e)).ToList();

        var gated = commands.Where(e => e.Metadata.GetMetadata<HccFeatureGateMetadata>() is not null)
            .GroupBy(e => e.Metadata.GetMetadata<HccFeatureGateMetadata>()!.FeatureId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(Name).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        foreach (var (feature, names) in Gated)
            Assert.Equal(names.Order(StringComparer.Ordinal), gated.GetValueOrDefault(feature) ?? []);
        Assert.Empty(gated.Keys.Except(Gated.Keys, StringComparer.Ordinal));
        Assert.All(gated.Keys, id => Assert.False(FeatureCatalog.Feature(id)!.Core, id));

        // A route carrying both gates (module, then feature) must have a feature that belongs to that very module switch.
        foreach (var e in commands)
        {
            var feature = e.Metadata.GetMetadata<HccFeatureGateMetadata>();
            var moduleMeta = e.Metadata.GetMetadata<HccModuleGateMetadata>();
            if (feature is not null && moduleMeta is not null)
                Assert.Equal(moduleMeta.ModuleId, FeatureCatalog.ModuleOf(feature.FeatureId)!.Switch);
        }
        foreach (var name in new[] { "CreateReservation", "RegisterGuest", "CheckIn", "CheckoutStay", "RecordReceipt", "RunBusinessDayClose", "SetSetting", "CreateUser", "IssueInvoice" })
            Assert.All(commands.Where(e => Name(e) == name), e => Assert.Null(e.Metadata.GetMetadata<HccFeatureGateMetadata>()));
        Assert.DoesNotContain(endpoints, e => IsGet(e) && e.Metadata.GetMetadata<HccFeatureGateMetadata>() is not null);
    }

    [Fact]
    public async Task A_switched_off_feature_refuses_its_commands_keeps_its_reads_leaves_others_alone_and_works_again_when_on()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        var probes = new (string Feature, string Path, Func<object> Body, string Read)[]
        {
            (FeatureIds.CashBook, "/commands/LockCashBookDay", () => new LockCashBookDayRequest(new DateOnly(2026, 10, 24)), "/closing/business-date"),
            (FeatureIds.Alerts, "/commands/SaveReminder", () => new SaveReminderRequest(null, "Gate test wake-up", "2026-10-25", "06:30"), "/closing/business-date"),
            (FeatureIds.Crm, "/commands/AddCustomerTag", () => new AddCustomerTagRequest("no-such-customer", "vip"), "/closing/business-date"),
            (FeatureIds.Tds, "/commands/SetTdsSettings", () => new SetTdsSettingsRequest(true, "firm"), "/closing/business-date"),
        };

        foreach (var p in probes)
        {
            var on = await PostAsync(owner, p.Path, p.Body());
            Assert.False(IsFeatureOff(on), $"{p.Feature} on: {(int)on.Status} {on.Body}");

            await db.SetAsync(h, h.Owner, FeatureIds.SettingKey(p.Feature), false);
            AssertFeatureOff(await PostAsync(owner, p.Path, p.Body()), p.Feature, p.Feature + " off");
            Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(owner, p.Read));
            var other = probes.First(o => o.Feature != p.Feature);
            Assert.False(IsFeatureOff(await PostAsync(owner, other.Path, other.Body())), $"{other.Feature} must not be affected by {p.Feature}");

            await db.SetAsync(h, h.Owner, FeatureIds.SettingKey(p.Feature), true);
            var again = await PostAsync(owner, p.Path, p.Body());
            Assert.False(IsFeatureOff(again), $"{p.Feature} back on: {(int)again.Status} {again.Body}");
        }
    }

    [Fact]
    public async Task Expenses_off_takes_Purchases_down_with_it_and_Expenses_on_does_not_bring_Purchases_back()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        object Pay() => new RecordVendorPaymentRequest("Gate Test Supplies (fictional)", 10_000, "Cash");

        Assert.False(IsFeatureOff(await PostAsync(owner, "/commands/RecordVendorPayment", Pay())));
        await db.SetAsync(h, h.Owner, FeatureIds.SettingKey(FeatureIds.Expenses), false);
        Assert.Equal(ConfigValue.Of(false), await db.ValueAsync(h, FeatureIds.SettingKey(FeatureIds.Purchases))); // cascaded, same date
        AssertFeatureOff(await PostAsync(owner, "/commands/RecordVendorPayment", Pay()), FeatureIds.Purchases, "purchases via expenses");
        Assert.False(await _app.Services.GetRequiredService<IModuleGate>().IsFeatureOnAsync(h.PropertyId, FeatureIds.Purchases, Ct));

        await db.SetAsync(h, h.Owner, FeatureIds.SettingKey(FeatureIds.Expenses), true);
        Assert.Equal(ConfigValue.Of(false), await db.ValueAsync(h, FeatureIds.SettingKey(FeatureIds.Purchases)));
        AssertFeatureOff(await PostAsync(owner, "/commands/RecordVendorPayment", Pay()), FeatureIds.Purchases, "purchases stays off");

        await db.SetAsync(h, h.Owner, FeatureIds.SettingKey(FeatureIds.Purchases), true);
        Assert.False(IsFeatureOff(await PostAsync(owner, "/commands/RecordVendorPayment", Pay())));
    }

    [Fact]
    public async Task A_required_feature_off_gates_the_dependant_even_when_the_dependant_was_never_switched_itself()
    {
        var h = await db.NewHotelAsync();
        var gate = _app.Services.GetRequiredService<IModuleGate>();
        Assert.True(await gate.IsFeatureOnAsync(h.PropertyId, FeatureIds.OtaPayouts, Ct));
        await db.SetAsync(h, h.Owner, ConfigModuleIds.SettingKey(ConfigModuleIds.Pos), false);   // a DEC-057 module switch
        Assert.False(await gate.IsFeatureOnAsync(h.PropertyId, FeatureIds.PosMenu, Ct));
        Assert.True(await gate.IsFeatureOnAsync(h.PropertyId, "frontdesk.checkin", Ct));           // core: always on
        var e = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => gate.EnsureFeatureOnAsync(h.PropertyId, FeatureIds.PosMenu, Ct));
        Assert.Equal(ConfigModuleIds.Pos, e.Details!["moduleId"].GetString());
    }

    [Fact]
    public async Task Core_features_have_no_switch_and_the_Tech_admin_may_switch_a_sub_function()
    {
        var h = await db.NewHotelAsync();
        var e = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => db.SetAsync(h, h.Owner, "feature.frontdesk.checkin", false));
        Assert.Equal(SettingRules.NotConfigurable, e.Message);

        var r = await db.SetAsync(h, h.TechAdmin, FeatureIds.SettingKey(FeatureIds.Handover), false);   // non-money: DEC-058 / DEC-218
        Assert.Equal("applied", r.Status);
        Assert.Equal(ConfigValue.Of(false), await db.ValueAsync(h, FeatureIds.SettingKey(FeatureIds.Handover)));
        await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => db.SetAsync(h, h.Manager, FeatureIds.SettingKey(FeatureIds.Handover), true));
    }
}
