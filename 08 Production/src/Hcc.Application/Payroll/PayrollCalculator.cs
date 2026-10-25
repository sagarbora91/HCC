using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

/// <summary>Everything a month's payroll depends on, loaded in one transaction.</summary>
public sealed record MonthWorld(PayMonth Month, IReadOnlyList<StaffRow> Staff, IReadOnlyDictionary<long, StaffDays> Days, IReadOnlyList<AdvanceRow> Advances,
    IReadOnlyList<LockedCoverage> Coverage, IReadOnlyList<FnfRow> Fnfs)
{
    public StaffDays DaysOf(long staffId) => Days.TryGetValue(staffId, out var d) ? d : new StaffDays();

    /// <summary>The open advance recovered in this month (one open advance per staff member; prototype hrAdvanceFor).</summary>
    public AdvanceRow? OpenAdvance(long staffId) => Advances.Where(a => a.StaffId == staffId && a.Balance > 0 && a.FromMonth <= Month).OrderBy(a => a.AdvanceId).FirstOrDefault();

    public IEnumerable<(PayMonth Month, bool? Covered)> CoverageOf(long staffId) => Coverage.Where(c => c.StaffId == staffId).Select(c => (c.Month, c.Covered));
}

/// <summary>Month payroll over a loaded <see cref="MonthWorld"/> (prototype hrComputeRun / hrFnfCompute).</summary>
public static class PayrollCalculator
{
    public static async Task<MonthWorld> LoadAsync(IPayrollDb db, int propertyId, PayMonth month, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var staff = await db.AllStaffAsync(propertyId, ct).ConfigureAwait(false);
        var days = await db.DaysAsync(propertyId, staff.Select(s => s.StaffId).ToList(), month.First, month.Last, ct).ConfigureAwait(false);
        var advances = await db.AdvancesAsync(propertyId, ct).ConfigureAwait(false);
        var coverage = await db.LockedCoverageAsync(propertyId, month.EsicPeriodStart, month, ct).ConfigureAwait(false);
        var fnfs = await db.FnfsAsync(propertyId, ct).ConfigureAwait(false);
        return new MonthWorld(month, staff, days, advances, coverage, fnfs);
    }

    /// <summary>Staff settled full-and-final with the exit month's salary inside the settlement are not in that month's run.</summary>
    private static bool SettledFor(MonthWorld w, long staffId) =>
        w.Fnfs.Any(f => f.Status == "Settled" && f.StaffId == staffId && f.SalaryIncluded && f.Month == w.Month);

    public static (IReadOnlyList<PayLine> Lines, IReadOnlyList<string> Warnings) ComputeRun(MonthWorld w, PayrollRates rates)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(rates);
        var lines = new List<PayLine>();
        var warnings = new List<string>();
        foreach (var s in w.Staff.OrderBy(s => s.StaffId))
        {
            var st = s.Profile;
            if (!st.InMonth(w.Month) || SettledFor(w, st.StaffId)) continue;
            var c = PayrollRules.Counts(st, w.DaysOf(st.StaffId), w.Month);
            warnings.AddRange(PayrollRules.Warnings(st, c));
            var adv = w.OpenAdvance(st.StaffId);
            var covered = PayrollRules.EsicCovered(st, w.Month, rates, w.CoverageOf(st.StaffId));
            lines.Add(PayrollRules.ComputeLine(st, c, rates, adv is null ? null : (adv.InstallmentPaise, adv.Balance), covered));
        }

        return (lines, warnings);
    }

    /// <summary>F&amp;F figures for an exited staff member: the exit month's salary unless a locked run already paid it, the full outstanding advance.</summary>
    public static FnfFigures ComputeFnf(MonthWorld w, StaffRow staff, PayrollRates rates, bool exitMonthPaid, IReadOnlyList<FnfAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(staff);
        var st = staff.Profile;
        var lastDay = st.ExitedOn ?? throw new InvalidOperationException("Exit date required.");
        PayLine? line = null;
        if (!exitMonthPaid)
        {
            var c = PayrollRules.Counts(st, w.DaysOf(st.StaffId), w.Month);
            line = PayrollRules.ComputeLine(st, c, rates, null, PayrollRules.EsicCovered(st, w.Month, rates, w.CoverageOf(st.StaffId)));
        }

        var outstanding = w.Advances.Where(a => a.StaffId == st.StaffId).Sum(a => a.Balance);
        return FnfRules.Compute(st, lastDay, line, rates, outstanding, adjustments);
    }
}
