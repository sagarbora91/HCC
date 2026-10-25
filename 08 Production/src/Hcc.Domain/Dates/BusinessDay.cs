using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Dates;

/// <summary>
/// The property's business date is persisted state (<c>inv.property.business_date</c>), advanced only by an authorized
/// day close (DEC-042) — never derived from the wall clock. Event times come from an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed record BusinessDay(DateOnly Date, IReadOnlySet<DateOnly> ClosedDates)
{
    public bool IsClosed(DateOnly date) => ClosedDates.Contains(date);

    /// <summary>Late-write barrier: a write against a closed date is rejected with DAY_CLOSED.</summary>
    public void EnsureOpen(DateOnly date)
    {
        if (IsClosed(date)) throw new HccException(HccErrorCode.DAY_CLOSED, $"Business day {StayDates.ToWire(date)} is closed.");
    }

    /// <summary>The next business date after a successful close of <see cref="Date"/>.</summary>
    public BusinessDay AdvanceAfterClose()
    {
        var closed = new HashSet<DateOnly>(ClosedDates) { Date };
        return new BusinessDay(Date.AddDays(1), closed);
    }
}
