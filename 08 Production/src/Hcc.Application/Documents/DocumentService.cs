using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.GuestIds;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Documents;

using Hcc.Domain.Configuration;
using Hcc.Domain.Documents;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;

namespace Hcc.Application.Documents;

/// <summary>
/// ID document capture (W3-3, DEC-068). Image and OCR text are sealed with the guest-ID AES-256-GCM service (<see cref="GuestIdProtector"/>);
/// staff review the capture and confirm, which writes the number into the existing sealed guest-ID storage (<see cref="IGuestIdStore"/>).
/// Everywhere else the number is masked (last 4). Viewing the image and revealing the extracted number are audited commands that need the
/// ID-view permission; no audit text, outbox event, log, error or stored command result carries the number, name, date of birth or image.
/// </summary>
public sealed class DocumentService(ICommandRunner runner, IAccessPolicy policy, IDocumentStore store, IDocumentImageStore images, IIdOcrEngine ocr,
    IGuestIdStore guestIds, GuestIdProtector protector, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null, ISettingsReader? settings = null)
{
    public const string OpCapture = "CaptureIdDocument";
    public const string OpConfirm = "ConfirmIdCapture";
    public const string OpReject = "RejectIdCapture";
    public const string OpViewImage = "ViewIdCaptureImage";
    public const string OpReveal = "RevealIdCaptureNumber";
    public const string OpPurge = "PurgeIdCaptures";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private sealed record OcrPayload(string Number, string Name, DateOnly? Dob);

    private static string ImagePurpose(Guid r) => "doc-img|" + r.ToString("N");

    private static string OcrPurpose(Guid r) => "doc-ocr|" + r.ToString("N");

    private static string Target(long id) => "id-capture-" + id.ToString(CultureInfo.InvariantCulture);

    public static IdCaptureView ToView(IdCaptureRow r) => new(r.CaptureId, r.GuestId, r.StayId, r.DocKind, DocumentRules.Label(r.DocKind), r.ContentType, r.SizeBytes, r.Status,
        r.OcrDocKind ?? string.Empty, GuestIdRules.Mask(r.OcrLast4), r.OcrConfidence ?? 0, r.CapturedDate, r.CapturedUtc, r.CapturedBy, r.ConfirmedUtc, r.ConfirmedBy,
        r.RejectReason ?? string.Empty, r.ImageAvailable, VersionToken.ToWire(r.RowVer));

    private async Task DayOpenAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        // P6-GUARD: writes need an open business day (the composition runner also guards; this keeps direct callers safe).
        if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, propertyId, ct).ConfigureAwait(false);
    }

    private static HccException NotFound() => new(HccErrorCode.NOT_FOUND, "ID capture not found.");

    public async Task<CommandResult<IdCaptureView>> CaptureAsync(CommandContext ctx, CaptureIdDocumentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.Capture, ct: ct).ConfigureAwait(false);
        if (req.GuestId <= 0) throw HccException.Validation("Select a guest.");
        var kind = DocumentRules.Kind(req.DocKind);
        var type = (req.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var bytes = DocumentRules.DecodeImage(type, req.ImageBase64);
        return await runner.RunAsync(ctx with { Operation = OpCapture }, req, async (tx, c) =>
        {
            await DayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (!await store.GuestExistsAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
            if (req.StayId is { } stay && !await store.StayBelongsToGuestAsync(tx, ctx.PropertyId, stay, req.GuestId, c).ConfigureAwait(false))
                throw HccException.Validation("That stay does not belong to this guest.");
            var read = await ocr.ExtractAsync(bytes, type, kind, c).ConfigureAwait(false);
            var reference = Guid.NewGuid();
            var sealedOcr = protector.SealSecret(ctx.PropertyId, OcrPurpose(reference),
                JsonSerializer.Serialize(new OcrPayload(GuestIdRules.Normalise(read.Number), read.Name, read.DateOfBirth)));
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var row = await store.InsertAsync(tx, new NewIdCapture(reference, ctx.PropertyId, req.GuestId, req.StayId, kind, type, bytes.Length, read.DocKind,
                GuestIdRules.Last4(read.Number), Math.Clamp(read.Confidence, 0, 100), sealedOcr, date, Now, ctx.PrincipalId), c).ConfigureAwait(false);
            await images.PutAsync(tx, row.CaptureId, protector.SealSecret(ctx.PropertyId, ImagePurpose(reference), Convert.ToBase64String(bytes)), c).ConfigureAwait(false);
            await tx.AuditAsync("ID_CAPTURE", Target(row.CaptureId),
                $"guest {req.GuestId} · {DocumentRules.Label(kind)} image captured · {bytes.Length} bytes · OCR confidence {row.OcrConfidence}%", c).ConfigureAwait(false);
            return ToView(row with { ImageAvailable = true });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<IdCaptureView>> ConfirmAsync(CommandContext ctx, ConfirmIdCaptureRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.Capture, ct: ct).ConfigureAwait(false);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.IdView, ct: ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var typed = GuestIdRules.Normalise(req.IdNumber);
        if (!req.AcceptOcr && typed.Length == 0) throw HccException.Validation("Enter the ID number, or accept the number read from the image.");
        // The idempotency payload hash sees a keyed fingerprint instead of the typed number (an unsalted SHA-256 of an Aadhaar number can be brute-forced).
        var hashable = req with { IdNumber = typed.Length == 0 ? null : protector.Fingerprint(typed) };
        return await runner.RunAsync(ctx with { Operation = OpConfirm }, hashable, async (tx, c) =>
        {
            await DayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var row = await store.LockAsync(tx, ctx.PropertyId, req.CaptureId, c).ConfigureAwait(false) ?? throw NotFound();
            VersionToken.EnsureMatches(row.RowVer, expected, "This ID capture");
            if (row.Status != DocumentRules.Captured) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a capture awaiting review can be confirmed.");
            var number = typed;
            var matchedOcr = false;
            if (row.OcrSealed is { Length: > 0 } sealedOcr)
            {
                var read = JsonSerializer.Deserialize<OcrPayload>(protector.OpenSecret(ctx.PropertyId, OcrPurpose(row.CaptureRef), sealedOcr));
                if (req.AcceptOcr) number = read?.Number ?? string.Empty;
                matchedOcr = read is not null && read.Number == number;
            }

            var label = DocumentRules.Label(row.DocKind);
            if (number.Length == 0) throw HccException.Validation("No ID number could be read from the image: type it in.");
            if (GuestIdRules.Error(label, number) is { Length: > 0 } problem) throw HccException.Validation(problem);
            await guestIds.UpsertAsync(tx, row.GuestId, protector.Seal(ctx.PropertyId, row.GuestId, number), Now, c).ConfigureAwait(false);
            await store.SetGuestIdProfileAsync(tx, row.GuestId, label, GuestIdRules.Last4(number), c).ConfigureAwait(false);
            await store.SetStatusAsync(tx, row.CaptureId, DocumentRules.Confirmed, ctx.PrincipalId, Now, null, c).ConfigureAwait(false);
            await tx.AuditAsync("ID_CAPTURE_CONFIRM", Target(row.CaptureId),
                $"guest {row.GuestId} · {label} confirmed after review · " + (req.AcceptOcr ? "OCR number accepted" : matchedOcr ? "typed number equals OCR" : "number corrected by staff"), c).ConfigureAwait(false);
            var done = await store.FindAsync(tx, ctx.PropertyId, row.CaptureId, c).ConfigureAwait(false) ?? throw NotFound();
            return ToView(done);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<IdCaptureView>> RejectAsync(CommandContext ctx, RejectIdCaptureRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.Capture, ct: ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var reason = req.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give a reason (3–200 characters).");
        return await runner.RunAsync(ctx with { Operation = OpReject }, req, async (tx, c) =>
        {
            await DayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var row = await store.LockAsync(tx, ctx.PropertyId, req.CaptureId, c).ConfigureAwait(false) ?? throw NotFound();
            VersionToken.EnsureMatches(row.RowVer, expected, "This ID capture");
            if (row.Status != DocumentRules.Captured) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a capture awaiting review can be rejected.");
            await store.SetStatusAsync(tx, row.CaptureId, DocumentRules.Rejected, ctx.PrincipalId, Now, reason, c).ConfigureAwait(false);
            await tx.AuditAsync("ID_CAPTURE_REJECT", Target(row.CaptureId), $"guest {row.GuestId} · image rejected · {reason}", c).ConfigureAwait(false);
            return ToView((await store.FindAsync(tx, ctx.PropertyId, row.CaptureId, c).ConfigureAwait(false)) ?? throw NotFound());
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Runs the audited part of a view: writes the audit row, returns only a marker (the sensitive payload is read after the transaction).</summary>
    private async Task<(IdCaptureRow Row, bool Replayed)> AuditedViewAsync(CommandContext ctx, string op, long captureId, string auditAction, string what, CancellationToken ct)
    {
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.IdView, ct: ct).ConfigureAwait(false);
        if (captureId <= 0) throw HccException.Validation("Select an ID capture.");
        var stored = await runner.RunAsync(ctx with { Operation = op }, new ViewIdCaptureImageRequest(captureId), async (tx, c) =>
        {
            var row = await store.FindAsync(tx, ctx.PropertyId, captureId, c).ConfigureAwait(false) ?? throw NotFound();
            if (row.Status == DocumentRules.Purged) throw new HccException(HccErrorCode.NOT_FOUND, "This capture was deleted by the retention purge.");
            await tx.AuditAsync(auditAction, Target(captureId), $"guest {row.GuestId} · {what} · {DocumentRules.Label(row.DocKind)}", c).ConfigureAwait(false);
            return new IdCaptureViewAudit(captureId, true);
        }, ct).ConfigureAwait(false);
        var now = await store.FindAsync(null, ctx.PropertyId, captureId, ct).ConfigureAwait(false) ?? throw NotFound();
        if (now.Status == DocumentRules.Purged) throw new HccException(HccErrorCode.NOT_FOUND, "This capture was deleted by the retention purge.");
        return (now, stored.Replayed);
    }

    public async Task<CommandResult<ViewIdCaptureImageResult>> ViewImageAsync(CommandContext ctx, ViewIdCaptureImageRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (row, replayed) = await AuditedViewAsync(ctx, OpViewImage, req.CaptureId, "ID_CAPTURE_IMAGE_VIEW", "ID image shown", ct).ConfigureAwait(false);
        var sealedImage = await images.GetAsync(null, row.CaptureId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "The image is no longer stored.");
        var base64 = protector.OpenSecret(ctx.PropertyId, ImagePurpose(row.CaptureRef), sealedImage);
        return new CommandResult<ViewIdCaptureImageResult>(new ViewIdCaptureImageResult(row.CaptureId, row.ContentType, base64), replayed);
    }

    public async Task<CommandResult<RevealIdCaptureResult>> RevealAsync(CommandContext ctx, RevealIdCaptureRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (row, replayed) = await AuditedViewAsync(ctx, OpReveal, req.CaptureId, "ID_CAPTURE_REVEAL", "extracted ID number shown", ct).ConfigureAwait(false);
        if (row.OcrSealed is not { Length: > 0 } sealedOcr) throw new HccException(HccErrorCode.NOT_FOUND, "No extracted number is stored for this capture.");
        var read = JsonSerializer.Deserialize<OcrPayload>(protector.OpenSecret(ctx.PropertyId, OcrPurpose(row.CaptureRef), sealedOcr))
            ?? throw new HccException(HccErrorCode.UNAVAILABLE, "The stored OCR text is not readable.");
        var label = DocumentRules.Label(row.OcrDocKind ?? row.DocKind);
        return new CommandResult<RevealIdCaptureResult>(new RevealIdCaptureResult(row.CaptureId, row.OcrDocKind ?? row.DocKind, read.Number, GuestIdRules.Format(label, read.Number),
            GuestIdRules.Mask(row.OcrLast4), read.Name, read.Dob, row.OcrConfidence ?? 0), replayed);
    }

    /// <summary>Retention days in force on the business date (Master configuration; the constant default without one).</summary>
    public async Task<int> RetentionDaysAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        if (settings is null) return DocumentRules.DefaultRetentionDays;
        var v = await settings.GetAsync(tx, propertyId, DocumentSettings.RetentionKey, null, ct).ConfigureAwait(false);
        return v.Kind == ConfigValueKind.Number ? DocumentRules.RetentionDays(v.Number) : DocumentRules.DefaultRetentionDays;
    }

    // TODO(owner): a background worker could call PurgeAsync nightly; for now the purge is an audited command (PERM-DOC-002).
    public async Task<CommandResult<PurgeIdCapturesResult>> PurgeAsync(CommandContext ctx, PurgeIdCapturesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, DocumentPermissions.Purge, ct: ct).ConfigureAwait(false);
        var reason = req.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give a reason (3–200 characters).");
        return await runner.RunAsync(ctx with { Operation = OpPurge }, req, async (tx, c) =>
        {
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var keep = await RetentionDaysAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var cutoff = DocumentRules.PurgeCutoff(today, keep);
            var due = await store.LockPurgeCandidatesAsync(tx, ctx.PropertyId, cutoff, c).ConfigureAwait(false);
            foreach (var r in due)
            {
                await images.DeleteAsync(tx, r.CaptureId, c).ConfigureAwait(false);
                await store.MarkPurgedAsync(tx, r.CaptureId, Now, c).ConfigureAwait(false);
            }

            await tx.AuditAsync("ID_CAPTURE_PURGE", "id-captures", $"{due.Count} ID captures purged · kept {keep} days · captured on or before {cutoff:yyyy-MM-dd} · {reason}", c).ConfigureAwait(false);
            return new PurgeIdCapturesResult(due.Count, keep, cutoff);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IdCaptureView>> ListAsync(int propertyId, int principalId, long? guestId, long? stayId, string? status, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, DocumentPermissions.Capture, ct: ct).ConfigureAwait(false);
        return (await store.ListAsync(propertyId, guestId, stayId, status, ct).ConfigureAwait(false)).Select(ToView).ToList();
    }

    public async Task<IdCaptureView> GetAsync(int propertyId, int principalId, long captureId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, DocumentPermissions.Capture, ct: ct).ConfigureAwait(false);
        return ToView(await store.FindAsync(null, propertyId, captureId, ct).ConfigureAwait(false) ?? throw NotFound());
    }
}
