using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Stock;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Stock;

namespace Hcc.Application.Stock;

/// <summary>
/// Stock reads. Readable by whoever works with stock (PERM-HK-003, any scope) or sees finance (PERM-RPT-002); every money field
/// (unit cost, value, laundry rate/charge, loss value) is null unless the caller holds PERM-RPT-002 property-wide.
/// </summary>
public sealed class StockQueries(IAccessPolicy policy, IStockReadStore reads)
{
    private const int MaxConsumptionDays = 93;

    private async Task<bool> RequireReadAsync(int propertyId, int principalId, CancellationToken ct)
    {
        var money = await policy.HasAsync(propertyId, principalId, StockPermissions.Money, AccessScopes.Property(propertyId), ct).ConfigureAwait(false);
        if (!money && !await policy.HasAsync(propertyId, principalId, StockPermissions.Work, AccessScopes.Any, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Access denied.");
        return money;
    }

    public async Task<StockItemsPage> ItemsAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? kind = null, bool belowMinimumOnly = false, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var money = await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var items = await reads.ItemsAsync(propertyId, ct).ConfigureAwait(false);
        var rooms = await reads.RoomsByTypeAsync(propertyId, ct).ConfigureAwait(false);
        var settings = await reads.SettingsAsync(propertyId, ct).ConfigureAwait(false);
        var batches = await reads.BatchesAsync(propertyId, "Out", ct).ConfigureAwait(false);
        var views = items.Select(i => View(i, rooms, settings.ParSets, money)).ToList();
        var filtered = views.Where(v => (string.IsNullOrEmpty(kind) || v.Kind == kind) && (!belowMinimumOnly || v.BelowMinimum)).ToList();
        long? totalValue = money ? items.Sum(i => StockRules.Total(i.Stock) * i.CostPaise) : null;
        return new StockItemsPage(page, pageSize, filtered.Count, filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), settings.ParSets,
            views.Count(v => v.BelowMinimum), batches.Sum(b => b.Lines.Sum(l => l.Sent)), await reads.PendingCountsAsync(propertyId, ct).ConfigureAwait(false), totalValue);
    }

