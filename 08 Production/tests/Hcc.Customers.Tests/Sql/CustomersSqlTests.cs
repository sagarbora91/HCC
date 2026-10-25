using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;
using Hcc.Domain.Errors;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// Customer 360 on SQL Server with the real access policy. Fixture (24 Oct 2026): Asha Rao = 2 profiles with the same mobile, 3 stays
/// (2 departed + 1 in house), 6 nights, 17,325.00 (1,732,500 paise), average nightly rate 4,166.67 (416,667 paise); Vikram Shah = name-only customer + one with a mobile.
/// </summary>
[Trait("Category", "Sql")]
public class CustomersSqlTests : IAsyncLifetime
{
    private CrmWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await CrmWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Key() => Guid.NewGuid().ToString("N");

    private int U(string user) => _w.Users[user];

    private static async Task Fails(HccErrorCode code, Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<HccException>(action);
        Assert.Equal(code, e.Code);
    }

    private Task<CustomerProfile> Profile(string user, string key) => _w.Queries.ProfileAsync(_w.PropertyId, U(user), key, Ct);

    private async Task<MergeView> Propose(string user, string source, string target, string reason = "Same person, no mobile on first booking") =>
        (await _w.Commands.ProposeMergeAsync(_w.Ctx(user, Key()), new ProposeCustomerMergeRequest(source, target, reason), Ct)).Value;

