using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Messaging;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;

namespace Hcc.Application.Customers;

internal static class CustomerViews
{
    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public static CustomerStayView Stay(CrmStay s, bool money) =>
        new(s.ReservationId, s.PublicNo, s.Status, CustomerRules.Date(s.Arrival), CustomerRules.Date(s.Departure), s.Nights, s.Room, money ? s.TotalPaise : null);

    public static CustomerSummary Summary(Customer c, bool contact, bool money, IReadOnlyList<string> tags, CustomerKeySpace keys, LegacyRoll? legacy = null)
    {
        var l = legacy ?? LegacyRoll.None;
        DateOnly? first = c.FirstStay is { } f && l.First is { } lf ? (f < lf ? f : lf) : c.FirstStay ?? l.First;
        return new(keys.TokenOf(c.Key), c.Name, contact ? c.Phone : CustomerRules.MaskPhone(c.Phone), contact ? c.Email : (c.Email is null ? null : CustomerRules.MaskEmail(c.Email)), !contact,
            c.Stays.Count, c.Completed.Count, c.Nights + l.Nights, money ? c.LifetimePaise + l.NetPaise : null, c.LastStay is { } d ? CustomerRules.Date(d) : null, c.Current is not null, tags,
            l.Stays, first is { } fs ? CustomerRules.Date(fs) : null);
    }

    public static LegacyStayView Legacy(CrmLegacyStay s, bool money) =>
        new(s.StayId, s.LegacyFy, s.LegacyBookingId, s.LegacyBillNo, CustomerRules.Date(s.Arrival), s.Departure is { } d ? CustomerRules.Date(d) : null, s.Nights, s.Room, s.RoomCount, s.Status,
            money ? s.NetPaise : null);

    public static MergeView Merge(CrmMerge m, CustomerKeySpace keys) =>
        new(m.MergeId, keys.TokenOf(m.SourceKey), keys.TokenOf(m.TargetKey), m.SourceName, m.TargetName, m.Reason, m.Status, m.ProposedBy, Iso(m.ProposedUtc), m.DecidedBy,
            m.DecidedUtc is { } u ? Iso(u) : null, m.DecisionNote, VersionToken.ToWire(m.RowVer));

    public static CustomerNoteView Note(CrmNote n) => new(n.NoteId, n.Body, n.AuthorId, Iso(n.CreatedUtc));
}

/// <summary>
/// AF-CRM (PR-01 / AF-DEC-3): the customer keys on the wire. Outbound every key is a <c>ck_</c> token (HMAC under the property secret); inbound a token,
/// a raw <c>n:</c> name key, or - for readers who may see contact details only - the raw mobile key is accepted. Stored rows keep the raw key.
/// </summary>
internal sealed class CustomerKeySpace
{
    private readonly byte[] _secret;
    private readonly Dictionary<string, string> _byToken = new(StringComparer.Ordinal);

    private CustomerKeySpace(byte[] secret) => _secret = secret;

    public static async Task<CustomerKeySpace> ForAsync(ICustomerReadStore reads, int propertyId, CrmSource src, IReadOnlyList<Customer> index, CancellationToken ct)
    {
        var ks = new CustomerKeySpace(await reads.KeySecretAsync(propertyId, ct).ConfigureAwait(false));
        foreach (var raw in index.Select(c => c.Key).Concat(src.Aliases.Keys)) ks._byToken[ks.TokenOf(raw)] = raw;
        return ks;
    }

    public string TokenOf(string rawKey) => CustomerKeyToken.Make(_secret, rawKey);

    /// <summary>The raw index key for what a client sent. A raw mobile key from a masked reader is a probe and is NOT_FOUND like an unknown token.</summary>
    public string ResolveInbound(string? value, bool contact)
    {
        var v = (value ?? string.Empty).Trim();
        if (CustomerKeyToken.IsToken(v)) return _byToken.TryGetValue(v, out var raw) ? raw : throw NotFound();
        if (v.Length > 0 && v.All(char.IsAsciiDigit) && !contact) throw NotFound();
        return v;
    }

    private static HccException NotFound() => new(HccErrorCode.NOT_FOUND, "Customer not found.");
}

/// <summary>Legacy (W1a LG) rollup of one customer, summed over its key and the keys merged into it.</summary>
internal sealed record LegacyRoll(int Stays, int Nights, long NetPaise, DateOnly? First)
{
    public static readonly LegacyRoll None = new(0, 0, 0, null);

