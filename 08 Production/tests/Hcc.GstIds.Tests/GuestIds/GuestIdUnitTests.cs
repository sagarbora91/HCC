using System.Text;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;
using Hcc.Domain.Stays;
using Hcc.GstIds.Tests.Harness;
using Hcc.Infrastructure.SqlServer.GuestIds;

namespace Hcc.GstIds.Tests.GuestIds;

/// <summary>DEC-068 rules ported from 03 Prototype/tests/stream-guestid-full.mjs ([GID]) + AES-GCM protector + key ring. Fictional numbers only.</summary>
public class GuestIdUnitTests
{
    // 11 digits + Verhoeff check digit; the leading 1 marks it fictional (real Aadhaar numbers never start with 0 or 1).
    private const string Body = "12345678901";
    public static readonly string Aad = Body + GuestIdRules.VerhoeffDigit(Body);

    private static GuestIdProtector P(string current = "k1") => new(new StaticGuestIdKeyRing(current, new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key1, ["k2"] = GstIdsDb.Key2 }));

    [Fact]
    public void Verhoeff_known_vector_round_trip_and_every_single_digit_error_is_caught()
    {
        Assert.True(GuestIdRules.Verhoeff("2363"));
        Assert.False(GuestIdRules.Verhoeff("2364"));
        Assert.True(GuestIdRules.Verhoeff(Aad));
        for (var i = 0; i < 12; i++)
        {
            var bad = Aad[..i] + (char)('0' + (Aad[i] - '0' + 1) % 10) + Aad[(i + 1)..];
            Assert.False(GuestIdRules.Verhoeff(bad), "digit " + i);
        }
    }

    [Fact]
    public void Validation_per_ID_type_with_the_prototype_wording()
    {
        static string Err(string t, string n) => GuestIdRules.Error(t, n);
        Assert.Equal("", Err("Aadhaar (demo)", Aad));
        Assert.Equal("", Err("Aadhaar", Aad[..4] + " " + Aad[4..8] + "-" + Aad[8..]));
        Assert.Contains("12 digits", Err("Aadhaar", "12345678901"), StringComparison.Ordinal);
        Assert.Contains("12 digits", Err("Aadhaar", "1234567890AB"), StringComparison.Ordinal);
        Assert.Contains("checksum", Err("Aadhaar", Aad[..11] + (char)('0' + (Aad[11] - '0' + 1) % 10)), StringComparison.Ordinal);
        Assert.Equal("", Err("Passport (demo)", "z1234567"));
        Assert.Contains("Passport", Err("Passport", "12345678"), StringComparison.Ordinal);
        Assert.Contains("Passport", Err("Passport", "Z123456"), StringComparison.Ordinal);
        Assert.Equal("", Err("Driving licence (demo)", "MH-12 20110012345"));
        Assert.Contains("licence", Err("Driving licence", "MH12"), StringComparison.Ordinal);
        Assert.Equal("", Err("Voter ID (demo)", "ABC1234567"));
        Assert.Contains("Voter", Err("Voter ID", "AB12345678"), StringComparison.Ordinal);
        Assert.Equal("", Err("PAN (demo)", "ABCDE1234F"));
        Assert.Contains("PAN", Err("PAN", "ABCDE12345"), StringComparison.Ordinal);
        Assert.Equal("", Err("Other (demo)", "REF-9876"));
        Assert.Contains("4 to 20", Err("Other", "AB"), StringComparison.Ordinal);
        Assert.Equal("", Err("Aadhaar", ""));
        // Messages never echo the input.
        foreach (var (t, n) in new[] { ("Aadhaar", Aad[..11] + "0"), ("PAN", "ZZZZZ12345"), ("Passport", "Q99999999") })
            Assert.DoesNotContain(GuestIdRules.Normalise(n), Err(t, n), StringComparison.Ordinal);
    }

    [Fact]
    public void Normalise_last4_mask_and_format()
    {
        Assert.Equal("MH1220110012345", GuestIdRules.Normalise(" mh-12 20110012345 "));
        Assert.Equal(Aad[^4..], GuestIdRules.Last4(Aad));
        Assert.Equal("XXXX-XXXX-" + Aad[^4..], GuestIdRules.Mask(Aad[^4..]));
        Assert.Equal("XXXX-XXXX-K555", GuestIdRules.Mask("k555"));
        Assert.Equal("", GuestIdRules.Mask(null));
        Assert.Equal(Aad[..4] + " " + Aad[4..8] + " " + Aad[8..], GuestIdRules.Format("Aadhaar (demo)", Aad));
        Assert.Equal("Z7654321", GuestIdRules.Format("Passport", "Z7654321"));
        Assert.Equal(GuestIdKind.DrivingLicence, GuestIdRules.KindOf("Driving licence (demo)"));
    }

    [Fact]
    public void Guest_draft_requires_a_number_or_an_older_last4_validates_the_number_and_derives_last4()
    {
        var full = new GuestDraft("Test Guest", "+91 98200 05555", "tg@example.test", "5 Demo Lane, Latur (fictional)", "Indian", "Aadhaar (demo)");
        Assert.Equal("Enter the ID number.", GuestRules.Validate(full, true)["idNumber"]);
        Assert.Empty(GuestRules.Validate(full with { IdNumber = Aad }, true));
        Assert.Empty(GuestRules.Validate(full with { IdLast4 = "4101" }, true)); // profile from before DEC-068
        Assert.True(GuestRules.Validate(full with { IdNumber = "123" }, true).ContainsKey("idNumber"));
        Assert.True(GuestRules.Validate(full with { IdType = "PAN (demo)", IdNumber = Aad }, true).ContainsKey("idNumber"));
        Assert.True(GuestRules.Validate(full with { IdType = null, IdNumber = Aad }, false).ContainsKey("idType"));
        var clean = GuestRules.Clean(full with { IdNumber = Aad[..4] + " " + Aad[4..8] + " " + Aad[8..], IdLast4 = "9999" });
        Assert.Equal((Aad, Aad[^4..]), (clean.IdNumber, clean.IdLast4));
        Assert.DoesNotContain(Aad, GuestRules.Summary(GuestRules.Validate(full with { IdType = "PAN (demo)", IdNumber = Aad }, true)), StringComparison.Ordinal);
    }

    [Fact]
    public void AES_GCM_round_trip_random_nonce_ciphertext_never_holds_the_number()
    {
        var p = P();
        var a = p.Seal(1, 42, Aad);
        var b = p.Seal(1, 42, Aad);
        Assert.Equal("k1", a.KeyId);
        Assert.NotEqual(Convert.ToHexString(a.Sealed), Convert.ToHexString(b.Sealed));
        Assert.Equal(1 + 12 + 16 + 12, a.Sealed.Length);
        Assert.Equal(Aad, p.Open(1, 42, a));
        Assert.Equal(Aad, p.Open(1, 42, b));
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Aad)), Convert.ToHexString(a.Sealed), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Aad[^4..])), Convert.ToHexString(a.Sealed[29..]), StringComparison.Ordinal);
        foreach (var n in new[] { "Z7654321", "ZZ0120110012345", "ZZZ1234567", "ZZZZZ1234Z", "FICT0001", "FICT0001FICT0001FICT" })
            Assert.Equal(n, p.Open(3, 7, p.Seal(3, 7, n)));
    }

    [Fact]
    public void Wrong_key_tampering_or_another_guests_row_fails_without_revealing_the_number()
    {
        var p = P();
        var s = p.Seal(1, 42, Aad);
        // Wrong key under the same key id (e.g. a restored database with a different key file).
        var wrong = new GuestIdProtector(new StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = GstIdsDb.Key2 }));
        var e1 = Assert.Throws<HccException>(() => wrong.Open(1, 42, s));
        Assert.Equal(HccErrorCode.UNAVAILABLE, e1.Code);
        Assert.DoesNotContain(Aad, e1.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Aad[^4..], e1.Message, StringComparison.Ordinal);
        // Key not in the ring.
        var missing = new GuestIdProtector(new StaticGuestIdKeyRing("k2", new Dictionary<string, byte[]> { ["k2"] = GstIdsDb.Key2 }));
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => missing.Open(1, 42, s)).Code);
        // Copied onto another guest / property, or altered.
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.Open(1, 43, s)).Code);
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.Open(2, 42, s)).Code);
        var tampered = (byte[])s.Sealed.Clone();
        tampered[^1] ^= 1;
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.Open(1, 42, s with { Sealed = tampered })).Code);
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.Open(1, 42, s with { KeyId = "k2" })).Code);
    }

    [Fact]
    public void Fingerprint_is_keyed_stable_and_not_the_number()
    {
        var f1 = P().Fingerprint(Aad);
        Assert.Equal(f1, P().Fingerprint(Aad));
        Assert.NotEqual(f1, P().Fingerprint("Z7654321"));
        Assert.NotEqual(f1, P("k2").Fingerprint(Aad));
        Assert.DoesNotContain(Aad, f1, StringComparison.Ordinal);
        Assert.StartsWith("hmac:", f1, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_ring_from_configuration_dpapi_round_trip_and_plain_keys_only_when_allowed()
    {
        var wrapped = DpapiGuestIdKeyRing.NewWrappedKey(machine: false);
        Assert.StartsWith("dpapi-user:", wrapped, StringComparison.Ordinal);
        var ring = new DpapiGuestIdKeyRing("2026-10", new Dictionary<string, string?> { ["2026-10"] = wrapped }, allowPlainKeys: false);
        var p = new GuestIdProtector(ring);
        Assert.Equal(Aad, p.Open(1, 5, p.Seal(1, 5, Aad)));
        Assert.Equal(32, ring.Key("2026-10")!.Length);
        var machine = new DpapiGuestIdKeyRing("m", new Dictionary<string, string?> { ["m"] = DpapiGuestIdKeyRing.NewWrappedKey(machine: true) }, false);
        Assert.NotNull(machine.Key("m"));

        var plain = "plain:" + Convert.ToBase64String(GstIdsDb.Key1);
        Assert.Throws<InvalidOperationException>(() => new DpapiGuestIdKeyRing("k1", new Dictionary<string, string?> { ["k1"] = plain }, allowPlainKeys: false));
        Assert.NotNull(new DpapiGuestIdKeyRing("k1", new Dictionary<string, string?> { ["k1"] = plain }, allowPlainKeys: true).Key("k1"));
        Assert.Throws<InvalidOperationException>(() => new DpapiGuestIdKeyRing("k1", new Dictionary<string, string?> { ["k1"] = "plain:" + Convert.ToBase64String(new byte[16]) }, true));
        Assert.Throws<InvalidOperationException>(() => new DpapiGuestIdKeyRing("k1", new Dictionary<string, string?>(), true));
        Assert.Throws<InvalidOperationException>(() => new DpapiGuestIdKeyRing(null, new Dictionary<string, string?>(), true));
        Assert.Throws<InvalidOperationException>(() => new DpapiGuestIdKeyRing("k1", new Dictionary<string, string?> { ["k1"] = "raw:abc" }, true));
    }
}
