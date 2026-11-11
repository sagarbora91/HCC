using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Domain.Messaging;

namespace Hcc.Application.Customers;

/// <summary>
/// Customers / CRM use cases: ProposeCustomerMerge / DecideCustomerMerge (maker-checker; history is kept, the duplicate becomes an alias of the survivor),
/// AddCustomerTag / RemoveCustomerTag, AddCustomerNote, SetCustomerPreferences, SetCustomerConsent (marketing consent is Messaging's per-guest consent, written
/// through its data port). Customers are derived on read; no row of another module is edited.
/// </summary>
public sealed class CustomerCommands(ICommandRunner runner, IAccessPolicy policy, ICustomerStore store, ICustomerReadStore reads, IMessagingDataFactory messaging, TimeProvider clock)
{
    public const string OpPropose = "ProposeCustomerMerge";
    public const string OpDecide = "DecideCustomerMerge";
    public const string OpAddTag = "AddCustomerTag";
    public const string OpRemoveTag = "RemoveCustomerTag";
    public const string OpAddNote = "AddCustomerNote";
    public const string OpPreferences = "SetCustomerPreferences";
    public const string OpConsent = "SetCustomerConsent";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>AF2 (phone digits): the audit target is the opaque ck_ token, never the raw key (a mobile number for walk-in customers).</summary>
    private static string Target(CustomerKeySpace keys, string rawKey) => "crm:" + keys.TokenOf(rawKey);

    /// <summary>A customer name goes into audit text; a name that is itself a phone number (or holds one) is masked so no raw number reaches the audit trail.
    /// af2-fix: a name whose digits total 8 or more (spaces, hyphens, plus signs and parentheses ignored) has every digit run masked, so
    /// "98765 43210" or "+91-98765-43210" no longer slips through; names with fewer digits (and Devanagari / ordinary names) are unchanged.</summary>
    internal static string SafeName(string? name)
    {
        var s = name ?? string.Empty;
        return s.Count(char.IsDigit) >= 8 ? Hcc.Domain.Audit.PhoneDigitsScrub.DigitRun.Replace(s, "***") : s;
    }

    /// <summary>
    /// Resolves a (possibly merged-away) key to the live customer or NOT_FOUND. AF-CRM (PR-01): the key arrives as a ck_ token or a raw n: key; a raw
    /// mobile key is accepted only from a reader who may see contact details.
    /// </summary>
    private static CustomerHead Find(CrmHeadSource src, CustomerHeadIndex index, CustomerKeySpace keys, bool contact, string? key)
    {
        var k = CustomerRules.Resolve(keys.ResolveInbound(key, contact), src.Aliases);
        return index.Heads.FirstOrDefault(c => c.Key == k) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
    }

