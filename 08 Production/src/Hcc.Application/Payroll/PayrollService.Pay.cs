using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

public sealed partial class PayrollService
{
    private static string Rs(long paise) => "₹" + (paise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));

    private static PayMonth? LastLocked(IReadOnlyList<RunRow> runs) => runs.Where(r => r.Locked).Select(r => (PayMonth?)r.Month).Max();

    // ── Advances ──

    public async Task<CommandResult<AdvanceResult>> GiveAdvanceAsync(CommandContext ctx, GiveAdvanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Advance, ct: ct).ConfigureAwait(false);
        if (req.AmountPaise is < 100 or > 100_000_000) throw HccException.Validation("Advance: ₹1 to ₹10,00,000.");
        if (req.InstallmentPaise < 0 || req.InstallmentPaise > req.AmountPaise)
            throw HccException.Validation("Monthly recovery must be between ₹0 (full recovery next payroll) and the advance amount.");
        var reason = Required(req.Reason, 1, 200, "Give a reason.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (dayGuard is not null) await dayGuard.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c2).ConfigureAwait(false); // P6 late-write barrier: DAY_CLOSED once the business date is closed
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            NotSelf(ctx, st, "give a salary advance");
            if (!st.Profile.Active) throw Invalid("This staff member is inactive.");
            var current = PayMonth.Of(clockInfo.BusinessDate);
            var from = req.FromMonth is null ? current : PayMonth.Parse(req.FromMonth);
            if (from < current) throw HccException.Validation("Recovery starts this month or later.");
            var advances = await db.AdvancesAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (advances.Any(a => a.StaffId == st.StaffId && a.Balance > 0)) throw Invalid("This staff member already has an open advance.");
            var id = await db.InsertAdvanceAsync(ctx.PropertyId, st.StaffId, req.AmountPaise, req.InstallmentPaise, clockInfo.BusinessDate, from, reason, ctx.PrincipalId, Now, c2)
                .ConfigureAwait(false);
            var text = $"{st.Profile.Name} · advance {Rs(req.AmountPaise)} · {(req.InstallmentPaise > 0 ? Rs(req.InstallmentPaise) + "/month" : "full recovery")} from {from.Label} (simulated bank transfer, not drawer cash)";
            await tx.AuditAsync("HR_ADVANCE_GIVEN", Target("hr.advance", id), text, c2).ConfigureAwait(false);
            await tx.PublishAsync("AdvanceGiven", id, new { advanceId = id, staffId = st.StaffId, amountPaise = req.AmountPaise, installmentPaise = req.InstallmentPaise, date = clockInfo.BusinessDate },
                c2).ConfigureAwait(false);
            return new AdvanceResult(id, st.StaffId, req.AmountPaise, req.InstallmentPaise, from.ToString(), text);
        }, ct).ConfigureAwait(false);
    }

    // ── Statutory rates (dated versions; F-05 carry-forward, F-06 default date) ──

    public async Task<CommandResult<RatesResult>> SetStatutoryRatesAsync(CommandContext ctx, SetStatutoryRatesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        StatutoryRounding? rounding = req.Rounding switch
        {
            null => null,
            "paise" => StatutoryRounding.Paise,
            "rupee" => StatutoryRounding.Rupee,
            _ => throw HccException.Validation("Rounding is paise or rupee."),
        };
        var change = new RateChange(req.BasicBps, req.HraBps, req.WashBps, req.PfEeBps, req.PfErBps, req.PfWageCap, req.EsicEeBps, req.EsicErBps, req.EsicCeiling,
            req.PtMaleExempt, req.PtMaleMid, req.PtMaleMidAmt, req.PtStd, req.PtFeb, req.PtFemaleExempt, req.OtBps, req.LateBlock, rounding, req.EsicPeriod);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var runs = await db.RunsAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var last = LastLocked(runs);
            var from = req.EffectiveFrom ?? RateVersions.DefaultFrom(clockInfo.BusinessDate, last);
            var versions = await db.RateVersionsAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var outcome = RateVersions.Save(versions, from, change, last);
            var before = versions.FirstOrDefault(v => v.From == from);
            await db.ReplaceRateVersionsAsync(ctx.PropertyId, from, outcome.Versions, before, outcome.Changed, outcome.Carried,
                ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            // F-06: completed months without a locked run that this version changes (their payroll must be recalculated).
            var past = new List<string>();
            for (var m = PayMonth.Of(from); m.Last < clockInfo.BusinessDate; m = m.Next)
                if (runs.FirstOrDefault(r => r.Month == m) is not { Locked: true }) past.Add(m.ToString());
            var note = outcome.Later.Count > 0
                ? $" · later version{(outcome.Later.Count == 1 ? "" : "s")} from {string.Join(", ", outcome.Later.Select(v => v.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}: "
                  + (outcome.Carried.Count > 0 ? "change carried forward (" + string.Join(", ", outcome.Carried) + ")" : "no change carried")
                  + (outcome.Kept.Count > 0 ? "; " + string.Join("; ", outcome.Kept) : "")
                : "";
            var text = $"Statutory rates from {from:yyyy-MM-dd} saved ({(outcome.Changed.Count > 0 ? string.Join(", ", outcome.Changed) : "no field changed")}; illustrative — CA to confirm) · locked runs keep their snapshot{note}"
                       + (past.Count > 0 ? $" · changes completed month{(past.Count == 1 ? "" : "s")} not yet locked: {string.Join(", ", past)} — recalculate" : "");
            await tx.AuditAsync("HR_RATES_SET", "hr.rates:" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Clip(text, 1000), c2).ConfigureAwait(false);
            return new RatesResult(from, outcome.Changed, outcome.Carried, outcome.Kept, past, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Pay-runs ──

    private static PayRunResult RunResult(long runId, PayMonth month, string status, IReadOnlyList<PayLine> lines, string version, IReadOnlyList<string> warnings, string notes)
        => new(runId, month.ToString(), status, lines.Count, lines.Sum(l => l.Net), version, warnings, notes);

    public async Task<CommandResult<PayRunResult>> PreparePayRunAsync(CommandContext ctx, PreparePayRunRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        var month = PayMonth.Parse(req.Month);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (month.Last >= clockInfo.BusinessDate) throw Invalid("Payroll can run only for a completed month.");
            var runs = await db.RunsAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var existing = runs.FirstOrDefault(r => r.Month == month);
            if (existing is { Locked: true }) throw Invalid("This payroll is locked and cannot be recalculated.");
            var rates = RateVersions.For(await db.RateVersionsAsync(ctx.PropertyId, c2).ConfigureAwait(false), month);
            var world = await PayrollCalculator.LoadAsync(db, ctx.PropertyId, month, c2).ConfigureAwait(false);
            var (lines, warnings) = PayrollCalculator.ComputeRun(world, rates);
            if (lines.Count == 0) throw HccException.Validation("No staff to pay for this month.");
            long runId;
            string version;
            string text;
            if (existing is not null)
            {
                runId = existing.RunId;
                version = await db.ReplaceDraftAsync(runId, rates, lines, warnings, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
                text = $"{month.Label} recalculated · {lines.Count} staff · net {Rs(lines.Sum(l => l.Net))}";
            }
            else
            {
                (runId, version) = await db.InsertRunAsync(ctx.PropertyId, month, rates, lines, warnings, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
                text = $"{month.Label} draft · {lines.Count} staff · net {Rs(lines.Sum(l => l.Net))}";
            }

            await tx.AuditAsync("HR_PAYROLL_PREPARED", Target("hr.run", runId), $"{text} · rates from {rates.From:yyyy-MM-dd}", c2).ConfigureAwait(false);
            return RunResult(runId, month, "Draft", lines, version, warnings, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Maker-checker lock: the preparer cannot lock; the stored draft must equal a fresh recalculation; recoveries post; bank details freeze (F-02).</summary>
    public async Task<CommandResult<PayRunResult>> LockPayRunAsync(CommandContext ctx, LockPayRunRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var run = await db.RunAsync(ctx.PropertyId, req.RunId, lockRow: true, c2).ConfigureAwait(false) ?? throw NotFound("Payroll run not found.");
            if (run.Locked) throw Invalid("This payroll is already locked.");
            if (run.PreparedBy == ctx.PrincipalId)
                throw Forbidden("The person who prepared the payroll cannot lock it. The other payroll user (Owner / Accountant) locks it.");
            var lines = await db.RunLinesAsync(run.RunId, c2).ConfigureAwait(false);
            var errors = PayrollRules.LockErrors(lines).ToList();
            if (errors.Count > 0) throw HccException.Validation("Fix before locking: " + string.Join("; ", errors) + ".");
            var rates = RateVersions.For(await db.RateVersionsAsync(ctx.PropertyId, c2).ConfigureAwait(false), run.Month);
            var world = await PayrollCalculator.LoadAsync(db, ctx.PropertyId, run.Month, c2).ConfigureAwait(false);
            var fresh = PayrollCalculator.ComputeRun(world, rates);
            if (rates != run.Rates || !fresh.Lines.SequenceEqual(lines))
                throw new HccException(Contracts.HccErrorCode.STALE_VERSION, "Attendance, leave, advances or rates changed after this draft. Recalculate before locking.");
            var (version, lockSeq) = await db.LockRunAsync(run.RunId, req.ExpectedVersion, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            foreach (var l in lines.Where(l => l.Advance > 0))
                if (world.OpenAdvance(l.StaffId) is { } a)
                    await db.RecordRecoveryAsync(a.AdvanceId, run.RunId, null, l.Advance, Now, c2).ConfigureAwait(false);
            var t = PayTotals.Of(lines);
            var text = $"{run.Month.Label} locked (lock {lockSeq}) · {t.Staff} staff · net {Rs(t.Net)} · bank details frozen · payslips released";
            await tx.AuditAsync("HR_PAYROLL_LOCKED", Target("hr.run", run.RunId), text, c2).ConfigureAwait(false);
            // Accounting (W2-3) books the salary journal from this (its PayrollPostedEvent contract); nothing is paid by HCC.
            var postedOn = (await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false)).BusinessDate;
            var other = t.Gross - t.Net - t.Pf - t.Esic - t.Pt - t.Advance; // gross = net + employee deductions
            await tx.PublishAsync(Contracts.Accounting.AccountingEvents.PayrollPosted, run.RunId, new Contracts.Accounting.PayrollPostedEvent(run.RunId, run.Month.ToString(),
                postedOn, t.Gross, t.PfEr, t.EsicEr, t.Pf, t.Esic, t.Pt, t.Advance, other, t.Net, lockSeq), c2).ConfigureAwait(false);
            return RunResult(run.RunId, run.Month, "Locked", lines, version, run.Warnings, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC unlockRun: Owner only, latest locked month only, with a reason; recoveries reversed; a bank file already issued needs confirmation (F-09).</summary>
    public async Task<CommandResult<PayRunResult>> UnlockPayRunAsync(CommandContext ctx, UnlockPayRunRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        var reason = Required(req.Reason, 5, 200, "Give the reason for unlocking (at least 5 characters).");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var run = await db.RunAsync(ctx.PropertyId, req.RunId, lockRow: true, c2).ConfigureAwait(false) ?? throw NotFound("Payroll run not found.");
            if (!run.Locked) throw Invalid("This payroll is not locked.");
            var runs = await db.RunsAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (runs.Any(r => r.Locked && r.Month > run.Month)) throw Invalid("A later month is locked. Unlock the latest month first.");
            var fnfs = await db.FnfsAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (fnfs.Any(f => f.Status != "Discarded" && f.Month >= run.Month)) throw Invalid("A full and final settlement was made for this month or later. It cannot be unlocked.");
            var files = (await db.BankFilesAsync(run.RunId, c2).ConfigureAwait(false)).Where(f => f.LockSeq == run.LockSeq).ToList();
            if (files.Count > 0 && !req.ConfirmBankFile)
                throw Invalid($"A bank transfer file for {run.Month.Label} was already generated ({files.Count}×). Unlocking may lead to paying twice — confirm to unlock.");
            var lines = await db.RunLinesAsync(run.RunId, c2).ConfigureAwait(false);
            var reversed = await db.ReverseRunRecoveriesAsync(run.RunId, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var version = await db.UnlockRunAsync(run.RunId, req.ExpectedVersion, run.LockSeq, reason, lines.ToDictionary(l => l.StaffId, l => l.Net), files.Count,
                ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var sent = (await db.PayslipSendsAsync(run.RunId, c2).ConfigureAwait(false)).Count(s => s.LockSeq == run.LockSeq);
            var text = $"{run.Month.Label} unlocked · {reversed} advance recover{(reversed == 1 ? "y" : "ies")} reversed · payslips withdrawn"
                       + (sent > 0 ? $" · {sent} sent payslip{(sent == 1 ? "" : "s")} marked superseded" : "")
                       + (files.Count > 0 ? " · bank file already issued — next file must not pay twice" : "") + " · reason: " + reason;
            await tx.AuditAsync("HR_PAYROLL_UNLOCKED", Target("hr.run", run.RunId), Clip(text, 1000), c2).ConfigureAwait(false);
            // Accounting (W3-b) books the exact reversal of this lock's PayrollPosted voucher; a bank file's payment voucher is kept and flagged.
            var unlockedOn = (await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false)).BusinessDate;
            await tx.PublishAsync(Contracts.Accounting.AccountingEvents.PayRunUnlocked, run.RunId,
                new Contracts.Accounting.PayRunUnlockedEvent(run.RunId, run.Month.ToString(), run.LockSeq, files.Count, unlockedOn), c2).ConfigureAwait(false);
            return RunResult(run.RunId, run.Month, "Draft", lines, version, run.Warnings, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Records a bank transfer file for the current lock (simulated — HCC moves no money); a repeat, including after an unlock, needs confirmation (F-09).</summary>
    public async Task<CommandResult<BankFileResult>> IssueBankFileAsync(CommandContext ctx, IssueBankFileRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var run = await db.RunAsync(ctx.PropertyId, req.RunId, lockRow: true, c2).ConfigureAwait(false) ?? throw NotFound("Payroll run not found.");
            if (!run.Locked) throw Invalid("The bank file is made after the pay-run is locked.");
            var prior = await db.BankFilesAsync(run.RunId, c2).ConfigureAwait(false);
            if (prior.Count > 0 && !req.ConfirmReissue)
                throw Invalid($"A bank file for {run.Month.Label} was already generated (net {Rs(prior[^1].Net)}). Paying this file in full again may pay twice — confirm to generate it again.");
            var net = (await db.RunLinesAsync(run.RunId, c2).ConfigureAwait(false)).Sum(l => l.Net);
            var issueNo = prior.Count + 1;
            await db.InsertBankFileAsync(run.RunId, issueNo, run.LockSeq, net, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            if (!prior.Any(f => f.LockSeq == run.LockSeq))
            {
                // The first bank file of this lock is the salary payment Accounting books (PayrollPaidEvent); a re-issue is not a second payment.
                var paidOn = (await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false)).BusinessDate;
                await tx.PublishAsync(Contracts.Accounting.AccountingEvents.PayrollPaid, run.RunId, new Contracts.Accounting.PayrollPaidEvent(run.RunId, paidOn, "Bank transfer", net,
                    $"Bank file {issueNo} · {run.Month.Label}", run.LockSeq), c2).ConfigureAwait(false);
            }
            var text = $"{run.Month.Label} bank transfer file {issueNo} · net {Rs(net)}{(prior.Count > 0 ? $" · REISSUE (earlier file {Rs(prior[^1].Net)})" : "")} (simulated — no money moves)";
            await tx.AuditAsync("HR_BANK_FILE", Target("hr.run", run.RunId), text, c2).ConfigureAwait(false);
            return new BankFileResult(run.RunId, issueNo, net, prior.Count > 0, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>DEC-056: simulated WhatsApp send of a locked payslip (nothing leaves the server).</summary>
    public async Task<CommandResult<PayslipSendResult>> SendPayslipAsync(CommandContext ctx, SendPayslipRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Run, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var run = await db.RunAsync(ctx.PropertyId, req.RunId, lockRow: false, c2).ConfigureAwait(false) ?? throw NotFound("Payroll run not found.");
            if (!run.Locked) throw Invalid("Payslips are sent after the pay-run is locked.");
            var line = (await db.RunLinesAsync(run.RunId, c2).ConfigureAwait(false)).FirstOrDefault(l => l.StaffId == req.StaffId) ?? throw NotFound("This staff member is not in the run.");
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: false, c2).ConfigureAwait(false);
            var to = PayrollFiles.WhatsAppNumber(st.Phone);
            if (to.Length == 0) throw HccException.Validation($"{st.Profile.Name} has no usable mobile number.");
            await db.InsertPayslipSendAsync(run.RunId, run.LockSeq, st.StaffId, to, line.Net, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var masked = "+" + to[..2] + " •••••• " + to[^4..];
            var text = $"Payslip {run.Month.Label} · {st.Profile.Name} · WhatsApp to {masked} (simulated — no message sent)";
            await tx.AuditAsync("HR_PAYSLIP_SENT", Target("hr.run", run.RunId), text, c2).ConfigureAwait(false);
            return new PayslipSendResult(run.RunId, st.StaffId, masked, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Full and final (maker-checker, F-12, DEC-066) ──

    private static FnfResult FnfResultOf(long id, long staffId, string status, FnfFigures f, string version, string notes)
        => new(id, staffId, status, f.Net, f.Earnings, f.Recoveries, f.SalaryIncluded, version, notes);

    private static async Task<(MonthWorld World, PayrollRates Rates, bool Paid)> FnfWorldAsync(IPayrollDb db, int propertyId, StaffRow st, CancellationToken ct)
    {
        var month = PayMonth.Of(st.Profile.ExitedOn!.Value);
        var runs = await db.RunsAsync(propertyId, ct).ConfigureAwait(false);
        var paid = false;
        if (runs.FirstOrDefault(r => r.Month == month && r.Locked) is { } locked)
            paid = (await db.RunLinesAsync(locked.RunId, ct).ConfigureAwait(false)).Any(l => l.StaffId == st.StaffId);
        var rates = RateVersions.For(await db.RateVersionsAsync(propertyId, ct).ConfigureAwait(false), month);
        return (await PayrollCalculator.LoadAsync(db, propertyId, month, ct).ConfigureAwait(false), rates, paid);
    }

    public async Task<CommandResult<FnfResult>> PrepareFnfAsync(CommandContext ctx, PrepareFnfRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Fnf, amount: 0, ct: ct).ConfigureAwait(false);
        var adj = (req.Adjustments ?? []).Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Label) && a.AmountPaise != 0).ToList();
        if (adj.Count > FnfRules.MaxAdjustments) throw HccException.Validation($"Up to {FnfRules.MaxAdjustments} adjustments.");
        foreach (var a in adj)
        {
            if (a.Kind is not ("add" or "ded")) throw HccException.Validation("Adjustment is an addition or a deduction.");
            if (a.AmountPaise is <= 0 or > FnfRules.AdjustmentMax) throw HccException.Validation($"Enter a valid adjustment amount (maximum {Rs(FnfRules.AdjustmentMax)} each).");
        }

        var clean = adj.Select(a => new FnfAdjustment(Clip(a.Label.Trim(), 80), a.Kind == "add", a.AmountPaise)).ToList();
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (IsOwn(ctx, st)) throw Forbidden("You cannot settle your own account.");
            if (st.Profile.Active || st.Profile.ExitedOn is null) throw Invalid("Record the exit in the staff master first.");
            var prev = (await db.FnfsAsync(ctx.PropertyId, c2).ConfigureAwait(false)).FirstOrDefault(f => f.StaffId == st.StaffId && f.Status != "Discarded");
            if (prev?.Status == "Settled") throw Invalid("Full and final is already done for this staff member.");
            if (prev is not null) throw Invalid("A draft settlement is already waiting for confirmation — confirm or discard it first.");
            var (world, rates, paid) = await FnfWorldAsync(db, ctx.PropertyId, st, c2).ConfigureAwait(false);
            var f = PayrollCalculator.ComputeFnf(world, st, rates, paid, clean);
            var (id, version) = await db.InsertFnfAsync(new NewFnf(ctx.PropertyId, st.StaffId, f, clean, ctx.PrincipalId, Now), c2).ConfigureAwait(false);
            var text = $"{st.Profile.Name} · full and final DRAFT · {(f.Net < 0 ? "recoverable from employee " + Rs(-f.Net) : "net payable " + Rs(f.Net))} · to be confirmed by "
                       + (f.Additions > FnfRules.OwnerAbove ? $"the Owner (additions above {Rs(FnfRules.OwnerAbove)})" : "the other payroll user");
            await tx.AuditAsync("HR_FNF_PREPARED", Target("hr.fnf", id), text, c2).ConfigureAwait(false);
            return FnfResultOf(id, st.StaffId, "Draft", f, version, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>F-12 checker: another payroll user (never the maker, never your own account) confirms; figures must match a fresh computation; the advance is written off.</summary>
    public async Task<CommandResult<FnfResult>> ConfirmFnfAsync(CommandContext ctx, DecideFnfRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Fnf, amount: 0, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var row = await db.FnfAsync(ctx.PropertyId, req.FnfId, lockRow: true, c2).ConfigureAwait(false) ?? throw NotFound("Settlement not found.");
            if (row.Status != "Draft") throw Invalid("This settlement is already decided.");
            if (row.PreparedBy == ctx.PrincipalId)
                throw Forbidden("The person who prepared the settlement cannot confirm it. The other payroll user (Owner / Accountant) confirms it.");
            var st = await RequireStaffAsync(db, ctx, row.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (IsOwn(ctx, st)) throw Forbidden("You cannot confirm your own settlement.");
            // DEC-066: additions above ₹1,00,000 need the Owner — the Accountant's PERM-HR-008 limit (APPROVAL_REQUIRED above it).
            await Require(ctx, PayrollPermissions.Fnf, amount: row.Additions, ct: c2).ConfigureAwait(false);
            var (world, rates, paid) = await FnfWorldAsync(db, ctx.PropertyId, st, c2).ConfigureAwait(false);
            var f = PayrollCalculator.ComputeFnf(world, st, rates, paid, row.Adjustments);
            if (f.Net != row.Net || f.Earnings != row.Earnings || f.Recoveries != row.Recoveries || f.AdvanceOutstanding != row.AdvanceOutstanding || f.Line != row.Line)
                throw new HccException(Contracts.HccErrorCode.STALE_VERSION, "Attendance, advances or rates changed after this draft. Discard it and prepare the settlement again.");
            var version = await db.DecideFnfAsync(row.FnfId, req.ExpectedVersion, "Settled", ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            foreach (var a in world.Advances.Where(a => a.StaffId == st.StaffId && a.Balance > 0))
                await db.RecordRecoveryAsync(a.AdvanceId, null, row.FnfId, a.Balance, Now, c2).ConfigureAwait(false);
            var text = $"{st.Profile.Name} · full and final confirmed · {(f.Net < 0 ? "recoverable from employee " + Rs(-f.Net) : "net payable " + Rs(f.Net))} (simulated transfer)";
            await tx.AuditAsync("HR_FNF_SETTLED", Target("hr.fnf", row.FnfId), text, c2).ConfigureAwait(false);
            await tx.PublishAsync("FnfSettled", row.FnfId, new { fnfId = row.FnfId, staffId = st.StaffId, month = f.Month.ToString(), earningsPaise = f.Earnings, recoveriesPaise = f.Recoveries, netPaise = f.Net, advanceWrittenOffPaise = f.AdvanceOutstanding },
                c2).ConfigureAwait(false);
            return FnfResultOf(row.FnfId, st.StaffId, "Settled", f, version, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FnfResult>> DiscardFnfAsync(CommandContext ctx, DecideFnfRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.Fnf, amount: 0, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var row = await db.FnfAsync(ctx.PropertyId, req.FnfId, lockRow: true, c2).ConfigureAwait(false) ?? throw NotFound("Settlement not found.");
            if (row.Status != "Draft") throw Invalid("A confirmed settlement cannot be discarded.");
            var version = await db.DecideFnfAsync(row.FnfId, req.ExpectedVersion, "Discarded", ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var text = $"full and final draft {row.FnfId} discarded";
            await tx.AuditAsync("HR_FNF_DISCARDED", Target("hr.fnf", row.FnfId), text, c2).ConfigureAwait(false);
            return new FnfResult(row.FnfId, row.StaffId, "Discarded", row.Net, row.Earnings, row.Recoveries, row.SalaryIncluded, version, text);
        }, ct).ConfigureAwait(false);
    }
}
