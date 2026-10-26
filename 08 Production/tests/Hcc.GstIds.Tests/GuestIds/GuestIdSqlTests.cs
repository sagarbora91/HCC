using System.Data;
using System.Text;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.GuestIds;
using Hcc.Contracts.Stays;
using Hcc.GstIds.Tests.Harness;

namespace Hcc.GstIds.Tests.GuestIds;

/// <summary>DEC-068 on SQL Server: encrypted storage, masking for every role, audited reveal, no leaks, edits, wrong key, rotation.</summary>
[Trait("Category", "Sql")]
public class GuestIdSqlTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Aad = GuestIdUnitTests.Aad;

    private static GuestDraftDto Full(string name, string type = "Aadhaar (demo)", string? number = null, string? last4 = null) =>
        new(name, "+91 98200 05555", "tg@example.test", "5 Demo Lane, Latur (fictional)", "Indian", type, last4, IdNumber: number);

    private async Task<(long ReservationId, string Version)> ReservationAsync()
    {
        var no = "R" + Guid.NewGuid().ToString("N")[..8];
        var id = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', '2026-10-24', '2026-10-26', 'Confirmed', 200000);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@no", SqlDbType.VarChar, no));
        var v = (byte[])(await db.ScalarAsync("SELECT row_ver FROM inv.reservation WHERE reservation_id = @r;", ("@r", SqlDbType.BigInt, id)))!;
        return (id, Convert.ToBase64String(v));
    }

    private async Task<RegisterGuestResult> RegisterAsync(GuestDraftDto guest, string role = "REC", string? key = null)
    {
        var (rid, ver) = await ReservationAsync();
        return (await db.StaysCmd.RegisterGuestAsync(db.Ctx(role, key), new RegisterGuestRequest(rid, ver, guest), Ct)).Value;
    }

    private Task<string> EverythingButSecretsAsync() => db.DumpAsync("""
        SELECT * FROM plat.audit_event; SELECT * FROM plat.outbox; SELECT * FROM plat.idempotency;
        SELECT guest_id, property_id, full_name, phone, email, address, nationality, id_type, id_last4, company, gstin, preferences, notes FROM gst.guest;
        SELECT * FROM gst.registration; SELECT guest_id, key_id FROM gst.guest_id_secret;
        """);

    [Fact]
    public async Task Full_number_is_stored_only_encrypted_with_its_key_id_and_last4_for_display()
    {
        var r = await RegisterAsync(Full("Enc Guest (fictional)", number: Aad[..4] + " " + Aad[4..8] + " " + Aad[8..]));
        Assert.Equal(Aad[^4..], await db.ScalarAsync($"SELECT id_last4 FROM gst.guest WHERE guest_id = {r.GuestId};"));
        Assert.Equal("k1", await db.ScalarAsync($"SELECT key_id FROM gst.guest_id_secret WHERE guest_id = {r.GuestId};"));
        var blob = (byte[])(await db.ScalarAsync($"SELECT sealed FROM gst.guest_id_secret WHERE guest_id = {r.GuestId};"))!;
        Assert.Equal(Aad, db.Protector.Open(db.PropertyId, r.GuestId, new SealedGuestId("k1", blob)));
        // The number appears nowhere in clear: guest row, audit, outbox, idempotency record, registration, the sealed blob.
        var all = await EverythingButSecretsAsync() + Convert.ToHexString(blob);
        Assert.DoesNotContain(Aad, all, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Aad)), all, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.Unicode.GetBytes(Aad)), all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotency_hash_uses_a_keyed_fingerprint_not_the_number_and_replays_still_match()
    {
        var (rid, ver) = await ReservationAsync();
        var req = new RegisterGuestRequest(rid, ver, Full("Idem Guest (fictional)", "Passport (demo)", "Z7654321"));
        var key = GstIdsDb.Key();
        var first = await db.StaysCmd.RegisterGuestAsync(db.Ctx("REC", key), req, Ct);
        var again = await db.StaysCmd.RegisterGuestAsync(db.Ctx("REC", key), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.GuestId, again.Value.GuestId);
        var stored = (byte[])(await db.ScalarAsync("SELECT payload_sha256 FROM plat.idempotency WHERE idem_key = @k;", ("@k", SqlDbType.VarChar, key)))!;
        Assert.NotEqual(Convert.ToHexString(CanonicalJson.Sha256(req)), Convert.ToHexString(stored));
        Assert.Equal(Convert.ToHexString(CanonicalJson.Sha256(db.GuestIds.HashablePayload(req))), Convert.ToHexString(stored));
        // Same key, different number → mismatch (the fingerprint differs).
        await Expect.Fails(HccErrorCode.IDEMPOTENCY_MISMATCH, () => db.StaysCmd.RegisterGuestAsync(db.Ctx("REC", key),
            req with { Guest = req.Guest! with { IdNumber = "Z7654322" } }, Ct));
    }

    [Fact]
    public async Task Reads_are_masked_for_every_role_and_never_carry_the_number()
    {
        var r = await RegisterAsync(Full("Mask Guest (fictional)", number: Aad));
        foreach (var role in GstIdsDb.Roles)
        {
            var user = db.User[role];
            if (role is "OWN" or "MGR" or "REC" or "CSH")
            {
                var v = await db.StayQueries.GetGuestAsync(db.PropertyId, user, r.GuestId, Ct);
                var json = JsonSerializer.Serialize(v);
                Assert.DoesNotContain(Aad, json, StringComparison.Ordinal);
                if (role == "CSH")
                {
                    Assert.Equal(("Restricted", "Restricted", "Restricted", true, false), (v.IdMasked, v.IdLast4, v.IdType, v.Masked, v.CanRevealId));
                    Assert.DoesNotContain(Aad[^4..], json, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal(("XXXX-XXXX-" + Aad[^4..], true, true, false), (v.IdMasked, v.IdOnFile, v.CanRevealId, v.Masked));
                }
            }
            else
            {
                await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.StayQueries.GetGuestAsync(db.PropertyId, user, r.GuestId, Ct));
            }
        }

        // An older last-4-only profile: masked, nothing on file to reveal.
        var old = await RegisterAsync(Full("Old Guest (fictional)", last4: "4101"));
        var ov = await db.StayQueries.GetGuestAsync(db.PropertyId, db.User["REC"], old.GuestId, Ct);
        Assert.Equal(("XXXX-XXXX-4101", false, false), (ov.IdMasked, ov.IdOnFile, ov.CanRevealId));
        await Expect.Fails(HccErrorCode.NOT_FOUND, () => db.GuestIds.RevealAsync(db.Ctx("REC"), new RevealGuestIdRequest(old.GuestId), Ct));
    }

    [Fact]
    public async Task Reveal_needs_the_ID_view_permission_and_writes_an_audit_row_without_the_number()
    {
        var r = await RegisterAsync(Full("Reveal Guest (fictional)", number: Aad));
        foreach (var role in new[] { "CSH", "HKP", "SUP", "ACC", "TAD", "MNT" })
            await Expect.Fails(HccErrorCode.FORBIDDEN, () => db.GuestIds.RevealAsync(db.Ctx(role), new RevealGuestIdRequest(r.GuestId), Ct));
        foreach (var role in new[] { "OWN", "MGR", "REC" })
        {
            var before = await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW';");
            var key = GstIdsDb.Key();
            var shown = await db.GuestIds.RevealAsync(db.Ctx(role, key), new RevealGuestIdRequest(r.GuestId), Ct);
            Assert.Equal((Aad, Aad[..4] + " " + Aad[4..8] + " " + Aad[8..], "Aadhaar (demo)", "XXXX-XXXX-" + Aad[^4..]),
                (shown.Value.IdNumber, shown.Value.Formatted, shown.Value.IdType, shown.Value.IdMasked));
            Assert.Equal(before + 1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW';"));
            var audit = await db.DumpAsync($"SELECT TOP (1) principal_id, target, detail, at_utc FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW' ORDER BY audit_id DESC;");
            Assert.StartsWith(db.User[role] + "|guest-" + r.GuestId + "|", audit, StringComparison.Ordinal);
            Assert.DoesNotContain(Aad, audit, StringComparison.Ordinal);
            Assert.DoesNotContain(Aad[^4..], audit, StringComparison.Ordinal);
            // Stored command result has no number; a replay shows it again and is audited again (F-L04), still without the number.
            var result = (string?)await db.ScalarAsync("SELECT result_json FROM plat.idempotency WHERE idem_key = @k;", ("@k", SqlDbType.VarChar, key));
            Assert.DoesNotContain(Aad, result!, StringComparison.Ordinal);
            var replay = await db.GuestIds.RevealAsync(db.Ctx(role, key), new RevealGuestIdRequest(r.GuestId), Ct);
            Assert.True(replay.Replayed);
            Assert.Equal(Aad, replay.Value.IdNumber);
            Assert.Equal(before + 2, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW';"));
            var again = await db.DumpAsync($"SELECT TOP (1) principal_id, target, detail FROM plat.audit_event WHERE action = 'GUEST_ID_VIEW' ORDER BY audit_id DESC;");
            Assert.StartsWith(db.User[role] + "|guest-" + r.GuestId + "|", again, StringComparison.Ordinal);
            Assert.Contains("replayed request", again, StringComparison.Ordinal);
            Assert.DoesNotContain(Aad[^4..], again, StringComparison.Ordinal);
        }

        await Expect.Fails(HccErrorCode.NOT_FOUND, () => db.GuestIds.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(99_999_999), Ct));
        Assert.DoesNotContain(Aad, await EverythingButSecretsAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_without_retyping_keeps_the_number_type_change_needs_a_new_number()
    {
        var (rid, ver) = await ReservationAsync();
        var reg = (await db.StaysCmd.RegisterGuestAsync(db.Ctx("REC"), new RegisterGuestRequest(rid, ver, Full("Edit Guest (fictional)", number: Aad)), Ct)).Value;
        async Task<string> ResVer() => Convert.ToBase64String((byte[])(await db.ScalarAsync($"SELECT row_ver FROM inv.reservation WHERE reservation_id = {rid};"))!);
        async Task<string> GuestVer() => Convert.ToBase64String((byte[])(await db.ScalarAsync($"SELECT row_ver FROM gst.guest WHERE guest_id = {reg.GuestId};"))!);
        async Task<RegisterGuestResult> Save(GuestDraftDto d) => (await db.StaysCmd.RegisterGuestAsync(db.Ctx("REC"),
            new RegisterGuestRequest(rid, await ResVer(), d, reg.GuestId, await GuestVer()), Ct)).Value;

        await Save(Full("Edit Guest (fictional)", last4: Aad[^4..]) with { Notes = "edited" });
        Assert.Equal(Aad, (await db.GuestIds.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(reg.GuestId), Ct)).Value.IdNumber);
        await Expect.Fails(HccErrorCode.VALIDATION, () => Save(Full("Edit Guest (fictional)", "PAN (demo)", last4: Aad[^4..])));
        await Expect.Fails(HccErrorCode.VALIDATION, () => Save(Full("Edit Guest (fictional)", last4: "9999")));
        await Expect.Fails(HccErrorCode.VALIDATION, () => Save(Full("Edit Guest (fictional)", "PAN (demo)", Aad)));
        await Save(Full("Edit Guest (fictional)", "PAN (demo)", "ZZZZZ1234Z"));
        var shown = (await db.GuestIds.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(reg.GuestId), Ct)).Value;
        Assert.Equal(("ZZZZZ1234Z", "PAN (demo)"), (shown.IdNumber, shown.IdType));
        Assert.Equal("234Z", await db.ScalarAsync($"SELECT id_last4 FROM gst.guest WHERE guest_id = {reg.GuestId};"));
    }

    [Fact]
    public async Task Invalid_numbers_fail_validation_without_echoing_the_number()
    {
        var bad = Aad[..11] + (char)('0' + (Aad[11] - '0' + 1) % 10);
        var ex = await Expect.Fails(HccErrorCode.VALIDATION, () => RegisterAsync(Full("Bad Guest (fictional)", number: bad)));
        Assert.DoesNotContain(bad, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(bad, JsonSerializer.Serialize(ex.ToResponse()), StringComparison.Ordinal);
        ex = await Expect.Fails(HccErrorCode.VALIDATION, () => RegisterAsync(Full("Bad Guest (fictional)", "PAN (demo)", Aad)));
        Assert.DoesNotContain(Aad, ex.Message, StringComparison.Ordinal);
        // A server without the guest ID key refuses numbers (never stores them in clear).
        var noKeys = new Hcc.Application.Stays.StaysCommands(db.Runner, db.Policy, new Hcc.Infrastructure.SqlServer.Stays.SqlStaysStore(), new NoFolios(), db.Clock);
        var (rid, ver) = await ReservationAsync();
        var un = await Expect.Fails(HccErrorCode.UNAVAILABLE, () => noKeys.RegisterGuestAsync(db.Ctx("REC"), new RegisterGuestRequest(rid, ver, Full("No Key (fictional)", number: Aad)), Ct));
        Assert.DoesNotContain(Aad, un.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_key_fails_closed_and_rotation_re_encrypts_under_the_new_key()
    {
        var r = await RegisterAsync(Full("Rotate Guest (fictional)", "Driving licence (demo)", "ZZ0120110012345"));
        // A server holding only a different key under another id cannot read it; nor can one with a wrong key under the same id.
        var other = db.WithRing(new Hcc.Application.GuestIds.StaticGuestIdKeyRing("k2", new Dictionary<string, byte[]> { ["k2"] = GstIdsDb.Key2 }));
        var e1 = await Expect.Fails(HccErrorCode.UNAVAILABLE, () => other.Service.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(r.GuestId), Ct));
        Assert.DoesNotContain("ZZ0120110012345", e1.Message, StringComparison.Ordinal);
        var wrong = db.WithRing(new Hcc.Application.GuestIds.StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key2 }));
        await Expect.Fails(HccErrorCode.UNAVAILABLE, () => wrong.Service.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(r.GuestId), Ct));

        // Rotation: ring {k1, k2 current}; Owner only; afterwards every row is under k2 and k1 can be retired.
        var both = db.WithRing(new Hcc.Application.GuestIds.StaticGuestIdKeyRing("k2", new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key1, ["k2"] = GstIdsDb.Key2 }));
        foreach (var role in new[] { "MGR", "TAD", "REC" })
            await Expect.Fails(HccErrorCode.FORBIDDEN, () => both.Service.RekeyAsync(db.Ctx(role), new RekeyGuestIdsRequest("Annual rotation (fictional)"), Ct));
        var rk = await both.Service.RekeyAsync(db.Ctx("OWN"), new RekeyGuestIdsRequest("Annual rotation (fictional)"), Ct);
        Assert.True(rk.Value.Rekeyed >= 1);
        Assert.Equal(("k2", "k1"), (rk.Value.CurrentKeyId, Assert.Single(rk.Value.FromKeyIds)));
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM gst.guest_id_secret WHERE key_id <> 'k2';"));
        Assert.Equal("ZZ0120110012345", (await other.Service.RevealAsync(db.Ctx("OWN"), new RevealGuestIdRequest(r.GuestId), Ct)).Value.IdNumber);
        var audit = await db.DumpAsync("SELECT detail FROM plat.audit_event WHERE action = 'GUEST_ID_REKEY';");
        Assert.Contains("to key k2 from k1", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("ZZ0120110012345", await EverythingButSecretsAsync(), StringComparison.Ordinal);
        // New registrations seal under the current key of the ring that registers them.
        var (rid2, ver2) = await ReservationAsync();
        var r2 = (await both.Stays.RegisterGuestAsync(db.Ctx("REC"), new RegisterGuestRequest(rid2, ver2, Full("New Key Guest (fictional)", "Voter ID (demo)", "ZZZ1234567")), Ct)).Value;
        Assert.Equal("k2", await db.ScalarAsync($"SELECT key_id FROM gst.guest_id_secret WHERE guest_id = {r2.GuestId};"));
    }
}
