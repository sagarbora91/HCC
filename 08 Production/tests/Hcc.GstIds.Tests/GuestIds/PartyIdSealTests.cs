using System.Data;
using System.Text;
using System.Text.Json;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.GuestIds;
using Hcc.Contracts.Stays;
using Hcc.GstIds.Tests.Harness;
using Hcc.Infrastructure.SqlServer.GuestIds;

namespace Hcc.GstIds.Tests.GuestIds;

/// <summary>
/// DEC-193 (ACC-3 lane partyid, contract §8): an accompanying guest's own ID is sealed with the same protector, the party seq in the associated
/// data (HCC-GUESTID-v1|property|guest|seq|keyId); masked per member, revealed only through the audited RevealGuestId {partySeq}; never in clear.
/// Fictional numbers only (Aadhaar starting with 1, passport Z…).
/// </summary>
public class PartyIdSealTests
{
    private static GuestIdProtector P() => new(new StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key1, ["k2"] = GstIdsDb.Key2 }));

    [Fact]
    public void Party_seal_binds_property_guest_and_seq_and_never_opens_as_the_registered_guest()
    {
        var p = P();
        var aad = GuestIdUnitTests.Aad;
        var s = p.Seal(1, 42, 2, aad);
        Assert.Equal(aad, p.Open(1, 42, 2, s));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => p.Open(1, 42, 3, s));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => p.Open(1, 43, 2, s));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => p.Open(2, 42, 2, s));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => p.Open(1, 42, s));                 // the registered guest's AAD
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => p.Open(1, 42, 2, p.Seal(1, 42, aad)));
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(aad)), Convert.ToHexString(s.Sealed), StringComparison.Ordinal);
    }
}

