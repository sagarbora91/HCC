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

    /// <summary>Key rotation is an Owner-only configuration change (PERM-CFG-003, role matrix v2; before: PERM-ADM-003 'A').</summary>
    public const string Rekey = "PERM-CFG-003";
}

/// <summary>Persistence of sealed numbers (<c>gst.guest_id_secret</c>, migration 0601).</summary>
public interface IGuestIdStore
{
    Task<bool> HasNumberAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default);

    Task UpsertAsync(ICommandTransaction tx, long guestId, SealedGuestId sealedId, DateTime utc, CancellationToken ct = default);

    /// <summary>DEC-193: seals an accompanying guest's number (<c>gst.guest_party_id_secret</c>, migration 4620); the party row must exist.</summary>
    Task UpsertPartyAsync(ICommandTransaction tx, long guestId, int seq, SealedGuestId sealedId, DateTime utc, CancellationToken ct = default);

    /// <summary>DEC-193: the member's ID type, last 4 and sealed number (null when the guest or member is unknown at the property).</summary>
    Task<(string? IdType, string? Last4, SealedGuestId? Sealed)?> ReadPartyAsync(ICommandTransaction? tx, int propertyId, long guestId, int seq, CancellationToken ct = default);

    /// <summary>DEC-193: sealed party numbers of the property not under <paramref name="currentKeyId"/>, row-locked for re-encryption.</summary>
    Task<IReadOnlyList<(long GuestId, int Seq, SealedGuestId Sealed)>> LockStalePartyAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default);

    /// <summary>The guest's ID type, last 4 and sealed number at the property (null when the guest is unknown there); inside <paramref name="tx"/> when given.</summary>
    Task<(string? IdType, string? Last4, SealedGuestId? Sealed)?> ReadAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct = default);

    /// <summary>Sealed numbers of the property not under <paramref name="currentKeyId"/>, row-locked for re-encryption.</summary>
    Task<IReadOnlyList<(long GuestId, SealedGuestId Sealed)>> LockStaleAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default);
}

/// <summary>What one participant re-sealed during a key rotation: a label for the audit text, how many values, and the key ids they were under.</summary>
public sealed record GuestIdRekeyPart(string What, int Rekeyed, IReadOnlyList<string> FromKeyIds);

/// <summary>
/// Other data sealed with the guest-ID key ring (A07-07): <see cref="GuestIdService.RekeyAsync"/> re-seals it in the same transaction as the ID
/// numbers, so after a rotation nothing stays under the old key and the old key can be retired. The Documents module takes part (ID capture
/// images and OCR text). Sealed configuration secrets (<c>msg.gatewayKey</c>) are not covered yet: see the GuestIds README, "Key rotation".
/// </summary>
public interface IGuestIdRekeyParticipant
{
    /// <summary>Re-seals, inside <paramref name="tx"/>, every value of the property not sealed under <paramref name="currentKeyId"/>.</summary>
    Task<GuestIdRekeyPart> RekeyAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default);
}

/// <summary>Result stored by the runner for a reveal: never the number (the idempotency record is not encrypted).</summary>
public sealed record GuestIdRevealAudit(long GuestId, bool Audited);

