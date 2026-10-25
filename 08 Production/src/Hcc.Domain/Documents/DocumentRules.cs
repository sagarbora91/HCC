using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Domain.Documents;

/// <summary>
/// ID document capture rules (W3-3, DEC-068): kinds, image limits, retention. Pure; no I/O. Messages are fixed text and never echo input.
/// </summary>
public static class DocumentRules
{
    public const int MaxImageBytes = 4 * 1024 * 1024;
    public const int MinImageBytes = 16;

    /// <summary>
    /// Retention of captured images and OCR text. Master configuration key <c>documents.retentionDays</c> (group "Data and system") overrides
    /// this default; owner/lawyer to confirm the period (DEC-068 legal risk) - until then 90 days, counted from the capture's business date.
    /// </summary>
    public const int DefaultRetentionDays = 90;

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    public const string Captured = "Captured", Confirmed = "Confirmed", Rejected = "Rejected", Purged = "Purged";

    public static readonly IReadOnlyList<string> Kinds = ["aadhaar", "passport", "dl", "voter", "pan", "other"];

    public static readonly IReadOnlyList<string> ContentTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Normalised kind code, or VALIDATION.</summary>
    public static string Kind(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return Kinds.Contains(k) ? k : throw HccException.Validation("Choose the ID type: Aadhaar, passport, driving licence, voter ID, PAN or other.");
    }

    /// <summary>The guest profile's ID type text for a kind (<see cref="GuestIdRules.KindOf"/> maps it back).</summary>
    public static string Label(string kind) => kind switch
    {
        "aadhaar" => "Aadhaar",
        "passport" => "Passport",
        "dl" => "Driving licence",
        "voter" => "Voter ID",
        "pan" => "PAN",
        _ => "Other",
    };

    /// <summary>Decodes and checks an uploaded image: content type, size limits, and the file's magic bytes must match the declared type.</summary>
    public static byte[] DecodeImage(string? contentType, string? base64)
    {
        var type = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (!ContentTypes.Contains(type)) throw HccException.Validation("The ID image must be a JPEG, PNG or WebP picture.");
        if (string.IsNullOrWhiteSpace(base64)) throw HccException.Validation("Attach the ID image.");
        if (base64.Length > (MaxImageBytes / 3 + 1) * 4) throw HccException.Validation("The ID image is too large (4 MB at most).");
        var bytes = new byte[base64.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64String(base64, bytes, out var n)) throw HccException.Validation("The ID image is not valid base64.");
        if (n > MaxImageBytes) throw HccException.Validation("The ID image is too large (4 MB at most).");
        if (n < MinImageBytes) throw HccException.Validation("The ID image is empty or too small to be a picture.");
        var image = bytes.AsSpan(0, n).ToArray();
        if (!MagicMatches(type, image)) throw HccException.Validation("The file content does not match its picture type.");
        return image;
    }

    private static bool MagicMatches(string type, ReadOnlySpan<byte> b) => type switch
    {
        "image/jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        "image/png" => b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };

    public static int RetentionDays(long configured) =>
        configured is >= MinRetentionDays and <= MaxRetentionDays ? (int)configured : DefaultRetentionDays;

    /// <summary>Captures made on or before this business date are past retention.</summary>
    public static DateOnly PurgeCutoff(DateOnly businessDate, int retentionDays) => businessDate.AddDays(-retentionDays);
}