[Trait("Category", "Sql")]
public class PartyIdSealSqlTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Pass = "Z7000123";
    private static readonly string Aad = GuestIdUnitTests.Aad;

    private static GuestDraftDto Lead(params GuestPartyDto[] party) =>
        new("Party Lead (fictional)", "+91 98200 07777", "pl@example.test", "7 Demo Lane, Latur (fictional)", "Indian", "Passport (demo)", null, Party: party, IdNumber: "Z1000007");

    private async Task<RegisterGuestResult> RegisterAsync(GuestDraftDto guest)
    {
        var no = "R" + Guid.NewGuid().ToString("N")[..8];
        var id = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', '2026-10-24', '2026-10-26', 'Confirmed', 200000);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@no", SqlDbType.VarChar, no));
        var v = (byte[])(await db.ScalarAsync("SELECT row_ver FROM inv.reservation WHERE reservation_id = @r;", ("@r", SqlDbType.BigInt, id)))!;
        return (await db.StaysCmd.RegisterGuestAsync(db.Ctx("REC"), new RegisterGuestRequest(id, Convert.ToBase64String(v), guest), Ct)).Value;
    }

    [Fact]
    public async Task Member_number_is_sealed_with_its_seq_masked_per_role_and_never_stored_in_clear()
    {
        var r = await RegisterAsync(Lead(new GuestPartyDto("Om Lead", "Child", "Son"), new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Aadhaar (demo)", Aad)));
        Assert.Equal(Aad[^4..], await db.ScalarAsync($"SELECT id_last4 FROM gst.guest_party WHERE guest_id = {r.GuestId} AND seq = 2;"));
        Assert.Equal(true, await db.ScalarAsync($"SELECT id_required FROM gst.guest_party WHERE guest_id = {r.GuestId} AND seq = 2;"));
        Assert.Equal(false, await db.ScalarAsync($"SELECT id_required FROM gst.guest_party WHERE guest_id = {r.GuestId} AND seq = 1;"));
        var blob = (byte[])(await db.ScalarAsync($"SELECT sealed FROM gst.guest_party_id_secret WHERE guest_id = {r.GuestId} AND seq = 2;"))!;
        Assert.Equal(Aad, db.Protector.Open(db.PropertyId, r.GuestId, 2, new SealedGuestId("k1", blob)));
        var all = await db.DumpAsync("""
            SELECT * FROM plat.audit_event; SELECT * FROM plat.outbox; SELECT * FROM plat.idempotency;
            SELECT guest_id, seq, name, age_band, relation, id_type, id_last4 FROM gst.guest_party; SELECT guest_id, seq, key_id FROM gst.guest_party_id_secret;
            """) + Convert.ToHexString(blob);
        Assert.DoesNotContain(Aad, all, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Aad)), all, StringComparison.Ordinal);

        var rec = await db.StayQueries.GetGuestAsync(db.PropertyId, db.User["REC"], r.GuestId, Ct);
        Assert.Equal(("XXXX-XXXX-" + Aad[^4..], true, true, (string?)null), (rec.Party[1].IdMasked, rec.Party[1].IdOnFile, rec.Party[1].IdRequired, rec.Party[1].IdNumber));
        Assert.False(rec.Party[0].IdRequired);
        var csh = await db.StayQueries.GetGuestAsync(db.PropertyId, db.User["CSH"], r.GuestId, Ct);
        Assert.Equal(("Restricted", "Restricted"), (csh.Party[1].IdMasked, csh.Party[1].IdLast4));
        Assert.DoesNotContain(Aad, JsonSerializer.Serialize(rec), StringComparison.Ordinal);
        Assert.DoesNotContain(Aad[^4..], JsonSerializer.Serialize(csh), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Party_reveal_needs_PERM_GST_003_and_audits_the_seq_without_the_number()
    {
        var r = await RegisterAsync(Lead(new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Passport (demo)", Pass)));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.GuestIds.RevealAsync(db.Ctx("CSH"), new RevealGuestIdRequest(r.GuestId, 1), Ct));
        await Expect.Fails(HccErrorCode.VALIDATION, () => db.GuestIds.RevealAsync(db.Ctx("REC"), new RevealGuestIdRequest(r.GuestId, 7), Ct));
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => db.GuestIds.RevealAsync(db.Ctx("REC"), new RevealGuestIdRequest(r.GuestId, 2), Ct));
        var shown = await db.GuestIds.RevealAsync(db.Ctx("MGR"), new RevealGuestIdRequest(r.GuestId, 1), Ct);
        Assert.Equal((Pass, "XXXX-XXXX-" + Pass[^4..]), (shown.Value.IdNumber, shown.Value.IdMasked));
        var audit = (string)(await db.ScalarAsync($"SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW' AND target = 'guest-{r.GuestId}' ORDER BY audit_id DESC;"))!;
        Assert.Contains("party member 1", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(Pass[^4..], audit, StringComparison.Ordinal);
        var dump = await db.DumpAsync("SELECT * FROM plat.audit_event; SELECT * FROM plat.idempotency;");
        Assert.DoesNotContain(Pass, dump, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_keeps_an_unchanged_members_sealed_number_and_drops_it_for_another_person()
    {
        var r = await RegisterAsync(Lead(new GuestPartyDto("Asha Lead", "Adult", "Spouse", "Passport (demo)", Pass)));
        var g = await db.StayQueries.GetGuestAsync(db.PropertyId, db.User["REC"], r.GuestId, Ct);
        var keep = new SaveGuestProfileRequest(Lead(new GuestPartyDto("Asha Lead", "Adult", "Wife")) with { IdNumber = null, IdLast4 = "0007" }, true, r.GuestId, g.Version);
        var saved = await db.StaysCmd.SaveGuestProfileAsync(db.Ctx("REC"), keep, Ct);
        Assert.Equal(1, Convert.ToInt32(await db.ScalarAsync($"SELECT COUNT(*) FROM gst.guest_party_id_secret WHERE guest_id = {r.GuestId} AND seq = 1;"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Pass[^4..], await db.ScalarAsync($"SELECT id_last4 FROM gst.guest_party WHERE guest_id = {r.GuestId} AND seq = 1;"));
        var other = new SaveGuestProfileRequest(Lead(new GuestPartyDto("Ravi Lead", "Child", "Son")) with { IdNumber = null, IdLast4 = "0007" }, false, r.GuestId, saved.Value.GuestVersion);
        await db.StaysCmd.SaveGuestProfileAsync(db.Ctx("REC"), other, Ct);
        Assert.Equal(0, Convert.ToInt32(await db.ScalarAsync($"SELECT COUNT(*) FROM gst.guest_party_id_secret WHERE guest_id = {r.GuestId};"), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Registration_without_an_adults_ID_is_refused_naming_the_member()
    {
        var e = await Expect.Fails(HccErrorCode.VALIDATION, () => RegisterAsync(Lead(new GuestPartyDto("Asha Lead", "Adult", "Spouse"), new GuestPartyDto("Om Lead", "Child", "Son"))));
        Assert.Equal("ID missing for Asha Lead", e.Message);
        Assert.True((await RegisterAsync(Lead(new GuestPartyDto("Om Lead", "Infant", "Son")))).GuestId > 0);
    }
}
