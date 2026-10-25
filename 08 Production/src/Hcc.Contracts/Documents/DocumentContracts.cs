namespace Hcc.Contracts.Documents;

// W3-3 ID document capture / OCR (DEC-068). Views are masked (last 4 of the extracted number only). The extracted number, name and date of
// birth, and the stored image, leave the server only in RevealIdCaptureResult / ViewIdCaptureImageResult - each an audited command that
// needs the ID-view permission, never cached, never part of a stored command result. Legal risk recorded in DEC-068 (Aadhaar Act 2016).

/// <summary>POST /api/v1/commands/CaptureIdDocument (K). <c>DocKind</c>: aadhaar | passport | dl | voter | pan | other. Image: JPEG/PNG/WebP, up to 4 MB, base64.</summary>
public sealed record CaptureIdDocumentRequest(long GuestId, long? StayId, string DocKind, string ContentType, string ImageBase64);

/// <summary>A capture as every role sees it: masked extracted number, no image, no name/date of birth.</summary>
public sealed record IdCaptureView(
    long CaptureId, long GuestId, long? StayId, string DocKind, string IdType, string ContentType, int SizeBytes, string Status,
    string OcrDocKind, string OcrIdMasked, int OcrConfidence, DateOnly CapturedDate, DateTime CapturedUtc, int CapturedBy,
    DateTime? ConfirmedUtc, int? ConfirmedBy, string RejectReason, bool ImageAvailable, string Version);

/// <summary>POST /api/v1/commands/ConfirmIdCapture (K): after reviewing the image, accept the OCR number or type the corrected one. Writes the sealed guest ID.</summary>
public sealed record ConfirmIdCaptureRequest(long CaptureId, string ExpectedVersion, bool AcceptOcr, string? IdNumber);

/// <summary>POST /api/v1/commands/RejectIdCapture (K): unreadable or wrong image; the image is kept until the retention purge.</summary>
public sealed record RejectIdCaptureRequest(long CaptureId, string ExpectedVersion, string Reason);

/// <summary>POST /api/v1/commands/ViewIdCaptureImage (K). Needs PERM-GST-003; audit ID_CAPTURE_IMAGE_VIEW.</summary>
public sealed record ViewIdCaptureImageRequest(long CaptureId);

public sealed record ViewIdCaptureImageResult(long CaptureId, string ContentType, string ImageBase64);

/// <summary>POST /api/v1/commands/RevealIdCaptureNumber (K). Needs PERM-GST-003; audit ID_CAPTURE_REVEAL (never the number).</summary>
public sealed record RevealIdCaptureRequest(long CaptureId);

public sealed record RevealIdCaptureResult(long CaptureId, string DocKind, string IdNumber, string Formatted, string IdMasked, string Name, DateOnly? DateOfBirth, int Confidence);

/// <summary>POST /api/v1/commands/PurgeIdCaptures (K). Deletes images and OCR text older than the retention setting (PERM-DOC-002).</summary>
public sealed record PurgeIdCapturesRequest(string Reason);

public sealed record PurgeIdCapturesResult(int Purged, int RetentionDays, DateOnly CutoffDate);

/// <summary>Stored by the runner for the two audited views: never the image or the number (the idempotency record is not encrypted).</summary>
public sealed record IdCaptureViewAudit(long CaptureId, bool Audited);
