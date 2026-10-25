using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Messaging;
using Hcc.Domain.Errors;
using Hcc.Domain.Messaging;

namespace Hcc.Application.Messaging;

/// <summary>
/// Messaging commands (prototype MSG_SEND / MSG_RUN_RULES / MSG_SIMULATE / MSG_RETRY / MSG_CONSENT / MSG_TEMPLATE / MSG_RULE).
/// ALL SENDING IS SIMULATED. Audit rows name the message id, channel and booking only: never the body, the contact or an ID number (DEC-068).
/// </summary>
public sealed class MessagingCommands(ICommandRunner runner, IAccessPolicy policy, IMessagingDataFactory data, TriggerEngine triggers, IMessageGateway gateway, MessagingOptions options, TimeProvider clock)
{
    public const string OpSend = "SendMessage";
    public const string OpRunTriggers = "RunMessagingTriggers";
    public const string OpSimulate = "SimulateMessageDelivery";
    public const string OpRetry = "RetryMessage";
    public const string OpConsent = "SetMessagingConsent";
    public const string OpTemplate = "UpdateMessageTemplate";
    public const string OpRule = "UpdateMessageRule";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

    public async Task<CommandResult<SendMessageResult>> SendAsync(CommandContext ctx, SendMessageRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.Send, null, null, ct).ConfigureAwait(false);
        if (!MsgChannel.IsValid(req.Channel)) throw HccException.Validation("Choose WhatsApp, SMS or Email.");
        var body = (req.Body ?? string.Empty).Trim();
        if (body.Length < 1 || body.Length > MessagingRules.ManualBodyMax) throw HccException.Validation($"Write a message of 1–{MessagingRules.ManualBodyMax} characters.");
        var unknown = MessagingRules.UnknownPlaceholders(body);
        if (unknown.Count > 0) throw HccException.Validation("Unknown placeholder: {" + string.Join("}, {", unknown) + "}.");
        var subjectRaw = string.IsNullOrWhiteSpace(req.Subject) ? null : req.Subject.Trim();
        if (subjectRaw is not null)
        {
            if (subjectRaw.Length > MessagingRules.SubjectMax) throw HccException.Validation($"Subject is at most {MessagingRules.SubjectMax} characters.");
            var u = MessagingRules.UnknownPlaceholders(subjectRaw);
            if (u.Count > 0) throw HccException.Validation("Unknown placeholder: {" + string.Join("}, {", u) + "}.");
        }

        if (MessagingRules.LooksLikeAadhaar(body) || MessagingRules.LooksLikeAadhaar(subjectRaw)) throw HccException.Validation("A message may not contain an ID number.");

