using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.GuestIds;
using Hcc.Domain.Configuration;
using Hcc.Domain.Documents;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Application.Documents;

/// <summary>
/// PERM ids of the Documents module (role matrix v2, DEC-079, migration 3700 redefines the 2101 ids). View = the masked capture list (no image,
/// no number); anything that touches a guest ID image or number (capture, confirm, reject, view) also needs <see cref="IdView"/> = PERM-DOC-004,
/// held exactly by the PERM-GST-003 holders (Owner, Manager, Reception; DEC-068), so the Cashier and Accountant capture bills, never guest IDs.
/// PERM-DOC-003 (verify a captured bill or form) has no command yet. Retention purge = Owner (PERM-DOC-005).
/// </summary>
public static class DocumentPermissions
{
    public const string View = "PERM-DOC-001";
    public const string Capture = "PERM-DOC-002";
    public const string IdView = "PERM-DOC-004";
    public const string Purge = "PERM-DOC-005";
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
            Help = "Used only by the manual purge (Purge ID captures): it deletes captured ID images and their extracted text older than this. Nothing is deleted automatically; ID data is kept until someone runs the purge (DEC-071). The confirmed ID number on the guest profile is not touched.",
            Affects = "The next manual purge (older captures are judged by the value in force on the purge date).",
        },
    ]);
}

/// <summary>What the (simulated) OCR read from an image. The number, name and date of birth are sensitive: sealed before they are stored.</summary>
public sealed record OcrExtraction(string DocKind, string Number, string Name, DateOnly? DateOfBirth, int Confidence);

/// <summary>
/// OCR engine port. The simulated engine below is deterministic (Development and the tests); in Production the not-configured engine refuses
/// (A09-08) until a real engine replaces it behind this interface.
/// </summary>
// ETP-REUSE: real OCR / document-capture engine (ETP has none on this PC); only the port, the simulated adapter and the not-configured one exist.
public interface IIdOcrEngine
{
    /// <summary>False when this server has no OCR engine (Production): a scan is refused with <see cref="OcrNotConfigured.Message"/> and the details are typed by hand.</summary>
    bool IsConfigured { get; }

    Task<OcrExtraction> ExtractAsync(ReadOnlyMemory<byte> image, string contentType, string hintDocKind, CancellationToken ct = default);
}

/// <summary>The words and the stable feature id of the "ID scanning is not set up" refusal (error UNAVAILABLE, details.reason NOT_CONFIGURED, details.feature "ocr").</summary>
public static class OcrNotConfigured
{
    public const string Feature = "ocr";

    public const string Message = "ID scanning (OCR) is not configured on this server: enter the details by hand.";

    public static HccException Refusal() => HccException.NotConfigured(Feature, Message);
}

/// <summary>
/// The Production OCR engine until a real one is wired (A09-08): it never invents an ID number. A scan is refused with a clear message,
/// so a fictional but checksum-valid number can never be stored as a real guest's ID.
/// </summary>
public sealed class NotConfiguredIdOcrEngine : IIdOcrEngine
{
    public bool IsConfigured => false;

    public Task<OcrExtraction> ExtractAsync(ReadOnlyMemory<byte> image, string contentType, string hintDocKind, CancellationToken ct = default) => throw OcrNotConfigured.Refusal();
}

/// <summary>
/// Deterministic fake OCR. If the image bytes contain the marker <c>HCCFAKEID|kind|number|name|yyyy-MM-dd</c> (a test/demo "scan") that
/// text is returned as read; otherwise a fictional document is derived from the SHA-256 of the image (same image, same result).
/// Generated numbers are obviously fake (Aadhaar starts with 1 with a valid checksum; others start with Z).
/// </summary>
public sealed class SimulatedIdOcrEngine : IIdOcrEngine
{
    public bool IsConfigured => true;

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

