namespace Hcc.Domain.Payroll;

/// <summary>Leave balance for one tracked category (F-15: <see cref="Left"/> never goes below 0; <see cref="Over"/> = approved days beyond the entitlement,
/// which SCC allows after its warning).</summary>
public sealed record LeaveBalance(LeaveType Type, decimal Entitled, decimal Used, decimal Left, decimal Over);

/// <summary>Leave balances (SCC leave\index.html, in full since DEC-116): entitlement = SCC entFor (override, else the saved default); used = SCC fyUsage
/// (APPROVED days, each record by its own date in the financial year). No carry-forward (SCC starts every year at the quota) and no span limit.</summary>
public static class LeaveRules
{
    /// <summary>SCC TRACKED_CATS (CL SL EL).</summary>
    public static readonly LeaveType[] TrackedTypes = LeaveType.Tracked;

    /// <summary>Approved leave days in the financial year containing <paramref name="date"/> for one category (SCC fyUsage).</summary>
    public static decimal Used(IEnumerable<LeaveSpan> leaves, DateOnly date, LeaveType type, long exceptLeaveId = 0)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        var fy = FinancialYear.Containing(date);
        return leaves.Where(l => l.LeaveId != exceptLeaveId && l.Type == type && l.Status == LeaveStatus.Approved && l.Date >= fy.From && l.Date <= fy.To).Sum(l => l.Days);
    }

    /// <summary>Days left for a tracked category: entitlement − approved use.</summary>
    public static decimal Available(LeavePolicy policy, long staffId, IReadOnlyList<LeaveSpan> leaves, DateOnly date, LeaveType type, long exceptLeaveId = 0)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.EntFor(staffId).Of(type) - Used(leaves, date, type, exceptLeaveId);
    }

    public static IReadOnlyList<LeaveBalance> Balance(LeavePolicy policy, long staffId, IEnumerable<LeaveSpan> leaves, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var all = leaves.ToList();
        var ent = policy.EntFor(staffId);
        return policy.TrackedTypes().Select(t =>
        {
            var used = Used(all, date, t);
            var e = ent.Of(t);
            return new LeaveBalance(t, e, used, Math.Max(0, e - used), Math.Max(0, used - e));
        }).ToList();
    }
}

public sealed record FnfAdjustment(string Label, bool Addition, long AmountPaise);

/// <summary>
/// Full-and-final figures (prototype hrFnfCompute). <see cref="Net"/> may be negative: recoverable from the employee.
/// <see cref="AdvanceOutstanding"/> = the open advance balance; <see cref="AdvanceRecovered"/> = the part this settlement recovers (DEC-074).
/// </summary>
public sealed record FnfFigures(DateOnly LastDay, PayMonth Month, bool SalaryIncluded, PayLine? Line, PayrollRates Rates, long AdvanceOutstanding,
    long Additions, long Earnings, long Recoveries, long Net, long AdvanceRecovered)
{
    /// <summary>The advance this settlement does not cover: stays an open advance owed by the leaver.</summary>
    public long AdvanceLeftOpen => AdvanceOutstanding - AdvanceRecovered;

    public long Deductions => Recoveries - Statutory - AdvanceRecovered;

    /// <summary>Employee PF + ESIC + PT of the exit-month line (0 when that month was paid in a locked run).</summary>
    public long Statutory => Line is { } l ? l.Pf + l.Esic + l.Pt : 0;
}

/// <summary>Full and final settlement (SCC fnfAssemble / fnfCompose; review F-12; DEC-066 limits).</summary>
public static class FnfRules
{
    /// <summary>DEC-066: each adjustment ≤ ₹2,00,000.</summary>
    public const long AdjustmentMax = 20_000_000;

    /// <summary>DEC-066: additions above ₹1,00,000 in total need the Owner as checker (seeded as the Accountant's PERM-HR-008 limit).</summary>
    public const long OwnerAbove = 10_000_000;

    public const int MaxAdjustments = 4;

    /// <summary>
    /// Final-month salary (unless already paid in a locked run) + additions − statutory deductions − deductions − the outstanding advance,
    /// but the advance only up to what the settlement covers (DEC-074 / A04-06): recovered = the settlement before the advance
    /// (earnings − statutory − deductions), clamped to 0…outstanding. The rest of the advance stays open (still owed by the leaver); a
    /// negative net (deductions beyond the earnings) is recoverable from the employee and recovers no advance.
    /// </summary>
    public static FnfFigures Compute(StaffProfile st, DateOnly lastDay, PayLine? unpaidMonthLine, PayrollRates rates, long advanceOutstanding,
        IReadOnlyList<FnfAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(adjustments);
        var add = adjustments.Where(a => a.Addition).Sum(a => a.AmountPaise);
        var ded = adjustments.Where(a => !a.Addition).Sum(a => a.AmountPaise);
        var earnings = (unpaidMonthLine?.Gross ?? 0) + add;
        var statutory = unpaidMonthLine is { } l ? l.Pf + l.Esic + l.Pt : 0;
        var advance = Math.Clamp(earnings - statutory - ded, 0, Math.Max(0, advanceOutstanding));
        var recoveries = statutory + advance + ded;
        return new FnfFigures(lastDay, PayMonth.Of(lastDay), unpaidMonthLine is not null, unpaidMonthLine, rates, advanceOutstanding, add, earnings, recoveries,
            earnings - recoveries, advance);
    }
}
