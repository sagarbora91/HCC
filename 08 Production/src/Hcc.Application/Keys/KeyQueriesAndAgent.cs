using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Domain.Errors;
using Hcc.Domain.Keys;

namespace Hcc.Application.Keys;

/// <summary>Read side: the key panel of a booking (counts per booking and per room, active cards, recent jobs, encoder status, card expiry vs folio check-out warning), the key log report, the lock mapping.</summary>
public sealed class KeyQueries(IAccessPolicy policy, IKeyStore store, TimeProvider clock)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private Task RequireReadAsync(int propertyId, int principalId, CancellationToken ct) => policy.RequireAsync(propertyId, principalId, KeyPermissions.Read, null, null, ct);

    public async Task<KeyPanelView> PanelAsync(int propertyId, int principalId, long reservationId, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var now = UtcNow;
        await store.ExpireAsync(propertyId, now, ct).ConfigureAwait(false);
        var res = await store.ReservationAsync(propertyId, reservationId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
        KeyLockRec? lk = res.RoomId is { } rid ? await store.LockOfRoomAsync(propertyId, rid, ct).ConfigureAwait(false) : null;
        var checkout = await store.FolioCheckoutAsync(propertyId, ct).ConfigureAwait(false);
        var warning = KeyRules.MismatchWarning(res.Departure, checkout);
        var booking = await store.CountsAsync(propertyId, reservationId, null, ct).ConfigureAwait(false);
        var room = res.RoomId is { } r2 ? await store.CountsAsync(propertyId, null, r2, ct).ConfigureAwait(false) : new KeyCountsView(0, 0, 0, 0, 0, 0);
        var cards = (await store.ActiveCardsAsync(propertyId, reservationId, ct).ConfigureAwait(false)).Select(c => new KeyCardView(c.CardId, c.CardNo, c.RoomId, c.RoomNo, c.Event, c.Reason,
            c.BeginAt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), c.EndAt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), c.Status, c.IssuedBy, KeyCommands.Wire(c.IssuedUtc))).ToList();
        var jobs = (await store.JobsOfReservationAsync(propertyId, reservationId, res.RoomId, 20, ct).ConfigureAwait(false)).Select(KeyViews.Job).ToList();
        var enc = (await store.EncodersAsync(propertyId, ct).ConfigureAwait(false)).OrderByDescending(e => e.LastSeenUtc).FirstOrDefault();
        return new KeyPanelView(reservationId, res.RoomId, res.RoomNo, lk?.HasLock ?? false, lk?.Note, booking, room, cards, jobs, KeyViews.Encoder(enc, now), warning,
            KeyRules.CardExpiry(res.Departure).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), checkout.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    public async Task<KeyEncoderView> EncoderAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var enc = (await store.EncodersAsync(propertyId, ct).ConfigureAwait(false)).OrderByDescending(e => e.LastSeenUtc).FirstOrDefault();
        return KeyViews.Encoder(enc, UtcNow);
    }

    public async Task<KeyJobView> JobAsync(int propertyId, int principalId, string jobId, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        await store.ExpireAsync(propertyId, UtcNow, ct).ConfigureAwait(false);
        var j = Guid.TryParse(jobId, out var g) ? await store.GetJobAsync(g, ct).ConfigureAwait(false) : null;
        if (j is null || j.PropertyId != propertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Key job not found.");
        return KeyViews.Job(j);
    }

    public async Task<KeyLogView> LogAsync(int propertyId, int principalId, string? from, string? to, string? room, string? booking, int? user, string? ev, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (ev is not null && !KeyEvents.All.Contains(ev)) throw HccException.Validation("Unknown event type.");
        var filter = new KeyLogFilter(Day(from), Day(to), string.IsNullOrWhiteSpace(room) ? null : room.Trim(), string.IsNullOrWhiteSpace(booking) ? null : booking.Trim(), user, ev);
        await store.ExpireAsync(propertyId, UtcNow, ct).ConfigureAwait(false);
        var (rows, totals, total) = await store.LogAsync(propertyId, filter, ct).ConfigureAwait(false);
        return new KeyLogView(rows, totals, total);
    }

    public async Task<KeyLockList> LocksAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, KeyPermissions.Configure, null, null, ct).ConfigureAwait(false);
        var locks = (await store.LocksAsync(propertyId, ct).ConfigureAwait(false)).Select(l => new KeyLockView(l.RoomId, l.RoomNo, l.HasLock, l.LockNo, l.DoorId, l.DoorName, l.Note, VersionToken.ToWire(l.Version))).ToList();
        var now = UtcNow;
        var enc = (await store.EncodersAsync(propertyId, ct).ConfigureAwait(false)).Select(e => KeyViews.Encoder(e, now)).ToList();
        return new KeyLockList(locks, enc);
    }

    private static DateOnly? Day(string? v) => string.IsNullOrWhiteSpace(v) ? null : DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation("Dates are yyyy-MM-dd.");
}

