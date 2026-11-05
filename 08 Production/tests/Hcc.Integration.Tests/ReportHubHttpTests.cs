using System.Net;
using System.Text.Json.Nodes;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// AF2-HUB-SERVER (audit "Missed A"): the production Report library runs on the server. Over real HTTP + SQL: the catalogue is filtered per
/// role (rule V), a report the role cannot view answers 403, DEC-061 keeps money reports from housekeeping, every-row runs (all=true), the
/// export permission on files (server export and a device-built file logged through print-log), the base64 export the web / APK transport
/// reads, and the export log (Owner: every row; others: their own).
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReportHubHttpTests
{
    private const string OwnerLogin = "owner.hub";
    private const string Password = "Maroon&Sand#Ledger26";
    private const string FirstPin = "816305";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        DECLARE @p INT = SCOPE_IDENTITY();
        INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 200000, 1);
        DECLARE @t INT = SCOPE_IDENTITY();
        INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '105', @t), (@p, '106', @t);
        EXEC inv.usp_rebuild_type_night_count @p, '2026-10-01', '2027-01-01';
        SELECT @p;
        """;

    private static async Task<HttpClient> PinUserAsync(HccServer s, HttpClient owner, string device, string role, string pin)
    {
        var login = "hub." + role.ToLowerInvariant();
        var u = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest(login, "Hub " + role, [new RoleAssignmentInput(role)]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(u.UserId, null, FirstPin, u.Version), Ct);
        var client = s.DeviceClient(device);
        using (var r = await Wire.PostAsync(client, "/auth/sign-in", new SignInRequest(login, Pin: FirstPin, ReturnToken: true), null, Ct))
            HccServer.UseBearer(client, (await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct)).SessionToken!);
        await Wire.CommandAsync<UserVersionResult>(client, "ChangeOwnCredential", new ChangeOwnCredentialRequest(FirstPin, null, pin), Ct);
        return client;
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(HttpClient c, string path)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<HttpStatusCode> PrintLogAsync(HttpClient c, string id, string? format)
    {
        using var r = await Wire.PostAsync(c, $"/reports/{id}/print-log", new { @params = new Dictionary<string, string>(), rows = 1, provisional = true, format }, null, Ct);
        return r.StatusCode;
    }

    private static Dictionary<string, JsonNode> Catalogue(string body) =>
        JsonNode.Parse(body)!.AsArray().ToDictionary(e => e!["definition"]!["id"]!.GetValue<string>(), e => e!);

    [Fact]
    public async Task Hub_catalogue_run_export_and_export_log_follow_the_role_rules_over_http()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Hub Owner", Password), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: Password), null, Ct))
            await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Hub tablet"), Ct);
        using var cashier = await PinUserAsync(s, owner, device.DeviceToken!, "CSH", "730582");
        using var housekeeper = await PinUserAsync(s, owner, device.DeviceToken!, "HKP", "583104");

        // Catalogue per role: the Owner sees the money and tax reports with export; the Cashier only its own-shift reports, without export.
        var (st, body) = await GetAsync(owner, "/reports/catalogue");
        Assert.Equal(HttpStatusCode.OK, st);
        var own = Catalogue(body);
        Assert.True(own.Count >= 20, $"owner catalogue: {own.Count}");
        Assert.True(own["rpt.money.shift-report"]["canExport"]!.GetValue<bool>());
        Assert.True(own["rpt.tax.gst-summary"]["money"]!.GetValue<bool>());
        var csh = Catalogue((await GetAsync(cashier, "/reports/catalogue")).Body);
        Assert.Contains("rpt.money.shift-report", csh.Keys);
        Assert.DoesNotContain("rpt.tax.gst-summary", csh.Keys);
        Assert.All(csh.Values, e => Assert.False(e["canExport"]!.GetValue<bool>()));

        // DEC-061: housekeeping never lists or runs a money report.
        var hk = Catalogue((await GetAsync(housekeeper, "/reports/catalogue")).Body);
        Assert.All(hk.Values, e => Assert.False(e["definition"]!["moneyFlag"]!.GetValue<bool>()));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(housekeeper, "/reports/rpt.money.shift-report")).Status);

        // Permission denial on run, export and a device-built file; a print needs only the view right.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(cashier, "/reports/rpt.tax.gst-summary")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(cashier, "/reports/rpt.money.shift-report/export?format=xlsx&encoding=base64")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, await PrintLogAsync(cashier, "rpt.money.shift-report", "xlsx"));
        Assert.Equal(HttpStatusCode.OK, await PrintLogAsync(cashier, "rpt.money.shift-report", "print"));

        // Every row in one answer (no page), the result shape the client maps.
        var (rs, run) = await GetAsync(cashier, "/reports/rpt.money.shift-report?all=true");
        Assert.Equal(HttpStatusCode.OK, rs);
        var res = JsonNode.Parse(run)!;
        Assert.Equal("rpt.money.shift-report", res["id"]!.GetValue<string>());
        Assert.Equal(0, res["pageSize"]!.GetValue<int>());
        Assert.NotEmpty(res["columns"]!.AsArray());

        // The Owner's Excel file as base64 text (the transport reads text): a zip ("PK"), named by the server; one export-log row.
        using (var x = await owner.GetAsync(ApiConventions.BasePath + "/reports/rpt.money.shift-report/export?format=xlsx&encoding=base64", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, x.StatusCode);
            var bytes = Convert.FromBase64String(await x.Content.ReadAsStringAsync(Ct));
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
            Assert.EndsWith(".xlsx", x.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "", StringComparison.Ordinal);
        }

        var all = JsonNode.Parse((await GetAsync(owner, "/reports/export-log")).Body)!.AsArray();
        Assert.Equal(2, all.Count);
        var mine = JsonNode.Parse((await GetAsync(cashier, "/reports/export-log")).Body)!.AsArray();
        var row = Assert.Single(mine);
        Assert.Equal("rpt.money.shift-report", row!["reportId"]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse((await GetAsync(housekeeper, "/reports/export-log")).Body)!.AsArray());
    }

    [Fact]
    public async Task Export_log_of_a_cashier_older_than_the_newest_200_property_rows_is_still_shown()
    {
        // af2-fix finding 3: the caller filter runs in SQL before TOP (n), not after it.
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Hub Owner", Password), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: Password), null, Ct))
            await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Hub tablet"), Ct);
        using var cashier = await PinUserAsync(s, owner, device.DeviceToken!, "CSH", "730582");

        Assert.Equal(HttpStatusCode.OK, await PrintLogAsync(cashier, "rpt.money.shift-report", "print"));
        // 250 newer rows by the Owner (more than the default 200 of the tab).
        await s.ScalarAsync($"""
            DECLARE @o INT = (SELECT user_id FROM plat.app_user WHERE login_name = '{OwnerLogin}');
            INSERT rpt.export_log(property_id, at_utc, principal_id, report_id, params_hash, params, format, row_count, file_name, provisional)
            SELECT TOP (250) {s.PropertyId}, SYSUTCDATETIME(), @o, 'rpt.money.shift-report', REPLICATE('0', 64), N'[]', 'print', 1, NULL, 0 FROM sys.all_objects;
            """, Ct);

        var mine = JsonNode.Parse((await GetAsync(cashier, "/reports/export-log")).Body)!.AsArray();
        Assert.Equal("rpt.money.shift-report", Assert.Single(mine)!["reportId"]!.GetValue<string>());
        Assert.Equal(200, JsonNode.Parse((await GetAsync(owner, "/reports/export-log")).Body)!.AsArray().Count); // the Owner: the newest 200 of everyone's
    }
}
