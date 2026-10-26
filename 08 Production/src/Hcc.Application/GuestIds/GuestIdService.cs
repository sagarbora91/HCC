using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.GuestIds;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;
using Hcc.Domain.GuestIds;
using Hcc.Domain.Stays;

namespace Hcc.Application.GuestIds;

public static class GuestIdPermissions
{
    /// <summary>guest.idDetails.view (field) — OWN, MGR, REC (seed 0101). Required for the audited reveal.</summary>
    public const string IdView = "PERM-GST-003";

    /// <summary>Key rotation is a configuration change: Owner only (PERM-ADM-003 'A').</summary>
    public const string Rekey = "PERM-ADM-003";
}

/// <summary>Persistence of sealed numbers (<c>gst.guest_id_secret</c>, migration 0601).</summary>
public interface IGuestIdStore
{
    Task<bool> HasNumberAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default);

    Task UpsertAsync(ICommandTransaction tx, long guestId, SealedGuestId sealedId, DateTime utc, CancellationToken ct = default);

    /// <summary>The guest's ID type, last 4 and sealed number at the property (null when the guest is unknown there); inside <paramref name="tx"/> when given.</summary>
    Task<(string? IdType, string? Last4, SealedGuestId? Sealed)?> ReadAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct = default);

    /// <summary>Sealed numbers of the property not under <paramref name="currentKeyId"/>, row-locked for re-encryption.</summary>
    Task<IReadOnlyList<(long GuestId, SealedGuestId Sealed)>> LockStaleAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default);
}

/// <summary>Result stored by the runner for a reveal: never the number (the idempotency record is not encrypted).</summary>
public sealed record GuestIdRevealAudit(long GuestId, bool Audited);

