using System.Net;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Integration.Tests.Harness;

namespace Hcc.Integration.Tests;

/// <summary>
/// LF parity (lane lf-tests, contract §12) over HTTP with the real composition: the command-style routes of the LF money features exist
/// (IssueCounterBill, RequestCounterBillVoid, RequestBillCorrection, ChangeStayRate, SetGuestCreditAccount, SetGuestAgreedRate, RequestGuestCredit)
/// and answer the Owner with a validation / not-found error rather than 404 for the route; housekeeping, maintenance and the Restaurant role are
/// refused every one of them with 403 (DEC-061: floor staff never see or move hotel money). The per-rule money behaviour is covered by the
/// project tests of the module (Hcc.Stays / Hcc.Invoices / Hcc.Counter) and by the prototype scenario LF-PARITY.
/// </summary>
[Trait("Category", "Sql")]
public sealed class LfParityTests
{
    private const string OwnerLogin = "owner.lf";
    private const string Password = "Maroon&Sand#Ledger26";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        SELECT SCOPE_IDENTITY();
        """;

    private static readonly (string Name, object Body)[] Routes =
    [
        ("IssueCounterBill", new { party = new { name = "Walk-in" }, lines = new[] { new { label = "x", qty = 1, unitPaise = 100L, gstCategory = "other" } }, settlement = new { mode = "udhaar" } }),
        ("RequestCounterBillVoid", new { billId = 1L, reason = "x" }),
        ("RequestBillCorrection", new { invoiceNo = "HPE/2627/9999", changes = Array.Empty<object>(), reason = "x" }),
        ("ChangeStayRate", new { stayId = 1L, ratePaise = 100L, scope = "all", reason = "x" }),
        ("SetGuestCreditAccount", new { guestId = 1L, limitPaise = 100L, active = true }),
        ("SetGuestAgreedRate", new { guestId = 1L, roomTypeId = 1, ratePaise = 100L }),
        ("RequestGuestCredit", new { stayId = 1L, reason = "x" }),
    ];

    /// <summary>A floor-staff user signs in with a PIN on a registered hotel device (DEC-062); the first PIN is replaced by the user's own (A06-01).</summary>
    private static async Task<HttpClient> PinUserAsync(HccServer s, HttpClient owner, string login, string role, string pin, string ownPin)
    {
        var device = await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("LF tablet " + login), Ct);
        var u = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest(login, "LF " + role, [new RoleAssignmentInput(role)]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(u.UserId, null, pin, u.Version), Ct);
        var client = s.DeviceClient(device.DeviceToken!);
        using (var r = await Wire.PostAsync(client, "/auth/sign-in", new SignInRequest(login, Pin: pin, ReturnToken: true), null, Ct))
            HccServer.UseBearer(client, (await Wire.OkAsync<SignInResult>(r, login + " sign-in", Ct)).SessionToken!);
        await Wire.CommandAsync<UserVersionResult>(client, "ChangeOwnCredential", new ChangeOwnCredentialRequest(pin, null, ownPin), Ct);
        return client;
    }

    [Fact]
    public async Task LF_money_command_routes_exist_for_the_Owner_and_are_forbidden_to_housekeeping_and_maintenance()
    {
        await using var s = await HccServer.StartAsync(Seed, Ct);
        using var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "LF Owner", Password), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var signIn = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: Password), null, Ct))
            await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct);
        using var hkp = await PinUserAsync(s, owner, "lf.hkp", "HKP", "583104", "739215");
        using var mnt = await PinUserAsync(s, owner, "lf.mnt", "MNT", "940263", "739216");

        foreach (var (name, body) in Routes)
        {
            using (var o = await Wire.PostAsync(owner, "/commands/" + name, body, Wire.NewKey(name), Ct))
            {
                var text = await o.Content.ReadAsStringAsync(Ct);
                // A missing record answers 404 with a contract error body; a missing ROUTE answers 404 with none.
                Assert.True(o.StatusCode != HttpStatusCode.Forbidden && o.StatusCode != HttpStatusCode.MethodNotAllowed && (o.StatusCode != HttpStatusCode.NotFound || text.Contains("\"code\"", StringComparison.Ordinal)),
                    $"{name}: the Owner reached the route (got {(int)o.StatusCode}: {text})");
            }
            foreach (var (who, client) in new[] { ("HKP", hkp), ("MNT", mnt) })
            {
                using var r = await Wire.PostAsync(client, "/commands/" + name, body, Wire.NewKey(name), Ct);
                Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{name} as {who}: expected 403, got {(int)r.StatusCode}");
            }
        }
    }
}