    /// <summary>CU-PER: the key pass (narrow rows) resolves the key; a command then reads the full customer only when it needs stays or guest ids.</summary>
    private async Task<(CrmHeadSource Src, CustomerHeadIndex Index, CustomerKeySpace Keys)> KeyPassAsync(int propertyId, CancellationToken ct)
    {
        var src = await reads.HeadsAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Heads();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, index.Heads.Select(h => h.Key).Concat(src.Aliases.Keys), ct).ConfigureAwait(false);
        return (src, index, keys);
    }

    public async Task<CommandResult<MergeView>> ProposeMergeAsync(CommandContext ctx, ProposeCustomerMergeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CustomerPermissions.MergePropose, null, null, ct).ConfigureAwait(false);
        var reason = CustomerRules.CleanReason(req.Reason);
        var (src, index, keys) = await KeyPassAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        var contact = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, CustomerPermissions.Edit, null, ct).ConfigureAwait(false);
        var sourceHead = Find(src, index, keys, contact, req.SourceKey);
        var targetHead = Find(src, index, keys, contact, req.TargetKey);
        if (sourceHead.Key == targetHead.Key) throw HccException.Validation("Choose two different customers.");
        var (both, _) = await src.CustomersAsync(reads, ctx.PropertyId, index, [sourceHead, targetHead], ct).ConfigureAwait(false);
        // CU-PER fix: a guest changed between the two reads (the key moved): same NOT_FOUND as an unknown key, never a 500.
        if (!both.TryGetValue(sourceHead.Key, out var source) || !both.TryGetValue(targetHead.Key, out var target)) throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
        return await runner.RunAsync(ctx with { Operation = OpPropose }, req, async (tx, c) =>
        {
            var id = await store.InsertMergeAsync(tx, ctx.PropertyId, source.Key, target.Key, source.Name, target.Name, reason, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This customer already has a pending merge, or one of the two was merged already.");
            await tx.AuditAsync("CRM_MERGE_PROPOSE", Target(keys, source.Key), $"merge {SafeName(source.Name)} ({source.Stays.Count} stays) into {SafeName(target.Name)} ({target.Stays.Count} stays) · {reason}", c).ConfigureAwait(false);
            var m = (await store.LockMergeAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false))!;
            return CustomerViews.Merge(m, keys);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MergeView>> DecideMergeAsync(CommandContext ctx, DecideCustomerMergeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CustomerPermissions.MergeDecide, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        // CU-PER: the decision only turns stored keys into wire tokens (outbound), so it needs no customer read at all.
        var keys = await CustomerKeySpace.ForAsync(reads, ctx.PropertyId, [], ct).ConfigureAwait(false);
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > CustomerRules.ReasonMax }) throw HccException.Validation($"The note is limited to {CustomerRules.ReasonMax} characters.");
        var result = await runner.RunAsync(ctx with { Operation = OpDecide }, req, async (tx, c) =>
        {
            var m = await store.LockMergeAsync(tx, ctx.PropertyId, req.MergeId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Merge proposal not found.");
            VersionToken.EnsureMatches(m.RowVer, expected, "This merge proposal");
            if (m.Status != "Pending") throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This merge was already {m.Status.ToLowerInvariant()}.");
            CustomerRules.EnsureChecker(m.ProposedBy, ctx.PrincipalId);
            string Name(string k) => SafeName(k == m.SourceKey ? m.SourceName : m.TargetName);
            if (req.Approve)
            {
                if (!await store.ApproveMergeAsync(tx, m, ctx.PrincipalId, UtcNow, note, c).ConfigureAwait(false))
                    throw new HccException(HccErrorCode.INVALID_TRANSITION, "One of the two customers was merged in the meantime. Reject this proposal and propose again.");
                await tx.AuditAsync("CRM_MERGE_APPROVE", Target(keys, m.SourceKey), $"{Name(m.SourceKey)} merged into {Name(m.TargetKey)} · history kept{(note is null ? string.Empty : " · " + note)}", c).ConfigureAwait(false);
                await tx.PublishAsync("CustomerMerged", m.MergeId, new { m.MergeId, SourceKey = keys.TokenOf(m.SourceKey), TargetKey = keys.TokenOf(m.TargetKey) }, c).ConfigureAwait(false);
            }
            else
            {
                await store.RejectMergeAsync(tx, m.MergeId, ctx.PrincipalId, UtcNow, note, c).ConfigureAwait(false);
                await tx.AuditAsync("CRM_MERGE_REJECT", Target(keys, m.SourceKey), $"merge of {Name(m.SourceKey)} into {Name(m.TargetKey)} rejected{(note is null ? string.Empty : " · " + note)}", c).ConfigureAwait(false);
            }

            var fresh = (await store.LockMergeAsync(tx, ctx.PropertyId, req.MergeId, c).ConfigureAwait(false))!;
            return CustomerViews.Merge(fresh, keys);
        }, ct).ConfigureAwait(false);
        if (req.Approve) CustomerKeyTokenCache.Invalidate(ctx.PropertyId); // CU-PER: a merge drops the cached key map of the property
        return result;
    }

    // CU-PER: tag / note / preference commands need only the key and the display name, both on the head; consent loads the one full customer.
    private async Task<(CustomerKeySpace Keys, CustomerHead C, CrmHeadSource Src, CustomerHeadIndex Index)> EditableAsync(CommandContext ctx, string? key, CancellationToken ct)
    {
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CustomerPermissions.Edit, null, null, ct).ConfigureAwait(false);
        var (src, index, keys) = await KeyPassAsync(ctx.PropertyId, ct).ConfigureAwait(false);
        return (keys, Find(src, index, keys, true, key), src, index);
    }

    public async Task<CommandResult<CustomerChangeResult>> AddTagAsync(CommandContext ctx, AddCustomerTagRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (keys, cust, _, _) = await EditableAsync(ctx, req.CustomerKey, ct).ConfigureAwait(false);
        var tag = CustomerRules.CleanTag(req.Tag);
        return await runner.RunAsync(ctx with { Operation = OpAddTag }, req, async (tx, c) =>
        {
            if (await store.TagCountAsync(tx, ctx.PropertyId, cust.Key, c).ConfigureAwait(false) >= CustomerRules.TagsMax) throw HccException.Validation($"A customer can have at most {CustomerRules.TagsMax} tags.");
            var added = await store.AddTagAsync(tx, ctx.PropertyId, cust.Key, tag, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("CRM_TAG", Target(keys, cust.Key), $"{SafeName(cust.Name)} · tag {tag}{(added ? " added" : " already set")}", c).ConfigureAwait(false);
            return new CustomerChangeResult(keys.TokenOf(cust.Key), added ? $"tag {tag} added" : $"tag {tag} was already set");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CustomerChangeResult>> RemoveTagAsync(CommandContext ctx, RemoveCustomerTagRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (keys, cust, _, _) = await EditableAsync(ctx, req.CustomerKey, ct).ConfigureAwait(false);
        var tag = CustomerRules.CleanTag(req.Tag);
        return await runner.RunAsync(ctx with { Operation = OpRemoveTag }, req, async (tx, c) =>
        {
            if (!await store.RemoveTagAsync(tx, ctx.PropertyId, cust.Key, tag, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Tag not found.");
            await tx.AuditAsync("CRM_TAG", Target(keys, cust.Key), $"{SafeName(cust.Name)} · tag {tag} removed", c).ConfigureAwait(false);
            return new CustomerChangeResult(keys.TokenOf(cust.Key), $"tag {tag} removed");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CustomerChangeResult>> AddNoteAsync(CommandContext ctx, AddCustomerNoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (keys, cust, _, _) = await EditableAsync(ctx, req.CustomerKey, ct).ConfigureAwait(false);
        var body = CustomerRules.CleanNote(req.Body);
        return await runner.RunAsync(ctx with { Operation = OpAddNote }, req, async (tx, c) =>
        {
            var id = await store.AddNoteAsync(tx, ctx.PropertyId, cust.Key, body, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("CRM_NOTE", Target(keys, cust.Key), $"{SafeName(cust.Name)} · note {id.ToString(CultureInfo.InvariantCulture)} added", c).ConfigureAwait(false);
            return new CustomerChangeResult(keys.TokenOf(cust.Key), "note added");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CustomerChangeResult>> SetPreferencesAsync(CommandContext ctx, SetCustomerPreferencesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (keys, cust, _, _) = await EditableAsync(ctx, req.CustomerKey, ct).ConfigureAwait(false);
        var text = CustomerRules.CleanPreferences(req.Preferences);
        byte[]? expected = req.ExpectedVersion is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpPreferences }, req, async (tx, c) =>
        {
            var cur = await store.LockPrefAsync(tx, ctx.PropertyId, cust.Key, c).ConfigureAwait(false);
            if (cur is not null)
            {
                if (expected is null) throw new HccException(HccErrorCode.STALE_VERSION, "These preferences exist already. Reload and send the version.");
                VersionToken.EnsureMatches(cur.RowVer, expected, "These preferences");
            }
            else if (expected is not null) throw new HccException(HccErrorCode.STALE_VERSION, "These preferences were removed. Reload and try again.");

            await store.SetPrefAsync(tx, ctx.PropertyId, cust.Key, text, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("CRM_PREFERENCES", Target(keys, cust.Key), $"{SafeName(cust.Name)} · preferences {(text.Length == 0 ? "cleared" : "updated")}", c).ConfigureAwait(false);
            return new CustomerChangeResult(keys.TokenOf(cust.Key), text.Length == 0 ? "preferences cleared" : "preferences saved");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Marketing consent for the customer: the flags are written to Messaging's per-guest consent for every guest profile of the customer; queued messages are stopped like Messaging does.</summary>
    public async Task<CommandResult<CustomerChangeResult>> SetConsentAsync(CommandContext ctx, SetCustomerConsentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var (keys, head, src, index) = await EditableAsync(ctx, req.CustomerKey, ct).ConfigureAwait(false);
        if (!(await src.CustomersAsync(reads, ctx.PropertyId, index, [head], ct).ConfigureAwait(false)).Customers.TryGetValue(head.Key, out var cust)) throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
        if (cust.GuestIds.Count == 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This customer has no saved guest profile yet, so there is nothing to attach consent to.");
        return await runner.RunAsync(ctx with { Operation = OpConsent }, req, async (tx, c) =>
        {
            var d = messaging.For(tx);
            var stopped = 0;
            MsgConsent last = MsgConsent.Default;
            foreach (var gid in cust.GuestIds)
            {
                var prev = (await d.GetStoredConsentAsync(ctx.PropertyId, gid, c).ConfigureAwait(false))?.Consent ?? MsgConsent.Default;
                var next = new MsgConsent(req.WhatsApp ?? prev.WhatsApp, req.Sms ?? prev.Sms, req.Email ?? prev.Email, req.OptOut ?? prev.OptOut);
                await d.UpsertConsentAsync(ctx.PropertyId, gid, next, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                last = next;
                foreach (var m in await d.MessagesAsync(ctx.PropertyId, null, gid, 1000, c).ConfigureAwait(false))
                {
                    if (m.Blocked || MessagingRules.StatusAt(m.QueuedUtc, UtcNow, m.BoostMinutes, m.Outcome, false) != MsgStatus.Queued) continue;
                    if (!next.OptOut && next.Allows(m.Channel)) continue;
                    await d.BlockMessageAsync(m.MessageId, next.OptOut ? "Guest opted out before sending" : $"{m.Channel} consent withdrawn before sending", c).ConfigureAwait(false);
                    stopped++;
                }
            }

            var summary = last.OptOut ? "opted out of marketing" : $"WhatsApp {Yn(last.WhatsApp)} · SMS {Yn(last.Sms)} · Email {Yn(last.Email)}";
            await tx.AuditAsync("CRM_CONSENT", Target(keys, cust.Key), $"{SafeName(cust.Name)} · {summary}{(stopped > 0 ? $" · {stopped} queued stopped" : string.Empty)}", c).ConfigureAwait(false);
            return new CustomerChangeResult(keys.TokenOf(cust.Key), summary);
        }, ct).ConfigureAwait(false);
    }

    private static string Yn(bool b) => b ? "yes" : "no";
}