        return await runner.RunAsync(ctx with { Operation = OpSend }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            var b = await d.LoadBookingAsync(ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Choose an in-house guest.");
            if (b.Status != "InHouse") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Manual messages go only to in-house guests.");
            var consent = await d.GetConsentAsync(ctx.PropertyId, b.GuestId, c).ConfigureAwait(false);
            var why = MessagingRules.BlockReason(consent, b.Contact, req.Channel);
            if (why.Length > 0) throw HccException.Validation(why + " — the message was not queued.");

            await d.EnsureDefaultsAsync(ctx.PropertyId, UtcNow, c).ConfigureAwait(false);
            var figures = await d.FolioFiguresAsync(b.ReservationId, null, c).ConfigureAwait(false);
            var values = MessagingRules.Values(new MsgBookingFacts(b.Hotel, b.PublicNo, b.GuestName, b.RoomNo, b.Arrival, b.Departure, options.CheckInTime, options.CheckOutTime, figures.TotalPaise, figures.BalancePaise, null));
            var template = string.IsNullOrWhiteSpace(req.TemplateCode) ? null : await d.GetTemplateAsync(ctx.PropertyId, req.TemplateCode.Trim(), c).ConfigureAwait(false);
            var recipient = MessagingRules.Recipient(b.Contact, req.Channel);
            var id = await d.NextMessageIdAsync(c).ConfigureAwait(false);
            var outcome = gateway.Submit(new OutboundMessage(id, req.Channel, recipient.Valid));
            var rendered = MessagingRules.Render(body, values).Text;
            var subject = req.Channel == MsgChannel.Email && subjectRaw is not null ? MessagingRules.Render(subjectRaw, values).Text : null;
            await d.InsertMessageAsync(new NewMessage(id, ctx.PropertyId, b.ReservationId, b.GuestId, template?.Def.Code, req.Channel, recipient.Masked, subject, rendered,
                UtcNow, outcome, null, null, null, null, ctx.PrincipalId, UtcNow), c).ConfigureAwait(false);
            await tx.AuditAsync("MSG_SEND", "msg-" + Num(id), $"{req.Channel} · booking {b.PublicNo} · simulated, nothing sent", c).ConfigureAwait(false);
            return new SendMessageResult(id, MessagingRules.WireId(id), MsgStatus.Queued, recipient.Masked, $"{MessagingRules.WireId(id)} · {req.Channel} to {b.GuestName} ({recipient.Masked}) · simulated, nothing sent");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Queues the state-driven triggers (R2 arrival today/tomorrow, R3 in house); event-driven ones are queued by the outbox consumer.</summary>
    public async Task<CommandResult<RunMessagingTriggersResult>> RunTriggersAsync(CommandContext ctx, RunMessagingTriggersRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.Send, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRunTriggers }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            await d.EnsureDefaultsAsync(ctx.PropertyId, UtcNow, c).ConfigureAwait(false);
            var bd = await d.BusinessDateAsync(ctx.PropertyId, c).ConfigureAwait(false);
            int queued = 0, notSent = 0;
            foreach (var cand in await d.SweepCandidatesAsync(ctx.PropertyId, bd, c).ConfigureAwait(false))
            {
                var (q, n) = await triggers.OnEventAsync(d, ctx.PropertyId, cand.Event, cand.ReservationId, null, UtcNow, c).ConfigureAwait(false);
                queued += q;
                notSent += n;
            }

            if (queued + notSent == 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "There are no pending triggers to queue.");
            var summary = $"{queued} queued · {notSent} not sent (consent/recipient) · simulated";
            await tx.AuditAsync("MSG_RUN_RULES", "msg-rules", summary, c).ConfigureAwait(false);
            return new RunMessagingTriggersResult(queued, notSent, summary);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<SimulateMessageDeliveryResult>> SimulateAsync(CommandContext ctx, SimulateMessageDeliveryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.Send, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpSimulate }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            var list = await d.MessagesAsync(ctx.PropertyId, req.MessageId, null, 5000, c).ConfigureAwait(false);
            if (list.Count == 0) throw new HccException(HccErrorCode.NOT_FOUND, "Message not found.");
            var now = UtcNow;
            var advanced = 0;
            foreach (var m in list)
            {
                var add = MessagingRules.NextBoost(m.QueuedUtc, now, m.BoostMinutes, m.Outcome, m.Blocked);
                if (add <= 0) continue;
                await d.AddBoostAsync(m.MessageId, add, c).ConfigureAwait(false);
                advanced++;
            }

            if (advanced == 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Delivery simulation is already complete for " + (req.MessageId is null ? "all messages." : "this message."));
            var summary = $"{advanced} message{(advanced == 1 ? string.Empty : "s")} advanced one simulated delivery step";
            await tx.AuditAsync("MSG_SIMULATE", req.MessageId is { } id ? "msg-" + Num(id) : "msg-all", summary, c).ConfigureAwait(false);
            return new SimulateMessageDeliveryResult(advanced, summary);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RetryMessageResult>> RetryAsync(CommandContext ctx, RetryMessageRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.Send, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRetry }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            var m = await d.LockMessageAsync(ctx.PropertyId, req.MessageId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Message not found.");
            if (MessagingRules.StatusAt(m.QueuedUtc, UtcNow, m.BoostMinutes, m.Outcome, m.Blocked) != MsgStatus.Failed)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a failed message can be retried.");
            if (m.RetriedBy is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This message was already retried.");
            var b = await d.LoadBookingAsync(ctx.PropertyId, m.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
            var consent = await d.GetConsentAsync(ctx.PropertyId, b.GuestId, c).ConfigureAwait(false);
            var why = MessagingRules.BlockReason(consent, b.Contact, m.Channel);
            if (why.Length > 0) throw HccException.Validation(why + " — not retried.");
            var recipient = MessagingRules.Recipient(b.Contact, m.Channel);
            var id = await d.NextMessageIdAsync(c).ConfigureAwait(false);
            await d.InsertMessageAsync(new NewMessage(id, ctx.PropertyId, m.ReservationId, b.GuestId, m.TemplateCode, m.Channel, recipient.Masked, m.Subject, m.Body,
                UtcNow, MessagingRules.RetryOutcome(m.Channel), null, m.RuleId, null, m.MessageId, ctx.PrincipalId, UtcNow), c).ConfigureAwait(false);
            await d.SetRetriedByAsync(m.MessageId, id, c).ConfigureAwait(false);
            var summary = $"{MessagingRules.WireId(m.MessageId)} retried as {MessagingRules.WireId(id)} · simulated";
            await tx.AuditAsync("MSG_RETRY", "msg-" + Num(m.MessageId), summary, c).ConfigureAwait(false);
            return new RetryMessageResult(m.MessageId, id, summary);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Consent / opt-out per guest profile; an opt-out or a withdrawn channel stops messages that are still Queued.</summary>
    public async Task<CommandResult<SetMessagingConsentResult>> SetConsentAsync(CommandContext ctx, SetMessagingConsentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.Send, null, ct).ConfigureAwait(false))
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.ConsentFallback, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpConsent }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            if (!await d.GuestExistsAsync(ctx.PropertyId, req.GuestId, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Guest not found.");
            var prev = (await d.GetStoredConsentAsync(ctx.PropertyId, req.GuestId, c).ConfigureAwait(false))?.Consent ?? MsgConsent.Default;
            var next = new MsgConsent(req.WhatsApp ?? prev.WhatsApp, req.Sms ?? prev.Sms, req.Email ?? prev.Email, req.OptOut ?? prev.OptOut);
            await d.UpsertConsentAsync(ctx.PropertyId, req.GuestId, next, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var stopped = 0;
            foreach (var m in await d.MessagesAsync(ctx.PropertyId, null, req.GuestId, 1000, c).ConfigureAwait(false))
            {
                if (m.Blocked || MessagingRules.StatusAt(m.QueuedUtc, UtcNow, m.BoostMinutes, m.Outcome, false) != MsgStatus.Queued) continue;
                if (!next.OptOut && next.Allows(m.Channel)) continue;
                await d.BlockMessageAsync(m.MessageId, next.OptOut ? "Guest opted out before sending" : $"{m.Channel} consent withdrawn before sending", c).ConfigureAwait(false);
                stopped++;
            }

            var summary = $"guest {Num(req.GuestId)} · " + (next.OptOut ? "opted out" : $"WhatsApp {YesNo(next.WhatsApp)} · SMS {YesNo(next.Sms)} · Email {YesNo(next.Email)}") + (stopped > 0 ? $" · {stopped} queued stopped" : string.Empty);
            await tx.AuditAsync("MSG_CONSENT", "guest-" + Num(req.GuestId), summary, c).ConfigureAwait(false);
            var stored = await d.GetStoredConsentAsync(ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
            return new SetMessagingConsentResult(MessagingQueries.ToView(req.GuestId, stored), stopped, summary);
        }, ct).ConfigureAwait(false);
    }

    private static string YesNo(bool b) => b ? "yes" : "no";

    public async Task<CommandResult<UpdateMessageTemplateResult>> UpdateTemplateAsync(CommandContext ctx, UpdateMessageTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.EditTemplates, null, null, ct).ConfigureAwait(false);
        var body = (req.Body ?? string.Empty).Trim();
        if (req.Channel is not null && !MsgChannel.IsValid(req.Channel)) throw HccException.Validation("Choose WhatsApp, SMS or Email.");
        return await runner.RunAsync(ctx with { Operation = OpTemplate }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            await d.EnsureDefaultsAsync(ctx.PropertyId, UtcNow, c).ConfigureAwait(false);
            var t = await d.LockTemplateAsync(ctx.PropertyId, req.Code, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
            var subject = (req.Subject ?? t.Def.Subject).Trim();
            MessagingRules.ValidateTemplateText(subject, body);
            VersionToken.EnsureMatches(t.Version, expected, "This template");
            await d.UpdateTemplateAsync(ctx.PropertyId, req.Code, expected, subject, body, req.Channel ?? t.Def.Channel, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("MSG_TEMPLATE", "msg-template-" + req.Code, $"{t.Def.Name} template updated", c).ConfigureAwait(false);
            var after = await d.LockTemplateAsync(ctx.PropertyId, req.Code, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Template not found.");
            return new UpdateMessageTemplateResult(MessagingQueries.ToView(after), $"{t.Def.Name} template updated");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UpdateMessageRuleResult>> UpdateRuleAsync(CommandContext ctx, UpdateMessageRuleRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MessagingPermissions.EditTemplates, null, null, ct).ConfigureAwait(false);
        if (req.Channel is not null && !MsgChannel.IsValid(req.Channel)) throw HccException.Validation("Choose WhatsApp, SMS or Email.");
        return await runner.RunAsync(ctx with { Operation = OpRule }, req, async (tx, c) =>
        {
            var d = data.For(tx);
            await d.EnsureDefaultsAsync(ctx.PropertyId, UtcNow, c).ConfigureAwait(false);
            var r = await d.LockRuleAsync(ctx.PropertyId, req.RuleId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Rule not found.");
            VersionToken.EnsureMatches(r.Version, expected, "This rule");
            await d.UpdateRuleAsync(ctx.PropertyId, req.RuleId, expected, req.Enabled ?? r.Def.Enabled, req.Channel ?? r.Def.Channel, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var summary = $"{r.Def.RuleId} {(req.Enabled ?? r.Def.Enabled ? "enabled" : "disabled")} · {req.Channel ?? r.Def.Channel}";
            await tx.AuditAsync("MSG_RULE", "msg-rule-" + req.RuleId, summary, c).ConfigureAwait(false);
            var after = await d.LockRuleAsync(ctx.PropertyId, req.RuleId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Rule not found.");
            return new UpdateMessageRuleResult(MessagingQueries.ToView(after), summary);
        }, ct).ConfigureAwait(false);
    }
}
