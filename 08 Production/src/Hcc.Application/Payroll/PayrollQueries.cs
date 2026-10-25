using System.Globalization;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

/// <summary>
/// Payroll and leave queries. Salaries only for PERM-HR-005 holders; payslips: payroll roles see all, everyone else only their OWN locked
/// payslip (DEC-061) — another person's payslip is NOT_FOUND (never reveals hidden records). Files are built from the run snapshot.
/// </summary>
public sealed class PayrollQueries(IPayrollStore store, IAccessPolicy policy)
{
    private Task<bool> Has(int property, int principal, string perm, AccessScope? scope = null, CancellationToken ct = default)
        => policy.HasAsync(property, principal, perm, scope, ct);

    private static string MaskPhone(string phone)
    {
        var d = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return d.Length == 0 ? "(none)" : "••••" + d[^Math.Min(4, d.Length)..];
    }

    public async Task<IReadOnlyList<StaffView>> GetStaffAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var view = await Has(propertyId, principalId, PayrollPermissions.StaffView, ct: ct).ConfigureAwait(false);
        var pay = await Has(propertyId, principalId, PayrollPermissions.Salary, ct: ct).ConfigureAwait(false);
        if (!view && !pay) await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var all = await db.AllStaffAsync(propertyId, ct).ConfigureAwait(false);
        return all.Where(s => view || pay || s.UserId == principalId).Select(s =>
        {
            var p = s.Profile;
            var showPay = pay || s.UserId == principalId;   // DEC-061: own pay is visible to its owner
            return new StaffView(p.StaffId, p.Name, p.Dept, p.Designation, p.DefaultShift.ToString(), p.WeeklyOff, p.Joined, p.ExitedOn, p.Active, s.UserId is not null,
                MaskPhone(s.Phone), s.Version,
                showPay ? PayrollService.PayTypeWire(p.PayType) : null, showPay ? p.MonthlyPaise : null, showPay ? p.DailyPaise : null,
                pay ? p.EffectiveGender.ToString() : null, pay ? p.EffectiveStructure.ToString() : null, pay ? p.PfOn : null, pay ? p.EsicOn : null,
                pay ? s.BankName : null, pay ? BankDetails.Mask(s.AccountNo) : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<LeaveBalanceView>> GetLeaveBalanceAsync(int propertyId, int principalId, long staffId, DateOnly? on, CancellationToken ct = default)
    {
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var st = await db.StaffAsync(propertyId, staffId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Staff member not found.");
        if (st.UserId != principalId && !await Has(propertyId, principalId, PayrollPermissions.StaffView, ct: ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.NOT_FOUND, "Staff member not found.");
        var date = on ?? (await db.PropertyAsync(propertyId, ct).ConfigureAwait(false)).BusinessDate;
        var days = (await db.DaysAsync(propertyId, [staffId], date, date, ct).ConfigureAwait(false)).GetValueOrDefault(staffId) ?? new StaffDays();
        var policyRow = await db.LeavePolicyAsync(propertyId, ct).ConfigureAwait(false);
        return LeaveRules.Balance(policyRow, days.Leaves, date).Select(b => new LeaveBalanceView(b.Type.ToString(), b.Entitled, b.Used, b.Left, b.Over)).ToList();
    }

    /// <summary>The rate version in force for a month (with its effective date) — Owner and Accountant.</summary>
    public async Task<PayrollRates> GetRatesAsync(int propertyId, int principalId, PayMonth month, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        return RateVersions.For(await db.RateVersionsAsync(propertyId, ct).ConfigureAwait(false), month);
    }

    /// <summary>F-09 payslip mark: 'sent' (this lock), 'resend' (sent before an unlock and the net changed), 'superseded' (sent before an unlock), else ''.</summary>
    internal static string PayslipMark(RunRow run, long staffId, long net, IReadOnlyList<(long StaffId, int LockSeq)> sends, IReadOnlyDictionary<int, IReadOnlyDictionary<long, long>> unlocks)
    {
        var current = run.Locked ? run.LockSeq : run.LockSeq + 1;
        if (run.Locked && sends.Any(s => s.StaffId == staffId && s.LockSeq == current)) return "sent";
        var earlier = sends.Where(s => s.StaffId == staffId && s.LockSeq < current).Select(s => s.LockSeq).DefaultIfEmpty(0).Max();
        if (earlier == 0) return "";
        return unlocks.TryGetValue(earlier, out var nets) && nets.TryGetValue(staffId, out var old) && old != net ? "resend" : "superseded";
    }

    internal static PayLineView View(PayLine l, string mark) => new(l.StaffId, l.Name, l.Dept, l.Designation, PayrollService.PayTypeWire(l.PayType), l.PayableDays,
        l.Earned, l.Basic, l.Hra, l.Washing, l.Special, l.Ot, l.Gross, l.PfWages, l.Pf, l.PfEr, l.EsicWages, l.Esic, l.EsicEr, l.Pt, l.Advance, l.Deductions, l.Net, l.Ctc,
        l.EsicCovered, mark);

    private static async Task<RunRow> RunOfMonthAsync(IPayrollDb db, int propertyId, PayMonth month, CancellationToken ct)
        => (await db.RunsAsync(propertyId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Month == month) ?? throw new HccException(HccErrorCode.NOT_FOUND, "No payroll run for this month.");

    public async Task<PayRunView> GetPayRunAsync(int propertyId, int principalId, PayMonth month, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await RunOfMonthAsync(db, propertyId, month, ct).ConfigureAwait(false);
        var lines = await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false);
        var sends = await db.PayslipSendsAsync(run.RunId, ct).ConfigureAwait(false);
        var unlocks = await db.UnlockNetsAsync(run.RunId, ct).ConfigureAwait(false);
        var files = await db.BankFilesAsync(run.RunId, ct).ConfigureAwait(false);
        return new PayRunView(run.RunId, month.ToString(), run.Locked ? "Locked" : "Draft", run.Rates.From, run.LockSeq, run.Version, run.Warnings,
            lines.Select(l => View(l, PayslipMark(run, l.StaffId, l.Net, sends, unlocks))).ToList(), lines.Sum(l => l.Net), files.Count);
    }

    private static string Pct(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>One payslip: payroll roles any; everyone else only their OWN payslip from a LOCKED run (DEC-061). Labels from the run's rates (F-14).</summary>
    public async Task<PayslipView> GetPayslipAsync(int propertyId, int principalId, long runId, long staffId, CancellationToken ct = default)
    {
        var payroll = await Has(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        if (!payroll) await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await db.RunAsync(propertyId, runId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Payslip not found.");
        if (!payroll)
        {
            var me = await db.StaffOfUserAsync(propertyId, principalId, ct).ConfigureAwait(false);
            if (me is null || me.StaffId != staffId || !run.Locked) throw new HccException(HccErrorCode.NOT_FOUND, "Payslip not found.");
        }

        var line = (await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false)).FirstOrDefault(l => l.StaffId == staffId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Payslip not found.");
        var sends = await db.PayslipSendsAsync(run.RunId, ct).ConfigureAwait(false);
        var unlocks = await db.UnlockNetsAsync(run.RunId, ct).ConfigureAwait(false);
        return new PayslipView(run.RunId, run.Month.ToString(), run.Locked ? "Locked" : "Draft", View(line, PayslipMark(run, staffId, line.Net, sends, unlocks)),
            $"Provident Fund ({Pct(run.Rates.PfEeBps)})", $"ESIC ({Pct(run.Rates.EsicEeBps)})", PayrollFiles.AmountInWords(line.Net), PayrollRates.Disclaimer);
    }

    /// <summary>The caller's own locked payslips, newest first (every role, DEC-061).</summary>
    public async Task<IReadOnlyList<PayslipView>> GetMyPayslipsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var me = await db.StaffOfUserAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (me is null) return [];
        var result = new List<PayslipView>();
        foreach (var run in (await db.RunsAsync(propertyId, ct).ConfigureAwait(false)).Where(r => r.Locked).OrderByDescending(r => r.Month))
        {
            var line = (await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false)).FirstOrDefault(l => l.StaffId == me.StaffId);
            if (line is null) continue;
            result.Add(new PayslipView(run.RunId, run.Month.ToString(), "Locked", View(line, ""), $"Provident Fund ({Pct(run.Rates.PfEeBps)})",
                $"ESIC ({Pct(run.Rates.EsicEeBps)})", PayrollFiles.AmountInWords(line.Net), PayrollRates.Disclaimer));
        }

        return result;
    }

    public async Task<CsvFile> RegisterCsvAsync(int propertyId, int principalId, PayMonth month, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await RunOfMonthAsync(db, propertyId, month, ct).ConfigureAwait(false);
        var depts = (await db.DepartmentsAsync(propertyId, ct).ConfigureAwait(false)).ToDictionary(d => d.Code, d => d.Name);
        var lines = await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false);
        return new CsvFile($"payroll-register-{month}.csv", PayrollFiles.Register(month, run.Locked, lines, c => depts.GetValueOrDefault(c, c)));
    }

    public async Task<CsvFile> StatutoryCsvAsync(int propertyId, int principalId, PayMonth month, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await RunOfMonthAsync(db, propertyId, month, ct).ConfigureAwait(false);
        var lines = await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false);
        return new CsvFile($"pf-esic-pt-{month}.csv", PayrollFiles.Statutory(month, run.Locked, run.Rates.From, lines));
    }

    /// <summary>An issued bank file of the CURRENT lock, built from the bank details frozen at lock (F-02); files of an undone lock are superseded.</summary>
    public async Task<CsvFile> BankCsvAsync(int propertyId, int principalId, long runId, int issueNo, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await db.RunAsync(propertyId, runId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Payroll run not found.");
        var file = (await db.BankFilesAsync(runId, ct).ConfigureAwait(false)).FirstOrDefault(f => f.IssueNo == issueNo);
        if (file == default) throw new HccException(HccErrorCode.NOT_FOUND, "Bank file not found. Issue it first (IssueBankFile).");
        if (!run.Locked || file.LockSeq != run.LockSeq) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This bank file belongs to a lock that was undone; it is superseded.");
        var lines = await db.RunLinesAsync(runId, ct).ConfigureAwait(false);
        var bank = await db.BankSnapshotAsync(runId, run.LockSeq, ct).ConfigureAwait(false);
        return new CsvFile($"bank-transfer-{run.Month}-{issueNo}.csv", PayrollFiles.Bank(run.Month, lines, bank, issueNo));
    }
}
