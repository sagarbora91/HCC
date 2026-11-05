using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Customers;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Customers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// AF-VERIFY (wave 4): hostile attempts against PR-01 (opaque ck_ wire key, masked roles never see a mobile number) and ID-01 (Devanagari / mixed-script
/// names). A passing test = the system refused or behaved correctly. Attempt ids are the V-CRM-nn prefixes used in af-verify-DONE.md.
/// </summary>
[Trait("Category", "Sql")]
public partial class AuditFixVerifyCrmTests : IAsyncLifetime
{
    private CrmWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\d{10}")]
    private static partial Regex TenDigits();

    [GeneratedRegex(@"\d[\d \-+().]{8,}\d")]
    private static partial Regex LooseDigitRun();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}(T[\d:.]+(Z|[+-]\d{2}:\d{2})?)?")]
    private static partial Regex IsoDate();

    public async ValueTask InitializeAsync()
    {
        _w = await CrmWorld.CreateAsync(Ct);
        // a custom role that only sees masked contact details and may propose merges: View in mode M, no Edit
        await _w.ExecAsync("INSERT plat.role(role_code, name, credential_kind, owner_only_assign, sort_order) VALUES ('ZZZ', N'Custom masked', 'PIN', 0, 99);"
            + "INSERT plat.role_grant(role_code, permission_id, mode, scope) VALUES ('ZZZ', 'PERM-CRM-001', 'M', 'P'), ('ZZZ', 'PERM-CRM-003', 'A', 'P');", Ct);
        var id = Convert.ToInt32(await _w.ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({_w.PropertyId}, 'custom', N'custom', '2026-01-01');", Ct));
        await _w.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, 'ZZZ', '2026-01-01', '2026-01-01');", Ct);
        _w.Users["custom"] = id;
        // the cashier is given the merge permission as well so the masked duplicates / merges responses can be read
        await _w.ExecAsync("INSERT plat.role_grant(role_code, permission_id, mode, scope) VALUES ('CSH', 'PERM-CRM-003', 'A', 'P');", Ct);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IMessagingDataFactory>(_w.Messaging);
        builder.Services.AddCustomersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapCustomersEndpoints();
        await _app.StartAsync(Ct);
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _w.DisposeAsync();
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private int U(string user) => _w.Users[user];

    private Task<string> T(string rawKey) => _w.Queries.TokenAsync(_w.PropertyId, rawKey, Ct);

    private Task<CustomerProfile> Profile(string user, string key) => _w.Queries.ProfileAsync(_w.PropertyId, U(user), key, Ct);

    private Task<CustomerList> List(string user, string? q = null) => _w.Queries.ListAsync(_w.PropertyId, U(user), q, null, 1, 100, Ct);

    private HttpRequestMessage Req(HttpMethod m, string path, string user)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(_w.Users[user]));
        return r;
    }

    private async Task<(HttpStatusCode Status, string Body)> Get(string path, string user)
    {
        var res = await _http.SendAsync(Req(HttpMethod.Get, path, user), Ct);
        return (res.StatusCode, await res.Content.ReadAsStringAsync(Ct));
    }

    private Task AddPhonelessBooking(string no, string name) => _w.ExecAsync(
        $"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({_w.PropertyId}, '{no}', N'{name.Replace("'", "''")}', '2026-10-20', '2026-10-21', 'Confirmed');", Ct);

    private static void NoPhoneInJson(string label, string json)
    {
        foreach (var phone in new[] { CrmWorld.Asha, CrmWorld.Asha2, CrmWorld.VikramPhone, "98200 11111", "98200-11111", "+91" })
            Assert.False(json.Contains(phone, StringComparison.Ordinal), $"{label} contains '{phone}'");
        Assert.False(TenDigits().IsMatch(json), $"{label} has a 10-digit run: {TenDigits().Match(json).Value}");
        var noDates = IsoDate().Replace(json, "D");
        Assert.False(LooseDigitRun().IsMatch(noDates), $"{label} has a separated digit run: {LooseDigitRun().Match(noDates).Value}");
        foreach (var local in new[] { "asha.rao", "asha.rao+stay", "Asha.Rao" })
            Assert.False(json.Contains(local, StringComparison.OrdinalIgnoreCase), $"{label} contains e-mail local part '{local}'");
    }

    // V-CRM-01 / 02 / 03: JSON-level mask test for every masked reader, every endpoint, direct + over HTTP
    [Theory]
    [InlineData("cashier")]
    [InlineData("custom")]
    public async Task V_CRM_01_masked_role_json_of_list_profile_duplicates_merges_has_no_phone_no_digit_run_no_email_local_part(string user)
    {
        // a pending merge so the merges list is not empty
        var vikName = await T(CrmWorld.VikramName);
        var vikPhone = await T(CrmWorld.VikramPhone);
        await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(vikName, vikPhone, "Same person, no mobile on first booking"), Ct);
        var token = await T(CrmWorld.Asha);

        var direct = new Dictionary<string, string>
        {
            ["list"] = JsonSerializer.Serialize(await List(user)),
            ["list-q-name"] = JsonSerializer.Serialize(await List(user, "asha")),
            ["list-q-phone"] = JsonSerializer.Serialize(await List(user, CrmWorld.Asha)),
            ["profile-token"] = JsonSerializer.Serialize(await Profile(user, token)),
            ["profile-name-key"] = JsonSerializer.Serialize(await Profile(user, CrmWorld.VikramName)),
            ["duplicates"] = JsonSerializer.Serialize(await _w.Queries.DuplicatesAsync(_w.PropertyId, U(user), Ct)),
            ["merges"] = JsonSerializer.Serialize(await _w.Queries.MergesAsync(_w.PropertyId, U(user), null, Ct)),
        };
        foreach (var (label, json) in direct) NoPhoneInJson($"{user}/{label}", json);
        Assert.Empty((await List(user, CrmWorld.Asha)).Customers); // a masked reader cannot search by number (oracle)

        foreach (var path in new[] { "/customers", "/customers?q=asha", "/customers?q=" + CrmWorld.Asha, "/customers/profile?key=" + token, "/customers/profile?key=" + Uri.EscapeDataString(CrmWorld.VikramName),
                     "/customers/duplicates", "/customers/merges" })
        {
            var (status, body) = await Get(path, user);
            Assert.Equal(HttpStatusCode.OK, status);
            NoPhoneInJson($"{user} GET {path}", body);
        }
    }

    // V-CRM-04: raw mobile key probes by a masked reader are NOT_FOUND in every spelling, and indistinguishable from a number that does not exist
    [Theory]
    [InlineData("cashier")]
    [InlineData("custom")]
    public async Task V_CRM_04_raw_phone_probe_by_a_masked_role_is_NOT_FOUND_in_every_spelling_and_gives_no_oracle(string user)
    {
        string[] spellings = [CrmWorld.Asha, " " + CrmWorld.Asha + " ", "0" + CrmWorld.Asha, "+91" + CrmWorld.Asha, "91" + CrmWorld.Asha, "98200 11111", "98200-11111", CrmWorld.VikramPhone, CrmWorld.Asha2];
        string? unknownMessage = null;
        try { await Profile(user, "9111111111"); }
        catch (HccException e) { unknownMessage = e.Message; Assert.Equal(HccErrorCode.NOT_FOUND, e.Code); }
        Assert.NotNull(unknownMessage);
        foreach (var s in spellings)
        {
            var e = await Assert.ThrowsAsync<HccException>(() => Profile(user, s));
            Assert.Equal(HccErrorCode.NOT_FOUND, e.Code);
            Assert.Equal(unknownMessage, e.Message); // same words for a real and an unknown number
            var (status, _) = await Get("/customers/profile?key=" + Uri.EscapeDataString(s), user);
            Assert.Equal(HttpStatusCode.NotFound, status);
        }
    }

    // V-CRM-05: raw phone as an owner / reception (contact roles) still works; token and raw give the same customer
    [Fact]
    public async Task V_CRM_05_contact_roles_resolve_raw_phone_and_token_to_the_same_customer_over_http()
    {
        var token = await T(CrmWorld.Asha);
        foreach (var user in new[] { "owner", "reception", "manager" })
        {
            var (s1, b1) = await Get("/customers/profile?key=" + CrmWorld.Asha, user);
            var (s2, b2) = await Get("/customers/profile?key=" + token, user);
            Assert.Equal(HttpStatusCode.OK, s1);
            Assert.Equal(HttpStatusCode.OK, s2);
            Assert.Equal(b1, b2);
            using var doc = JsonDocument.Parse(b1);
            Assert.Equal(token, doc.RootElement.GetProperty("key").GetString()); // even for a raw-key request the answer carries the token
        }
    }

    // V-CRM-06: forged / malformed / foreign tokens
    [Fact]
    public async Task V_CRM_06_forged_and_malformed_tokens_are_NOT_FOUND_for_reads_and_commands()
    {
        string[] fakes = ["ck_AAAAAAAAAAAAAAAAAAAAAA", "ck_" + new string('Z', 22), "ck_short", "ck_", "CK_AAAAAAAAAAAAAAAAAAAAAA", "ck_AAAAAAAAAAAAAAAAAAAAAAA", "ck_AAAAAAAAAAAAAAAAAAAA!A",
            "ck_AAAAAAAAAAAAAAAAAAAAAA' OR 1=1 --", "n:", "", "   "];
        foreach (var user in new[] { "owner", "cashier", "custom" })
            foreach (var f in fakes)
            {
                var e = await Assert.ThrowsAsync<HccException>(() => Profile(user, f));
                Assert.Equal(HccErrorCode.NOT_FOUND, e.Code);
            }

        foreach (var f in fakes)
        {
            var tag = await Assert.ThrowsAsync<HccException>(() => _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(f, "vip"), Ct));
            Assert.Equal(HccErrorCode.NOT_FOUND, tag.Code);
            var merge = await Assert.ThrowsAsync<HccException>(() => _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(f, CrmWorld.VikramPhone, "forged source key"), Ct));
            Assert.Equal(HccErrorCode.NOT_FOUND, merge.Code);
        }

        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag", Ct));
        // a token computed with a different secret is a forged token
        var foreign = CustomerKeyToken.Make(new byte[32], CrmWorld.Asha);
        Assert.NotEqual(await T(CrmWorld.Asha), foreign);
        await Assert.ThrowsAsync<HccException>(() => Profile("owner", foreign));
    }

    // V-CRM-07: token stability across calls, a restarted queries object (secret persisted) and the very first concurrent calls
    [Fact]
    public async Task V_CRM_07_token_is_stable_across_a_restarted_queries_object_and_derives_from_the_persisted_secret()
    {
        var first = (await List("owner")).Customers.ToDictionary(c => c.Key, c => c.Name);
        var restarted = new CustomerQueries(_w.Policy, new SqlCustomerReadStore(_w.Platform.Connections), _w.Messaging);
        var again = (await restarted.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 100, Ct)).Customers.ToDictionary(c => c.Key, c => c.Name);
        Assert.Equal(first, again);
        var secret = (byte[])(await _w.ScalarAsync($"SELECT secret FROM crm.customer_key_secret WHERE property_id = {_w.PropertyId}", Ct))!;
        Assert.Equal(32, secret.Length);
        Assert.Equal(CustomerKeyToken.Make(secret, CrmWorld.Asha), await T(CrmWorld.Asha));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_key_secret", Ct));
    }

    [Fact]
    public async Task V_CRM_08_the_first_concurrent_calls_create_exactly_one_secret_and_agree_on_every_token()
    {
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_key_secret", Ct)); // nothing has called yet in this fresh database
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var q = new CustomerQueries(_w.Policy, new SqlCustomerReadStore(_w.Platform.Connections), _w.Messaging);
            return (await q.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 100, Ct)).Customers.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_key_secret", Ct));
        Assert.All(runs, r => Assert.Equal(runs[0], r));
    }

    // V-CRM-09: a merged-away (stale) token still resolves to the survivor; tags added through it land on the survivor; same customer in two forms is refused as a merge
    [Fact]
    public async Task V_CRM_09_merge_flow_stale_token_resolves_to_survivor_and_same_customer_in_two_spellings_is_refused()
    {
        var vikName = await T(CrmWorld.VikramName);
        var vikPhone = await T(CrmWorld.VikramPhone);
        var self = await Assert.ThrowsAsync<HccException>(() => _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(CrmWorld.VikramPhone, vikPhone, "same customer twice"), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, self.Code);
        // a masked reader with the merge permission proposes using only tokens; a raw phone as the TARGET is a probe and is refused
        var probe = await Assert.ThrowsAsync<HccException>(() => _w.Commands.ProposeMergeAsync(_w.Ctx("custom", Key()), new ProposeCustomerMergeRequest(vikName, CrmWorld.VikramPhone, "probe with a raw phone"), Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, probe.Code);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_merge", Ct));
        var m = (await _w.Commands.ProposeMergeAsync(_w.Ctx("custom", Key()), new ProposeCustomerMergeRequest(vikName, vikPhone, "Same person, no mobile on first booking"), Ct)).Value;
        NoPhoneInJson("propose result", JsonSerializer.Serialize(m));
        var maker = await Assert.ThrowsAsync<HccException>(() => _w.Commands.DecideMergeAsync(_w.Ctx("custom", Key()), new DecideCustomerMergeRequest(m.MergeId, true, m.Version), Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, maker.Code);
        var done = (await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m.MergeId, true, m.Version), Ct)).Value;
        Assert.Equal("Approved", done.Status);
        // the stale token of the merged-away customer still opens the survivor, for a masked reader too
        foreach (var user in new[] { "owner", "cashier", "custom" })
        {
            var p = await Profile(user, vikName);
            Assert.Equal(vikPhone, p.Key);
            Assert.Equal([vikName], p.MergedFrom);
            if (user != "owner") NoPhoneInJson(user + " merged profile", JsonSerializer.Serialize(p));
        }

        var tag = await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(vikName, "Corporate"), Ct);
        Assert.Equal(vikPhone, tag.Value.CustomerKey);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = '{CrmWorld.VikramPhone}'", Ct));
        // a second proposal for the already-merged source
        var again = await Assert.ThrowsAsync<HccException>(() => _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(vikName, vikPhone, "again"), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, again.Code);
    }

    // V-CRM-10: Devanagari, mixed script, normalisation collisions, symbol-only names
    [Fact]
    public async Task V_CRM_10_names_devanagari_mixed_script_and_normalisation()
    {
        var before = (await List("owner")).Total;
        await AddPhonelessBooking("HCC-N1", "राम पाटील");
        await AddPhonelessBooking("HCC-N2", "सीता जोशी");
        await AddPhonelessBooking("HCC-N3", "  राम   पाटील "); // same person, extra spaces
        await AddPhonelessBooking("HCC-N4", "Rahul राहुल");
        await AddPhonelessBooking("HCC-N5", "rahul  राहुल"); // same mixed-script person, case + spaces
        await AddPhonelessBooking("HCC-N6", "Rahul");          // a different (Latin-only) person
        await AddPhonelessBooking("HCC-N7", "!!!");
        await AddPhonelessBooking("HCC-N8", "???");
        await AddPhonelessBooking("HCC-N9", "क़ादिर"); // precomposed QA
        await AddPhonelessBooking("HCC-NA", "क़ादिर"); // decomposed KA + NUKTA
        var l = await List("owner");
        Assert.Equal(before + 7, l.Total); // ram, sita, rahul-mixed, rahul, !!!, ???, qadir (NFKC-equal) = 7 new customers
        Assert.Equal(2, l.Customers.Single(c => c.Name.Trim() == "राम पाटील" || c.Name.Contains("राम", StringComparison.Ordinal)).Stays);
        Assert.Single(l.Customers, c => c.Name.Contains("राहुल", StringComparison.Ordinal));
        Assert.Equal(2, l.Customers.Single(c => c.Name.Contains("राहुल", StringComparison.Ordinal)).Stays);
        Assert.Equal(1, l.Customers.Single(c => c.Name == "Rahul").Stays);
        Assert.NotEqual(CustomerRules.KeyOf(null, "!!!"), CustomerRules.KeyOf(null, "???"));
        Assert.Equal(CustomerRules.KeyOf(null, "!!!"), CustomerRules.KeyOf(null, " !!! ")); // the identical raw name is the same bucket (see DONE: plan wording "twice")
        Assert.Equal(CustomerRules.KeyOf(null, "क़ादिर"), CustomerRules.KeyOf(null, "क़ादिर"));
        // search: Latin word does not find Devanagari-only guests, Devanagari word finds only its guest, mixed finds the mixed one
        Assert.Equal(["Rahul"], (await List("owner", "rahul")).Customers.Where(c => !c.Name.Contains("राहुल", StringComparison.Ordinal)).Select(c => c.Name));
        Assert.Equal(2, (await List("owner", "Rahul")).Customers.Count);
        Assert.Single((await List("owner", "राहुल")).Customers);
        Assert.Empty((await List("owner", "गीता")).Customers);
        Assert.Empty((await List("owner", "!!!")).Customers); // symbol-only query: no letters, no digits -> nothing (not everything)
    }

    // V-CRM-11 (closed by AF2 phone digits): the audit target is the opaque ck_ token, not the raw key
    [Fact]
    public async Task V_CRM_11_audit_target_is_the_opaque_token()
    {
        var token = await T(CrmWorld.Asha);
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(token, "vip"), Ct);
        var target = (string)(await _w.ScalarAsync("SELECT TOP 1 target FROM plat.audit_event WHERE action = 'CRM_TAG' ORDER BY audit_id DESC", Ct))!;
        // observation only (plan: "stored rows keep the raw key"): who can read plat.audit_event is outside this wave
        Assert.StartsWith("crm:", target, StringComparison.Ordinal);
    }
}
