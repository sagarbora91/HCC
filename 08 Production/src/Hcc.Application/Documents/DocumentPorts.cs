using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Domain.Documents;
using Hcc.Domain.GuestIds;

namespace Hcc.Application.Documents;

/// <summary>PERM ids of the Documents module. Viewing the image / number and confirming reuse the existing ID-view permission PERM-GST-003.</summary>
public static class DocumentPermissions
{
    public const string Capture = "PERM-DOC-001";
    public const string Purge = "PERM-DOC-002";
    public const string IdView = "PERM-GST-003";
}

/// <summary>Master configuration setting of the module (group "Data and system", Owner-only like the rest of the group).</summary>
public static class DocumentSettings
{
    public const string RetentionKey = "documents.retentionDays";

    public static ISettingsProvider Provider { get; } = new SettingsProvider("documents", ConfigGroupIds.Data,
    [
        new(RetentionKey, "ID image retention", SettingType.Integer)
        {
            Min = DocumentRules.MinRetentionDays, Max = DocumentRules.MaxRetentionDays, Default = ConfigValue.Of(DocumentRules.DefaultRetentionDays),
            Suffix = "days", Unit = "days after capture", Effective = EffectiveMode.Date, Dated = true,
            Help = "Captured ID images and their extracted text are deleted by the purge once older than this. The confirmed ID number on the guest profile is not touched.",
            Affects = "The next purge run (older captures are judged by the value in force on the purge date).",
        },
    ]);
}

/// <summary>What the (simulated) OCR read from an image. The number, name and date of birth are sensitive: sealed before they are stored.</summary>
public sealed record OcrExtraction(string DocKind, string Number, string Name, DateOnly? DateOfBirth, int Confidence);

/// <summary>OCR engine port. The simulated engine below is deterministic; a real engine replaces it behind this interface.</summary>
// ETP-REUSE: real OCR / document-capture engine (ETP has none on this PC); only the port and the simulated adapter exist.
public interface IIdOcrEngine
{
    Task<OcrExtraction> ExtractAsync(ReadOnlyMemory<byte> image, string contentType, string hintDocKind, CancellationToken ct = default);
}

/// <summary>
/// Deterministic fake OCR. If the image bytes contain the marker <c>HCCFAKEID|kind|number|name|yyyy-MM-dd</c> (a test/demo "scan") that
/// text is returned as read; otherwise a fictional document is derived from the SHA-256 of the image (same image, same result).
/// Generated numbers are obviously fake (Aadhaar starts with 1 with a valid checksum; others start with Z).
/// </summary>
public sealed class SimulatedIdOcrEngine : IIdOcrEngine
{
    private static readonly string[] First = ["Aarav", "Diya", "Ishaan", "Meera", "Rohan", "Sana", "Vihaan", "Anaya"];
    private static readonly string[] Last = ["Demo", "Sample", "Testwala", "Fictional", "Specimen"];

    public Task<OcrExtraction> ExtractAsync(ReadOnlyMemory<byte> image, string contentType, string hintDocKind, CancellationToken ct = default)
    {
        var text = Encoding.Latin1.GetString(image.Span);
        var at = text.IndexOf("HCCFAKEID|", StringComparison.Ordinal);
        if (at >= 0)
        {
            var end = text.IndexOfAny(['\r', '\n', '\0'], at);
            var parts = text[at..(end < 0 ? text.Length : end)].Split('|');
            if (parts.Length >= 5)
            {
                DateOnly? dob = DateOnly.TryParseExact(parts[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                return Task.FromResult(new OcrExtraction(DocumentRules.Kind(parts[1]), GuestIdRules.Normalise(parts[2]), parts[3].Trim(), dob, 97));
            }
        }

        var seed = SHA256.HashData(image.Span);
        string Digits(int n, int from)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < n; i++) sb.Append((char)('0' + seed[(from + i) % seed.Length] % 10));
            return sb.ToString();
        }

        var kind = DocumentRules.Kind(hintDocKind);
        var number = kind switch
        {
            "aadhaar" => AadhaarOf("1" + Digits(10, 0)),
            "passport" => "Z" + Digits(7, 0),
            "dl" => "ZZ" + Digits(13, 0),
            "voter" => "ZZZ" + Digits(7, 0),
            "pan" => "ZZZZZ" + Digits(4, 0) + "Z",
            _ => "ZZ" + Digits(6, 0),
        };
        var name = First[seed[20] % First.Length] + " " + Last[seed[21] % Last.Length];
        var dob2 = new DateOnly(1960 + seed[22] % 41, 1 + seed[23] % 12, 1 + seed[24] % 28);
        return Task.FromResult(new OcrExtraction(kind, number, name, dob2, 80 + seed[25] % 15));
    }

