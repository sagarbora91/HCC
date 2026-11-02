using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Domain.Errors;
using Hcc.Domain.Keys;

namespace Hcc.Application.Keys;

/// <summary>
/// Staff side of the room keys (spec §3-§6): RequestKey (issue / re-issue / extend / reactivate / lost card), CancelKey, ReadKey, RecordManualKey,
/// SaveKeyLock, RegisterKeyEncoder. A request never talks to the encoder: it writes a job the front-desk agent polls. Failures that need no agent
/// (room without a lock, encoder offline) are written as failed jobs at once so the log shows them and the manual-key button appears.
/// Key problems never throw into check-in / check-out: <see cref="CancelForDepartureAsync"/> swallows everything.
/// </summary>
public sealed class KeyCommands(ICommandRunner runner, IAccessPolicy policy, IKeyStore store, TimeProvider clock)
{
    public const string OpRequestKey = "RequestKey";
    public const string OpCancelKey = "CancelKey";
    public const string OpReadKey = "ReadKey";
    public const string OpManualKey = "RecordManualKey";
    public const string OpSaveLock = "SaveKeyLock";
    public const string OpRegisterEncoder = "RegisterKeyEncoder";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc.AddHours(5.5), DateTimeKind.Unspecified);

    public static string Wire(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public async Task<CommandResult<KeyJobView>> RequestKeyAsync(CommandContext ctx, RequestKeyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Issue, null, null, ct).ConfigureAwait(false);
        var ev = req.Event?.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim().ToLowerInvariant();
        KeyRules.ValidateEvent(ev, reason);
        if (ev is KeyEvents.Cancelled or KeyEvents.Read) throw HccException.Validation("Use the cancel or read action for this event.");
        if (req.Cards is < 1 or > 4) throw HccException.Validation("Cards must be 1 to 4.");
        return await runner.RunAsync(ctx with { Operation = OpRequestKey }, req, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
            if (res.Status != "InHouse") throw new HccException(HccErrorCode.INVALID_TRANSITION, "A key can only be issued for an in-house stay.");
            var roomId = res.RoomId ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The booking has no room.");
            var nowLocal = Local(UtcNow);
            var today = DateOnly.FromDateTime(nowLocal);
            var lastDay = req.ValidTo is null ? res.Departure : ParseDay(req.ValidTo);
            if (lastDay <= today) throw HccException.Validation("The key must be valid until a day after today.");
            if (lastDay > today.AddDays(60)) throw HccException.Validation("A key can be valid for at most 60 days.");
            var checkout = await store.FolioCheckoutAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var warning = KeyRules.MismatchWarning(lastDay, checkout);
            var payload = new { room = res.RoomNo, validFrom = nowLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), validTo = KeyRules.CardExpiry(lastDay).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), cards = req.Cards, guestRef = res.PublicNo };
            var job = await EnqueueAsync(tx, ctx, KeyRules.KindFor(ev!), ev!, reason, res.ReservationId, roomId, res.RoomNo, payload, warning, null, c).ConfigureAwait(false);
            await tx.AuditAsync("KEY_REQUEST", "key-job-" + job.JobId.ToString("N")[..12], $"{ev} · {reason ?? "-"} · room {res.RoomNo} · {res.PublicNo} · {job.State}{(job.ErrorCode is null ? "" : " " + job.ErrorCode)}", c).ConfigureAwait(false);
            return await ViewAsync(job, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<KeyJobView>> CancelKeyAsync(CommandContext ctx, CancelKeyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Cancel, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpCancelKey }, req, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
            var roomId = res.RoomId ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The booking has no room.");
            var job = await EnqueueAsync(tx, ctx, KeyKinds.Cancel, KeyEvents.Cancelled, null, res.ReservationId, roomId, res.RoomNo, new { room = res.RoomNo, guestRef = res.PublicNo }, null, null, c).ConfigureAwait(false);
            await tx.AuditAsync("KEY_CANCEL", "key-job-" + job.JobId.ToString("N")[..12], $"room {res.RoomNo} · {res.PublicNo} · {job.State}", c).ConfigureAwait(false);
            return await ViewAsync(job, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<KeyJobView>> ReadKeyAsync(CommandContext ctx, ReadKeyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Read, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpReadKey }, req, async (tx, c) =>
        {
            var job = await EnqueueAsync(tx, ctx, KeyKinds.Read, KeyEvents.Read, null, null, null, null, new { note = req.Note }, null, null, c).ConfigureAwait(false);
            await tx.AuditAsync("KEY_READ", "key-job-" + job.JobId.ToString("N")[..12], "read the card on the encoder · " + job.State, c).ConfigureAwait(false);
            return await ViewAsync(job, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Offline fallback: the receptionist made the card in BIS by hand; HCC records "key issued manually" (counts like the same event).</summary>
    public async Task<CommandResult<KeyJobView>> RecordManualKeyAsync(CommandContext ctx, RecordManualKeyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Issue, null, null, ct).ConfigureAwait(false);
        var ev = req.Event?.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim().ToLowerInvariant();
        KeyRules.ValidateEvent(ev, reason);
        if (ev is KeyEvents.Cancelled or KeyEvents.Read) throw HccException.Validation("A manual record is for issued, reissued, extended or reactivated.");
        return await runner.RunAsync(ctx with { Operation = OpManualKey }, req, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
            if (res.Status != "InHouse") throw new HccException(HccErrorCode.INVALID_TRANSITION, "A key can only be recorded for an in-house stay.");
            var roomId = res.RoomId ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The booking has no room.");
            var now = UtcNow;
            var jobId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new { room = res.RoomNo, guestRef = res.PublicNo, note = Trim(req.Note, 200) }, Json);
            var job = new KeyJobRec(jobId, ctx.PropertyId, KeyKinds.Manual, ev!, reason, res.ReservationId, roomId, res.RoomNo, payload, ctx.PrincipalId, string.Empty, now, null,
                KeyStates.Done, null, JsonSerializer.Serialize(new { code = KeyCodes.Ok, message = "key issued manually" }, Json), now, now, null);
            await store.InsertJobAsync(tx, job, null, c).ConfigureAwait(false);
            await store.InsertManualCardAsync(tx, ctx.PropertyId, jobId, ev!, reason, res.ReservationId, roomId, Local(now), KeyRules.CardExpiry(res.Departure), ctx.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("KEY_MANUAL", "key-job-" + jobId.ToString("N")[..12], $"{ev} manually · {reason ?? "-"} · room {res.RoomNo} · {res.PublicNo}", c).ConfigureAwait(false);
            return await ViewAsync(job, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<KeyLockView>> SaveLockAsync(CommandContext ctx, SaveKeyLockRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Configure, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var door = Trim(req.DoorName, 20);
        if (req.HasLock && string.IsNullOrEmpty(door)) throw HccException.Validation("A room with a lock needs the BIS door name.");
        if (req.DoorId is { Length: > 6 }) throw HccException.Validation("Door ID is at most 6 characters.");
        return await runner.RunAsync(ctx with { Operation = OpSaveLock }, req, async (tx, c) =>
        {
            var cur = await store.LockOfRoomAsync(tx, ctx.PropertyId, req.RoomId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            VersionToken.EnsureMatches(cur.Version, expected, "This lock mapping");
            var saved = await store.SaveLockAsync(tx, ctx.PropertyId, req with { DoorName = door, Note = Trim(req.Note, 200) }, expected, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.STALE_VERSION, "This lock mapping changed. Reload and try again.");
            await tx.AuditAsync("KEY_LOCK", "room-" + req.RoomId.ToString(CultureInfo.InvariantCulture), $"room {saved.RoomNo} · {(saved.HasLock ? "lock " + saved.DoorName : "no lock (manual key)")}", c).ConfigureAwait(false);
            return new KeyLockView(saved.RoomId, saved.RoomNo, saved.HasLock, saved.LockNo, saved.DoorId, saved.DoorName, saved.Note, VersionToken.ToWire(saved.Version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RegisterKeyEncoderResult>> RegisterEncoderAsync(CommandContext ctx, RegisterKeyEncoderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, KeyPermissions.Configure, null, null, ct).ConfigureAwait(false);
        var id = req.EncoderId?.Trim() ?? string.Empty;
        if (id.Length is < 3 or > 40 || !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')) throw HccException.Validation("Encoder ID is 3-40 letters, digits or dashes (e.g. FRONTDESK-1).");
        var pc = Trim(req.PcName, 80);
        if (string.IsNullOrEmpty(pc)) throw HccException.Validation("Enter the PC name.");
        // The token is shown once. A replay of the same idempotency key returns the stored result.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return await runner.RunAsync(ctx with { Operation = OpRegisterEncoder }, req, async (tx, c) =>
        {
            await store.InsertEncoderAsync(tx, ctx.PropertyId, id, pc, HashToken(token), c).ConfigureAwait(false);
            await tx.AuditAsync("KEY_ENCODER", "encoder-" + id, $"encoder {id} registered on {pc}", c).ConfigureAwait(false);
            return new RegisterKeyEncoderResult(id, token);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check-out hook (outbox consumer of StayDeparted): queue a software cancel of the room's key when it has an active card. NEVER throws:
    /// a failure leaves HCC check-out untouched (spec §6.8); an offline encoder leaves a failed cancel job as the reconciliation task.
    /// </summary>
    public async Task<bool> CancelForDepartureAsync(int propertyId, long reservationId, int principalId, string idemKey, CancellationToken ct = default)
    {
        try
        {
            var ctx = new CommandContext(propertyId, principalId, OpCancelKey, idemKey);
            await runner.RunAsync(ctx, new { reservationId, idemKey }, async (tx, c) =>
            {
                var res = await store.LockReservationAsync(tx, propertyId, reservationId, c).ConfigureAwait(false);
                if (res?.RoomId is not { } roomId)
                {
                    await tx.AuditAsync("KEY_CANCEL_SKIP", "reservation-" + reservationId.ToString(CultureInfo.InvariantCulture), "check-out: booking or room not found, no key to cancel", c).ConfigureAwait(false);
                    return new KeyHookResult(false);
                }

                if (!await store.HasActiveCardAsync(tx, propertyId, roomId, c).ConfigureAwait(false))
                {
                    await tx.AuditAsync("KEY_CANCEL_SKIP", "reservation-" + reservationId.ToString(CultureInfo.InvariantCulture), $"check-out: room {res.RoomNo} has no active key card (manual key or none issued)", c).ConfigureAwait(false);
                    return new KeyHookResult(false);
                }

                var job = await EnqueueAsync(tx, ctx, KeyKinds.Cancel, KeyEvents.Cancelled, null, res.ReservationId, roomId, res.RoomNo, new { room = res.RoomNo, guestRef = res.PublicNo }, null, idemKey, c).ConfigureAwait(false);
                await tx.AuditAsync("KEY_CANCEL", "key-job-" + job.JobId.ToString("N")[..12], $"check-out · room {res.RoomNo} · {res.PublicNo} · {job.State}", c).ConfigureAwait(false);
                return new KeyHookResult(true);
            }, ct).ConfigureAwait(false);
            return true;
        }
#pragma warning disable CA1031 // The hook must never block check-out.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    public sealed record KeyHookResult(bool Queued);

    internal async Task<KeyJobRec> EnqueueAsync(ICommandTransaction tx, CommandContext ctx, string kind, string ev, string? reason, long? reservationId, int? roomId, string? roomNo, object payload, string? warning, string? idem, CancellationToken ct)
    {
        var now = UtcNow;
        string? failCode = null;
        if (roomId is { } rid && kind != KeyKinds.Read)
        {
            var lk = await store.LockOfRoomAsync(tx, ctx.PropertyId, rid, ct).ConfigureAwait(false);
            if (lk is null || !lk.HasLock) failCode = KeyCodes.NoLockRoom;
        }

        if (failCode is null && await store.OnlineEncoderAsync(tx, ctx.PropertyId, now, ct).ConfigureAwait(false) is null) failCode = KeyCodes.EncoderOffline;
        var state = failCode is null ? KeyStates.Queued : KeyStates.Failed;
        var json = JsonSerializer.Serialize(payload, Json);
        var result = failCode is null ? null : JsonSerializer.Serialize(new { code = failCode, message = KeyRules.ManualOnly(failCode) }, Json);
        var job = new KeyJobRec(Guid.NewGuid(), ctx.PropertyId, kind, ev, reason, reservationId, roomId, roomNo, json, ctx.PrincipalId, string.Empty, now, null, state, failCode, result,
            now + KeyRules.QueueTtl, failCode is null ? null : now, warning);
        await store.InsertJobAsync(tx, job, idem, ct).ConfigureAwait(false);
        return job;
    }

    internal static Task<KeyJobView> ViewAsync(KeyJobRec j, CancellationToken ct) => Task.FromResult(KeyViews.Job(j));

    private static DateOnly ParseDay(string v) => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation("Valid-to must be a date yyyy-MM-dd.");

    private static string? Trim(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public static class KeyViews
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static KeyJobView Job(KeyJobRec j)
    {
        string? message = null, cardNo = null;
        if (j.ResultJson is { Length: > 0 })
        {
            using var doc = JsonDocument.Parse(j.ResultJson);
            message = doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            cardNo = doc.RootElement.TryGetProperty("cardNo", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }

        var hint = j.ErrorCode is { } e ? KeyRules.ManualOnly(e) : string.Empty;
        return new KeyJobView(j.JobId.ToString(), j.Kind, j.Event, j.Reason, j.ReservationId, j.RoomId, j.RoomNo, j.State, j.ErrorCode, message, cardNo, j.RequestedBy, j.RequestedByName,
            KeyCommands.Wire(j.RequestedUtc), KeyCommands.Wire(j.TimeoutUtc), j.FinishedUtc is { } f ? KeyCommands.Wire(f) : null, j.EncoderId, j.Warning, hint.Length == 0 ? null : hint);
    }

    public static KeyEncoderView Encoder(KeyEncoderRec? e, DateTime utcNow) => e is null
        ? new KeyEncoderView(null, null, false, null, "No encoder registered", null)
        : new KeyEncoderView(e.EncoderId, e.PcName, e.Enabled && e.Online && e.LastSeenUtc is { } t && utcNow - t <= KeyRules.HeartbeatStale, e.Adapter, e.Detail, e.LastSeenUtc is { } l ? KeyCommands.Wire(l) : null);

    public static string Serialize(object o) => JsonSerializer.Serialize(o, Web);
}