/// <summary>
/// DEC-068 guest ID numbers: sealing on registration/edit (called by the Stays commands inside their transaction), the audited
/// RevealGuestId command, and key rotation. The number is never written to audit text, outbox events, the idempotency record
/// (results or payload hash) or exception messages.
/// </summary>
public sealed class GuestIdService(ICommandRunner runner, IAccessPolicy policy, IGuestIdStore store, GuestIdProtector protector, TimeProvider clock)
{
    public const string OpReveal = "RevealGuestId";
    public const string OpRekey = "RekeyGuestIds";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// The registration payload as the idempotency hash sees it: the full number replaced by a keyed fingerprint (same number →
    /// same fingerprint, so replays still match; the SHA-256 in plat.idempotency cannot be brute-forced to the number).
    /// </summary>
    public SaveGuestProfileRequest HashablePayload(SaveGuestProfileRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.Guest?.IdNumber is not { } raw || GuestIdRules.Normalise(raw).Length == 0) return req;
        return req with { Guest = req.Guest with { IdNumber = protector.Fingerprint(GuestIdRules.Normalise(raw)) } };
    }

    public RegisterGuestRequest HashablePayload(RegisterGuestRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.Guest?.IdNumber is not { } raw || GuestIdRules.Normalise(raw).Length == 0) return req;
        return req with { Guest = req.Guest with { IdNumber = protector.Fingerprint(GuestIdRules.Normalise(raw)) } };
    }

    /// <summary>
    /// Applies the ID part of a cleaned draft to a profile inside the Stays command transaction. With a number: sealed under the
    /// current key. Without one on an existing profile that has a number on file: the stored number stays, provided the type kind and
    /// the last 4 are unchanged (otherwise VALIDATION — the full number must be retyped). Returns the draft to store (last 4 kept).
    /// </summary>
    public async Task<GuestDraft> ApplyAsync(ICommandTransaction tx, int propertyId, long guestId, GuestDraft clean, GuestDraft? previous, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(clean);
        if (clean.IdNumber is { Length: > 0 } number)
        {
            await store.UpsertAsync(tx, guestId, protector.Seal(propertyId, guestId, number), Now, ct).ConfigureAwait(false);
            return clean;
        }

        if (previous is not null && await store.HasNumberAsync(tx, guestId, ct).ConfigureAwait(false))
        {
            if (GuestIdRules.KindOf(clean.IdType) != GuestIdRules.KindOf(previous.IdType))
                throw HccException.Validation("The ID type changed: enter the full ID number for the new ID type.");
            if (!string.Equals(clean.IdLast4, previous.IdLast4, StringComparison.Ordinal))
                throw HccException.Validation("To change the ID, enter the full ID number again.");
        }

        return clean;
    }

    /// <summary>Validates that a guest profile change may carry ID numbers (guards a draft before the transaction).</summary>
    public static bool HasNumber(GuestDraftDto? draft) => draft?.IdNumber is { } n && GuestIdRules.Normalise(n).Length > 0;

    public async Task<CommandResult<RevealGuestIdResult>> RevealAsync(CommandContext ctx, RevealGuestIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.GuestId <= 0) throw HccException.Validation("Select a guest.");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestIdPermissions.IdView, ct: ct).ConfigureAwait(false);
        string? idType = null, last4 = null;
        SealedGuestId? sealedId = null;
        var stored = await runner.RunAsync(ctx with { Operation = OpReveal }, req, async (tx, c) =>
        {
            var row = await store.ReadAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
            if (row.Sealed is null) throw new HccException(HccErrorCode.NOT_FOUND, "No full ID number is on file for this guest (only the last 4).");
            (idType, last4, sealedId) = row;
            // Who and when (the audit row's actor and time), which guest — never the number or its last 4.
            await tx.AuditAsync("GUEST_ID_VIEW", "guest-" + req.GuestId.ToString(CultureInfo.InvariantCulture),
                $"guest {req.GuestId} · full ID number shown ({GuestIdRules.KindOf(row.IdType)})", c).ConfigureAwait(false);
            return new GuestIdRevealAudit(req.GuestId, true);
        }, ct).ConfigureAwait(false);

        if (sealedId is null)
        {
            // Replay of an earlier reveal under the same Idempotency-Key: that request's audit row covers it (same caller, same guest).
            var row = await store.ReadAsync(null, ctx.PropertyId, req.GuestId, ct).ConfigureAwait(false);
            if (row?.Sealed is null) throw new HccException(HccErrorCode.NOT_FOUND, "No full ID number is on file for this guest (only the last 4).");
            (idType, last4, sealedId) = row.Value;
        }

        var number = protector.Open(ctx.PropertyId, req.GuestId, sealedId!);
        return new CommandResult<RevealGuestIdResult>(
            new RevealGuestIdResult(req.GuestId, idType ?? string.Empty, number, GuestIdRules.Format(idType, number), GuestIdRules.Mask(last4)), stored.Replayed);
    }

    public async Task<CommandResult<RekeyGuestIdsResult>> RekeyAsync(CommandContext ctx, RekeyGuestIdsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestIdPermissions.Rekey, ct: ct).ConfigureAwait(false);
        var reason = req.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give a reason for the key rotation (3–200 characters).");
        return await runner.RunAsync(ctx with { Operation = OpRekey }, req, async (tx, c) =>
        {
            var current = protector.CurrentKeyId;
            var stale = await store.LockStaleAsync(tx, ctx.PropertyId, current, c).ConfigureAwait(false);
            foreach (var (guestId, sealedId) in stale)
            {
                var number = protector.Open(ctx.PropertyId, guestId, sealedId);
                await store.UpsertAsync(tx, guestId, protector.Seal(ctx.PropertyId, guestId, number), Now, c).ConfigureAwait(false);
            }

            var from = stale.Select(s => s.Sealed.KeyId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            await tx.AuditAsync("GUEST_ID_REKEY", "guest-id-keys",
                $"{stale.Count} guest ID numbers re-encrypted to key {current}" + (from.Count > 0 ? " from " + string.Join(", ", from) : string.Empty) + " · " + reason, c).ConfigureAwait(false);
            return new RekeyGuestIdsResult(stale.Count, current, from);
        }, ct).ConfigureAwait(false);
    }
}
