using Hcc.Contracts;
using Hcc.Application.Access;
using Hcc.Contracts.GuestServices;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestServices;

namespace Hcc.Application.GuestServices;

/// <summary>Stay facts needed to record feedback.</summary>
public sealed record FeedbackStay(long StayId, int? RoomId, string RoomNo, string GuestName, string Status);

/// <summary>A complaint opened by the feedback (module 18 table ops.guest_complaint).</summary>
public sealed record FeedbackComplaint(string Category, string Severity, string Description);

public interface IFeedbackStore
{
    Task<FeedbackStay?> StayAsync(int propertyId, long stayId, CancellationToken ct = default);
    /// <summary>One transaction: feedback row, optional complaint, audit row. CONFLICT when the stay already has feedback.</summary>
    Task<FeedbackResult> InsertAsync(int propertyId, int principalId, FeedbackStay stay, int stars, string? comment, FeedbackComplaint? complaint, DateTime utc, CancellationToken ct = default);
    Task<IReadOnlyList<FeedbackView>> ListAsync(int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<IReadOnlyList<FeedbackFollowUpView>> FollowUpsAsync(int propertyId, bool openOnly, CancellationToken ct = default);
    /// <summary>Marks an open follow-up done (audited). False when it is not found or already done.</summary>
    Task<bool> ResolveFollowUpAsync(int propertyId, int principalId, long followUpId, DateTime utc, CancellationToken ct = default);
}

/// <summary>
/// F4-T3 guest feedback (record, list) and Owner follow-up calls (list, resolve). Record: PERM-FBK-001 (Owner, Manager, Reception, Cashier);
/// follow-ups: PERM-FBK-002 (Owner). The follow-up rows are created by the day-close projector (<c>FeedbackFollowUpProjector</c>).
/// </summary>
public sealed class FeedbackService(IAccessPolicy policy, IFeedbackStore store, TimeProvider clock)
{
    public const string RecordPermission = "PERM-FBK-001", FollowUpPermission = "PERM-FBK-002";

    public async Task<FeedbackResult> RecordAsync(int propertyId, int principalId, RecordGuestFeedbackRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await policy.RequireAsync(propertyId, principalId, RecordPermission, AccessScopes.Property(propertyId), ct: ct).ConfigureAwait(false);
        if (!FeedbackRules.StarsValid(r.Stars)) throw HccException.Validation("Choose 1 to 5 stars.");
        var comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();
        if (comment is { Length: > FeedbackRules.MaxComment }) throw HccException.Validation($"Keep the comment under {FeedbackRules.MaxComment} characters.");
        var category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
        if (category is not null && !GuestServiceVocab.ComplaintCategories.Contains(category)) throw HccException.Validation("Choose a complaint category from the list.");
        var stay = await store.StayAsync(propertyId, r.StayId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
        if (stay.Status is not ("InHouse" or "Departed")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Feedback is recorded for a guest who is staying or has left.");
        FeedbackComplaint? complaint = null;
        if (FeedbackRules.OpensComplaint(r.Stars, comment, category))
            complaint = new FeedbackComplaint(category ?? "Other", FeedbackRules.Severity(r.Stars), comment ?? $"Guest rated the stay {r.Stars} of 5 at check-out.");
        return await store.InsertAsync(propertyId, principalId, stay, r.Stars, comment, complaint, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
    }

    public async Task<FeedbackPage> ListAsync(int propertyId, int principalId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, RecordPermission, AccessScopes.Property(propertyId), ct: ct).ConfigureAwait(false);
        if (from is not null && to is not null && to < from) throw HccException.Validation("'to' must not be before 'from'.");
        var rows = await store.ListAsync(propertyId, from, to, ct).ConfigureAwait(false);
        return new FeedbackPage(rows.Count, FeedbackRules.Average(rows.Select(x => x.Stars).ToList()), rows);
    }

    public async Task<IReadOnlyList<FeedbackFollowUpView>> FollowUpsAsync(int propertyId, int principalId, bool openOnly, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, FollowUpPermission, AccessScopes.Property(propertyId), ct: ct).ConfigureAwait(false);
        return await store.FollowUpsAsync(propertyId, openOnly, ct).ConfigureAwait(false);
    }

    public async Task ResolveFollowUpAsync(int propertyId, int principalId, ResolveFeedbackFollowUpRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await policy.RequireAsync(propertyId, principalId, FollowUpPermission, AccessScopes.Property(propertyId), ct: ct).ConfigureAwait(false);
        if (!await store.ResolveFollowUpAsync(propertyId, principalId, r.FollowUpId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.NOT_FOUND, "Open follow-up not found.");
    }
}
