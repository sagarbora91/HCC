using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests;

/// <summary>Kernel half of DEC-193 (ACC-3 lane partyid, contract §8): one constant for the required age bands, the per-member checks, the carry rule.</summary>
public class PartyIdRulesTests
{
    private static readonly GuestDraft Lead = new("Party Lead", "+91 98200 07777", null, "7 Demo Lane, Latur (fictional)", "Indian", "Passport (demo)", "0007");

    [Fact]
    public void Adults_and_seniors_need_an_ID_children_and_infants_do_not()
    {
        Assert.Equal(["Adult", "Senior"], GuestRules.PartyIdRequiredBands);
        Assert.Equal([true, false, false, true], GuestRules.AgeBands.Select(GuestRules.PartyIdRequired).ToArray());
        Assert.Null(GuestRules.PartyIdMissing([new("Om", "Child", null), new("Baby", "Infant", null)]));
        Assert.Equal("ID missing for Asha", GuestRules.PartyIdMissing([new("Om", "Child", null), new("Asha", "Adult", null)]));
        Assert.Null(GuestRules.PartyIdMissing([new("Asha", "Adult", null, "Passport (demo)", "0123")]));
    }

    [Fact]
    public void Registration_validation_names_the_member_and_checks_each_ID_by_type()
    {
        string? P(params GuestPartyMember[] party) => GuestRules.Validate(Lead with { Party = party }, registration: true).TryGetValue("party", out var m) ? m : null;
        Assert.Equal("ID missing for Asha", P(new GuestPartyMember("Asha", "Senior", null)));
        Assert.Null(P(new GuestPartyMember("Om", "Child", null)));
        Assert.Equal("Party member 1: choose the ID type.", P(new GuestPartyMember("Asha", "Adult", null, IdNumber: "Z7000123")));
        Assert.StartsWith("Party member 1: Passport number", P(new GuestPartyMember("Asha", "Adult", null, "Passport (demo)", IdNumber: "Z12")), StringComparison.Ordinal);
        Assert.Null(P(new GuestPartyMember("Asha", "Adult", null, "Passport (demo)", IdNumber: "Z7000123")));
        Assert.False(GuestRules.Validate(Lead with { Party = [new GuestPartyMember("Asha", "Adult", null)] }, registration: false).ContainsKey("party"));
        var c = GuestRules.CleanMember(new GuestPartyMember(" Asha ", "Adult", " ", "Passport (demo)", null, " z7000123 "));
        Assert.Equal(new GuestPartyMember("Asha", "Adult", null, "Passport (demo)", "0123", "Z7000123"), c);
    }

    [Fact]
    public void An_edit_keeps_a_members_last4_only_for_the_same_person_and_ID_kind()
    {
        var before = Lead with { Party = [new GuestPartyMember("Asha", "Adult", null, "Passport (demo)", "0123")] };
        Assert.Equal("0123", GuestIdService.CarryPartyIds(Lead with { Party = [new GuestPartyMember("asha", "Adult", "Wife")] }, before).Party![0].IdLast4);
        Assert.Null(GuestIdService.CarryPartyIds(Lead with { Party = [new GuestPartyMember("Ravi", "Adult", null)] }, before).Party![0].IdLast4);
        Assert.Null(GuestIdService.CarryPartyIds(Lead with { Party = [new GuestPartyMember("Asha", "Adult", null, "PAN (demo)")] }, before).Party![0].IdLast4);
    }
}

/// <summary>SQL half: RegisterGuest / CheckIn refusal "ID missing for &lt;name&gt;", party ID columns (no number stored without the key ring).</summary>
[Trait("Category", "Sql")]
public class PartyIdTests : IAsyncLifetime
{
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static GuestDraftDto Lead(params GuestPartyDto[] party) => new("Party Lead", "+91 98200 07777", "pl@example.test", "7 Demo Lane, Latur (fictional)", "Indian",
        "Passport (demo)", "K777", Party: party);

    [Fact]
    public async Task Register_refuses_an_adult_without_an_ID_and_stores_the_members_ID_columns()
    {
        var res = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-25", "Party Lead", ct: Ct);
        var ver = await _w.Seed.ReservationVersionAsync(res, Ct);
        var e = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "p0"),
            new RegisterGuestRequest(res, ver, Lead(new GuestPartyDto("Om Lead", "Child", "Son"), new GuestPartyDto("Asha Lead", "Adult", "Spouse"))), Ct));
        Assert.Contains("ID missing for Asha Lead", e.Message, StringComparison.Ordinal);
        // a full number without the guest-ID key ring is refused, never stored in clear
        await Expect.ErrorAsync(HccErrorCode.UNAVAILABLE, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "p1"),
            new RegisterGuestRequest(res, ver, Lead(new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Passport (demo)", "Z7000123"))), Ct));
        var ok = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "p2"),
            new RegisterGuestRequest(res, ver, Lead(new GuestPartyDto("Om Lead", "Child", "Son"), new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Passport (demo)", IdLast4: "k123"))), Ct);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.guest_party WHERE guest_id = {ok.Value.GuestId} AND seq = 2 AND id_last4 = 'K123' AND id_type = N'Passport (demo)' AND id_required = 1;", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.guest_party WHERE guest_id = {ok.Value.GuestId} AND seq = 1 AND id_last4 IS NULL AND id_required = 0;", Ct));
        var v = await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, ok.Value.GuestId, Ct);
        Assert.Equal(("XXXX-XXXX-K123", true, false), (v.Party[1].IdMasked, v.Party[1].IdRequired, v.Party[1].IdOnFile));
    }

    [Fact]
    public async Task CheckIn_is_refused_while_a_registered_adult_has_no_ID_on_file()
    {
        var res = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-25", "Party Lead", ct: Ct);
        var reg = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "c0"),
            new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), Lead(new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Passport (demo)", IdLast4: "K123"))), Ct);
        // data from before DEC-193: the member's ID is not on file
        await _w.Db.ExecAsync($"UPDATE gst.guest_party SET id_type = NULL, id_last4 = NULL WHERE guest_id = {reg.Value.GuestId};", Ct);
        var ver1 = await _w.Seed.ReservationVersionAsync(res, Ct);
        var e = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "c1"), new CheckInRequest(res, ver1), Ct));
        Assert.Equal("ID missing for Asha Lead", e.Message);
        await _w.Db.ExecAsync($"UPDATE gst.guest_party SET id_type = N'Passport (demo)', id_last4 = 'K123' WHERE guest_id = {reg.Value.GuestId};", Ct);
        var ci = await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "c2"), new CheckInRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct)), Ct);
        Assert.True(ci.Value.StayId > 0);
    }

}