    private static string AadhaarOf(string eleven) => eleven + GuestIdRules.VerhoeffDigit(eleven).ToString(CultureInfo.InvariantCulture);
}

public sealed record IdCaptureRow(
    long CaptureId, Guid CaptureRef, int PropertyId, long GuestId, long? StayId, string DocKind, string ContentType, int SizeBytes, string Status,
    string? OcrDocKind, string? OcrLast4, int? OcrConfidence, string? OcrSealed, DateOnly CapturedDate, DateTime CapturedUtc, int CapturedBy,
    DateTime? ConfirmedUtc, int? ConfirmedBy, string? RejectReason, DateTime? PurgedUtc, bool ImageAvailable, byte[] RowVer);

public sealed record NewIdCapture(
    Guid CaptureRef, int PropertyId, long GuestId, long? StayId, string DocKind, string ContentType, int SizeBytes,
    string OcrDocKind, string OcrLast4, int OcrConfidence, string OcrSealed, DateOnly CapturedDate, DateTime CapturedUtc, int CapturedBy);

/// <summary>Persistence of capture metadata and the sealed OCR text (<c>doc.id_capture</c>, migration 2100); touches <c>gst.guest</c> only to read it and to set the ID type/last 4.</summary>
public interface IDocumentStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<bool> GuestExistsAsync(ICommandTransaction tx, int propertyId, long guestId, CancellationToken ct = default);

    /// <summary>True when the stay belongs to the guest at the property.</summary>
    Task<bool> StayBelongsToGuestAsync(ICommandTransaction tx, int propertyId, long stayId, long guestId, CancellationToken ct = default);

    Task<IdCaptureRow> InsertAsync(ICommandTransaction tx, NewIdCapture c, CancellationToken ct = default);

    Task<IdCaptureRow?> LockAsync(ICommandTransaction tx, int propertyId, long captureId, CancellationToken ct = default);

    /// <summary>Read on an own connection (<paramref name="tx"/> null) or inside the transaction.</summary>
    Task<IdCaptureRow?> FindAsync(ICommandTransaction? tx, int propertyId, long captureId, CancellationToken ct = default);

    Task<IReadOnlyList<IdCaptureRow>> ListAsync(int propertyId, long? guestId, long? stayId, string? status, CancellationToken ct = default);

    Task SetStatusAsync(ICommandTransaction tx, long captureId, string status, int principalId, DateTime utc, string? rejectReason, CancellationToken ct = default);

    /// <summary>Sets the guest profile's ID type and last 4 (the display values; the number itself goes through IGuestIdStore).</summary>
    Task SetGuestIdProfileAsync(ICommandTransaction tx, long guestId, string idType, string last4, CancellationToken ct = default);

    /// <summary>Row-locked captures of the property not yet purged and captured on or before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<IdCaptureRow>> LockPurgeCandidatesAsync(ICommandTransaction tx, int propertyId, DateOnly cutoff, CancellationToken ct = default);

    /// <summary>Status Purged, sealed OCR text removed.</summary>
    Task MarkPurgedAsync(ICommandTransaction tx, long captureId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Where the sealed image text is kept (SQL adapter in production; in-memory adapter for tests). Only sealed text ever reaches it.</summary>
public interface IDocumentImageStore
{
    Task PutAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default);

    Task<string?> GetAsync(ICommandTransaction? tx, long captureId, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, long captureId, CancellationToken ct = default);
}

/// <summary>Test adapter: keeps sealed images in memory.</summary>
public sealed class InMemoryDocumentImageStore : IDocumentImageStore
{
    public ConcurrentDictionary<long, string> Images { get; } = new();

    public Task PutAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default)
    {
        Images[captureId] = sealedImage;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(ICommandTransaction? tx, long captureId, CancellationToken ct = default) =>
        Task.FromResult(Images.TryGetValue(captureId, out var s) ? s : null);

    public Task DeleteAsync(ICommandTransaction tx, long captureId, CancellationToken ct = default)
    {
        Images.TryRemove(captureId, out _);
        return Task.CompletedTask;
    }
}
