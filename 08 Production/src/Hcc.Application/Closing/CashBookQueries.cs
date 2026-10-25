using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Application.Access;

namespace Hcc.Application.Closing;

/// <summary>Reads the cash book's source records (drawer movements/shifts and the DEC-065 lock tables) for a range.</summary>
public interface ICashBookSource
{
    Task<CashBookData> LoadAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// Cash book for a day or a date range (prototype <c>cbDay</c>/<c>cbRange</c>), PERM-RPT-002 like the other closing reads. Lock/reopen stay in
/// <see cref="ClosingService"/>; a locked day refuses cash changes through <see cref="IBusinessDayGuard.EnsureCashBookOpenAsync"/> (DEC-065).
/// </summary>
public sealed class CashBookQueries(ICashBookSource source, IClosingStore closing, IAccessPolicy policy)
{
    public const int MaxRangeDays = 93;

    public async Task<CashBookRangeView> GetRangeAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) throw HccException.Validation($"Choose at most {MaxRangeDays} business dates.");
        var bd = await closing.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await source.LoadAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return CashBookCalculator.Compute(from, to, bd, data);
    }

    /// <summary>One day with its entries; NOT_FOUND when the date is in the future or has no cash activity and is not closed.</summary>
    public async Task<CashBookDayView> GetDayAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        var r = await GetRangeAsync(caller, date, date, ct).ConfigureAwait(false);
        return r.Days.Count == 1 ? r.Days[0] : throw new HccException(HccErrorCode.NOT_FOUND, "The cash book has no entry for that date.");
    }
}