    public async Task<IReadOnlyList<StockAlertView>> AlertsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var items = await reads.ItemsAsync(propertyId, ct).ConfigureAwait(false);
        return items.Select(i => (Item: i, Clean: StockRules.Clean(i.Stock)))
            .Where(x => StockRules.BelowMinimum(x.Clean, x.Item.MinQty))
            .Select(x => new StockAlertView(x.Item.Code, x.Item.Name, x.Clean, x.Item.MinQty, StockRules.SuggestedReorder(x.Clean, x.Item.MinQty, x.Item.ReorderQty)))
            .OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<LaundryBatchesPage> BatchesAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? status = null, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var money = await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (status is not null && status is not ("Out" or "Returned" or "Returned short")) throw HccException.Validation("Unknown batch status.");
        var rows = await reads.BatchesAsync(propertyId, status, ct).ConfigureAwait(false);
        var out_ = await reads.BatchesAsync(propertyId, "Out", ct).ConfigureAwait(false);
        return new LaundryBatchesPage(page, pageSize, rows.Count,
            rows.Skip((page - 1) * pageSize).Take(pageSize).Select(b => new LaundryBatchView(b.BatchId, b.Vendor, StayDates.ToWire(b.SentDate), b.Status,
                b.ReturnedDate is { } r ? StayDates.ToWire(r) : null, b.Note,
                b.Lines.Select(l => new LaundryLineView(l.ItemCode, l.ItemName, l.Sent, l.Received, l.Damaged, StockRules.Missing(l.Sent, l.Received, l.Damaged))).ToList(),
                money ? b.ChargePaise : null, VersionToken.ToWire(b.Version))).ToList(),
            out_.Sum(b => b.Lines.Sum(l => l.Sent)));
    }

    public async Task<CountsPage> CountsAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? status = null, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var money = await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (status is not null && status is not ("Submitted" or "Posted" or "Rejected")) throw HccException.Validation("Unknown count status.");
        var rows = await reads.CountsAsync(propertyId, status, ct).ConfigureAwait(false);
        var costs = money ? (await reads.ItemsAsync(propertyId, ct).ConfigureAwait(false)).ToDictionary(i => i.Code, i => i.CostPaise, StringComparer.Ordinal) : null;
        return new CountsPage(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).Select(c => new CountView(c.CountId, StayDates.ToWire(c.BusinessDate), c.Loc, c.Status,
            c.ActorId, c.ReviewerId, c.Reason, c.Note, c.Lines.Select(l => new CountLineView(l.ItemCode, l.ItemName, l.System, l.Counted)).ToList(),
            costs is null ? null : c.Lines.Sum(l => (l.Counted - l.System) * costs.GetValueOrDefault(l.ItemCode)), VersionToken.ToWire(c.Version))).ToList());
    }

    public async Task<LossesPage> LossesAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? from = null, string? to = null, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        var money = await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var rows = await reads.LossesAsync(propertyId, OptDate(from), OptDate(to), ct).ConfigureAwait(false);
        return new LossesPage(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LossView(l.LossId, StayDates.ToWire(l.BusinessDate), l.ItemCode, l.ItemName, l.Loc, l.Qty, l.Reason, l.Note, l.ActorId, money ? l.ValuePaise : null, l.Ref)).ToList(),
            money ? rows.Sum(l => l.ValuePaise) : null);
    }

    public async Task<MovesPage> MovesAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, string? itemCode = null, string? from = null, string? to = null, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var rows = await reads.MovesAsync(propertyId, itemCode, OptDate(from), OptDate(to), 5000, ct).ConfigureAwait(false);
        return new MovesPage(page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MoveView(m.MoveId, StayDates.ToWire(m.BusinessDate), DateTime.SpecifyKind(m.AtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                m.Kind, m.ItemCode, m.Qty, m.From, m.To, m.Ref, m.Reason, m.ActorId)).ToList());
    }

    /// <summary>Consumption per occupied room-night (amenities/cleaning: use moves; linen: room-change moves) against the standard.</summary>
    public async Task<ConsumptionReport> ConsumptionAsync(int propertyId, int principalId, string from, string to, CancellationToken ct = default)
    {
        await RequireReadAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var f = OptDate(from) ?? throw HccException.Validation("Enter the from date.");
        var t = OptDate(to) ?? throw HccException.Validation("Enter the to date.");
        if (t < f) throw HccException.Validation("The to date cannot be before the from date.");
        if (t.DayNumber - f.DayNumber + 1 > MaxConsumptionDays) throw HccException.Validation($"At most {MaxConsumptionDays} days at a time.");
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var occ = await reads.OccupancyAsync(propertyId, f, t, ct).ConfigureAwait(false);
        var days = occ.ToDictionary(o => o.Date);
        var rn = 0;
        for (var d = f; d <= t; d = d.AddDays(1))
            rn += days.TryGetValue(d, out var o) ? StockRules.Occupied(d, bd, o.Live, o.History) : 0;
        var used = (await reads.ConsumptionAsync(propertyId, f, t, ct).ConfigureAwait(false)).ToDictionary(u => u.ItemCode, u => u.Used, StringComparer.Ordinal);
        var items = await reads.ItemsAsync(propertyId, ct).ConfigureAwait(false);
        var rows = items.OrderBy(i => i.Code, StringComparer.Ordinal).Select(i =>
        {
            var u = used.GetValueOrDefault(i.Code);
            var std = i.StdX100 ?? 0;
            int? variance = StockRules.VariancePercent(u, rn, std);
            return new ConsumptionRow(i.Code, i.Name, i.Kind, u, rn > 0 ? Math.Round((decimal)u / rn, 4) : 0m, std / 100m, variance, StockRules.FlagVariance(variance));
        }).ToList();
        return new ConsumptionReport(StayDates.ToWire(f), StayDates.ToWire(t), rn, rows);
    }

    private static StockItemView View(StockItemRecord i, IReadOnlyDictionary<string, int> rooms, int parSets, bool money)
    {
        var clean = StockRules.Clean(i.Stock);
        var total = StockRules.Total(i.Stock);
        var required = StockRules.ParRequired(i.Par, rooms, parSets);
        var below = StockRules.BelowMinimum(clean, i.MinQty);
        return new StockItemView(i.Code, i.Name, i.Kind, i.Unit, i.MinQty, i.ReorderQty, i.StdX100, i.Par,
            StockRules.AllLocations.Select(l => new StockLevelView(l, StockRules.Qty(i.Stock, l))).ToList(),
            clean, total, required, StockRules.ParPerSet(i.Par, rooms), StockRules.ParCoveragePercent(total, required),
            below, below ? StockRules.SuggestedReorder(clean, i.MinQty, i.ReorderQty) : 0, i.Supplier,
            money ? i.CostPaise : null, money ? total * i.CostPaise : null, money ? i.LaundryRatePaise : null, VersionToken.ToWire(i.Version));
    }

    private static DateOnly? OptDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, StayDates.WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation("Enter valid dates (yyyy-MM-dd).");
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        return (page, pageSize);
    }
}
