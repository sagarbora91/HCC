namespace Hcc.Domain.Payroll;

/// <summary>Leave balance for one tracked type (F-15: <see cref="Left"/> never goes below 0; <see cref="Over"/> = beyond a reduced entitlement).</summary>
public sealed record LeaveBalance(LeaveType Type, int Entitled, decimal Used, decimal Left, decimal Over);

/// <summary>Leave rules (SCC leave\index.html via the prototype): FY entitlements, used = approved + pending, capacity per department.</summary>
public static class LeaveRules
{
    public static readonly LeaveType[] TrackedTypes = [LeaveType.CL, LeaveType.SL, LeaveType.EL];

    /// <summary>Maximum calendar span of one request (prototype demo rule: 15 days).</summary>
    public const int MaxSpanDays = 15;

    /// <summary>Leave used (approved + pending) in the financial year containing <paramref name="date"/>, per type.</summary>
    public static decimal Used(IEnumerable<LeaveSpan> leaves, DateOnly date, LeaveType type, long exceptLeaveId = 0)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        var fy = FinancialYear.Containing(date);
        return leaves.Where(l => l.LeaveId != exceptLeaveId && l.Type == type && l.From >= fy.From && l.From <= fy.To && l.Holds).Sum(l => l.Days);
    }

    public static IReadOnlyList<LeaveBalance> Balance(LeavePolicy policy, IEnumerable<LeaveSpan> leaves, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var all = leaves.ToList();
        return TrackedTypes.Select(t =>
        {
            var used = Used(all, date, t);
            var ent = policy.Entitled(t);
            return new LeaveBalance(t, ent, used, Math.Max(0, ent - used), Math.Max(0, used - ent));
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

    /// <summary>Suggested earned-leave encashment: EL left × (monthly ÷ days in the exit month); a prefill only (CA item P13).</summary>
    public static long ElEncashment(StaffProfile st, decimal elLeft, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(st);
        if (st.PayType != PayType.Monthly) return 0;
        return PayMath.MulDiv(st.MonthlyPaise, (long)Math.Round(Math.Max(0, elLeft) * 2, MidpointRounding.AwayFromZero), 1, 2L * PayMonth.Of(lastDay).Days);
    }
}
