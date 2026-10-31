using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// W1a LG (LG-HISTORY-CONTRACT §2, §11; DEC-162 Q4): the optional date of birth is validated (a date, after 1900, not in the future), stored in
/// gst.guest.dob, read back on GuestView (contact readers only) together with LegacyGuestId. Fictional data only.
/// </summary>
[Trait("Category", "Sql")]
public class GuestDobSqlTests : IAsyncLifetime
{
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static readonly GuestDraftDto Full = new("Kiran Patil", "+91 98200 06666", null, "4 Demo Lane, Latur (fictional)", "Indian", Dob: "1985-03-12");

    [Fact]
    public void Dob_rules_accept_blank_and_real_dates_and_refuse_text_old_and_future_dates()
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Null(GuestRules.DobError(null, today));
        Assert.Null(GuestRules.DobError(" ", today));
        Assert.Null(GuestRules.DobError("1900-01-01", today));
        Assert.Null(GuestRules.DobError("2026-10-24", today));
        Assert.NotNull(GuestRules.DobError("12/03/1985", today));
        Assert.NotNull(GuestRules.DobError("1985-02-30", today));
        Assert.NotNull(GuestRules.DobError("1899-12-31", today));
        Assert.NotNull(GuestRules.DobError("2026-10-25", today));
        Assert.Null(GuestRules.DobError("2999-01-01", null));   // no date given: the future check is skipped
        var errors = GuestRules.Validate(new GuestDraft("Kiran Patil", "9820006666", Dob: "2999-01-01"), false, today);
        Assert.Equal("Date of birth cannot be in the future.", errors["dob"]);
        Assert.Equal("1985-03-12", GuestRules.Clean(new GuestDraft("Kiran Patil", "9820006666", Dob: " 1985-03-12 ")).Dob);
        // LG-R6: an explicit blank is kept as "" (= clear on update); an omitted dob stays null (= keep the stored date).
        Assert.Equal("", GuestRules.Clean(new GuestDraft("Kiran Patil", "9820006666", Dob: " ")).Dob);
        Assert.Null(GuestRules.Clean(new GuestDraft("Kiran Patil", "9820006666")).Dob);
    }

    [Fact]
    public async Task Dob_is_saved_read_back_masked_for_the_cashier_and_cleared_by_an_edit()
    {
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "d0"), new SaveGuestProfileRequest(Full with { Dob = "2999-01-01" }), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "d1"), new SaveGuestProfileRequest(Full with { Dob = "1850-01-01" }), Ct));
        var created = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "d2"), new SaveGuestProfileRequest(Full), Ct);
        var id = created.Value.GuestId;
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.guest WHERE guest_id = {id} AND dob = '1985-03-12' AND legacy_guest_id IS NULL;", Ct));

        var g = await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, id, Ct);
        Assert.Equal(("1985-03-12", (long?)null), (g.Dob, g.LegacyGuestId));
        Assert.Null((await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Cashier, id, Ct)).Dob);

        // the legacy importer sets legacy_guest_id; the views carry it
        await _w.Db.ExecAsync($"UPDATE gst.guest SET legacy_guest_id = 4711 WHERE guest_id = {id};", Ct);
        var listed = (await _w.StayQueries.ListGuestsAsync(_w.PropertyId, Users.Reception, "patil", 1, 50, Ct)).Items.Single();
        Assert.Equal(("1985-03-12", (long?)4711), (listed.Dob, listed.LegacyGuestId));

        // LG-R6: a save that omits dob (a client that does not send the field) keeps the stored date
        var kept = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "d3"), new SaveGuestProfileRequest(Full with { Dob = null, Notes = "edited" }, GuestId: id, GuestExpectedVersion: listed.Version), Ct);
        var afterKeep = await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, kept.Value.GuestId, Ct);
        Assert.Equal(("1985-03-12", "edited"), (afterKeep.Dob, afterKeep.Notes));
        // an explicit empty value clears it
        var edited = await _w.Stays.SaveGuestProfileAsync(_w.Ctx(Users.Reception, "d4"), new SaveGuestProfileRequest(Full with { Dob = "" }, GuestId: id, GuestExpectedVersion: afterKeep.Version), Ct);
        Assert.Null((await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, edited.Value.GuestId, Ct)).Dob);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.guest WHERE guest_id = {id} AND dob IS NULL;", Ct));
        Assert.Equal(4711L, (await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, id, Ct)).LegacyGuestId);   // an edit never touches the legacy id
    }
}
