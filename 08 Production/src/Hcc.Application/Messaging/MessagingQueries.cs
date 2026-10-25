using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Messaging;
using Hcc.Domain.Errors;
using Hcc.Domain.Messaging;

namespace Hcc.Application.Messaging;

/// <summary>Reads for the messaging screens (outbox, KPI strip, templates, rules, consent). Recipients are masked; there is no ID data to hide.</summary>
public sealed class MessagingQueries(IAccessPolicy policy, IMessagingDataFactory data, TimeProvider clock)
{
    public const int MaxPageSize = 200;
    private const int Window = 2000;

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    internal static ConsentView ToView(long guestId, StoredConsent? c) => c is null
        ? new ConsentView(guestId, true, true, true, false, null, null)
        : new ConsentView(guestId, c.Consent.WhatsApp, c.Consent.Sms, c.Consent.Email, c.Consent.OptOut, Iso(c.UpdatedUtc), VersionToken.ToWire(c.Version));

    internal static TemplateView ToView(StoredTemplate t) => new(t.Def.Code, t.Def.Name, t.Def.Channel, t.Def.Subject, t.Def.Body, VersionToken.ToWire(t.Version));

    internal static RuleView ToView(StoredRule r) => new(r.Def.RuleId, r.Def.Event, r.Def.TemplateCode, r.Def.Channel, r.Def.Enabled, VersionToken.ToWire(r.Version));

    private static MessageView ToView(StoredMessage m, DateTime now)
    {
        var timeline = MessagingRules.Timeline(m.QueuedUtc, now, m.BoostMinutes, m.Outcome, m.Blocked);
        return new MessageView(m.MessageId, MessagingRules.WireId(m.MessageId), m.ReservationId, m.BookingNo, m.GuestName, m.TemplateCode, m.Channel, m.RecipientMasked,
            m.Subject, m.Body, timeline[^1].Status, m.BlockedReason, Iso(m.QueuedUtc), timeline.Select(t => new MessageTimelineStep(t.Status, Iso(t.AtUtc))).ToList(),
            m.RuleId, m.RetryOf, m.RetriedBy, m.Channel == MsgChannel.Sms ? MessagingRules.SmsSegments(m.Body) : null, true);
    }

    public async Task<MessageListResult> MessagesAsync(int propertyId, int principalId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var all = await data.ReadAsync((d, c) => d.MessagesAsync(propertyId, null, null, Window, c), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var views = all.Select(m => ToView(m, now));
        if (!string.IsNullOrWhiteSpace(status)) views = views.Where(v => string.Equals(v.Status, status, StringComparison.OrdinalIgnoreCase));
        var list = views.ToList();
        return new MessageListResult(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize);
    }

    public async Task<MessageView> GetMessageAsync(int propertyId, int principalId, long messageId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        var m = (await data.ReadAsync((d, c) => d.MessagesAsync(propertyId, messageId, null, 1, c), ct).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Message not found.");
        return ToView(m, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<MessagingSummary> SummaryAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var (all, pending) = await data.ReadAsync(async (d, c) =>
        {
            await d.EnsureDefaultsAsync(propertyId, now, c).ConfigureAwait(false);
            var msgs = await d.MessagesAsync(propertyId, null, null, Window, c).ConfigureAwait(false);
            var bd = await d.BusinessDateAsync(propertyId, c).ConfigureAwait(false);
            var cands = await d.SweepCandidatesAsync(propertyId, bd, c).ConfigureAwait(false);
            return (msgs, cands.Count);
        }, ct).ConfigureAwait(false);
        var counts = all.GroupBy(m => MessagingRules.StatusAt(m.QueuedUtc, now, m.BoostMinutes, m.Outcome, m.Blocked)).ToDictionary(g => g.Key, g => g.Count());
        int N(string s) => counts.GetValueOrDefault(s);
        return new MessagingSummary(N(MsgStatus.Queued), N(MsgStatus.Sent), N(MsgStatus.Delivered), N(MsgStatus.Read), N(MsgStatus.Failed), N(MsgStatus.NotSent), pending, true);
    }

    public async Task<TemplatesResult> TemplatesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        var list = await data.ReadAsync(async (d, c) =>
        {
            await d.EnsureDefaultsAsync(propertyId, clock.GetUtcNow().UtcDateTime, c).ConfigureAwait(false);
            return await d.TemplatesAsync(propertyId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return new TemplatesResult(list.Select(ToView).ToList(), MessagingRules.Placeholders);
    }

    public async Task<IReadOnlyList<RuleView>> RulesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        var list = await data.ReadAsync(async (d, c) =>
        {
            await d.EnsureDefaultsAsync(propertyId, clock.GetUtcNow().UtcDateTime, c).ConfigureAwait(false);
            return await d.RulesAsync(propertyId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return list.Select(ToView).ToList();
    }

    public async Task<ConsentView> ConsentAsync(int propertyId, int principalId, long guestId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MessagingPermissions.View, null, null, ct).ConfigureAwait(false);
        var c = await data.ReadAsync(async (d, cc) =>
        {
            if (!await d.GuestExistsAsync(propertyId, guestId, cc).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Guest not found.");
            return await d.GetStoredConsentAsync(propertyId, guestId, cc).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return ToView(guestId, c);
    }
}
