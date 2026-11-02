using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Contracts;

namespace Hcc.Application.Reporting;

public static class DigestPermissions
{
    /// <summary>digest.view — Owner only (migration 4377).</summary>
    public const string View = "PERM-DGS-001";
}

/// <summary>A held owner-digest draft (F4-CONTRACT §3). <see cref="Status"/> is always "held": nothing sends it.</summary>
public sealed record DigestDraft(DateOnly Date, string Channel, bool CountsOnly, string Status, string Reason, string Body, DateTime CreatedUtc, int CreatedBy);

public interface IDigestDraftStore
{
    /// <summary>Replaces the draft of (date, channel) — a re-close writes the day's drafts again.</summary>
    Task UpsertAsync(int propertyId, DigestDraft draft, CancellationToken ct);

    Task<IReadOnlyList<DigestDraft>> ListAsync(int propertyId, DateOnly date, CancellationToken ct);
}

/// <summary>Owner digest drafts (F4-T2): read for the Owner, written by the day close. They are never delivered from here.</summary>
public sealed class DigestDraftQueries(IDigestDraftStore store, IAccessPolicy policy)
{
    public const string HeldReason = "Delivery waits for the messaging gateway (F9) and the internet switch (F5)";

    public async Task<IReadOnlyList<DigestDraft>> ListAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, DigestPermissions.View, ct: ct).ConfigureAwait(false);
        return await store.ListAsync(caller.PropertyId, date, ct).ConfigureAwait(false);
    }

    /// <summary>The two held drafts of a closed date (idempotent per date and channel). The e-mail may carry money; the WhatsApp one is counts only.</summary>
    public async Task WriteAsync(int propertyId, int principalId, DateOnly date, string emailBody, string whatsAppCountsBody, DateTime nowUtc, CancellationToken ct = default)
    {
        if (whatsAppCountsBody.Contains('₹', StringComparison.Ordinal)) throw Hcc.Domain.Errors.HccException.Validation("The WhatsApp digest carries counts only, no money.");
        await store.UpsertAsync(propertyId, new DigestDraft(date, "Email", false, "held", HeldReason, emailBody, nowUtc, principalId), ct).ConfigureAwait(false);
        await store.UpsertAsync(propertyId, new DigestDraft(date, "WhatsApp", true, "held", HeldReason, whatsAppCountsBody, nowUtc, principalId), ct).ConfigureAwait(false);
    }
}
