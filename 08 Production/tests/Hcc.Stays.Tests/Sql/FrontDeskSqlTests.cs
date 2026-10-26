using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>W4-1 front desk: SaveGuestProfile (create / edit / verify without a reservation), the GET /guests list with masks, and "use existing profile" through RegisterGuest.</summary>
[Trait("Category", "Sql")]
public class FrontDeskSqlTests : IAsyncLifetime
{
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static readonly GuestDraftDto Full = new("Meera Joshi", "+91 98200 07777", "meera@example.test", "9 Demo Road, Latur (fictional)", "Indian",
        "Passport (demo)", "J777", Preferences: "High floor", Notes: "Prefers early breakfast", Party: [new GuestPartyDto("Ira Joshi", "Child", "Daughter")]);

    [Fact]
    public async Task SaveGuestProfile_creates_edits_versions_and_keeps_ID_details_out_of_the_audit()
    {
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s0"), new SaveGuestProfileRequest(Full with { Name = "M" }), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s1"),
            new SaveGuestProfileRequest(Full with { IdLast4 = null, IdType = null }, Verified: true), Ct)); // verified needs ID + address
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Cashier, "s2"), new SaveGuestProfileRequest(Full), Ct));

        var created = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s3"), new SaveGuestProfileRequest(Full), Ct);
        Assert.True(created.Value.Created);
        Assert.False(created.Value.Verified);
        var id = created.Value.GuestId;

        // Editing needs the version; a stale one is rejected.
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s4"), new SaveGuestProfileRequest(Full, GuestId: id), Ct));
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s5"),
            new SaveGuestProfileRequest(Full, GuestId: id, GuestExpectedVersion: "AAAAAAAAAAA="), Ct));
        var upd = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s6"),
            new SaveGuestProfileRequest(Full with { Address = "10 Demo Road" }, Verified: true, GuestId: id, GuestExpectedVersion: created.Value.GuestVersion), Ct);
        Assert.False(upd.Value.Created);
        Assert.True(upd.Value.Verified);
        Assert.NotEqual(created.Value.GuestVersion, upd.Value.GuestVersion);
        // A later unverified edit keeps a verified profile verified while it still has an ID and an address.
        var again = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "s7"),
            new SaveGuestProfileRequest(Full with { Notes = "x" }, GuestId: id, GuestExpectedVersion: upd.Value.GuestVersion), Ct);
        Assert.True(again.Value.Verified);

        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%J777%' OR detail LIKE '%Meera%' OR detail LIKE '%98200%' OR detail LIKE '%Demo Road%';", Ct));
        Assert.Equal(3, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'SAVE_GUEST';", Ct));

        // The list returns the profile with preferences, notes, origin and the same masks as the single read.
        var owner = await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, "joshi", 1, 50, Ct);
        var g = owner.Items.Single();
        Assert.Equal((id, "J777", "High floor", "x", "manual", true), (g.GuestId, g.IdLast4, g.Preferences, g.Notes, g.Origin, g.Verified));
        Assert.Single(g.Party);
        var cashier = (await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Cashier, "joshi", 1, 50, Ct)).Items.Single();
        Assert.Equal(("Restricted", true), (cashier.IdLast4, cashier.Masked));
        Assert.DoesNotContain("98200", cashier.Phone, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_list_pages_filters_and_validates()
    {
        var all = await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, null, 1, 500, Ct);
        Assert.True(all.Total >= 1);
        Assert.Equal(all.Total, all.Items.Count);
        var first = await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, null, 1, 1, Ct);
        Assert.Equal((all.Total, 1), (first.Total, first.Items.Count));
        Assert.Empty((await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, "no-such-guest-100%", 1, 10, Ct)).Items);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, null, 0, 10, Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, null, 1, 501, Ct));
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Housekeeper, null, 1, 10, Ct));
    }

    [Fact]
    public async Task RegisterGuest_with_only_a_guest_id_links_the_chosen_profile()
    {
        var p = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "u0"), new SaveGuestProfileRequest(Full, Verified: true), Ct);
        var res = await _w.Seed.ReservationAsync("107", "2026-10-24", "2026-10-25", "Meera Joshi", ct: Ct);
        var ver = await _w.Seed.ReservationVersionAsync(res, Ct);
        var reg = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "u1"), new RegisterGuestRequest(res, ver, null, p.Value.GuestId), Ct);
        Assert.Equal(p.Value.GuestId, reg.Value.GuestId);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.registration WHERE reservation_id = {res} AND guest_id = {p.Value.GuestId};", Ct));
    }
}
