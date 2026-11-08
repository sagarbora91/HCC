// F8-QW-NS · GET /reservations/no-show-due: confirmed arrivals of the business date not checked in once fd.noShowTime has passed. Read only: marking
// goes through the existing MarkNoShow command (which still refuses while an advance is held); nothing is posted (DEC-MA-5b) and nothing is marked automatically (QWQ-7).
using Hcc.Application.Configuration;
using Hcc.Application.FrontDeskMasters;
using Hcc.Contracts.Reservations;

namespace Hcc.Application.Reservations;

public sealed record NoShowDueItem(long ReservationId, string PublicNo, string Version, string GuestName, string RoomNo, string RoomType, string Arrival, string Source,
    string? Policy, long? ChargePaise, string ChargeText);

public sealed record NoShowDueView(string BusinessDate, string Cutoff, bool PastCutoff, IReadOnlyList<NoShowDueItem> Items);

public sealed class NoShowQueries(ReservationService reservations, FrontDeskMastersQueries policies, ISettingsReader settings, TimeProvider clock)
{
    /// <summary>Property local time is IST (UTC+05:30), as for every business-date rule in HCC.</summary>
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public async Task<NoShowDueView> DueAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var list = await reservations.ListAsync(propertyId, principalId, ReservationViews.Range, null, null, "Confirmed", null, 1, 500, ct).ConfigureAwait(false);
        var business = DateOnly.Parse(list.BusinessDate, System.Globalization.CultureInfo.InvariantCulture);
        var cfg = await settings.GetAsync(propertyId, "fd.noShowTime", business, ct).ConfigureAwait(false);
        var cutoff = NoShowRules.Cutoff(cfg.Text);
        var local = clock.GetUtcNow().UtcDateTime + Ist;
        var past = NoShowRules.PastCutoff(business, local, cutoff);
        var cut = cutoff.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (!past) return new NoShowDueView(list.BusinessDate, cut, false, []);

        var pols = (await policies.PoliciesAsync(propertyId, principalId, null, 500, "active", ct).ConfigureAwait(false)).Items;
        var items = new List<NoShowDueItem>();
        foreach (var r in list.Items.Where(x => x.Arrival == list.BusinessDate && !x.Registered && x.StayId is null).OrderBy(x => x.PublicNo, StringComparer.Ordinal))
        {
            var pol = pols.FirstOrDefault(p => p.Sources.Any(s => string.Equals(s, r.Source, StringComparison.OrdinalIgnoreCase))) ?? pols.FirstOrDefault(p => p.Sources.Count == 0);
            long? charge = pol?.NoShowChargeBps is { } bps ? NoShowRules.Charge(r.NightlyRatePaise ?? 0, bps) : pol is null ? 0 : null;
            items.Add(new NoShowDueItem(r.ReservationId, r.PublicNo, r.Version, r.GuestName, r.RoomNo, r.RoomType, r.Arrival, r.Source, pol?.Name, charge,
                NoShowRules.ChargeText(pol?.Name, charge)));
        }

        return new NoShowDueView(list.BusinessDate, cut, true, items);
    }
}