    /// <summary>Rolls the store's per-key rows up to each live customer key (an alias key counts for its target).</summary>
    public static async Task<IReadOnlyDictionary<string, LegacyRoll>> ForAsync(ICustomerReadStore reads, int propertyId, IReadOnlyCollection<string> customerKeys, IReadOnlyDictionary<string, string> aliases, CancellationToken ct)
    {
        var wanted = new HashSet<string>(customerKeys, StringComparer.Ordinal);
        var lookup = customerKeys.ToList();
        lookup.AddRange(aliases.Where(a => wanted.Contains(CustomerRules.Resolve(a.Key, aliases))).Select(a => a.Key));
        var rows = await reads.LegacyRollupAsync(propertyId, lookup, ct).ConfigureAwait(false);
        var result = new Dictionary<string, LegacyRoll>(StringComparer.Ordinal);
        foreach (var (key, r) in rows)
        {
            var target = CustomerRules.Resolve(key, aliases);
            if (!wanted.Contains(target)) continue;
            var cur = result.GetValueOrDefault(target) ?? None;
            result[target] = new LegacyRoll(cur.Stays + r.Stays, cur.Nights + r.Nights, cur.NetPaise + r.NetPaise, cur.First is { } f && f < r.First ? f : r.First);
        }

        return result;
    }
}

/// <summary>Customer 360 reads. Contact details are masked unless the reader holds PERM-GST-002; money only with PERM-FOL-001; never any ID data (DEC-068).</summary>
public sealed class CustomerQueries(IAccessPolicy policy, ICustomerReadStore reads, IMessagingDataFactory messaging)
{
    public const int MaxPageSize = 100;

