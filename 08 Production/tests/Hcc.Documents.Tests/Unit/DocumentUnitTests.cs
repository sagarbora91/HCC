#pragma warning disable xUnit1051 // helper calls are short-lived and do not need per-call cancellation
using System.Text;
using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Domain.Documents;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Documents.Tests.Unit;

public static class Imgs
{
    public static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string AadhaarNumber(string tenDigits) { var eleven = "1" + tenDigits; return eleven + GuestIdRules.VerhoeffDigit(eleven); }

    /// <summary>A "PNG" that the simulated OCR reads as the given document (marker text after the PNG signature).</summary>
    public static byte[] Scan(string kind, string number, string name = "Test Guest", string dob = "1990-01-31")
        => [.. PngHead, .. Encoding.ASCII.GetBytes($"....HCCFAKEID|{kind}|{number}|{name}|{dob}\n....padding....")];

    public static string B64(byte[] b) => Convert.ToBase64String(b);

    public static GuestIdProtector Protector() => new(new StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray() }));
}

public class DocumentUnitTests
{
    [Fact]
    public void Image_limits_type_and_magic_bytes()
    {
        var ok = Imgs.Scan("aadhaar", "100000000000");
        Assert.Equal(ok, DocumentRules.DecodeImage("image/png", Imgs.B64(ok)));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("application/pdf", Imgs.B64(ok))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("image/jpeg", Imgs.B64(ok))).Code); // PNG bytes declared as JPEG
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("image/png", "")).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("image/png", "not base64 !!")).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("image/png", Imgs.B64(Imgs.PngHead))).Code); // too small
        var big = new byte[DocumentRules.MaxImageBytes + 1];
        Imgs.PngHead.CopyTo(big, 0);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DocumentRules.DecodeImage("image/png", Imgs.B64(big))).Code);
        var atLimit = new byte[DocumentRules.MaxImageBytes];
        Imgs.PngHead.CopyTo(atLimit, 0);
        Assert.Equal(DocumentRules.MaxImageBytes, DocumentRules.DecodeImage("image/png", Imgs.B64(atLimit)).Length);
    }

    [Fact]
    public void Retention_cutoff_and_out_of_range_setting()
    {
        Assert.Equal(new DateOnly(2026, 7, 26), DocumentRules.PurgeCutoff(new DateOnly(2026, 10, 24), 90));
        Assert.Equal(90, DocumentRules.RetentionDays(0));
        Assert.Equal(90, DocumentRules.RetentionDays(99999));
        Assert.Equal(30, DocumentRules.RetentionDays(30));
    }

    [Theory]
    [InlineData("aadhaar")]
    [InlineData("passport")]
    [InlineData("dl")]
    [InlineData("voter")]
    [InlineData("pan")]
    [InlineData("other")]
    public async Task Simulated_ocr_is_deterministic_and_yields_valid_fictional_numbers(string kind)
    {
        var img = Imgs.PngHead.Concat(Encoding.ASCII.GetBytes("some picture bytes " + kind)).ToArray();
        var ocr = new SimulatedIdOcrEngine();
        var a = await ocr.ExtractAsync(img, "image/png", kind);
        var b = await ocr.ExtractAsync(img, "image/png", kind);
        Assert.Equal(a, b);
        Assert.Equal(kind, a.DocKind);
        Assert.Equal(string.Empty, GuestIdRules.Error(DocumentRules.Label(kind), a.Number));
        Assert.NotNull(a.DateOfBirth);
        if (kind == "aadhaar") Assert.StartsWith("1", a.Number, StringComparison.Ordinal);
        var other = await ocr.ExtractAsync(img.Concat(new byte[] { 1 }).ToArray(), "image/png", kind);
        Assert.NotEqual(a.Number, other.Number);
    }

    [Fact]
    public async Task Simulated_ocr_reads_the_marker()
    {
        var n = Imgs.AadhaarNumber("2345678901");
        var r = await new SimulatedIdOcrEngine().ExtractAsync(Imgs.Scan("aadhaar", n, "Meera Specimen", "1985-06-15"), "image/png", "other");
        Assert.Equal(("aadhaar", n, "Meera Specimen", new DateOnly(1985, 6, 15)), (r.DocKind, r.Number, r.Name, r.DateOfBirth));
    }

    [Fact]
    public void Sealed_image_and_ocr_text_do_not_contain_the_plaintext_and_are_bound_to_their_purpose()
    {
        var p = Imgs.Protector();
        var number = Imgs.AadhaarNumber("2345678901");
        var sealedText = p.SealSecret(1, "doc-ocr|abc", "{\"number\":\"" + number + "\"}");
        Assert.DoesNotContain(number, sealedText, StringComparison.Ordinal);
        Assert.Contains(number, p.OpenSecret(1, "doc-ocr|abc", sealedText), StringComparison.Ordinal);
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.OpenSecret(1, "doc-ocr|other", sealedText)).Code);
        Assert.Equal(HccErrorCode.UNAVAILABLE, Assert.Throws<HccException>(() => p.OpenSecret(2, "doc-ocr|abc", sealedText)).Code);
    }

    [Fact]
    public void Masking_shows_last_four_only()
    {
        var n = Imgs.AadhaarNumber("2345678901");
        Assert.Equal("XXXX-XXXX-" + n[^4..], GuestIdRules.Mask(GuestIdRules.Last4(n)));
    }

    [Fact]
    public async Task In_memory_image_store_round_trips()
    {
        var s = new InMemoryDocumentImageStore();
        await s.PutAsync(null!, 5, "sealed");
        Assert.Equal("sealed", await s.GetAsync(null, 5));
        await s.DeleteAsync(null!, 5);
        Assert.Null(await s.GetAsync(null, 5));
    }
}
