using Hcc.Application.Access;
using Hcc.Contracts.Yield;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;

namespace Hcc.Application.Yield;

/// <summary>Yield reads (calendar with explanations, configuration, quotes, proposals, publish history) and the <see cref="IYieldRateQuery"/> port for P2.</summary>
public sealed class YieldQueries(IYieldStore store, IAccessPolicy access, TimeProvider clock) : IYieldRateQuery
{
    public const int MaxCalendarNights = 60;

    public async Task<YieldCalendarResult> GetCalendarAsync(int propertyId, int principalId, string? from, int nights, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        if (nights < 1 || nights > MaxCalendarNights) throw HccException.Validation($"Choose 1–{MaxCalendarNights} nights.");
        var start = from is null ? (DateOnly?)null : StayDates.Parse(from, "from");
        var now = clock.GetUtcNow().UtcDateTime;
        var inputs = await store.LoadInputsAsync(propertyId, start, nights, now, null, ct).ConfigureAwait(false);
        var cfg = await store.LoadConfigAsync(propertyId, null, ct).ConfigureAwait(false);
        var engine = new YieldEngine(cfg, inputs);
        var first = start ?? inputs.BusinessDate;
        var cells = engine.Calendar(first, nights).Select(n => new YieldNightView(n.RoomTypeId, n.RoomType, StayDates.ToWire(n.Date), n.Base, n.BaseSource,
            n.Steps.Select(s => new YieldStepView(s.Label, s.Bps, s.Detail)).ToList(), n.RawBps, n.AppliedBps, n.Clamped, n.Computed, n.Override, n.Suggested, n.Live,
            new YieldOccupancyView(n.Occ.OnBooks, n.Occ.Forecast, n.Occ.Sellable, n.Occ.Pct, n.Occ.Simulated), n.Restrictions.Select(x => x.Code).ToList(), n.Explain)).ToList();
        var figures = engine.Summary(inputs.BusinessDate, YieldService.WindowNights);
        var proposals = await store.ListProposalsAsync(propertyId, 50, ct).ConfigureAwait(false);
        var live = proposals.Count(p => p.Status == "Pending" && p.ExpiresUtc > now);
        return new YieldCalendarResult(StayDates.ToWire(first), nights, cells, new YieldSummaryView(figures.AverageBar7Paise, figures.HighDemandNights, figures.PendingNights, live));
    }

    public async Task<YieldConfigResult> GetConfigAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        return await store.GetConfigViewAsync(propertyId, 100, ct).ConfigureAwait(false);
    }

    public async Task<YieldQuoteResult> QuoteAsync(int propertyId, int principalId, string planCode, int roomTypeId, string arrival, int nights, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        return await QuoteAsync(propertyId, planCode, roomTypeId, StayDates.Parse(arrival, "arrival"), nights, ct).ConfigureAwait(false);
    }

    /// <summary>Server-side quote for P2 (no access check: the calling module authorizes its own user).</summary>
    public async Task<YieldQuoteResult> QuoteAsync(int propertyId, string planCode, int roomTypeId, DateOnly arrival, int nights, CancellationToken ct = default)
    {
        if (nights < 0 || nights > 30) throw HccException.Validation("Choose 1–30 nights.");
        var now = clock.GetUtcNow().UtcDateTime;
        var inputs = await store.LoadInputsAsync(propertyId, arrival, Math.Max(1, nights), now, null, ct).ConfigureAwait(false);
        if (inputs.Types.All(t => t.Id != roomTypeId)) throw HccException.Validation("Choose a room type.");
        var cfg = await store.LoadConfigAsync(propertyId, null, ct).ConfigureAwait(false);
        var q = new YieldEngine(cfg, inputs).Quote(planCode, roomTypeId, arrival, nights);
        return new YieldQuoteResult(q.Plan.Code, roomTypeId, StayDates.ToWire(arrival), q.Nights, q.Rows.Select(r => new YieldQuoteRow(StayDates.ToWire(r.Date), r.Rate, r.Bar)).ToList(), q.Total, q.Issues);
    }

    public async Task<IReadOnlyList<YieldProposalView>> ListProposalsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var list = await store.ListProposalsAsync(propertyId, 50, ct).ConfigureAwait(false);
        var inputs = await store.LoadInputsAsync(propertyId, null, 1, now, null, ct).ConfigureAwait(false);
        return list.Select(p =>
        {
            var status = p.Status;
            if (status == "Pending")
                status = now > p.ExpiresUtc ? "Expired" : YieldEngine.TariffSignature(inputs.Tariffs, p.RoomTypeIds) != p.Signature ? "Stale" : "Pending";
            return new YieldProposalView(p.ProposalId, status, StayDates.ToWire(p.From), StayDates.ToWire(p.To), p.RoomTypeIds, p.ChangedNights, p.ProposedBy,
                YieldService.Iso(p.ProposedUtc), YieldService.Iso(p.ExpiresUtc), p.Reason, p.DecidedBy, p.DecidedUtc is { } d ? YieldService.Iso(d) : null, p.DecisionNote,
                p.PublishId, YieldVersion.ToWire(p.RowVersion));
        }).ToList();
    }

    public async Task<IReadOnlyList<YieldPublishView>> ListPublishesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, YieldPermissions.View, ct: ct).ConfigureAwait(false);
        return await store.ListPublishesAsync(propertyId, 30, ct).ConfigureAwait(false);
    }
}
