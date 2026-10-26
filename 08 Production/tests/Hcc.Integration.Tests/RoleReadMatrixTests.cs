using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// D5 (W4 E2E): the production web client must never call a read the signed-in role does not hold. This test signs in one user per
/// seeded role template (0101 + module RBAC seeds, DEC-063) on the real server and sends every GET the sync loaders use (the route list in
/// <c>03 Prototype/tests/_role-reads.json</c>), then compares each role's capabilities and per-route HTTP status with that file. The
/// prototype test <c>tests/sync-roles.mjs</c> serves 403 exactly where the file says so and asserts the client sends no such request.
/// Regenerate after a deliberate grant or endpoint change: <c>HCC_UPDATE_ROLE_READS=1 dotnet test --filter RoleReadMatrix</c>.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RoleReadMatrixTests
{
    private const string OwnerLogin = "owner.matrix";
    private const string Password = "Maroon&Sand#Ledger26";

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

    /// <summary>Role code → PIN (null = password role, DEC-062).</summary>
    private static readonly (string Role, string? Pin)[] Roles =
    [
        ("OWN", null), ("MGR", "618204"), ("REC", "472913"), ("CSH", "730582"), ("ACC", null), ("SUP", "829471"), ("HKP", "583104"), ("MNT", "940263"), ("TAD", null),
    ];

    private static string FixturePath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "03 Prototype", "tests", "_role-reads.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("03 Prototype/tests/_role-reads.json not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Every_loader_read_answers_each_seeded_role_as_recorded_in_the_client_fixture()
    {
        var path = FixturePath();
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!.AsObject();
        var routes = fixture["routes"]!.AsObject().Select(kv => (Pattern: kv.Key, Sample: kv.Value!.GetValue<string>())).ToList();

        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Matrix Owner", Password), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: Password), null, Ct))
            await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Matrix tablet"), Ct);

        var actual = new JsonObject();
        var clients = new List<HttpClient>();
        try
        {
            foreach (var (role, pin) in Roles)
            {
                HttpClient client;
                if (role == "OWN") client = owner;
                else
                {
                    var login = "matrix." + role.ToLowerInvariant();
                    var u = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest(login, "Matrix " + role, [new RoleAssignmentInput(role)]), Ct);
                    await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(u.UserId, pin is null ? Password : null, pin, u.Version), Ct);
                    if (pin is null)
                    {
                        client = s.CookieClient();
                        using var r = await Wire.PostAsync(client, "/auth/sign-in", new SignInRequest(login, Password: Password), null, Ct);
                        await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct);
                    }
                    else
                    {
                        client = s.DeviceClient(device.DeviceToken!);
                        using var r = await Wire.PostAsync(client, "/auth/sign-in", new SignInRequest(login, Pin: pin, ReturnToken: true), null, Ct);
                        HccServer.UseBearer(client, (await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct)).SessionToken!);
                    }

                    clients.Add(client);
                }

                var caps = await Wire.GetAsync<CapabilitiesResult>(client, "/me/capabilities", Ct);
                var reads = new JsonObject();
                foreach (var (pattern, sample) in routes)
                {
                    using var res = await client.GetAsync(ApiConventions.BasePath + sample, Ct);
                    var code = (int)res.StatusCode;
                    Assert.True(code != 500, $"{role} {sample}: 500 {await res.Content.ReadAsStringAsync(Ct)}");
                    reads[pattern] = code.ToString(CultureInfo.InvariantCulture);
                }

                actual[role] = new JsonObject
                {
                    ["roles"] = new JsonArray(caps.Roles.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
                    ["permissions"] = new JsonArray(caps.Permissions.OrderBy(p => p.PermissionId, StringComparer.Ordinal)
                        .Select(p => (JsonNode)new JsonArray(p.PermissionId, p.Mode, p.Scope)).ToArray()),
                    ["fieldMasks"] = new JsonObject(caps.FieldMasks.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))),
                    ["reads"] = reads,
                };
            }
        }
        finally
        {
            foreach (var c in clients) c.Dispose();
        }

        // Sanity: the two D5 cases are what the fixture exists for.
        Assert.Equal("403", actual["REC"]!["reads"]!["GET /rooms/board"]!.GetValue<string>());
        Assert.Equal("200", actual["REC"]!["reads"]!["GET /rooms/status"]!.GetValue<string>());
        Assert.Equal("403", actual["CSH"]!["reads"]!["GET /reservations"]!.GetValue<string>());
        Assert.Equal("200", actual["CSH"]!["reads"]!["GET /cashier/folios"]!.GetValue<string>());

        var options = new JsonSerializerOptions { WriteIndented = true };
        var expected = fixture["roles"]?.ToJsonString(options) ?? "{}";
        var got = actual.ToJsonString(options);
        if (Environment.GetEnvironmentVariable("HCC_UPDATE_ROLE_READS") == "1" && expected != got)
        {
            fixture["roles"] = JsonNode.Parse(got);
            var text = fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            await File.WriteAllTextAsync(path, text.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", Ct);
            return;
        }

        Assert.True(expected == got, $"Role reads drifted from {path}. Re-run with HCC_UPDATE_ROLE_READS=1 after checking the change.\nActual:\n{got}");
    }
}
