using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Ota;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;
using Hcc.Domain.Ota;

namespace Hcc.Application.Ota;

/// <summary>
/// OTA channel commands (prototype CHN_* handlers; everything simulated). Authorization first, every command through <see cref="ICommandRunner"/>.
/// Inbound bookings are never inserted here: Accept runs the existing QuoteStay / CreateReservation / AmendReservation / CancelReservation commands under keys
/// derived from the message, so availability, capacity, holds and restrictions are identical to manual bookings and a crash or retry replays instead of duplicating.
/// A message is first CLAIMED (Pending -> Importing, by the accepting user), worked, then FINALIZED (Accepted / Applied + commission ledger) in one transaction;
/// a failed attempt releases the claim.
/// </summary>
public sealed class OtaService(IOtaStore store, OtaQueries queries, InventoryService inventory, ReservationService reservations, ICommandRunner runner,
    IAccessPolicy access, TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null)
{
    public const string Importing = "Importing";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ───────────── channel setup (Manage) ─────────────
    public async Task<CommandResult<ChannelResult>> SaveChannelAsync(CommandContext ctx, SaveChannelRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            OtaRules.Terms(r.CommissionBps, r.MarkupBps);
            var name = (r.Name ?? "").Trim();
            if (name.Length < 2 || name.Length > 80) throw HccException.Validation("Name the channel (2-80 characters).");
            var types = await store.ListRoomTypesAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            var map = new List<(int RoomTypeId, string ChannelCode)>();
            foreach (var m in r.Mapping ?? [])
            {
                var type = types.FirstOrDefault(t => t.Id == m.RoomTypeId);
                if (type.Id == 0) throw HccException.Validation($"Unknown room type {m.RoomTypeId.ToString(CultureInfo.InvariantCulture)}.");
                if (map.Any(x => x.RoomTypeId == m.RoomTypeId)) throw HccException.Validation($"Room type {type.Code} is mapped twice.");
                var code = OtaRules.ChannelCode(m.ChannelCode, type.Code);
                if (code is null) continue;
                if (map.Any(x => x.ChannelCode == code)) throw HccException.Validation($"Channel code {code} is used twice.");
                map.Add((m.RoomTypeId, code));
            }

            if (r.ChannelId is null)
            {
                var code = OtaRules.InternalCode(r.Code);
                var id = await store.InsertChannelAsync(tx, ctx.PropertyId, code, name, r.CommissionBps, r.MarkupBps, map, c).ConfigureAwait(false);
                var created = await store.GetChannelAsync(ctx.PropertyId, id, tx, false, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
                var text = $"{name} added · {OtaRules.Pct(r.CommissionBps)} · markup {OtaRules.Pct(r.MarkupBps)} · {map.Count} mapped";
                await tx.AuditAsync("SaveChannel", code, text, c).ConfigureAwait(false);
                return new ChannelResult(id, InventoryErrors.VersionToWire(created.RowVersion), created.Status, text);
            }

            var expected = InventoryErrors.ParseVersion(r.ExpectedVersion);
            var old = await store.GetChannelAsync(ctx.PropertyId, r.ChannelId.Value, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            var before = $"{OtaRules.Pct(old.CommissionBps)} · markup {OtaRules.Pct(old.MarkupBps)} · {old.Map.Count} mapped";
            await store.UpdateChannelAsync(tx, ctx.PropertyId, old.ChannelId, expected, name, r.CommissionBps, r.MarkupBps, map, c).ConfigureAwait(false);
            var after = $"{OtaRules.Pct(r.CommissionBps)} · markup {OtaRules.Pct(r.MarkupBps)} · {map.Count} mapped";
            var fresh = await store.GetChannelAsync(ctx.PropertyId, old.ChannelId, tx, false, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            await tx.AuditAsync("SaveChannel", old.Code, $"{name} · {before} → {after}", c).ConfigureAwait(false);
            return new ChannelResult(old.ChannelId, InventoryErrors.VersionToWire(fresh.RowVersion), fresh.Status, $"{name} · {before} → {after}");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChannelResult>> SetStatusAsync(CommandContext ctx, SetChannelStatusRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Manage, ct: ct).ConfigureAwait(false);
        var to = (r.Action ?? "").Trim().ToLowerInvariant() switch
        {
            ChannelActions.Connect => ChannelStatuses.Connected,
            ChannelActions.Pause => ChannelStatuses.Paused,
            ChannelActions.Disconnect => ChannelStatuses.Disconnected,
            _ => throw HccException.Validation("Unknown connection action (connect, pause or disconnect)."),
        };
        var expected = InventoryErrors.ParseVersion(r.ExpectedVersion);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var ch = await store.GetChannelAsync(ctx.PropertyId, r.ChannelId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            if (!InventoryErrors.VersionEquals(ch.RowVersion, expected)) throw InventoryErrors.Stale("This channel");
            if (ch.Status == to) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{ch.Name} is already {to.ToLowerInvariant()}.");
            var note = to == ChannelStatuses.Connected ? "Simulated connection test passed"
                : to == ChannelStatuses.Paused ? "Paused by staff — nothing is sent or received" : "Disconnected by staff";
            await store.SetStatusAsync(tx, ch.ChannelId, to, note, to == ChannelStatuses.Connected ? Now : null, c).ConfigureAwait(false);
            var fresh = await store.GetChannelAsync(ctx.PropertyId, ch.ChannelId, tx, false, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            var text = $"{ch.Name} · {ch.Status} → {to} (simulated)";
            await tx.AuditAsync("SetChannelStatus", ch.Code, text, c).ConfigureAwait(false);
            return new ChannelResult(ch.ChannelId, InventoryErrors.VersionToWire(fresh.RowVersion), to, text);
        }, ct).ConfigureAwait(false);
    }

    // ───────────── stop-sell and push (Operate) ─────────────
    public async Task<CommandResult<ChannelResult>> SetStopSellAsync(CommandContext ctx, SetChannelStopSellRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var types = await store.ListRoomTypesAsync(ctx.PropertyId, tx, c).ConfigureAwait(false);
            var type = types.FirstOrDefault(t => t.Id == r.RoomTypeId);
            if (type.Id == 0) throw HccException.Validation("Choose a room type.");
            string label;
            int id;
            string version;
            string status;
            if (r.ChannelId is { } cid)
            {
                var ch = await store.GetChannelAsync(ctx.PropertyId, cid, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
                if (ch.MapFor(type.Id) is null) throw HccException.Validation($"{type.Code} is not mapped on {ch.Name}.");
                label = ch.Name;
                id = ch.ChannelId;
                version = InventoryErrors.VersionToWire(ch.RowVersion);
                status = ch.Status;
            }
            else
            {
                label = "all channels";
                id = 0;
                version = "";
                status = "";
            }

            await store.SetStopSellAsync(tx, ctx.PropertyId, r.ChannelId, type.Id, r.On, Now, c).ConfigureAwait(false);
            if (r.ChannelId is { } cid2)
                version = InventoryErrors.VersionToWire((await store.GetChannelAsync(ctx.PropertyId, cid2, tx, false, c).ConfigureAwait(false))!.RowVersion);
            var text = $"{type.Code} · stop-sell {(r.On ? "on" : "off")} · {label}";
            await tx.AuditAsync("SetChannelStopSell", type.Code, text, c).ConfigureAwait(false);
            return new ChannelResult(id, version, status, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<PushResult>> PushRatesAsync(CommandContext ctx, PushChannelRatesRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var ch = await store.GetChannelAsync(ctx.PropertyId, r.ChannelId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            if (ch.Status != ChannelStatuses.Connected)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{ch.Name} is {ch.Status.ToLowerInvariant()}. Connect it before pushing.");
            var (rows, unmapped, _) = await queries.BuildRowsAsync(ctx.PropertyId, ctx.PrincipalId, ch, c).ConfigureAwait(false);
            if (rows.Count == 0) throw HccException.Validation($"No room types are mapped on {ch.Name}.");
            var closed = rows.Count(x => x.Closed);
            var summary = $"{rows.Count} rate/availability rows · {closed} closed" + (unmapped.Count > 0 ? " · not mapped: " + string.Join(", ", unmapped) : "");
            var now = Now;
            var id = await store.InsertPushAsync(tx, new PushDraft(ctx.PropertyId, ch.ChannelId, now, ctx.PrincipalId, summary, rows), c).ConfigureAwait(false);
            await store.MarkPushedAsync(tx, ch.ChannelId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("PushChannelRates", ch.Code, $"{ch.Name} · simulated push · {summary}", c).ConfigureAwait(false);
            await tx.PublishAsync(OtaEvents.PushQueued, id, new { pushId = id, channelId = ch.ChannelId, rows = rows.Count, closed }, c).ConfigureAwait(false);
            return new PushResult(id, "Queued", rows.Count, closed, summary);
        }, ct).ConfigureAwait(false);
    }

    // ───────────── inbound messages ─────────────
    public async Task<CommandResult<ChannelMessageResult>> ImportMessageAsync(CommandContext ctx, ImportChannelMessageRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var m = r.Message ?? throw HccException.Validation("The message is required.");
            var ch = await store.GetChannelAsync(ctx.PropertyId, m.ChannelId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            if (ch.Status != ChannelStatuses.Connected)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{ch.Name} is {ch.Status.ToLowerInvariant()}: nothing is received until it is connected.");
            return await InsertAsync(tx, ctx, ch, m, MessageStatuses.Pending, Now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChannelMessageResult>> QueueSimulatedAsync(CommandContext ctx, QueueSimulatedMessageRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Manage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var m = r.Message ?? throw HccException.Validation("The message is required.");
            var ch = await store.GetChannelAsync(ctx.PropertyId, m.ChannelId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            return await InsertAsync(tx, ctx, ch, m, MessageStatuses.Queued, null, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<ChannelMessageResult> InsertAsync(ICommandTransaction tx, CommandContext ctx, ChannelRecord ch, ChannelMessageInput m, string status, DateTime? received,
        CancellationToken ct)
    {
        var kind = m.Kind switch { MessageKinds.New or MessageKinds.Modify or MessageKinds.Cancel => m.Kind, _ => throw HccException.Validation("Kind must be New, Modify or Cancel.") };
        var external = (m.ExternalId ?? "").Trim();
        var cref = (m.ChannelRef ?? "").Trim();
        if (external.Length is < 1 or > 40) throw HccException.Validation("The channel message id is required (at most 40 characters).");
        if (cref.Length is < 1 or > 30) throw HccException.Validation("The channel reservation number is required (at most 30 characters).");
        var guest = StayRules.GuestName(m.Guest);
        var code = (m.ChannelRoomCode ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 20) throw HccException.Validation("The channel room code is required (2-20 characters).");
        var a = StayDates.Parse(m.Arrival, "arrival");
        var d = StayDates.Parse(m.Departure, "departure");
        StayDates.Nights(a, d);
        if (m.Adults < 1 || m.Adults > 10 || m.Children < 0 || m.Children > 10) throw HccException.Validation("Check the number of guests.");
        if (m.AmountPaise < 0 || m.AmountPaise > 10_000_000_000) throw HccException.Validation("Check the amount.");
        string? phone;
        try
        {
            phone = StayRules.PhoneNumber(m.Phone);
        }
        catch (HccException)
        {
            phone = null; // an unusable channel phone format must not block the booking
        }

        var email = string.IsNullOrWhiteSpace(m.Email) ? null : m.Email.Trim()[..Math.Min(200, m.Email.Trim().Length)];
        var (rec, created) = await store.InsertMessageAsync(tx, new MessageDraft(ctx.PropertyId, ch.ChannelId, external, kind, cref, guest, phone, email, code, a, d, m.Adults,
            m.Children, m.AmountPaise, status, received), ct).ConfigureAwait(false);
        var version = InventoryErrors.VersionToWire(rec.RowVersion);
        if (!created)
        {
            await tx.AuditAsync("ImportChannelMessage", rec.ChannelRef, $"{ch.Name} {cref} · duplicate ignored (already message {rec.MessageId.ToString(CultureInfo.InvariantCulture)})", ct).ConfigureAwait(false);
            return new ChannelMessageResult(rec.MessageId, version, rec.Status, true, $"{ch.Name} {cref} already received · no new message");
        }

        var text = $"{kind} message {cref} {(status == MessageStatuses.Queued ? "scripted for" : "received from")} {ch.Name} (simulated)";
        await tx.AuditAsync(status == MessageStatuses.Queued ? "QueueChannelMessage" : "ImportChannelMessage", cref, text, ct).ConfigureAwait(false);
        if (status == MessageStatuses.Pending)
            await tx.PublishAsync(OtaEvents.MessageReceived, rec.MessageId, new { messageId = rec.MessageId, channelId = ch.ChannelId, kind, channelRef = cref }, ct).ConfigureAwait(false);
        return new ChannelMessageResult(rec.MessageId, version, rec.Status, false, text);
    }

    public async Task<CommandResult<PullResult>> PullAsync(CommandContext ctx, PullChannelMessagesRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var next = await store.NextQueuedAsync(tx, ctx.PropertyId, r.ChannelId, c).ConfigureAwait(false);
            if (next is null)
            {
                await tx.AuditAsync("PullChannelMessages", "inbox", "Checked connected channels · no new messages (simulated)", c).ConfigureAwait(false);
                return new PullResult(null, "Checked connected channels · no new messages (simulated)");
            }

            await store.ActivateAsync(tx, next.MessageId, Now, c).ConfigureAwait(false);
            var ch = await store.GetChannelAsync(ctx.PropertyId, next.ChannelId, tx, false, c).ConfigureAwait(false);
            var text = $"{next.Kind} message {next.ChannelRef} received from {ch?.Name} (simulated)";
            await tx.AuditAsync("PullChannelMessages", next.ChannelRef, text, c).ConfigureAwait(false);
            await tx.PublishAsync(OtaEvents.MessageReceived, next.MessageId, new { messageId = next.MessageId, channelId = next.ChannelId, kind = next.Kind, channelRef = next.ChannelRef }, c).ConfigureAwait(false);
            return new PullResult(next.MessageId, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChannelMessageResult>> RejectAsync(CommandContext ctx, RejectChannelMessageRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(r.ExpectedVersion);
        var reason = (r.Reason ?? "").Trim();
        if (reason.Length < 3) throw HccException.Validation("Enter a reason (at least 3 characters).");
        if (reason.Length > 200) reason = reason[..200];
        return await runner.RunAsync(ctx, r, async (tx, c) =>
        {
            var m = await store.GetMessageAsync(ctx.PropertyId, r.MessageId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
            if (m.Status != MessageStatuses.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This message is already {m.Status.ToLowerInvariant()}.");
            if (!InventoryErrors.VersionEquals(m.RowVersion, expected)) throw InventoryErrors.Stale("This message");
            await store.SetMessageStateAsync(tx, m.MessageId, MessageStatuses.Rejected, reason, ctx.PrincipalId, Now, null, null, c).ConfigureAwait(false);
            var ch = await store.GetChannelAsync(ctx.PropertyId, m.ChannelId, tx, false, c).ConfigureAwait(false);
            var text = $"{ch?.Name} {m.ChannelRef} ({m.Kind}) rejected · {reason} · channel notified (simulated)";
            await tx.AuditAsync("RejectChannelMessage", m.ChannelRef, text, c).ConfigureAwait(false);
            var fresh = await store.GetMessageAsync(ctx.PropertyId, m.MessageId, tx, false, c).ConfigureAwait(false);
            return new ChannelMessageResult(m.MessageId, InventoryErrors.VersionToWire(fresh!.RowVersion), MessageStatuses.Rejected, false, text);
        }, ct).ConfigureAwait(false);
    }

    // ───────────── accept: New -> reservation, Modify -> amend, Cancel -> cancel ─────────────
    public async Task<CommandResult<AcceptResult>> AcceptAsync(CommandContext ctx, AcceptChannelMessageRequest r, CancellationToken ct = default)
    {
        Check(ctx, r);
        await access.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(r.ExpectedVersion);
        var m = await store.GetMessageAsync(ctx.PropertyId, r.MessageId, null, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
        if (m.Status is MessageStatuses.Accepted or MessageStatuses.Applied or MessageStatuses.Rejected)
        {
            // same key + same request replays the stored result; anything else is refused
            return await runner.RunAsync<AcceptChannelMessageRequest, AcceptResult>(ctx, r, (_, _) => throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This message is already {m.Status.ToLowerInvariant()}."), ct).ConfigureAwait(false);
        }

        if (m.Status == MessageStatuses.Queued) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This message has not been delivered yet.");
        var resume = m.Status == Importing;
        if (resume && m.DecidedBy != ctx.PrincipalId)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "This message is being imported by another user.");
        if (!resume)
        {
            if (!InventoryErrors.VersionEquals(m.RowVersion, expected)) throw InventoryErrors.Stale("This message");
            var chk = await queries.CheckAsync(ctx.PropertyId, ctx.PrincipalId, m, r.RoomId, ct).ConfigureAwait(false);
            if (!chk.Ok)
            {
                var code = Contains(chk.Reason, "free", "overbooking", "not free") ? HccErrorCode.NOT_AVAILABLE
                    : Contains(chk.Reason, "not found") ? HccErrorCode.NOT_FOUND
                    : Contains(chk.Reason, "Duplicate", "handle at the front desk", "rejected", "Already", "is cancelled", "is inhouse", "is departed") ? HccErrorCode.INVALID_TRANSITION
                    : HccErrorCode.VALIDATION;
                throw new HccException(code, chk.Reason + " Reject the message with a reason instead.");
            }

            var claimKey = "ota-claim-" + r.MessageId.ToString(CultureInfo.InvariantCulture) + "-" + Convert.ToHexString(expected);
            var claim = await runner.RunAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "OtaClaim", claimKey), new { r.MessageId }, async (tx, c) =>
            {
                var row = await store.GetMessageAsync(ctx.PropertyId, r.MessageId, tx, true, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
                if (row.Status != MessageStatuses.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This message is already {row.Status.ToLowerInvariant()}.");
                if (!InventoryErrors.VersionEquals(row.RowVersion, expected)) throw InventoryErrors.Stale("This message");
                await store.SetMessageStateAsync(tx, row.MessageId, Importing, null, ctx.PrincipalId, Now, null, chk.RoomId, c).ConfigureAwait(false);
                await tx.AuditAsync("ClaimChannelMessage", row.ChannelRef, $"{row.Kind} {row.ChannelRef} claimed for import", c).ConfigureAwait(false);
                var after = await store.GetMessageAsync(ctx.PropertyId, row.MessageId, tx, false, c).ConfigureAwait(false);
                return new ClaimResult(Convert.ToHexString(after!.RowVersion));
            }, ct).ConfigureAwait(false);
            m = await store.GetMessageAsync(ctx.PropertyId, r.MessageId, null, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
            return await WorkAsync(ctx, r, m, claim.Value.Token, chk.Warning, ct).ConfigureAwait(false);
        }

        return await WorkAsync(ctx, r, m, Convert.ToHexString(m.RowVersion), null, ct).ConfigureAwait(false);
    }

    private sealed record ClaimResult(string Token);

    private static bool Contains(string text, params string[] needles) => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private async Task<CommandResult<AcceptResult>> WorkAsync(CommandContext ctx, AcceptChannelMessageRequest r, MessageRecord m, string token, string? warning, CancellationToken ct)
    {
        Plan plan;
        ChannelRecord ch;
        try
        {
            ch = await store.GetChannelAsync(ctx.PropertyId, m.ChannelId, null, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
            plan = m.Kind switch
            {
                MessageKinds.New => await ImportNewAsync(ctx, m, ch, token, warning, ct).ConfigureAwait(false),
                MessageKinds.Modify => await ApplyModifyAsync(ctx, m, ch, token, ct).ConfigureAwait(false),
                _ => await ApplyCancelAsync(ctx, m, ch, token, ct).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (ex is HccException or OperationCanceledException)
        {
            await ReleaseAsync(ctx, m, token).ConfigureAwait(false);
            throw;
        }

        try
        {
            return await runner.RunAsync(ctx, r, (tx, c) => FinalizeAsync(tx, ctx, m, ch, plan, c), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (m.Kind != MessageKinds.New && ex is HccException or OperationCanceledException)
        {
            // a New keeps its claim once the reservation exists (retry resumes with the same keys); Modify / Cancel are safe to repeat
            await ReleaseAsync(ctx, m, token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReleaseAsync(CommandContext ctx, MessageRecord m, string token)
    {
        try
        {
            await runner.RunAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "OtaRelease", "ota-rel-" + m.MessageId.ToString(CultureInfo.InvariantCulture) + "-" + token),
                new { m.MessageId, token }, async (tx, c) =>
                {
                    var row = await store.GetMessageAsync(ctx.PropertyId, m.MessageId, tx, true, c).ConfigureAwait(false);
                    if (row is { Status: Importing } && row.DecidedBy == ctx.PrincipalId)
                        await store.SetMessageStateAsync(tx, m.MessageId, MessageStatuses.Pending, null, null, null, null, null, c).ConfigureAwait(false);
                    await tx.AuditAsync("ReleaseChannelMessage", m.ChannelRef, $"{m.Kind} {m.ChannelRef} import released", c).ConfigureAwait(false);
                    return new ClaimResult(token);
                }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HccException)
        {
            // the claim stays; the same user can retry the accept and it resumes
        }
    }

    private sealed record Plan(string Final, long? ReservationId, string? PublicNo, int? RoomId, long? NewBase, string Summary, bool PendingOriginal, MessageRecord? Original,
        bool Cancelled);

    private async Task<Plan> ImportNewAsync(CommandContext ctx, MessageRecord m, ChannelRecord ch, string token, string? warning, CancellationToken ct)
    {
        var room = m.RoomId ?? throw new HccException(HccErrorCode.VALIDATION, "No room was chosen for this message.");
        var key = m.MessageId.ToString(CultureInfo.InvariantCulture) + "-" + token;
        var quote = (await inventory.QuoteStayAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "QuoteStay", "ota-q-" + key),
            new QuoteStayRequest(StayDates.ToWire(m.Arrival), StayDates.ToWire(m.Departure), m.Adults, m.Children, room), ct).ConfigureAwait(false)).Value;
        var source = ("OTA · " + ch.Name);
        if (source.Length > 40) source = source[..40];
        var created = (await reservations.CreateAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "CreateReservation", "ota-c-" + key),
            new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(m.Guest, m.Phone), room, false,
                quote.Violations.Count > 0 ? $"OTA reservation {m.ChannelRef} already sold on {ch.Name}" : null, null, source,
                $"{ch.Name} reservation {m.ChannelRef} · simulated channel import"), ct).ConfigureAwait(false)).Value;
        var d = await reservations.GetAsync(ctx.PropertyId, ctx.PrincipalId, created.ReservationId, ct).ConfigureAwait(false);
        var text = $"{ch.Name} {m.ChannelRef} → {d.PublicNo} · {m.Guest} · room {d.RoomNo} · {StayDates.ToWire(m.Arrival)} → {StayDates.ToWire(m.Departure)}"
            + (warning is null ? "" : " · " + warning);
        return new Plan(MessageStatuses.Accepted, created.ReservationId, d.PublicNo, d.RoomId, d.NetPaise, text, false, null, false);
    }

    private async Task<(MessageRecord Original, ReservationDetails? Details)> OriginalAsync(CommandContext ctx, MessageRecord m, CancellationToken ct)
    {
        var o = await store.FindNewAsync(ctx.PropertyId, m.ChannelId, m.ChannelRef, null, false, ct).ConfigureAwait(false);
        if (o is null || o.MessageId == m.MessageId) throw new HccException(HccErrorCode.NOT_FOUND, $"Original reservation {m.ChannelRef} not found.");
        if (o.Status == MessageStatuses.Rejected) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Original reservation {m.ChannelRef} was rejected.");
        if (o.Status == MessageStatuses.Pending) return (o, null);
        if (o.Status != MessageStatuses.Accepted || o.ReservationId is not { } id)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Original reservation {m.ChannelRef} is {o.Status.ToLowerInvariant()}.");
        return (o, await reservations.GetAsync(ctx.PropertyId, ctx.PrincipalId, id, ct).ConfigureAwait(false));
    }

    private async Task<Plan> ApplyCancelAsync(CommandContext ctx, MessageRecord m, ChannelRecord ch, string token, CancellationToken ct)
    {
        var (o, d) = await OriginalAsync(ctx, m, ct).ConfigureAwait(false);
        if (d is null) return new Plan(MessageStatuses.Applied, null, null, null, null, $"{ch.Name} {m.ChannelRef} · pending request withdrawn", true, o, true);
        if (d.Status != ReservationStatuses.Cancelled)
        {
            if (d.Status != ReservationStatuses.Confirmed) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{d.PublicNo} is {d.Status} — handle at the front desk.");
            await reservations.CancelAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "CancelReservation", "ota-x-" + m.MessageId.ToString(CultureInfo.InvariantCulture) + "-" + token),
                new CancelReservationRequest(d.ReservationId, d.Version, $"{ch.Name} cancellation {m.ChannelRef}"), ct).ConfigureAwait(false);
        }

        return new Plan(MessageStatuses.Applied, d.ReservationId, d.PublicNo, d.RoomId, 0, $"{ch.Name} {m.ChannelRef} · {d.PublicNo} cancelled", false, o, true);
    }

    private async Task<Plan> ApplyModifyAsync(CommandContext ctx, MessageRecord m, ChannelRecord ch, string token, CancellationToken ct)
    {
        var (o, d) = await OriginalAsync(ctx, m, ct).ConfigureAwait(false);
        if (d is null)
            return new Plan(MessageStatuses.Applied, null, null, null, null, $"{ch.Name} {m.ChannelRef} · pending request updated to {StayDates.ToWire(m.Arrival)} → {StayDates.ToWire(m.Departure)}", true, o, false);
        if (d.Status != ReservationStatuses.Confirmed) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{d.PublicNo} is {d.Status} — handle at the front desk.");
        if (m.Adults != d.Adults || m.Children != d.Children)
            throw new HccException(HccErrorCode.VALIDATION, "The party size changed: the front desk must amend the reservation. Reject the message with a reason instead.");
        var changedDates = StayDates.ToWire(m.Arrival) != d.Arrival || StayDates.ToWire(m.Departure) != d.Departure;
        var finalRoom = d.RoomId;
        var details = d;
        if (changedDates)
        {
            var tried = new List<int>();
            var candidates = new List<int> { d.RoomId };
            var amended = false;
            for (var i = 0; i < candidates.Count && !amended; i++)
            {
                var room = candidates[i];
                try
                {
                    await reservations.AmendAsync(new CommandContext(ctx.PropertyId, ctx.PrincipalId, "AmendReservation",
                        "ota-a-" + m.MessageId.ToString(CultureInfo.InvariantCulture) + "-" + token + "-" + room.ToString(CultureInfo.InvariantCulture)),
                        new AmendReservationRequest(d.ReservationId, d.Version, StayDates.ToWire(m.Arrival), StayDates.ToWire(m.Departure), room, PriceDecisions.Keep), ct).ConfigureAwait(false);
                    finalRoom = room;
                    amended = true;
                }
                catch (HccException ex) when (ex.Code == HccErrorCode.NOT_AVAILABLE)
                {
                    tried.Add(room);
                    if (i == 0)
                    {
                        var av = await inventory.GetAvailabilityAsync(ctx.PropertyId, ctx.PrincipalId, new AvailabilityQuery(StayDates.ToWire(m.Arrival), StayDates.ToWire(m.Departure),
                            m.Adults, m.Children, d.RoomTypeId), ct).ConfigureAwait(false);
                        candidates.AddRange(av.Rooms.Where(x => x.Available && x.RoomId != d.RoomId).Select(x => x.RoomId));
                    }
                }
            }

            if (!amended) throw new HccException(HccErrorCode.NOT_AVAILABLE, $"No {d.RoomType} room free for the new dates. Reject the message with a reason instead.");
            details = await reservations.GetAsync(ctx.PropertyId, ctx.PrincipalId, d.ReservationId, ct).ConfigureAwait(false);
        }

        var text = $"{ch.Name} {m.ChannelRef} · {d.PublicNo} modified · room {details.RoomNo} · {details.Arrival} → {details.Departure}";
        return new Plan(MessageStatuses.Applied, d.ReservationId, d.PublicNo, finalRoom, details.NetPaise, text, false, o, false);
    }

    private async Task<AcceptResult> FinalizeAsync(ICommandTransaction tx, CommandContext ctx, MessageRecord m, ChannelRecord ch, Plan plan, CancellationToken ct)
    {
        if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, ct).ConfigureAwait(false); // P6: DAY_CLOSED
        var row = await store.GetMessageAsync(ctx.PropertyId, m.MessageId, tx, true, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
        if (row.Status != Importing || row.DecidedBy != ctx.PrincipalId)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This message is already {row.Status.ToLowerInvariant()}.");
        var now = Now;
        long commission = 0;
        if (m.Kind == MessageKinds.New)
        {
            var resId = plan.ReservationId!.Value;
            commission = OtaRules.Commission(plan.NewBase!.Value, ch.CommissionBps);
            await store.AddLedgerAsync(tx, new LedgerEntry(ctx.PropertyId, ch.ChannelId, m.MessageId, resId, "Accrual", plan.NewBase.Value, ch.CommissionBps, commission,
                Clip($"{ch.Name} {m.ChannelRef} · {plan.PublicNo}"), now, ctx.PrincipalId), ct).ConfigureAwait(false);
            await store.SetMessageStateAsync(tx, m.MessageId, MessageStatuses.Accepted, null, ctx.PrincipalId, now, resId, plan.RoomId, ct).ConfigureAwait(false);
            await tx.AuditAsync("AcceptChannelMessage", m.ChannelRef, Clip(plan.Summary), ct).ConfigureAwait(false);
            await tx.PublishAsync(OtaEvents.BookingImported, resId, new { messageId = m.MessageId, channelId = ch.ChannelId, channelRef = m.ChannelRef, reservationId = resId, commissionPaise = commission }, ct).ConfigureAwait(false);
        }
        else
        {
            var o = plan.Original!;
            if (plan.PendingOriginal)
            {
                if (m.Kind == MessageKinds.Cancel) await store.SetMessageStateAsync(tx, o.MessageId, MessageStatuses.Rejected, "Cancelled by channel before acceptance", ctx.PrincipalId, now, null, null, ct).ConfigureAwait(false);
                else await store.UpdateStayAsync(tx, o.MessageId, m.Arrival, m.Departure, m.Adults, m.Children, m.AmountPaise > 0 ? m.AmountPaise : null, null, ct).ConfigureAwait(false);
            }
            else
            {
                var t = await store.LedgerTotalsAsync(tx, o.MessageId, ct).ConfigureAwait(false);
                var bps = t.CommissionBps ?? ch.CommissionBps;
                var resId = plan.ReservationId!.Value;
                var target = plan.NewBase!.Value;
                var targetCommission = OtaRules.Commission(target, bps);
                if (target != t.BasePaise || targetCommission != t.CommissionPaise)
                {
                    var kind = m.Kind == MessageKinds.Cancel ? "Reversal" : "Adjustment";
                    await store.AddLedgerAsync(tx, new LedgerEntry(ctx.PropertyId, ch.ChannelId, o.MessageId, resId, kind, target - t.BasePaise, bps, targetCommission - t.CommissionPaise,
                        Clip($"{ch.Name} {m.ChannelRef} · {plan.PublicNo} · {kind.ToLowerInvariant()}"), now, ctx.PrincipalId), ct).ConfigureAwait(false);
                }

                commission = targetCommission;
                if (m.Kind == MessageKinds.Modify)
                {
                    await store.UpdateStayAsync(tx, o.MessageId, m.Arrival, m.Departure, m.Adults, m.Children, m.AmountPaise > 0 ? m.AmountPaise : null, plan.RoomId, ct).ConfigureAwait(false);
                }
                else if (m.AmountPaise > 0) await store.UpdateStayAsync(tx, o.MessageId, o.Arrival, o.Departure, o.Adults, o.Children, m.AmountPaise, null, ct).ConfigureAwait(false);

                await tx.PublishAsync(m.Kind == MessageKinds.Cancel ? OtaEvents.BookingCancelled : OtaEvents.BookingModified, resId,
                    new { messageId = m.MessageId, channelId = ch.ChannelId, channelRef = m.ChannelRef, reservationId = resId, commissionPaise = targetCommission }, ct).ConfigureAwait(false);
            }

            await store.SetMessageStateAsync(tx, m.MessageId, MessageStatuses.Applied, null, ctx.PrincipalId, now, plan.ReservationId, plan.RoomId, ct).ConfigureAwait(false);
            await tx.AuditAsync(m.Kind == MessageKinds.Cancel ? "ApplyChannelCancel" : "ApplyChannelModify", m.ChannelRef, Clip(plan.Summary), ct).ConfigureAwait(false);
        }

        var fresh = await store.GetMessageAsync(ctx.PropertyId, m.MessageId, tx, false, ct).ConfigureAwait(false);
        return new AcceptResult(m.MessageId, InventoryErrors.VersionToWire(fresh!.RowVersion), plan.Final, plan.ReservationId, plan.PublicNo, plan.RoomId, plan.Summary, commission);
    }

    private static string Clip(string s) => s.Length > 1000 ? s[..1000] : s;

    private static void Check(CommandContext ctx, object request)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(request);
    }
}
