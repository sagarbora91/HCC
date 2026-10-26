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
                pay ? s.BankName : null, pay ? BankDetails.Mask(s.AccountNo) : null, s.UserId,
                PayrollFiles.WhatsAppNumber(s.Phone).Length > 0, pay ? s.Ifsc : null);
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
        await db.AuditDownloadAsync(propertyId, principalId, "HR_REGISTER_DOWNLOADED", PayrollService.Target("hr.run", run.RunId),
            $"{month.Label} payroll register CSV ({(run.Locked ? "locked" : "draft")}, {lines.Count} staff)", ct).ConfigureAwait(false);
        return new CsvFile($"payroll-register-{month}.csv", PayrollFiles.Register(month, run.Locked, lines, c => depts.GetValueOrDefault(c, c)));
    }

    public async Task<CsvFile> StatutoryCsvAsync(int propertyId, int principalId, PayMonth month, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await RunOfMonthAsync(db, propertyId, month, ct).ConfigureAwait(false);
        var lines = await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false);
        await db.AuditDownloadAsync(propertyId, principalId, "HR_STATUTORY_DOWNLOADED", PayrollService.Target("hr.run", run.RunId),
            $"{month.Label} PF / ESIC / PT statement CSV ({(run.Locked ? "locked" : "draft")})", ct).ConfigureAwait(false);
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
        return await BankFileAsync(db, propertyId, principalId, run, issueNo, ct).ConfigureAwait(false);
    }

    /// <summary>W4 G2: the latest bank file of the CURRENT lock (the one IssueBankFile just recorded) — the production app downloads it
    /// right after issuing, so the file carries the real account numbers frozen at lock while the screen only ever holds masked ones.</summary>
    public async Task<CsvFile> LatestBankCsvAsync(int propertyId, int principalId, long runId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var run = await db.RunAsync(propertyId, runId, false, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Payroll run not found.");
        if (!run.Locked) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The bank file is made after the pay-run is locked.");
        var file = (await db.BankFilesAsync(runId, ct).ConfigureAwait(false)).Where(f => f.LockSeq == run.LockSeq).OrderBy(f => f.IssueNo).LastOrDefault();
        if (file == default) throw new HccException(HccErrorCode.NOT_FOUND, "Bank file not found. Issue it first (IssueBankFile).");
        return await BankFileAsync(db, propertyId, principalId, run, file.IssueNo, ct).ConfigureAwait(false);
    }

    private static async Task<CsvFile> BankFileAsync(IPayrollReadDb db, int propertyId, int principalId, RunRow run, int issueNo, CancellationToken ct)
    {
        var lines = await db.RunLinesAsync(run.RunId, ct).ConfigureAwait(false);
        var bank = await db.BankSnapshotAsync(run.RunId, run.LockSeq, ct).ConfigureAwait(false);
        await db.AuditDownloadAsync(propertyId, principalId, "HR_BANK_FILE_DOWNLOADED", PayrollService.Target("hr.run", run.RunId),
            $"{run.Month.Label} bank transfer file {issueNo} downloaded · {lines.Count} staff · net Rs. {(lines.Sum(l => l.Net) / 100m).ToString("N2", CultureInfo.InvariantCulture)} (simulated — no money moves)", ct).ConfigureAwait(false);
        return new CsvFile($"bank-transfer-{run.Month}-{issueNo}.csv", PayrollFiles.Bank(run.Month, lines, bank, issueNo));
    }

    // ── HR desk board (W4-6) ──

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static RateVersionView RatesView(PayrollRates r) => new(r.From, r.BasicBps, r.HraBps, r.WashBps, r.PfEeBps, r.PfErBps, r.PfWageCap, r.EsicEeBps, r.EsicErBps,
        r.EsicCeiling, r.PtMaleExempt, r.PtMaleMid, r.PtMaleMidAmt, r.PtStd, r.PtFeb, r.PtFemaleExempt, r.OtBps, r.LateBlock,
        r.Rounding == StatutoryRounding.Rupee ? "rupee" : "paise", r.EsicPeriod);

    internal static BoardLineView LineView(PayLine l)
    {
        var c = l.Counts;
        return new BoardLineView(l.StaffId, l.Name, l.Dept, l.Designation, PayrollService.PayTypeWire(l.PayType), l.Structure.ToString(), l.Gender.ToString(), l.Base,
            new AttendanceCountsView(c.Ym, c.Days, c.P, c.L, c.H, c.HL, c.A, c.OFFW, c.Off, c.PaidLeave, c.LopLeave, c.Unmarked, c.Na, c.OtMin),
            l.LatePenaltyHalf, l.PayableDays, l.Earned, l.Basic, l.Hra, l.Washing, l.Special, l.Ot, l.Gross, l.PfWages, l.Pf, l.PfEr, l.EsicWages, l.Esic, l.EsicEr, l.Pt,
            l.Advance, l.Deductions, l.Net, l.Ctc, l.EsicCovered);
    }

    private static string MaskTo(string to) => to.Length <= 4 ? "••••" : "••••" + to[^4..];

    /// <summary>
    /// The HR desk board (W4-6): departments, leave policy, roster / attendance in [from, to] and every leave of the visible staff (all staff
    /// for PERM-HR-001 / PERM-HR-006 holders, otherwise the caller's own record, PERM-HR-004); payroll (rate versions, runs, advances, F&amp;F)
    /// for PERM-HR-006 only — everyone else gets only their OWN line of each LOCKED run, without other people's figures (DEC-061).
    /// Defaults: from = the 1st of the month six months before the business date, to = business date + 56 days (at most 400 days).
    /// </summary>
    public async Task<PayrollBoardView> GetBoardAsync(int propertyId, int principalId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var view = await Has(propertyId, principalId, PayrollPermissions.StaffView, ct: ct).ConfigureAwait(false);
        var payroll = await Has(propertyId, principalId, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        if (!view && !payroll) await policy.RequireAsync(propertyId, principalId, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        await using var db = await store.OpenReadAsync(ct).ConfigureAwait(false);
        var clock = await db.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var f = from ?? PayMonth.Of(clock.BusinessDate).First.AddMonths(-6);
        var t = to ?? clock.BusinessDate.AddDays(56);
        if (t < f) throw HccException.Validation("The board end date must be on or after its start date.");
        if (t.DayNumber - f.DayNumber > 400) throw HccException.Validation("The board covers at most 400 days.");

        var staff = await db.AllStaffAsync(propertyId, ct).ConfigureAwait(false);
        var me = staff.FirstOrDefault(s => s.UserId == principalId);
        var all = view || payroll;
        var visible = all ? staff.Select(s => s.StaffId).ToHashSet() : me is null ? [] : new HashSet<long> { me.StaffId };
        var depts = await db.DepartmentsAsync(propertyId, ct).ConfigureAwait(false);
        var leavePolicy = await db.LeavePolicyAsync(propertyId, ct).ConfigureAwait(false);
        var d = await db.BoardDetailAsync(propertyId, f, t, ct).ConfigureAwait(false);

        var roster = d.Roster.Where(x => visible.Contains(x.StaffId)).Select(x => new BoardRosterView(x.StaffId, x.Date, x.Shift.ToString())).ToList();
        var att = d.Attendance.Where(x => visible.Contains(x.StaffId))
            .Select(x => new BoardAttendanceView(x.StaffId, x.Date, x.Status.ToString(), x.OtMinutes, x.MarkedBy, Iso(x.MarkedUtc), x.Note)).ToList();
        var leaves = d.Leaves.Where(x => visible.Contains(x.Span.StaffId)).Select(x => new BoardLeaveView(x.Span.LeaveId, x.Span.StaffId, x.Span.Type.ToString(),
            x.Span.From, x.Span.To, x.Span.Half?.ToString(), x.Span.Days, x.Reason, x.Span.Status.ToString(), x.RequestedBy, Iso(x.RequestedUtc), x.DecidedBy,
            d.LeaveDecidedUtc.TryGetValue(x.Span.LeaveId, out var du) ? Iso(du) : null, x.Note, x.Version)).ToList();

        var runs = new List<BoardRunView>();
        foreach (var r in await db.RunsAsync(propertyId, ct).ConfigureAwait(false))
        {
            if (!payroll && (!r.Locked || me is null)) continue;
            var lines = await db.RunLinesAsync(r.RunId, ct).ConfigureAwait(false);
            if (!payroll) lines = lines.Where(l => l.StaffId == me!.StaffId).ToList();
            if (lines.Count == 0 && !payroll) continue;
            var times = d.Runs.FirstOrDefault(x => x.RunId == r.RunId);
            runs.Add(new BoardRunView(r.RunId, r.Month.ToString(), r.Locked ? "Locked" : "Draft", RatesView(r.Rates), payroll ? r.Warnings : [], r.LockSeq, r.Version,
                r.PreparedBy, times is null ? "" : Iso(times.PreparedUtc), r.LockedBy, times?.LockedUtc is { } lu ? Iso(lu) : null,
                lines.Select(LineView).ToList(),
                payroll ? d.Sends.Where(x => x.RunId == r.RunId).Select(x => new BoardSendView(x.StaffId, x.LockSeq, MaskTo(x.To), x.SentBy, Iso(x.SentUtc))).ToList() : [],
                payroll ? d.BankFiles.Where(x => x.RunId == r.RunId).Select(x => new BoardBankFileView(x.IssueNo, x.LockSeq, x.NetPaise, x.IssuedBy, Iso(x.IssuedUtc))).ToList() : [],
                payroll ? d.Unlocks.Where(x => x.RunId == r.RunId).Select(x => new BoardUnlockView(x.LockSeq, x.Reason,
                    x.Nets.ToDictionary(n => n.Key.ToString(CultureInfo.InvariantCulture), n => n.Value), x.BankFiles, x.UnlockedBy, Iso(x.UnlockedUtc))).ToList() : []));
        }

        IReadOnlyList<RateVersionView> rates = [];
        IReadOnlyList<BoardAdvanceView> advances = [];
        IReadOnlyList<BoardFnfView> fnf = [];
        if (payroll)
        {
            rates = (await db.RateVersionsAsync(propertyId, ct).ConfigureAwait(false)).Select(RatesView).ToList();
            advances = (await db.AdvancesAsync(propertyId, ct).ConfigureAwait(false)).Select(a =>
            {
                var x = d.Advances.FirstOrDefault(q => q.AdvanceId == a.AdvanceId);
                return new BoardAdvanceView(a.AdvanceId, a.StaffId, a.AmountPaise, a.InstallmentPaise, a.RecoveredPaise, a.Date, a.FromMonth.ToString(), x?.Reason ?? "",
                    x?.GivenBy ?? 0, d.Recoveries.Where(q => q.AdvanceId == a.AdvanceId).Select(q => new BoardRecoveryView(q.RunId, q.FnfId, q.AmountPaise)).ToList());
            }).ToList();
            fnf = (await db.FnfsAsync(propertyId, ct).ConfigureAwait(false)).Where(x => x.Status != "Discarded").Select(x =>
            {
                var m = d.Fnfs.First(q => q.FnfId == x.FnfId);
                return new BoardFnfView(x.FnfId, x.StaffId, m.LastDay, x.Month.ToString(), x.SalaryIncluded, x.Line is null ? null : LineView(x.Line), RatesView(m.Rates),
                    x.AdvanceOutstanding, x.Adjustments.Select(a => new FnfAdjustmentDto(a.Label, a.Addition ? "add" : "ded", a.AmountPaise)).ToList(), x.Earnings,
                    x.Recoveries, x.Net, x.Status, x.PreparedBy, Iso(m.PreparedUtc), m.DecidedBy, m.DecidedUtc is { } dd ? Iso(dd) : null, x.Version);
            }).ToList();
        }

        return new PayrollBoardView(clock.BusinessDate, f, t, all, payroll, depts.Select(x => new DepartmentView(x.Code, x.Name, x.LeaveCap)).ToList(),
            new LeavePolicyView(leavePolicy.Cl, leavePolicy.Sl, leavePolicy.El, leavePolicy.DefaultCap), roster, att, leaves, rates, runs, advances, fnf);
    }
}