    [Fact]
    public async Task Customer_360_reproduces_the_fixture_figures_in_paise()
    {
        var p = await Profile("owner", CrmWorld.Asha);
        Assert.Equal("Asha Rao", p.Name);
        Assert.Equal(3, p.Stays.Count);
        Assert.Equal(3, p.CompletedStays);
        Assert.Equal(6, p.Nights);
        Assert.Equal(1_732_500, p.LifetimePaise);
        Assert.Equal(416_667, p.AvgRatePaise);
        Assert.Equal("2026-10-10", p.FirstStay);
        Assert.Equal("2026-10-23", p.LastStay);
        Assert.Equal("HCC-A3", p.Current?.BookingNo);
        Assert.Equal(["101", "102"], p.TopRooms);
        Assert.Equal(2, p.GuestIds.Count);
        Assert.Equal("Quiet room, extra pillow", p.Preferences);
        Assert.Equal(["HCC-A3", "HCC-A2", "HCC-A1"], p.Stays.Select(s => s.BookingNo));
        Assert.Equal(840_000, p.Stays.Single(s => s.BookingNo == "HCC-A1").TotalPaise);
        // list: 5 customers (the expired hold is not a booking); newest stay first
        var list = await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 50, Ct);
        Assert.Equal(5, list.Total);
        Assert.Equal(CrmWorld.VikramPhone, list.Customers[0].Key);
        var vik = await Profile("owner", CrmWorld.VikramName);
        Assert.Equal(1, vik.CompletedStays);
        Assert.Equal(315_000, vik.LifetimePaise);
    }

    [Fact]
    public async Task Contacts_are_masked_without_the_reveal_permission_and_roles_without_view_are_refused()
    {
        var owner = await Profile("owner", CrmWorld.Asha);
        Assert.False(owner.Masked);
        Assert.Equal("asha.rao@gmail.com", owner.Email);
        Assert.Equal(1_732_500, (await Profile("reception", CrmWorld.Asha)).LifetimePaise);
        var cash = await Profile("cashier", CrmWorld.Asha);
        Assert.True(cash.Masked);
        Assert.Equal("••••••1111", cash.Phone);
        Assert.Equal("a•••@gmail.com", cash.Email);
        // masked roles cannot search by number, and the list rows are masked too
        Assert.Empty((await _w.Queries.ListAsync(_w.PropertyId, U("cashier"), "982001", null, 1, 50, Ct)).Customers);
        Assert.Single((await _w.Queries.ListAsync(_w.PropertyId, U("reception"), "982001", null, 1, 50, Ct)).Customers);
        Assert.All((await _w.Queries.ListAsync(_w.PropertyId, U("cashier"), null, null, 1, 50, Ct)).Customers.Where(c => c.Phone != string.Empty), c => Assert.True(c.Masked));
        // name search works for everyone with view; Accountant and Housekeeper have no Customers page
        Assert.NotEmpty((await _w.Queries.ListAsync(_w.PropertyId, U("cashier"), "rao asha", null, 1, 50, Ct)).Customers);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.ListAsync(_w.PropertyId, U("accountant"), null, null, 1, 50, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => Profile("housekeeper", CrmWorld.Asha));
    }

    [Fact]
    public async Task No_id_document_data_leaves_the_customer_views()
    {
        var json = JsonSerializer.Serialize(await Profile("owner", CrmWorld.Asha)) + JsonSerializer.Serialize(await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 50, Ct));
        Assert.DoesNotContain("AB12", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Aadhaar", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_detection_finds_normalised_email_and_name_without_mobile()
    {
        var d = await _w.Queries.DuplicatesAsync(_w.PropertyId, U("manager"), Ct);
        Assert.Equal(3, d.Groups.Count);
        Assert.Equal(["email", "name-without-phone", "name-without-phone"], d.Groups.Select(g => g.Reason));
        Assert.Equal([CrmWorld.Asha2, CrmWorld.Asha], d.Groups[0].Customers.Select(c => c.Key));
        Assert.Equal(CrmWorld.VikramPhone, d.Groups[1].SuggestedSurvivor);
        Assert.Equal(CrmWorld.Asha, d.Groups[2].SuggestedSurvivor);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Queries.DuplicatesAsync(_w.PropertyId, U("reception"), Ct));
    }

    [Fact]
    public async Task Merge_is_maker_checker_keeps_history_and_repoints_stays_through_the_alias()
    {
        await Fails(HccErrorCode.FORBIDDEN, () => Propose("reception", CrmWorld.VikramName, CrmWorld.VikramPhone));
        await Fails(HccErrorCode.VALIDATION, () => Propose("manager", CrmWorld.VikramName, CrmWorld.VikramName));
        await Fails(HccErrorCode.NOT_FOUND, () => Propose("manager", "n:nobody", CrmWorld.VikramPhone));
        await Fails(HccErrorCode.VALIDATION, () => Propose("manager", CrmWorld.VikramName, CrmWorld.VikramPhone, "x"));
        await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(CrmWorld.VikramName, "Corporate"), Ct);
        await _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest(CrmWorld.VikramName, "Prefers ground floor"), Ct);

        var m = await Propose("manager", CrmWorld.VikramName, CrmWorld.VikramPhone);
        Assert.Equal("Pending", m.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => Propose("owner", CrmWorld.VikramName, CrmWorld.VikramPhone)); // one open proposal per duplicate
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.DecideMergeAsync(_w.Ctx("manager", Key()), new DecideCustomerMergeRequest(m.MergeId, true, m.Version), Ct)); // maker = checker
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.DecideMergeAsync(_w.Ctx("reception", Key()), new DecideCustomerMergeRequest(m.MergeId, true, m.Version), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m.MergeId, true, "AAAAAAAAAAA="), Ct));
        Assert.Equal(5, (await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 50, Ct)).Total); // nothing changed yet

        var key = Key();
        var done = await _w.Commands.DecideMergeAsync(_w.Ctx("owner", key), new DecideCustomerMergeRequest(m.MergeId, true, m.Version, "Confirmed by phone"), Ct);
        Assert.Equal("Approved", done.Value.Status);
        Assert.True((await _w.Commands.DecideMergeAsync(_w.Ctx("owner", key), new DecideCustomerMergeRequest(m.MergeId, true, m.Version, "Confirmed by phone"), Ct)).Replayed);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m.MergeId, false, done.Value.Version), Ct));

        var p = await Profile("owner", CrmWorld.VikramPhone);
        Assert.Equal(3, p.Stays.Count);
        Assert.Equal(["HCC-V2", "HCC-V1", "HCC-V3"], p.Stays.Select(s => s.BookingNo));
        Assert.Equal(1, p.CompletedStays);
        Assert.Equal(315_000, p.LifetimePaise);
        Assert.Equal(1, p.Cancelled);
        Assert.Equal([CrmWorld.VikramName], p.MergedFrom);
        Assert.Equal(["corporate"], p.Tags);
        Assert.Equal(["Prefers ground floor"], p.Notes.Select(n => n.Body));
        Assert.Equal(4, (await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 50, Ct)).Total);
        // the old key still resolves (bookmarks, notes written by an older client)
        Assert.Equal(CrmWorld.VikramPhone, (await Profile("owner", CrmWorld.VikramName)).Key);
        // no other module's rows were touched
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM gst.guest WHERE full_name = N'Vikram Shah'", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_alias", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action IN ('CRM_MERGE_PROPOSE','CRM_MERGE_APPROVE')", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'CustomerMerged'", Ct));
        var approved = await _w.Queries.MergesAsync(_w.PropertyId, U("manager"), "Approved", Ct);
        Assert.Equal("Vikram Shah", approved.Merges.Single().SourceName);
        // the duplicate is gone from the duplicate list
        Assert.DoesNotContain((await _w.Queries.DuplicatesAsync(_w.PropertyId, U("manager"), Ct)).Groups, g => g.Customers.Any(c => c.Key == CrmWorld.VikramName));
    }

    [Fact]
    public async Task A_rejected_merge_changes_nothing_and_can_be_proposed_again()
    {
        var m = await Propose("owner", CrmWorld.VikramName, CrmWorld.VikramPhone);
        var r = await _w.Commands.DecideMergeAsync(_w.Ctx("manager", Key()), new DecideCustomerMergeRequest(m.MergeId, false, m.Version, "Not the same guest"), Ct);
        Assert.Equal("Rejected", r.Value.Status);
        Assert.Equal(5, (await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, null, 1, 50, Ct)).Total);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_alias", Ct));
        Assert.Equal("Pending", (await Propose("manager", CrmWorld.VikramName, CrmWorld.VikramPhone)).Status);
    }

    [Fact]
    public async Task Chained_merges_follow_the_survivor()
    {
        var m1 = await Propose("manager", CrmWorld.VikramName, CrmWorld.VikramPhone);
        await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m1.MergeId, true, m1.Version), Ct);
        var m2 = await Propose("manager", CrmWorld.VikramPhone, CrmWorld.Asha, "Vikram is Asha's husband, same account");
        await _w.Commands.DecideMergeAsync(_w.Ctx("owner", Key()), new DecideCustomerMergeRequest(m2.MergeId, true, m2.Version), Ct);
        var p = await Profile("owner", CrmWorld.VikramName);
        Assert.Equal(CrmWorld.Asha, p.Key);
        Assert.Equal(6, p.Stays.Count);
        Assert.Equal(2, p.MergedFrom.Count);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM crm.customer_alias WHERE target_key = '" + CrmWorld.Asha + "'", Ct));
    }

    [Fact]
    public async Task Tags_notes_and_preferences_need_the_edit_permission_and_validate_input()
    {
        var ctx = _w.Ctx("reception", Key());
        var req = new AddCustomerTagRequest(CrmWorld.Asha, " VIP ");
        Assert.Contains("vip", (await _w.Commands.AddTagAsync(ctx, req, Ct)).Value.Summary, StringComparison.Ordinal);
        Assert.Contains("already", (await _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), req, Ct)).Value.Summary, StringComparison.Ordinal);
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.AddTagAsync(_w.Ctx("cashier", Key()), req, Ct));
        await Fails(HccErrorCode.VALIDATION, () => _w.Commands.AddTagAsync(_w.Ctx("reception", Key()), new AddCustomerTagRequest(CrmWorld.Asha, "no<good>"), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest("n:ghost", "hello"), Ct));
        Assert.Equal(["vip"], (await Profile("cashier", CrmWorld.Asha)).Tags);
        Assert.Equal(["vip"], (await _w.Queries.ListAsync(_w.PropertyId, U("owner"), null, "VIP", 1, 50, Ct)).Customers.Single().Tags);
        await _w.Commands.RemoveTagAsync(_w.Ctx("reception", Key()), new RemoveCustomerTagRequest(CrmWorld.Asha, "vip"), Ct);
        await Fails(HccErrorCode.NOT_FOUND, () => _w.Commands.RemoveTagAsync(_w.Ctx("reception", Key()), new RemoveCustomerTagRequest(CrmWorld.Asha, "vip"), Ct));

        await _w.Commands.AddNoteAsync(_w.Ctx("reception", Key()), new AddCustomerNoteRequest(CrmWorld.Asha, "Anniversary on 12 Oct"), Ct);
        Assert.Equal("Anniversary on 12 Oct", (await Profile("owner", CrmWorld.Asha)).Notes.Single().Body);

        await _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(CrmWorld.Asha, "Top floor, no feather pillows"), Ct);
        var p = await Profile("owner", CrmWorld.Asha);
        Assert.Equal("Top floor, no feather pillows", p.Preferences);
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(CrmWorld.Asha, "x"), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(CrmWorld.Asha, "x", "AAAAAAAAAAA="), Ct));
        await _w.Commands.SetPreferencesAsync(_w.Ctx("reception", Key()), new SetCustomerPreferencesRequest(CrmWorld.Asha, string.Empty, p.PreferencesVersion), Ct);
        Assert.Equal("Quiet room, extra pillow", (await Profile("owner", CrmWorld.Asha)).Preferences); // the profile's own preferences show again
    }

    [Fact]
    public async Task Marketing_consent_is_written_to_messaging_for_every_guest_profile_of_the_customer()
    {
        Assert.True((await Profile("owner", CrmWorld.Asha)).Consent is { WhatsApp: true, OptOut: false });
        await Fails(HccErrorCode.FORBIDDEN, () => _w.Commands.SetConsentAsync(_w.Ctx("cashier", Key()), new SetCustomerConsentRequest(CrmWorld.Asha, OptOut: true), Ct));
        await _w.Commands.SetConsentAsync(_w.Ctx("reception", Key()), new SetCustomerConsentRequest(CrmWorld.Asha, WhatsApp: false, Sms: true), Ct);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM msg.consent WHERE whatsapp = 0 AND sms = 1 AND opt_out = 0", Ct));
        await _w.Commands.SetConsentAsync(_w.Ctx("manager", Key()), new SetCustomerConsentRequest(CrmWorld.Asha, OptOut: true), Ct);
        var c = (await Profile("owner", CrmWorld.Asha)).Consent;
        Assert.True(c.OptOut);
        Assert.False(c.WhatsApp);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM msg.consent WHERE opt_out = 1", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CRM_CONSENT'", Ct));
        // a name-only customer without a saved profile has nothing to attach consent to
        await Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.SetConsentAsync(_w.Ctx("reception", Key()), new SetCustomerConsentRequest("n:asha rao", OptOut: true), Ct));
    }
}
