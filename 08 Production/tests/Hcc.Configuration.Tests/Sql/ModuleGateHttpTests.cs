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
using Hcc.Contracts.Configuration;
using Hcc.Contracts.Documents;
using Hcc.Contracts.Groups;
using Hcc.Contracts.Maintenance;
using Hcc.Contracts.Messaging;
using Hcc.Contracts.Ota;
using Hcc.Contracts.Payroll;
using Hcc.Contracts.Pos;
using Hcc.Contracts.Stock;
using Hcc.Contracts.WebBooking;
using Hcc.Domain.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>
/// A11-01 / DEC-057 on the server: Master configuration › Modules on/off is enforced on the command routes of every switchable module
/// (whole composition over real HTTP, real P1 policy and role templates). A switched-off module's commands answer FORBIDDEN with the
/// prototype's sentence, <c>details.reason</c> MODULE_OFF and <c>details.moduleId</c>; its reads keep answering (data kept); core
/// commands never stop; core modules cannot be switched off.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ModuleGateHttpTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The command routes each switchable module must gate — exactly these (prototype CFG_MODULES prefixes ↔ server operations).</summary>
    private static readonly Dictionary<string, string[]> Gated = new(StringComparer.Ordinal)
    {
        [ConfigModuleIds.Pos] = ["CreateOutlet", "SaveMenuItem", "DecideMenuChange", "SetMenuItemAvailability", "OpenCheck", "AddCheckItem", "SetCheckItemQty",
            "SendKot", "AdvanceKot", "SettleCheck", "VoidCheckItem", "VoidBill", "DecideVoid"],
        [ConfigModuleIds.Groups] = ["CreateGroup", "UpdateGroup", "SetGroupRouting", "AttachGroupMember", "DetachGroupMember", "ApplyGroupRouting", "RerouteGroupCharge",
            "TransferGroupMember", "RecordGroupReceipt", "RequestGroupAttrition", "DecideGroupAttrition", "SettleGroup", "SaveCompany", "SetCompanyLimit",
            "DecideCompanyLimit", "RecordCompanyPayment", "CreateGroupBlock", "ReleaseGroupBlock"],
        [ConfigModuleIds.Ota] = ["SaveChannel", "SetChannelStatus", "SetChannelStopSell", "PushChannelRates", "ImportChannelMessage", "QueueSimulatedMessage",
            "PullChannelMessages", "AcceptChannelMessage", "RejectChannelMessage"],
        [ConfigModuleIds.Website] = ["ConfirmWebRequest", "DeclineWebRequest", "CreateWebDepositLink", "MarkWebDepositPaid"],
        [ConfigModuleIds.Messaging] = ["SendMessage", "RunMessagingTriggers", "SimulateMessageDelivery", "RetryMessage", "SetMessagingConsent", "UpdateMessageTemplate",
            "UpdateMessageRule"],
        [ConfigModuleIds.Documents] = ["CaptureIdDocument", "ConfirmIdCapture", "RejectIdCapture"],
        [ConfigModuleIds.Assistant] = [], // no server routes yet (the management assistant is prototype-only)
        [ConfigModuleIds.Inventory] = ["StockReceiveGrn", "StockIssue", "StockUse", "StockRoomChange", "StockLaundrySend", "StockLaundryReceive", "StockRecordLoss",
            "StockCountSubmit", "StockCountDecide", "StockItemCreate", "StockItemSet", "StockSettingsSet"],
        [ConfigModuleIds.Maintenance] = ["CreateWorkOrder", "ConvertRepairToWorkOrder", "AssignWorkOrder", "StartWorkOrder", "HoldWorkOrder", "AddWorkOrderPart",
            "CompleteWorkOrder", "VerifyWorkOrder", "CancelWorkOrder", "BlockWorkOrderRoom", "GeneratePmWorkOrders", "SavePmPlan", "SaveMaintenanceAsset",
            "SaveMaintenanceVendor", "SaveAmc", "ReceiveSpare"],
        [ConfigModuleIds.Payroll] = ["GiveAdvance", "SetStatutoryRates", "PreparePayRun", "LockPayRun", "UnlockPayRun", "IssueBankFile", "SendPayslip", "PrepareFnf",
            "ConfirmFnf", "DiscardFnf"],
    };

    /// <summary>Command routes that must stay open whatever is switched off (core modules, attendance and leave, audited views, erasure).</summary>
    private static readonly string[] NeverGated =
    [
        "SetSetting", "DecideSettingRequest", "CreateReservation", "CheckIn", "CheckoutStay", "PostCharge", "RecordReceipt", "OpenShift", "RunBusinessDayClose",
        "CreateUser", "QuoteStay", "TransitionHousekeepingTask", "RequestRepair", "SetReservationCompany", "IssueInvoice",
        "SaveDepartment", "SaveStaff", "SetRoster", "MarkAttendance", "RequestLeave", "DecideLeave", "CorrectAttendance", "DecidePayrollRequest", "SaveHoliday",
        "CancelLeave", "SetLeavePolicy", "SetLeaveEntOverride", "AddLeaveBlackout", "RemoveLeaveBlackout", "AddLeaveAgenda", "RemoveLeaveAgenda", "ImportLeave", "ClearLeave",   // SCC LeaveDesk (DEC-116)
        "ViewIdCaptureImage", "RevealIdCaptureNumber", "PurgeIdCaptures", "RevealGuestId",
    ];

    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, 40).Select(i => (byte)(i * 7 + 3))]);

    private WebApplication _app = null!;
    private Uri _base = null!;
    private int _n;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // "Testing", not the default Production: Production registers the not-configured OCR / messaging adapters (A09-08, Wave C8), which
        // would refuse before the module gate this test is about.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Document capture needs the guest-ID key (development plain key; fictional data only).
            ["Hcc:GuestIdKeys:Current"] = "gate1",
            ["Hcc:GuestIdKeys:Keys:gate1"] = "plain:" + Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            ["Hcc:GuestIdKeys:AllowPlainKeys"] = "true",
        });
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

    private static void AssertModuleOff((HttpStatusCode Status, string Body) r, string moduleId, string what)
    {
        Assert.True(r.Status == HttpStatusCode.Forbidden, $"{what}: {(int)r.Status} {r.Body}");
        var e = JsonSerializer.Deserialize<ErrorResponse>(r.Body, Json)!;
        Assert.Equal("FORBIDDEN", e.Code);
        Assert.Equal(ConfigCatalog.ModuleOffMessage(ConfigCatalog.Module(moduleId)!), e.Message);
        Assert.NotNull(e.Details);
        Assert.Equal(ConfigCatalog.ModuleOffReason, e.Details!["reason"].GetString());
        Assert.Equal(moduleId, e.Details["moduleId"].GetString());
    }

    private string Next() => Interlocked.Increment(ref _n).ToString("00", CultureInfo.InvariantCulture);

    [Fact]
    public void Every_switchable_module_gates_exactly_its_command_routes_and_no_read_route()
    {
        var endpoints = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
        static string Name(RouteEndpoint e) => e.RoutePattern.RawText!.TrimEnd('/').Split('/')[^1];
        static bool IsGet(RouteEndpoint e) => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true;
        var commands = endpoints.Where(e => e.RoutePattern.RawText!.Contains("/commands/", StringComparison.Ordinal) && !IsGet(e)).ToList();
        Assert.True(commands.Count > 150, $"only {commands.Count} command routes found");

        Assert.Equal(ConfigCatalog.Modules.Select(m => m.Id).Order(StringComparer.Ordinal), Gated.Keys.Order(StringComparer.Ordinal));
        var gated = commands.Where(e => e.Metadata.GetMetadata<HccModuleGateMetadata>() is not null)
            .GroupBy(e => e.Metadata.GetMetadata<HccModuleGateMetadata>()!.ModuleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(Name).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        foreach (var (module, names) in Gated)
            Assert.Equal(names.Order(StringComparer.Ordinal), gated.GetValueOrDefault(module) ?? []);
        Assert.Empty(gated.Keys.Except(Gated.Keys, StringComparer.Ordinal));

        var byName = commands.ToLookup(Name, StringComparer.Ordinal);
        foreach (var name in NeverGated)
        {
            Assert.True(byName.Contains(name), $"{name} is not a command route");
            Assert.All(byName[name], e => Assert.Null(e.Metadata.GetMetadata<HccModuleGateMetadata>()));
        }

        // Reads are never gated (DEC-057: data is kept and stays readable).
        Assert.DoesNotContain(endpoints, e => IsGet(e) && e.Metadata.GetMetadata<HccModuleGateMetadata>() is not null);
    }

    [Fact]
    public async Task Each_switched_off_module_refuses_its_commands_with_MODULE_OFF_keeps_its_reads_and_works_again_when_on()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        var guest = Convert.ToInt64(await db.ScalarAsync(
            "INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES (@p, N'Gate Test Guest (fictional)', '9000000001', 'manual', '2026-10-24');",
            ("@p", h.PropertyId)), CultureInfo.InvariantCulture);

        // One representative command per module (the Owner may run all of them), the status it answers while the module is on, and a read.
        var probes = new (string Module, string Path, Func<object> Body, HttpStatusCode On, string Read)[]
        {
            (ConfigModuleIds.Pos, "/commands/CreateOutlet", () => new CreateOutletRequest("GT" + Next(), "Gate test terrace", "table", ["T1"]), HttpStatusCode.OK, "/pos/outlets"),
            (ConfigModuleIds.Groups, "/commands/SaveCompany", () => new SaveCompanyRequest(null, null, "Gate Test Traders " + Next()), HttpStatusCode.OK, "/groups/companies"),
            (ConfigModuleIds.Ota, "/commands/SaveChannel", () => new SaveChannelRequest(null, null, "GATE-" + Next(), "Gate test channel", 1500, 0, null), HttpStatusCode.OK, "/ota/channels"),
            // No web request exists: while on, the command reaches the module and answers NOT_FOUND (the full public flow is in Hcc.WebBooking.Tests).
            (ConfigModuleIds.Website, "/commands/DeclineWebRequest", () => new DeclineWebRequestRequest(999_999, "Gate test"), HttpStatusCode.NotFound, "/web-booking/requests"),
            (ConfigModuleIds.Messaging, "/commands/SetMessagingConsent", () => new SetMessagingConsentRequest(guest, WhatsApp: int.Parse(Next(), CultureInfo.InvariantCulture) % 2 == 0), HttpStatusCode.OK, "/messaging/summary"),
            (ConfigModuleIds.Documents, "/commands/CaptureIdDocument", () => new CaptureIdDocumentRequest(guest, null, "passport", "image/png", Png), HttpStatusCode.OK, "/documents/id-captures"),
            (ConfigModuleIds.Inventory, "/commands/StockItemCreate", () => new CreateStockItemRequest("AMN-G" + Next(), "Gate test soap", "amenity", "pcs", 10, 20), HttpStatusCode.OK, "/stock/items"),
            (ConfigModuleIds.Maintenance, "/commands/SaveMaintenanceVendor", () => new SaveVendorRequest(null, null, "Gate Test Electricals " + Next(), "Electrical", "+91 90000 112" + Next()), HttpStatusCode.OK, "/maintenance/registers"),
            (ConfigModuleIds.Payroll, "/commands/SetStatutoryRates", () => new SetStatutoryRatesRequest(OtBps: 20000 + int.Parse(Next(), CultureInfo.InvariantCulture)), HttpStatusCode.OK, "/payroll/staff"),
        };
        Assert.Equal(ConfigCatalog.Modules.Count - 1, probes.Length); // every switchable module with server routes (the assistant has none)

        foreach (var p in probes)
        {
            var on = await PostAsync(owner, p.Path, p.Body());
            Assert.True(on.Status == p.On, $"{p.Module} on: {(int)on.Status} {on.Body}");

            await db.SetAsync(h, h.Owner, ConfigModuleIds.SettingKey(p.Module), false);
            AssertModuleOff(await PostAsync(owner, p.Path, p.Body()), p.Module, p.Module + " off");
            Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(owner, p.Read));                                              // data kept, still readable
            var others = probes.Where(o => o.Module != p.Module).Take(1).Single();
            Assert.True((await PostAsync(owner, others.Path, others.Body())).Status == others.On, $"{others.Module} must not be affected by {p.Module}");

            await db.SetAsync(h, h.Owner, ConfigModuleIds.SettingKey(p.Module), true);
            var again = await PostAsync(owner, p.Path, p.Body());
            Assert.True(again.Status == p.On, $"{p.Module} back on: {(int)again.Status} {again.Body}");
        }

        // The prototype's exact sentence (cfgModuleOffMessage) reaches the client.
        await db.SetAsync(h, h.Owner, "module.pos", false);
        var refused = JsonSerializer.Deserialize<ErrorResponse>((await PostAsync(owner, "/commands/CreateOutlet", probes[0].Body())).Body, Json)!;
        Assert.Equal("Restaurant & POS is switched off in Master configuration › Modules on/off. Its data is kept; the Owner or Tech admin can switch it back on.", refused.Message);
    }

    [Fact]
    public async Task With_every_module_off_core_commands_attendance_and_the_ID_erasure_still_work()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        foreach (var m in ConfigCatalog.Modules) await db.SetAsync(h, h.Owner, ConfigModuleIds.SettingKey(m.Id), false);
        var modules = JsonSerializer.Deserialize<ModulesResult>(await (await owner.GetAsync(ApiConventions.BasePath + "/configuration/modules", Ct)).Content.ReadAsStringAsync(Ct), Json)!;
        Assert.All(modules.Modules, m => Assert.False(m.On));

        // Payroll off: the pay-run side is refused, staff / attendance / leave stay on (prototype "attendance and leave stay on").
        AssertModuleOff(await PostAsync(owner, "/commands/PreparePayRun", new PreparePayRunRequest("2026-09")), ConfigModuleIds.Payroll, "PreparePayRun");
        var dept = await PostAsync(owner, "/commands/SaveDepartment", new SaveDepartmentRequest("GT", "Gate test desk"));
        Assert.True(dept.Status == HttpStatusCode.OK, dept.Body);
        // Core: Master configuration itself (and with it switching a module back on) never stops.
        var core = await PostAsync(owner, "/commands/SetSetting", new SetSettingRequest("brand.printFormat", ConfigDatabase.J("A5")));
        Assert.True(core.Status == HttpStatusCode.OK, core.Body);
        // Document capture off: the manual erasure stays available (DEC-071, a guest's request), so does the masked list.
        var purge = await PostAsync(owner, "/commands/PurgeIdCaptures", new PurgeIdCapturesRequest("Guest asked for erasure (test)"));
        Assert.True(purge.Status == HttpStatusCode.OK, purge.Body);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(owner, "/documents/id-captures"));
        // Group blocks belong to Groups & companies.
        AssertModuleOff(await PostAsync(owner, "/commands/ReleaseGroupBlock", new Hcc.Contracts.Inventory.ReleaseGroupBlockRequest(1, "AAAAAAAAAAE=")), ConfigModuleIds.Groups, "ReleaseGroupBlock");
    }

    [Fact]
    public async Task Core_modules_can_never_be_switched_off()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        var modules = JsonSerializer.Deserialize<ModulesResult>(await (await owner.GetAsync(ApiConventions.BasePath + "/configuration/modules", Ct)).Content.ReadAsStringAsync(Ct), Json)!;
        Assert.Equal(ConfigCatalog.Modules.Select(m => m.Id), modules.Modules.Select(m => m.Id));
        Assert.Equal(new[] { "Front desk", "Guests & folios", "Cashier", "Day closing", "Access control", "Master configuration" }, modules.Core);

        foreach (var core in new[] { "frontdesk", "folios", "cashier", "closing", "access", "configuration", "settings" })
        {
            var r = await PostAsync(owner, "/commands/SetSetting", new SetSettingRequest("module." + core, ConfigDatabase.J(false)));
            Assert.True(r.Status == HttpStatusCode.UnprocessableEntity, $"module.{core}: {(int)r.Status} {r.Body}");
            Assert.Equal("VALIDATION", JsonSerializer.Deserialize<ErrorResponse>(r.Body, Json)!.Code);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Gate.IsOnAsync(h.PropertyId, core));
            Assert.Throws<InvalidOperationException>(() => ModuleGate.Refusal(core));
        }

        // A route cannot be gated by a core module either: mapping fails at start-up, not at request time.
        await using var other = WebApplication.CreateSlimBuilder().Build();
        Assert.Throws<ArgumentException>(() => other.MapGroup("/x").RequireHccModule("frontdesk"));
    }
}