    private async Task<(bool Contact, bool Money)> ViewerAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, CustomerPermissions.View, null, null, ct).ConfigureAwait(false);
        return (await policy.HasAsync(propertyId, principalId, CustomerPermissions.Edit, null, ct).ConfigureAwait(false),
            await policy.HasAsync(propertyId, principalId, CustomerPermissions.Money, null, ct).ConfigureAwait(false));
    }

    public async Task<CustomerList> ListAsync(int propertyId, int principalId, string? q, string? tag, int page, int pageSize, CancellationToken ct = default)
    {
        var (contact, money) = await ViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var tags = await reads.TagsAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Index();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, index, ct).ConfigureAwait(false);
        var found = CustomerRules.Search(index, q, contact);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = CustomerRules.CleanTag(tag);
            found = found.Where(c => tags.TryGetValue(c.Key, out var ts) && ts.Contains(t)).ToList();
        }

        var pageRows = found.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var legacy = await LegacyRoll.ForAsync(reads, propertyId, pageRows.Select(c => c.Key).ToList(), src.Aliases, ct).ConfigureAwait(false);
        var rows = pageRows.Select(c => CustomerViews.Summary(c, contact, money, tags.GetValueOrDefault(c.Key) ?? [], keys, legacy.GetValueOrDefault(c.Key))).ToList();
        return new CustomerList(rows, found.Count, page, pageSize);
    }

    public async Task<CustomerProfile> ProfileAsync(int propertyId, int principalId, string? key, CancellationToken ct = default)
    {
        var (contact, money) = await ViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Index();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, index, ct).ConfigureAwait(false);
        var k = CustomerRules.Resolve(keys.ResolveInbound(key, contact), src.Aliases);
        var c = index.FirstOrDefault(x => x.Key == k) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
        var o = await reads.OverlayAsync(propertyId, c.Key, ct).ConfigureAwait(false);
        var prefs = o.Pref?.Preferences ?? string.Join(" | ", src.Guests.Where(g => c.GuestIds.Contains(g.GuestId) && !string.IsNullOrWhiteSpace(g.Preferences)).Select(g => g.Preferences!.Trim()).Distinct());
        var consent = await messaging.ReadAsync(async (d, t) =>
        {
            bool wa = true, sms = true, em = true, opt = false;
            foreach (var gid in c.GuestIds)
            {
                var s = (await d.GetStoredConsentAsync(propertyId, gid, t).ConfigureAwait(false))?.Consent;
                if (s is null) continue;
                wa &= s.WhatsApp;
                sms &= s.Sms;
                em &= s.Email;
                opt |= s.OptOut;
            }

            return new CustomerConsentView(wa, sms, em, opt);
        }, ct).ConfigureAwait(false);
        var legacyStays = new List<CrmLegacyStay>();
        foreach (var lk in new[] { c.Key }.Concat(src.Aliases.Where(a => CustomerRules.Resolve(a.Key, src.Aliases) == c.Key).Select(a => a.Key)).Distinct(StringComparer.Ordinal))
            legacyStays.AddRange(await reads.LegacyStaysAsync(propertyId, lk, ct).ConfigureAwait(false));
        legacyStays = legacyStays.OrderByDescending(s => s.Arrival).ThenByDescending(s => s.StayId).ToList();
        var roll = legacyStays.Count == 0 ? LegacyRoll.None
            : new LegacyRoll(legacyStays.Count, legacyStays.Sum(s => s.Nights), legacyStays.Sum(s => s.NetPaise ?? 0), legacyStays.Min(s => s.Arrival));
        var summary = CustomerViews.Summary(c, contact, money, o.Tags, keys, roll);
        return new CustomerProfile(summary.Key, summary.Name, summary.Phone, summary.Email, !contact, c.GuestIds, c.Stays.Select(s => CustomerViews.Stay(s, money)).ToList(),
            c.Completed.Count, summary.Nights, summary.LifetimePaise, money ? c.AvgRatePaise : null, summary.FirstStay, summary.LastStay,
            c.Current is null ? null : CustomerViews.Stay(c.Current, money), c.Upcoming.Select(s => CustomerViews.Stay(s, money)).ToList(), c.Cancelled, c.TopRooms,
            prefs.Length == 0 ? null : prefs, o.Pref is null ? null : VersionToken.ToWire(o.Pref.RowVer), o.Tags, o.Notes.Select(CustomerViews.Note).ToList(), consent, o.MergedFrom.Select(keys.TokenOf).ToList(),
            legacyStays.Select(s => CustomerViews.Legacy(s, money)).ToList(), money ? roll.NetPaise : null);
    }

    private async Task MergeAccessAsync(int propertyId, int principalId, CancellationToken ct)
    {
        if (!await policy.HasAsync(propertyId, principalId, CustomerPermissions.MergeDecide, null, ct).ConfigureAwait(false))
            await policy.RequireAsync(propertyId, principalId, CustomerPermissions.MergePropose, null, null, ct).ConfigureAwait(false);
    }

    public async Task<DuplicateList> DuplicatesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await MergeAccessAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var contact = await policy.HasAsync(propertyId, principalId, CustomerPermissions.Edit, null, ct).ConfigureAwait(false);
        var money = await policy.HasAsync(propertyId, principalId, CustomerPermissions.Money, null, ct).ConfigureAwait(false);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Index();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, index, ct).ConfigureAwait(false);
        var byKey = index.ToDictionary(c => c.Key);
        var legacy = await LegacyRoll.ForAsync(reads, propertyId, byKey.Keys, src.Aliases, ct).ConfigureAwait(false);
        var groups = new List<DuplicateCandidate>();
        foreach (var g in CustomerRules.FindDuplicates(index))
        {
            var members = g.Keys.Select(k => byKey[k]).ToList();
            groups.Add(new DuplicateCandidate(g.Reason, members.Select(c => CustomerViews.Summary(c, contact, money, [], keys, legacy.GetValueOrDefault(c.Key))).ToList(),
                keys.TokenOf(members.Skip(1).Aggregate(members[0].Key, (best, c) => CustomerRules.SuggestSurvivor(byKey[best], c)))));
        }

        return new DuplicateList(groups.Where(g => g.Customers.Count > 0).ToList());
    }

    public async Task<MergeList> MergesAsync(int propertyId, int principalId, string? status, CancellationToken ct = default)
    {
        await MergeAccessAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (status is not null and not ("Pending" or "Approved" or "Rejected")) throw HccException.Validation("status must be Pending, Approved or Rejected.");
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, src.Index(), ct).ConfigureAwait(false);
        return new MergeList((await reads.MergesAsync(propertyId, status, ct).ConfigureAwait(false)).Select(m => CustomerViews.Merge(m, keys)).ToList());
    }

    /// <summary>The ck_ wire token of a raw customer key (what every response carries). For tests and tools; the secret is the property's.</summary>
    public async Task<string> TokenAsync(int propertyId, string rawKey, CancellationToken ct = default) =>
        CustomerKeyToken.Make(await reads.KeySecretAsync(propertyId, ct).ConfigureAwait(false), rawKey);
}
