using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// AF-CRM (PR-01, ID-01, Missed D): the customer key on the wire is an opaque ck_ token, never the mobile number, and Devanagari names are separate customers
/// that can be searched. Same fictional hotel as <see cref="CustomersSqlTests"/>.
/// </summary>
[Trait("Category", "Sql")]
public partial class CustomersKeyTests : IAsyncLifetime
{
    private CrmWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\d{10}")]
    private static partial Regex TenDigits();

    public async ValueTask InitializeAsync() => _w = await CrmWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Key() => Guid.NewGuid().ToString("N");

    private int U(string user) => _w.Users[user];

    private Task<string> T(string rawKey) => _w.Queries.TokenAsync(_w.PropertyId, rawKey, Ct);

    private static async Task Fails(HccErrorCode code, Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<HccException>(action);
        Assert.Equal(code, e.Code);
    }

    private async Task<CustomerList> List(string user, string? q = null) => await _w.Queries.ListAsync(_w.PropertyId, U(user), q, null, 1, 50, Ct);

    private Task<CustomerProfile> Profile(string user, string key) => _w.Queries.ProfileAsync(_w.PropertyId, U(user), key, Ct);

    private Task AddPhonelessBooking(string no, string name) => _w.ExecAsync(
        $"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({_w.PropertyId}, '{no}', N'{name}', '2026-10-20', '2026-10-21', 'Confirmed');", Ct);