/// <summary>
/// DEC-068 guest ID numbers: sealing on registration/edit (called by the Stays commands inside their transaction), the audited
/// RevealGuestId command, and key rotation. The number is never written to audit text, outbox events, the idempotency record
/// (results or payload hash) or exception messages.
/// </summary>
public sealed class GuestIdService(ICommandRunner runner, IAccessPolicy policy, IGuestIdStore store, GuestIdProtector protector, TimeProvider clock,
    IEnumerable<IGuestIdRekeyParticipant>? rekeyParticipants = null)
{
    private readonly IReadOnlyList<IGuestIdRekeyParticipant> _participants = rekeyParticipants?.ToList() ?? [];

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
        if (req.Guest is null) return req;
        return req with { Guest = Hashable(req.Guest) };
    }

    public RegisterGuestRequest HashablePayload(RegisterGuestRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.Guest is null) return req;
        return req with { Guest = Hashable(req.Guest) };
    }

    /// <summary>The draft with every full number (registered guest and DEC-193 party members) replaced by its keyed fingerprint.</summary>
    private GuestDraftDto Hashable(GuestDraftDto g)
    {
        string? F(string? raw) => raw is { } r && GuestIdRules.Normalise(r).Length > 0 ? protector.Fingerprint(GuestIdRules.Normalise(r)) : raw;
        return g with { IdNumber = F(g.IdNumber), Party = g.Party?.Select(p => p with { IdNumber = F(p.IdNumber) }).ToList() };
    }

    /// <summary>DEC-193: the seq of a party member (1–6).</summary>
    private static void CheckSeq(int seq)
    {
        if (seq is < 1 or > GuestRules.MaxParty) throw HccException.Validation("Select a party member.");
    }

    /// <summary>
    /// DEC-193: an edit that does not retype a member's number keeps that member's stored last 4 when the member at the same seq keeps
    /// the same name and ID type kind (the sealed number stays with it; the store drops it otherwise). Pure; no number involved.
    /// </summary>
    public static GuestDraft CarryPartyIds(GuestDraft clean, GuestDraft? previous)
    {
        ArgumentNullException.ThrowIfNull(clean);
        var now = clean.Party ?? [];
        var before = previous?.Party ?? [];
        var list = new List<GuestPartyMember>(now.Count);
        for (var i = 0; i < now.Count; i++)
        {
            var p = now[i];
            if (p.IdNumber is null && i < before.Count && SameMember(before[i], p) && before[i].IdLast4 is { } kept
                && (p.IdLast4 is null || string.Equals(p.IdLast4, kept, StringComparison.Ordinal)))
                p = p with { IdLast4 = kept, IdType = p.IdType ?? before[i].IdType };
            list.Add(p);
        }

        return clean with { Party = list };
    }

    /// <summary>Same person and same ID kind at the same seq (case-insensitive name; a member without a type keeps the stored one).</summary>
    public static bool SameMember(GuestPartyMember stored, GuestPartyMember now)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(now);
        return string.Equals(stored.Name.Trim(), now.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && (now.IdType is null || GuestIdRules.KindOf(stored.IdType) == GuestIdRules.KindOf(now.IdType));
    }

    /// <summary>
    /// DEC-193: seals every party member's new number under the current key, associated data <c>HCC-GUESTID-v1|property|guest|seq|keyId</c>,
    /// after the store wrote the party rows (inside the Stays command transaction). Members without a new number are left as the store kept them.
    /// </summary>
    public async Task ApplyPartyAsync(ICommandTransaction tx, int propertyId, long guestId, GuestDraft clean, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(clean);
        var party = clean.Party ?? [];
        for (var i = 0; i < party.Count; i++)
            if (party[i].IdNumber is { Length: > 0 } number)
                await store.UpsertPartyAsync(tx, guestId, i + 1, protector.Seal(propertyId, guestId, i + 1, number), Now, ct).ConfigureAwait(false);
    }

    /// <summary>True when any party member carries a full number.</summary>
    public static bool HasPartyNumber(GuestDraftDto? draft) => draft?.Party?.Any(p => p.IdNumber is { } n && GuestIdRules.Normalise(n).Length > 0) == true;

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
        if (req.PartySeq is { } partySeq) CheckSeq(partySeq);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, GuestIdPermissions.IdView, ct: ct).ConfigureAwait(false);
        if (req.PartySeq is { } seq) return await RevealPartyAsync(ctx, req, seq, ct).ConfigureAwait(false);
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
            // F-L04: a replay of an earlier reveal (same Idempotency-Key) shows the number again, so it is audited again, as its own
            // command under a fresh server key (the idempotency record only ever holds the audit marker, never the number).
            await runner.RunAsync(ctx with { Operation = OpReveal + "Replay", IdempotencyKey = "replay-" + Guid.NewGuid().ToString("N") }, req, async (tx, c) =>
            {
                var row = await store.ReadAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
                if (row?.Sealed is null) throw new HccException(HccErrorCode.NOT_FOUND, "No full ID number is on file for this guest (only the last 4).");
                (idType, last4, sealedId) = row.Value;
                await tx.AuditAsync("GUEST_ID_VIEW", "guest-" + req.GuestId.ToString(CultureInfo.InvariantCulture),
                    $"guest {req.GuestId} · full ID number shown again (replayed request) ({GuestIdRules.KindOf(row.Value.IdType)})", c).ConfigureAwait(false);
                return new GuestIdRevealAudit(req.GuestId, true);
            }, ct).ConfigureAwait(false);
        }

        var number = protector.Open(ctx.PropertyId, req.GuestId, sealedId!);
        return new CommandResult<RevealGuestIdResult>(
            new RevealGuestIdResult(req.GuestId, idType ?? string.Empty, number, GuestIdRules.Format(idType, number), GuestIdRules.Mask(last4)), stored.Replayed);
    }

    /// <summary>
    /// DEC-193: the audited reveal of an accompanying guest's own number. Same rules as the registered guest's (PERM-GST-003, audit GUEST_ID_VIEW
    /// with the seq — never the number or its last 4 — a replay audited again, number never in the stored result).
    /// </summary>
    private async Task<CommandResult<RevealGuestIdResult>> RevealPartyAsync(CommandContext ctx, RevealGuestIdRequest req, int seq, CancellationToken ct)
    {
        string? idType = null, last4 = null;
        SealedGuestId? sealedId = null;
        var target = "guest-" + req.GuestId.ToString(CultureInfo.InvariantCulture);
        async Task<GuestIdRevealAudit> Body(ICommandTransaction tx, CancellationToken c, string what)
        {
            var row = await store.ReadPartyAsync(tx, ctx.PropertyId, req.GuestId, seq, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Party member not found.");
            if (row.Sealed is null) throw new HccException(HccErrorCode.NOT_FOUND, "No full ID number is on file for this party member.");
            (idType, last4, sealedId) = row;
            await tx.AuditAsync("GUEST_ID_VIEW", target,
                string.Create(CultureInfo.InvariantCulture, $"guest {req.GuestId} · party member {seq} · full ID number shown{what} ({GuestIdRules.KindOf(row.IdType)})"), c).ConfigureAwait(false);
            return new GuestIdRevealAudit(req.GuestId, true);
        }

        var stored = await runner.RunAsync(ctx with { Operation = OpReveal }, req, (tx, c) => Body(tx, c, string.Empty), ct).ConfigureAwait(false);
        if (sealedId is null)
            await runner.RunAsync(ctx with { Operation = OpReveal + "Replay", IdempotencyKey = "replay-" + Guid.NewGuid().ToString("N") }, req,
                (tx, c) => Body(tx, c, " again (replayed request)"), ct).ConfigureAwait(false);

        var number = protector.Open(ctx.PropertyId, req.GuestId, seq, sealedId!);
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
        if (GuestIdRules.LooksLikeIdNumber(reason)) throw HccException.Validation(GuestIdRules.FreeTextIdMessage); // A07-05: the reason is audited in clear
        return await runner.RunAsync(ctx with { Operation = OpRekey }, req, async (tx, c) =>
        {
            var current = protector.CurrentKeyId;
            var stale = await store.LockStaleAsync(tx, ctx.PropertyId, current, c).ConfigureAwait(false);
            foreach (var (guestId, sealedId) in stale)
            {
                var number = protector.Open(ctx.PropertyId, guestId, sealedId);
                await store.UpsertAsync(tx, guestId, protector.Seal(ctx.PropertyId, guestId, number), Now, c).ConfigureAwait(false);
            }

            // DEC-193: accompanying guests' numbers move to the current key with the guests' own (counted with them).
            var staleParty = await store.LockStalePartyAsync(tx, ctx.PropertyId, current, c).ConfigureAwait(false);
            foreach (var (guestId, seq, sealedId) in staleParty)
            {
                var number = protector.Open(ctx.PropertyId, guestId, seq, sealedId);
                await store.UpsertPartyAsync(tx, guestId, seq, protector.Seal(ctx.PropertyId, guestId, seq, number), Now, c).ConfigureAwait(false);
            }

            // A07-07: the other data sealed with the same ring (ID capture images and OCR text) moves to the current key in the same transaction.
            var parts = new List<GuestIdRekeyPart>();
            foreach (var p in _participants) parts.Add(await p.RekeyAsync(tx, ctx.PropertyId, current, c).ConfigureAwait(false));

            static string From(IEnumerable<string> keyIds)
            {
                var k = keyIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                return k.Count > 0 ? " from " + string.Join(", ", k) : string.Empty;
            }

            var from = stale.Select(s => s.Sealed.KeyId).Concat(staleParty.Select(s => s.Sealed.KeyId)).Concat(parts.SelectMany(p => p.FromKeyIds)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var others = string.Concat(parts.Select(p => string.Create(CultureInfo.InvariantCulture, $" · {p.Rekeyed} {p.What} re-encrypted") + From(p.FromKeyIds)));
            await tx.AuditAsync("GUEST_ID_REKEY", "guest-id-keys",
                $"{stale.Count + staleParty.Count} guest ID numbers re-encrypted to key {current}" + From(stale.Select(s => s.Sealed.KeyId).Concat(staleParty.Select(s => s.Sealed.KeyId))) + others + " · " + reason, c).ConfigureAwait(false);
            return new RekeyGuestIdsResult(stale.Count + staleParty.Count, current, from, parts.Sum(p => p.Rekeyed));
        }, ct).ConfigureAwait(false);
    }
}
