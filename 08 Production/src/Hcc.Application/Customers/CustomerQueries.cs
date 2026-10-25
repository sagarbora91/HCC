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

    public static CustomerSummary Summary(Customer c, bool contact, bool money, IReadOnlyList<string> tags) =>
        new(c.Key, c.Name, contact ? c.Phone : CustomerRules.MaskPhone(c.Phone), contact ? c.Email : (c.Email is null ? null : CustomerRules.MaskEmail(c.Email)), !contact,
            c.Stays.Count, c.Completed.Count, c.Nights, money ? c.LifetimePaise : null, c.LastStay is { } d ? CustomerRules.Date(d) : null, c.Current is not null, tags);

    public static MergeView Merge(CrmMerge m) =>
        new(m.MergeId, m.SourceKey, m.TargetKey, m.SourceName, m.TargetName, m.Reason, m.Status, m.ProposedBy, Iso(m.ProposedUtc), m.DecidedBy,
            m.DecidedUtc is { } u ? Iso(u) : null, m.DecisionNote, VersionToken.ToWire(m.RowVer));

    public static CustomerNoteView Note(CrmNote n) => new(n.NoteId, n.Body, n.AuthorId, Iso(n.CreatedUtc));
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
        var found = CustomerRules.Search(src.Index(), q, contact);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = CustomerRules.CleanTag(tag);
            found = found.Where(c => tags.TryGetValue(c.Key, out var ts) && ts.Contains(t)).ToList();
        }

        var rows = found.Skip((page - 1) * pageSize).Take(pageSize).Select(c => CustomerViews.Summary(c, contact, money, tags.GetValueOrDefault(c.Key) ?? [])).ToList();
        return new CustomerList(rows, found.Count, page, pageSize);
    }

    public async Task<CustomerProfile> ProfileAsync(int propertyId, int principalId, string? key, CancellationToken ct = default)
    {
        var (contact, money) = await ViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var k = CustomerRules.Resolve((key ?? string.Empty).Trim(), src.Aliases);
        var c = src.Index().FirstOrDefault(x => x.Key == k) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
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
        var summary = CustomerViews.Summary(c, contact, money, o.Tags);
        return new CustomerProfile(c.Key, summary.Name, summary.Phone, summary.Email, !contact, c.GuestIds, c.Stays.Select(s => CustomerViews.Stay(s, money)).ToList(),
            c.Completed.Count, c.Nights, money ? c.LifetimePaise : null, money ? c.AvgRatePaise : null, c.FirstStay is { } f ? CustomerRules.Date(f) : null, summary.LastStay,
            c.Current is null ? null : CustomerViews.Stay(c.Current, money), c.Upcoming.Select(s => CustomerViews.Stay(s, money)).ToList(), c.Cancelled, c.TopRooms,
            prefs.Length == 0 ? null : prefs, o.Pref is null ? null : VersionToken.ToWire(o.Pref.RowVer), o.Tags, o.Notes.Select(CustomerViews.Note).ToList(), consent, o.MergedFrom);
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
        var byKey = index.ToDictionary(c => c.Key);
        var groups = new List<DuplicateCandidate>();
        foreach (var g in CustomerRules.FindDuplicates(index))
        {
            var members = g.Keys.Select(k => byKey[k]).ToList();
            groups.Add(new DuplicateCandidate(g.Reason, members.Select(c => CustomerViews.Summary(c, contact, money, [])).ToList(),
                members.Skip(1).Aggregate(members[0].Key, (best, c) => CustomerRules.SuggestSurvivor(byKey[best], c))));
        }

        return new DuplicateList(groups.Where(g => g.Customers.Count > 0).ToList());
    }

    public async Task<MergeList> MergesAsync(int propertyId, int principalId, string? status, CancellationToken ct = default)
    {
        await MergeAccessAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (status is not null and not ("Pending" or "Approved" or "Rejected")) throw HccException.Validation("status must be Pending, Approved or Rejected.");
        return new MergeList((await reads.MergesAsync(propertyId, status, ct).ConfigureAwait(false)).Select(CustomerViews.Merge).ToList());
    }
}