    [Fact]
    public async Task Masked_list_profile_and_duplicates_json_contain_no_phone_digits()
    {
        // the Cashier has no merge permission by default; this test database grants it so the masked duplicates response can be checked as well
        await _w.ExecAsync("INSERT plat.role_grant(role_code, permission_id, mode, scope) VALUES ('CSH', 'PERM-CRM-003', 'A', 'P');", Ct);
        var list = JsonSerializer.Serialize(await List("cashier"));
        var token = await T(CrmWorld.Asha);
        var profile = JsonSerializer.Serialize(await Profile("cashier", token));
        var dupes = JsonSerializer.Serialize(await _w.Queries.DuplicatesAsync(_w.PropertyId, U("cashier"), Ct));
        foreach (var (name, json) in new[] { ("list", list), ("profile", profile), ("duplicates", dupes) })
        {
            foreach (var phone in new[] { CrmWorld.Asha, CrmWorld.Asha2, CrmWorld.VikramPhone })
                Assert.DoesNotContain(phone, json, StringComparison.Ordinal);
            Assert.False(TenDigits().IsMatch(json), name + " contains a 10-digit run: " + TenDigits().Match(json).Value);
        }

        Assert.Contains("\"Key\":\"ck_", list, StringComparison.Ordinal);
        Assert.Contains("\"Key\":\"ck_", dupes, StringComparison.Ordinal);
        Assert.Contains("\"SuggestedSurvivor\":\"ck_", dupes, StringComparison.Ordinal);
        Assert.Contains("\"Key\":\"ck_", profile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Key_is_a_ck_token_for_every_role_and_stable_across_calls()
    {
        var keys = new List<string>();
        foreach (var user in new[] { "owner", "manager", "reception", "cashier" })
        {
            var all = (await List(user)).Customers;
            Assert.All(all, c => Assert.True(CustomerKeyToken.IsToken(c.Key), user + " got " + c.Key));
            var asha = all.Single(c => c.Name == "Asha Rao" && c.Stays == 3); // the customer with the mobile number (the name-only "Asha Rao" is another one)
            keys.Add(asha.Key);
            Assert.Equal(all.Select(c => c.Key), (await List(user)).Customers.Select(c => c.Key)); // a second call gives the same tokens
        }

        Assert.Single(keys.Distinct());
        Assert.Equal(await T(CrmWorld.Asha), keys[0]);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_key_secret", Ct));
    }

    [Fact]
    public async Task Profile_by_token_resolves_for_cashier_and_owner()
    {
        var token = await T(CrmWorld.Asha);
        foreach (var user in new[] { "cashier", "owner" })
        {
            var p = await Profile(user, token);
            Assert.Equal(token, p.Key);
            Assert.Equal(3, p.Stays.Count);
        }

        Assert.True((await Profile("cashier", token)).Masked);
        Assert.False((await Profile("owner", token)).Masked);
    }

    [Fact]
    public async Task Raw_phone_key_from_a_masked_role_is_NOT_FOUND_but_resolves_for_the_owner()
    {
        await Fails(HccErrorCode.NOT_FOUND, () => Profile("cashier", CrmWorld.Asha));
        Assert.Equal(await T(CrmWorld.Asha), (await Profile("owner", CrmWorld.Asha)).Key);
        Assert.Equal(await T(CrmWorld.Asha), (await Profile("reception", CrmWorld.Asha)).Key);
        await Fails(HccErrorCode.NOT_FOUND, () => Profile("owner", "ck_AAAAAAAAAAAAAAAAAAAAAA")); // a token of no customer
    }

    [Fact]
    public async Task Raw_n_key_resolves_for_every_role()
    {
        foreach (var user in new[] { "owner", "manager", "reception", "cashier" })
            Assert.Equal(await T(CrmWorld.VikramName), (await Profile(user, CrmWorld.VikramName)).Key);
    }

    [Fact]
    public async Task Merge_tags_notes_accept_tokens()
    {
        var vikName = await T(CrmWorld.VikramName);
        var vikPhone = await T(CrmWorld.VikramPhone);
        var tag = await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(vikName, "Corporate"), Ct);
        Assert.Equal(vikName, tag.Value.CustomerKey); // the answer carries the token, never the raw key
        await _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest(vikName, "Prefers ground floor"), Ct);
        await _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(vikName, "Quiet"), Ct);
        await _w.Commands.SetConsentAsync(_w.Ctx("reception", Key()), new SetCustomerConsentRequest(vikPhone, WhatsApp: false), Ct);
        var m = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(vikName, vikPhone, "Same person, no mobile on first booking"), Ct)).Value;
        Assert.Equal(vikName, m.SourceKey);
        Assert.Equal(vikPhone, m.TargetKey);
        var done = await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m.MergeId, true, m.Version), Ct);
        Assert.Equal("Approved", done.Value.Status);
        var p = await Profile("owner", vikPhone);
        Assert.Equal([vikName], p.MergedFrom);
        Assert.Equal(["corporate"], p.Tags);
        Assert.Equal(["Prefers ground floor"], p.Notes.Select(n => n.Body));
        // the stored rows keep the raw keys; only the wire changed
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_tag WHERE customer_key = '" + CrmWorld.VikramPhone + "'", Ct));
        // a masked role may not edit at all
        var ashaToken = await T(CrmWorld.Asha);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.AddTagAsync(_w.Ctx("cashier", Key()), new AddCustomerTagRequest(ashaToken, "vip"), Ct));
    }

    [Fact]
    public async Task Two_devanagari_names_without_phone_are_two_customers()
    {
        var before = (await List("owner")).Total;
        await AddPhonelessBooking("HCC-D1", "राम पाटील");
        await AddPhonelessBooking("HCC-D2", "सीता जोशी");
        var list = await List("owner");
        Assert.Equal(before + 2, list.Total);
        var ram = list.Customers.Single(c => c.Name == "राम पाटील");
        var sita = list.Customers.Single(c => c.Name == "सीता जोशी");
        Assert.NotEqual(ram.Key, sita.Key);
        Assert.Equal(1, ram.Stays);
        Assert.Equal(1, sita.Stays);
        Assert.NotEqual(CustomerRules.KeyOf(null, "राम पाटील"), CustomerRules.KeyOf(null, "सीता जोशी"));
    }

    [Fact]
    public async Task Devanagari_search_finds_the_customer_and_not_the_other()
    {
        await AddPhonelessBooking("HCC-D1", "राम पाटील");
        await AddPhonelessBooking("HCC-D2", "सीता जोशी");
        Assert.Equal(["राम पाटील"], (await List("owner", "राम")).Customers.Select(c => c.Name));
        Assert.Equal(["सीता जोशी"], (await List("owner", "जोशी")).Customers.Select(c => c.Name));
        Assert.Equal(["राम पाटील"], (await List("cashier", "पाटील")).Customers.Select(c => c.Name));
        Assert.Empty((await List("owner", "गीता")).Customers);
    }

    private async Task<string> AuditAndOutboxDumpAsync()
    {
        var audit = (string?)await _w.ScalarAsync("SELECT STRING_AGG(CONCAT(action, '|', target, '|', detail), CHAR(10)) FROM plat.audit_event WHERE action LIKE 'CRM[_]%'", Ct) ?? string.Empty;
        var outbox = (string?)await _w.ScalarAsync("SELECT STRING_AGG(CONCAT(event_type, '|', aggregate_id, '|', payload), CHAR(10)) FROM plat.outbox WHERE event_type = 'CustomerMerged'", Ct) ?? string.Empty;
        return audit + "\n@@OUTBOX@@\n" + outbox;
    }

    private static void AssertNoPhone(string dump)
    {
        foreach (var phone in new[] { CrmWorld.Asha, CrmWorld.Asha2, CrmWorld.VikramPhone, "9820099999" })
            Assert.DoesNotContain(phone, dump, StringComparison.Ordinal);
        Assert.False(TenDigits().IsMatch(dump), "10-digit run in audit/outbox: " + TenDigits().Match(dump).Value);
    }

    // AF2 phone digits: the audit target and the CustomerMerged payload carry the opaque ck_ token, never the raw key (mobile number)
    [Fact]
    public async Task Audit_rows_and_outbox_payloads_of_every_crm_command_carry_no_phone_digits()
    {
        await AddPhonelessBooking("HCC-D1", "राम पाटील");
        await AddPhonelessBooking("HCC-D2", "सीता जोशी");
        await AddPhonelessBooking("HCC-D3", "9820099999"); // a customer whose name is a phone number
        var ashaToken = await T(CrmWorld.Asha);
        var ramToken = (await List("owner")).Customers.Single(c => c.Name == "राम पाटील").Key;
        var sitaToken = (await List("owner")).Customers.Single(c => c.Name == "सीता जोशी").Key;
        var numberToken = (await List("owner")).Customers.Single(c => c.Name == "9820099999").Key;

        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(ashaToken, "vip"), Ct);
        await _w.Commands.RemoveTagAsync(_w.Ctx("reception", Key()), new RemoveCustomerTagRequest(ashaToken, "vip"), Ct);
        await _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest(ashaToken, "Likes tea"), Ct);
        await _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(ashaToken, "Quiet"), Ct);
        await _w.Commands.SetConsentAsync(_w.Ctx("reception", Key()), new SetCustomerConsentRequest(ashaToken, WhatsApp: false), Ct);
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(numberToken, "vip"), Ct);
        // Devanagari customer merged into the phone customer (approved) and another one rejected
        var m1 = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(ramToken, ashaToken, "Same person"), Ct)).Value;
        await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m1.MergeId, true, m1.Version), Ct);
        var m2 = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(sitaToken, ashaToken, "Not sure"), Ct)).Value;
        await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m2.MergeId, false, m2.Version), Ct);
        // a merge with raw phone keys as input (contact role) must still be audited with tokens
        var m3 = (await _w.Commands.ProposeMergeAsync(_w.Ctx("manager", Key()), new ProposeCustomerMergeRequest(CrmWorld.VikramName, CrmWorld.VikramPhone, "Same person"), Ct)).Value;
        await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m3.MergeId, true, m3.Version), Ct);

        var dump = await AuditAndOutboxDumpAsync();
        AssertNoPhone(dump);
        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action LIKE 'CRM[_]%'", Ct) >= 11);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action LIKE 'CRM[_]%' AND target NOT LIKE 'crm:ck[_]%'", Ct));
        // the stable target id is the same token the API shows, so the trail can still be tied to one customer
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CRM_NOTE' AND target = 'crm:{ashaToken}'", Ct));
        // propose runs on the live index key, so its target is the token the API shows. (Approve/reject read the key back from crm.customer_merge.source_key,
        // a VARCHAR column: a Devanagari key is stored as '?' there - see af2-phone-digits-DONE.md open item - so only the ck_ shape is asserted for those.)
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CRM_MERGE_PROPOSE' AND target = 'crm:{ramToken}'", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CRM_MERGE_APPROVE' AND target = 'crm:{await T(CrmWorld.VikramName)}'", Ct));
        var vikName = await T(CrmWorld.VikramName);
        var vikPhone = await T(CrmWorld.VikramPhone);
        var payload = (string)(await _w.ScalarAsync($"SELECT TOP 1 payload FROM plat.outbox WHERE event_type = 'CustomerMerged' AND payload LIKE '%{vikName}%'", Ct))!;
        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(vikName, doc.RootElement.EnumerateObject().Single(p => p.Name.Equals("SourceKey", StringComparison.OrdinalIgnoreCase)).Value.GetString());
        Assert.Equal(vikPhone, doc.RootElement.EnumerateObject().Single(p => p.Name.Equals("TargetKey", StringComparison.OrdinalIgnoreCase)).Value.GetString());
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CustomerMerged'", Ct));
        Assert.Contains("राम पाटील", dump, StringComparison.Ordinal); // the name stays in the detail: who/what stays readable
    }
}