    /// <summary>
    /// Key rotation (A07-07): row-locked captures of the property whose sealed OCR text or sealed image is not sealed under <paramref name="keyId"/>
    /// (whatever their status), capture id order. Reads only the start of the sealed image, never the image itself.
    /// </summary>
    Task<IReadOnlyList<IdCaptureRow>> LockSealedUnderOtherKeysAsync(ICommandTransaction tx, int propertyId, string keyId, CancellationToken ct = default);

    /// <summary>Replaces the sealed OCR text of a capture (key rotation: the same content sealed under the current key).</summary>
    Task ResealOcrAsync(ICommandTransaction tx, long captureId, string ocrSealed, CancellationToken ct = default);
}

/// <summary>Where the sealed image text is kept (SQL adapter in production; in-memory adapter for tests). Only sealed text ever reaches it.</summary>
public interface IDocumentImageStore
{
    Task PutAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default);

    Task<string?> GetAsync(ICommandTransaction? tx, long captureId, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, long captureId, CancellationToken ct = default);

    /// <summary>Replaces the sealed image of a capture (key rotation: the same image sealed under the current key).</summary>
    Task ReplaceAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default);
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

    public Task ReplaceAsync(ICommandTransaction tx, long captureId, string sealedImage, CancellationToken ct = default)
    {
        if (!Images.ContainsKey(captureId)) throw new InvalidOperationException("No stored image to replace.");
        Images[captureId] = sealedImage;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Key rotation of the ID capture secrets (A07-07): part of <see cref="GuestIdService.RekeyAsync"/>, in its transaction. Every capture whose OCR text
/// or image is still sealed under an older key is opened with that key and sealed again under the current one (same associated data: property and
/// capture reference), so after a rotation no capture depends on the old key and it can be retired. Nothing is decrypted outside this loop and no
/// number, name or image reaches the audit text (only counts and key ids).
/// </summary>
public sealed class DocumentSecretsRekey(IDocumentStore store, IDocumentImageStore images, GuestIdProtector protector) : IGuestIdRekeyParticipant
{
    public async Task<GuestIdRekeyPart> RekeyAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentException.ThrowIfNullOrEmpty(currentKeyId);
        if (!string.Equals(protector.CurrentKeyId, currentKeyId, StringComparison.Ordinal))
            throw new InvalidOperationException("The guest ID key ring changed during the key rotation.");
        var count = 0;
        var from = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in await store.LockSealedUnderOtherKeysAsync(tx, propertyId, currentKeyId, ct).ConfigureAwait(false))
        {
            if (Reseal(propertyId, DocumentService.OcrPurpose(row.CaptureRef), row.OcrSealed, currentKeyId, from) is { } ocr)
            {
                await store.ResealOcrAsync(tx, row.CaptureId, ocr, ct).ConfigureAwait(false);
                count++;
            }

            if (Reseal(propertyId, DocumentService.ImagePurpose(row.CaptureRef), await images.GetAsync(tx, row.CaptureId, ct).ConfigureAwait(false), currentKeyId, from) is { } image)
            {
                await images.ReplaceAsync(tx, row.CaptureId, image, ct).ConfigureAwait(false);
                count++;
            }
        }

        return new GuestIdRekeyPart("ID capture images and OCR texts", count, [.. from]);
    }

    /// <summary>The value sealed again under the current key, or null when it is empty or already under it (the old key id goes to <paramref name="from"/>).</summary>
    private string? Reseal(int propertyId, string purpose, string? sealedText, string currentKeyId, ISet<string> from)
    {
        if (string.IsNullOrEmpty(sealedText) || GuestIdProtector.SecretKeyId(sealedText) == currentKeyId) return null;
        // UNAVAILABLE (the whole rotation rolls back) when the old key is not in the ring or the text was altered — never a half-rotated table.
        var plain = protector.OpenSecret(propertyId, purpose, sealedText);
        from.Add(GuestIdProtector.SecretKeyId(sealedText)!);
        return protector.SealSecret(propertyId, purpose, plain);
    }
}