/// <summary>
/// Agent side of the contract (spec §4): the front-desk PC polls, so the server needs no inbound port. Each call authenticates with the encoder's bearer token.
/// A job is handed to one agent once (atomic claim), its state only moves forward and a repeated result is ignored (idempotent jobId).
/// </summary>
public sealed class KeyAgentService(IKeyStore store, TimeProvider clock)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<KeyEncoderRec> AuthenticateAsync(string encoderId, string? bearer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearer)) throw new HccException(HccErrorCode.UNAUTHENTICATED, "Encoder token required.");
        var enc = await store.AuthenticateAsync(encoderId, KeyCommands.HashToken(bearer), ct).ConfigureAwait(false);
        return enc is { Enabled: true } ? enc : throw new HccException(HccErrorCode.UNAUTHENTICATED, "Unknown encoder or token.");
    }

    public async Task HeartbeatAsync(string encoderId, string? bearer, AgentHeartbeatRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await AuthenticateAsync(encoderId, bearer, ct).ConfigureAwait(false);
        await store.HeartbeatAsync(encoderId, req.Adapter, req.Online, req.Detail, req.X86, UtcNow, ct).ConfigureAwait(false);
    }

    /// <summary>Long-poll body: the next job for this encoder or null (the endpoint answers 204). The agent's own call counts as a heartbeat of the previous state.</summary>
    public async Task<AgentJob?> NextJobAsync(string encoderId, string? bearer, CancellationToken ct = default)
    {
        var enc = await AuthenticateAsync(encoderId, bearer, ct).ConfigureAwait(false);
        var now = UtcNow;
        await store.ExpireAsync(enc.PropertyId, now, ct).ConfigureAwait(false);
        var job = await store.ClaimNextAsync(encoderId, enc.PropertyId, now, ct).ConfigureAwait(false);
        if (job is null) return null;
        using var doc = JsonDocument.Parse(job.PayloadJson);
        var p = doc.RootElement;
        string S(string n) => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
        var cards = p.TryGetProperty("cards", out var cv) && cv.TryGetInt32(out var cn) ? cn : 1;
        var timeout = (int)Math.Max(10, (job.TimeoutUtc - now).TotalSeconds + 60);
        return new AgentJob(job.JobId.ToString(), job.Kind, S("room"), S("doorName"), S("validFrom"), S("validTo"), cards, S("guestRef"), job.RequestedByName.Length == 0 ? job.RequestedBy.ToString(CultureInfo.InvariantCulture) : job.RequestedByName, Math.Min(timeout, 60));
    }

    public async Task SetStateAsync(string encoderId, string? bearer, string jobId, AgentStateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await AuthenticateAsync(encoderId, bearer, ct).ConfigureAwait(false);
        var to = req.State;
        if (to is not (KeyStates.Picked or KeyStates.WaitingCard)) throw HccException.Validation("State must be picked or waiting_card.");
        var job = await JobOfAsync(jobId, encoderId, ct).ConfigureAwait(false);
        if (job.State == to) return;
        if (!KeyRules.CanMove(job.State, to)) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"A {job.State} key job cannot become {to}.");
        await store.MoveStateAsync(job.JobId, encoderId, job.State, to, UtcNow, ct).ConfigureAwait(false);
    }

    public async Task<KeyJobView> ResultAsync(string encoderId, string? bearer, string jobId, AgentResultRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await AuthenticateAsync(encoderId, bearer, ct).ConfigureAwait(false);
        if (!KeyCodes.All.Contains(req.Code)) throw HccException.Validation("Unknown result code.");
        var job = await JobOfAsync(jobId, encoderId, ct).ConfigureAwait(false);
        if (KeyStates.IsTerminal(job.State)) return KeyViews.Job(job);   // exactly-once: a repeated result changes nothing
        if (job.State == KeyStates.Queued) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The job has not been picked up yet.");
        var done = await store.CompleteAsync(job, encoderId, req.Code, req.Message, req.CardNo, req.GuestSn, req.AgentVersion, UtcNow, ct).ConfigureAwait(false);
        return KeyViews.Job(done);
    }

    private async Task<KeyJobRec> JobOfAsync(string jobId, string encoderId, CancellationToken ct)
    {
        var job = Guid.TryParse(jobId, out var g) ? await store.GetJobAsync(g, ct).ConfigureAwait(false) : null;
        if (job is null) throw new HccException(HccErrorCode.NOT_FOUND, "Key job not found.");
        if (job.EncoderId is not null && job.EncoderId != encoderId) throw new HccException(HccErrorCode.FORBIDDEN, "This job belongs to another encoder.");
        return job;
    }
}
