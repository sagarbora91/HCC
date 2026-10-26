using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Domain.Billing;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Reporting;

namespace Hcc.Application.Closing;

/// <summary>
/// RunBusinessDayClose (contracts §3) as persisted, resumable steps — preflight → room nights (P4 PostRoomNight, idempotent per
/// stay) → shift/cash checks → close + immutable snapshot (one transaction that raises the late-write barrier first) → next
/// business date. Ports the prototype stream F close (<c>CLOSE_RUN</c>, <c>DAY_CLOSE</c>, <c>NEXT_DAY</c> in
/// <c>03 Prototype/src/services/mock/handlers/closing.ts</c>) to multi-transaction production steps. One close per property runs
/// at a time (session lease, released if the process dies); a rerun after a crash continues where the last attempt stopped.
/// Also the DEC-065 cash-book day lock / Owner reopen and the closing read models.
/// </summary>
/// <param name="runner">The platform runner (not the business-day guarded one: the close writes on the day it closes).</param>
public sealed class ClosingService(ICommandRunner runner, IClosingStore store, IReportSource reports, IAccessPolicy policy, BillingService billing,
    ICloseStepObserver observer, TimeProvider clock, Hcc.Application.Pos.IPosCloseGate? pos = null)
{
    public const string OpRun = "RunBusinessDayClose";
    public const string OpPostNights = "PostRoomNights";
    public const int MaxRecordDays = 100;
    public const string OpLockCashBook = "LockCashBookDay";
    public const string OpReopenCashBook = "ReopenCashBookDay";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Target(DateOnly d) => "day:" + Iso(d);

    private static CommandContext Ctx(Caller c, string op, string key) => new(c.PropertyId, c.PrincipalId, op, key);

    private sealed record AttemptResult(long RunId, int Attempt, string Status, string CurrentStep);

    private sealed record StepResult(bool Blocked, IReadOnlyList<PreflightCheck> Blockers);

    private sealed record CloseResult(long SnapshotId);

    private sealed record NextDayResult(DateOnly BusinessDate, bool Advanced);

    // ── RunBusinessDayClose ──

    public async Task<CommandResult<CloseRunResult>> RunBusinessDayCloseAsync(Caller caller, string idempotencyKey, RunBusinessDayCloseRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        Ctx(caller, OpRun, idempotencyKey).Validate();
        var runRef = CloseRules.ValidateRunRef(req.RunId);
        var date = req.BusinessDate;
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.Close, ct: ct).ConfigureAwait(false);

        var existing = await store.FindRunAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false);
        if (existing?.Status == CloseRunStatus.Completed) return new(await ResultAsync(existing, ct).ConfigureAwait(false), true);

        await using var lease = await store.TryAcquireRunLeaseAsync(caller.PropertyId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "A day close is already running for this property. Wait for it to finish, then reload.");

        var attempt = await StartAttemptAsync(caller, date, runRef, idempotencyKey, ct).ConfigureAwait(false);
        if (attempt.Status == CloseRunStatus.Completed)
            return new(await ResultAsync((await store.FindRunAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false))!, ct).ConfigureAwait(false), true);

        if (attempt.Status != CloseRunStatus.Closed)
        {
            await PreflightStepAsync(caller, date, attempt, ct).ConfigureAwait(false);
            await NightsStepAsync(caller, date, attempt, ct).ConfigureAwait(false);
            await CashStepAsync(caller, date, attempt, ct).ConfigureAwait(false);
            await CloseAndSnapshotAsync(caller, date, attempt, ct).ConfigureAwait(false);
        }

        await NextDayStepAsync(caller, date, attempt, ct).ConfigureAwait(false);
        var run = (await store.FindRunAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false))!;
        return new(await ResultAsync(run, ct).ConfigureAwait(false), false);
    }

    /// <summary>
    /// PostRoomNights: the close's night step on its own (prototype CLOSE_RUN / NIGHT_POST). Only the current, still open business date;
    /// each stay goes through P4 PostRoomNight (idempotent per folio and service date), so a repeat, or the later close, posts nothing twice.
    /// </summary>
    public async Task<CommandResult<PostRoomNightsResult>> PostRoomNightsAsync(Caller caller, string idempotencyKey, PostRoomNightsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        Ctx(caller, OpPostNights, idempotencyKey).Validate();
        var date = req.BusinessDate;
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.Close, ct: ct).ConfigureAwait(false);
        var bd = await store.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        if (date != bd)
        {
            if (date < bd || await store.IsClosedAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false)) throw CloseRules.DayClosed(date);
            throw HccException.Validation($"Only the current business date ({Iso(bd)}) can be posted.");
        }

        if (await store.IsClosedAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false)) throw CloseRules.DayClosed(date);
        var targets = (await store.PreflightDataAsync(null, caller.PropertyId, date, Now, ct).ConfigureAwait(false)).Nights;
        var posted = 0;
        foreach (var t in targets.Where(t => !t.HasNight))
        {
            var r = await billing.PostRoomNightAsync(caller, $"nights:{date:yyyyMMdd}:{t.StayId}", new PostRoomNightRequest(t.StayId, date), ct).ConfigureAwait(false);
            if (r.Value.Created && !r.Replayed) posted++;
        }

        return new(new PostRoomNightsResult(date, targets.Count, posted, targets.Count - posted), false);
    }

    /// <summary>The current business date for any signed-in user (screens need it before anything else); no permission beyond the session.</summary>
    public async Task<BusinessDateView> GetBusinessDateAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var d = await store.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        return new BusinessDateView(d, await store.IsClosedAsync(null, caller.PropertyId, d, ct).ConfigureAwait(false));
    }

    /// <summary>The stored closing records of a date range in one read (the closing history screens).</summary>
    public async Task<ClosingRecordsView> GetRecordsAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxRecordDays) throw HccException.Validation($"Choose at most {MaxRecordDays} business dates.");
        var rows = await store.ReadSnapshotsAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return new ClosingRecordsView(from, to, rows.Select(s => new ClosingSnapshotView(s.SnapshotId, Convert.ToHexString(s.Sha256), ParseRecord(s.Json))).ToList());
    }

    private async Task<AttemptResult> StartAttemptAsync(Caller caller, DateOnly date, string runRef, string idempotencyKey, CancellationToken ct)
    {
        // A fresh key per attempt: every start or resume is recorded (attempts > 1 = resumed, shown on the closing record).
        var key = "cls-attempt:" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" + Guid.NewGuid().ToString("N");
        var r = await runner.RunAsync(Ctx(caller, "CloseRunAttempt", key), new { businessDate = date, runRef, idempotencyKey }, async (tx, ct2) =>
        {
            var bd = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
            var run = await store.FindRunAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false);
            var now = Now;
            if (run is null)
            {
                if (date != bd)
                {
                    if (date < bd || await store.IsClosedAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false)) throw CloseRules.DayClosed(date);
                    throw HccException.Validation($"Only the current business date ({Iso(bd)}) can be closed.");
                }

                run = await store.InsertRunAsync(tx, caller.PropertyId, date, runRef, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("DAY_CLOSE_STARTED", Target(date), $"run {run.RunId} · ref {runRef} · request {idempotencyKey}", ct2).ConfigureAwait(false);
                return new AttemptResult(run.RunId, 1, run.Status, run.CurrentStep);
            }

            if (run.Status == CloseRunStatus.Completed) return new AttemptResult(run.RunId, run.Attempts, run.Status, run.CurrentStep);
            var status = run.Status == CloseRunStatus.Closed ? CloseRunStatus.Closed : CloseRunStatus.Running;
            await store.UpdateRunAsync(tx, run.RunId, status, run.CurrentStep, 1, runRef, caller.PrincipalId, now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("DAY_CLOSE_RESUMED", Target(date), $"run {run.RunId} · attempt {run.Attempts + 1} · from step {run.CurrentStep} · ref {runRef}", ct2).ConfigureAwait(false);
            return new AttemptResult(run.RunId, run.Attempts + 1, status, run.CurrentStep);
        }, ct).ConfigureAwait(false);
        return r.Value;
    }

    /// <summary>Preflight data from the closing store plus what POS holds open (<see cref="Hcc.Application.Pos.IPosCloseGate"/>; absent POS adds nothing).</summary>
    private async Task<PreflightData> PreflightAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
    {
        var data = await store.PreflightDataAsync(tx, propertyId, date, Now, ct).ConfigureAwait(false);
        if (pos is null) return data;
        var list = await pos.ListAsync(propertyId, date, ct).ConfigureAwait(false);
        return data with
        {
            PosChecks = list.OpenChecks.Select(i => new PreflightItem(i.Label, i.Ref)).ToList(),
            PosVoids = list.PendingVoids.Select(i => new PreflightItem(i.Label, i.Ref)).ToList(),
        };
    }

    private string StepKey(AttemptResult a, CloseStep step) => $"cls:{a.RunId}:{CloseSteps.Code(step)}:{a.Attempt}";

    private async Task<StepResult> CheckStepAsync(Caller caller, DateOnly date, AttemptResult a, CloseStep step, IReadOnlySet<string> blocking, CloseStep next,
        CancellationToken ct)
    {
        var r = await runner.RunAsync(Ctx(caller, "Close" + char.ToUpperInvariant(CloseSteps.Code(step)[0]) + CloseSteps.Code(step)[1..], StepKey(a, step)),
            new { a.RunId, a.Attempt, step = CloseSteps.Code(step) }, async (tx, ct2) =>
        {
            var checks = CloseRules.Evaluate(await PreflightAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false));
            var blockers = CloseRules.Blockers(checks, blocking);
            var detail = CanonicalJson.Serialize(new
            {
                checks = checks.Select(c => new { id = c.Id, items = c.Items.Count }).ToArray(),
                blocking = blockers.Select(b => b.Id).ToArray(),
            });
            var status = blockers.Count > 0 ? "Blocked" : "Done";
            await store.UpsertStepAsync(tx, a.RunId, step, status, a.Attempt, detail, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await store.UpdateRunAsync(tx, a.RunId, blockers.Count > 0 ? CloseRunStatus.Blocked : CloseRunStatus.Running,
                CloseSteps.Code(blockers.Count > 0 ? step : next), null, null, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("DAY_CLOSE_STEP", Target(date), $"{CloseSteps.Code(step)} · {status}" + (blockers.Count > 0 ? " · " + string.Join(", ", blockers.Select(b => b.Id)) : ""), ct2).ConfigureAwait(false);
            return new StepResult(blockers.Count > 0, blockers);
        }, ct).ConfigureAwait(false);
        if (r.Value.Blocked) throw CloseRules.Blocked(date, r.Value.Blockers);
        return r.Value;
    }

    private async Task PreflightStepAsync(Caller caller, DateOnly date, AttemptResult a, CancellationToken ct)
    {
        await observer.OnStepAsync(caller.PropertyId, date, "preflight", null, ct).ConfigureAwait(false);
        await CheckStepAsync(caller, date, a, CloseStep.Preflight, CloseRules.PreflightBlocking, CloseStep.Nights, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts tonight's room line for every in-house stay through P4 PostRoomNight (its own idempotent transaction per stay; a night
    /// already posted — earlier by hand or by an interrupted attempt — is never posted again). The charges themselves are the progress.
    /// </summary>
    private async Task NightsStepAsync(Caller caller, DateOnly date, AttemptResult a, CancellationToken ct)
    {
        var targets = (await store.PreflightDataAsync(null, caller.PropertyId, date, Now, ct).ConfigureAwait(false)).Nights;
        var posted = 0;
        foreach (var t in targets.Where(t => !t.HasNight))
        {
            await observer.OnStepAsync(caller.PropertyId, date, "night", t.StayId, ct).ConfigureAwait(false);
            var r = await billing.PostRoomNightAsync(caller, $"cls:{a.RunId}:night:{t.StayId}", new PostRoomNightRequest(t.StayId, date), ct).ConfigureAwait(false);
            if (r.Value.Created && !r.Replayed) posted++;
        }

        await runner.RunAsync(Ctx(caller, "CloseNights", StepKey(a, CloseStep.Nights)), new { a.RunId, a.Attempt }, async (tx, ct2) =>
        {
            var detail = CanonicalJson.Serialize(new { stays = targets.Count, postedNow = posted, alreadyPosted = targets.Count(t => t.HasNight) });
            await store.UpsertStepAsync(tx, a.RunId, CloseStep.Nights, "Done", a.Attempt, detail, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await store.UpdateRunAsync(tx, a.RunId, CloseRunStatus.Running, CloseSteps.Code(CloseStep.Cash), null, null, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("DAY_CLOSE_STEP", Target(date), $"nights · {posted} new night(s) posted · {targets.Count} in-house stays", ct2).ConfigureAwait(false);
            return new StepResult(false, []);
        }, ct).ConfigureAwait(false);
    }

    private async Task CashStepAsync(Caller caller, DateOnly date, AttemptResult a, CancellationToken ct)
    {
        await observer.OnStepAsync(caller.PropertyId, date, "cash", null, ct).ConfigureAwait(false);
        await CheckStepAsync(caller, date, a, CloseStep.Cash, CloseRules.CashBlocking, CloseStep.Close, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// One transaction: barrier row first (waits for in-flight writers; later writers get DAY_CLOSED), every check again (consistent
    /// with the barrier up), then the snapshot from the same tables the report reads. Any blocker rolls the barrier back.
    /// </summary>
    private async Task CloseAndSnapshotAsync(Caller caller, DateOnly date, AttemptResult a, CancellationToken ct)
    {
        try
        {
            await runner.RunAsync(Ctx(caller, "CloseBusinessDay", StepKey(a, CloseStep.Close)), new { a.RunId, a.Attempt }, async (tx, ct2) =>
            {
                var now = Now;
                if (await store.IsClosedAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false))
                    throw new InvalidOperationException("The day is closed but its run is not marked Closed."); // closed + snapshot commit together

                await store.InsertClosedDayAsync(tx, caller.PropertyId, date, a.RunId, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await observer.OnStepAsync(caller.PropertyId, date, "barrier", null, ct2).ConfigureAwait(false);

                var pre = await PreflightAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false);
                var blockers = CloseRules.Blockers(CloseRules.Evaluate(pre));
                if (blockers.Count > 0) throw CloseRules.Blocked(date, blockers);

                var data = await reports.LoadAsync(tx, caller.PropertyId, date, date, ct2).ConfigureAwait(false);
                var figures = DailyPackCalculator.Compute(data, date, date);
                var exceptions = new List<string>();
                foreach (var t in figures.Drawer.Tills.Where(t => t.DifferencePaise != 0))
                    exceptions.Add($"Cash difference {new Paise(t.DifferencePaise)} on till {t.TillCode} accepted" + (t.ApprovedBy is { } by ? $" by user {by}" : "") + (t.Reason is { } why ? " · " + why : ""));
                if (a.Attempt > 1) exceptions.Add($"Day close resumed after an interruption or a blocker ({a.Attempt} attempts; no night posted twice)");
                foreach (var l in data.Lines.Where(l => l.Kind == ChargeKind.Correction && l.ServiceDate == date))
                    exceptions.Add($"Correction {l.Amount} on folio {l.FolioId}" + (l.ReversesChargeId is { } rc ? $" reverses charge {rc}" : ""));

                var record = new ClosingRecord(date, caller.PrincipalId, now, a.RunId, a.Attempt, a.Attempt > 1, pre.Nights.Count,
                    await store.DepositsHeldAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false), exceptions, figures);
                var json = CanonicalJson.Serialize(record);
                var sha = CanonicalJson.Sha256(record);
                var snapshotId = await store.InsertSnapshotAsync(tx, new NewSnapshot(caller.PropertyId, date, a.RunId, caller.PrincipalId, now, record, json, sha), ct2).ConfigureAwait(false);
                var detail = CanonicalJson.Serialize(new { snapshotId, netRevenuePaise = figures.Revenue.NetPaise, netReceiptsPaise = figures.Receipts.NetPaise, cashExpectedPaise = figures.Drawer.ExpectedPaise });
                await store.UpsertStepAsync(tx, a.RunId, CloseStep.Close, "Done", a.Attempt, CanonicalJson.Serialize(new { closedBy = caller.PrincipalId }), caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await store.UpsertStepAsync(tx, a.RunId, CloseStep.Snapshot, "Done", a.Attempt, detail, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await store.UpdateRunAsync(tx, a.RunId, CloseRunStatus.Closed, CloseSteps.Code(CloseStep.NextDay), null, null, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("DAY_CLOSED", Target(date), $"run {a.RunId} · snapshot {snapshotId} · net revenue {new Paise(figures.Revenue.NetPaise)} · net receipts {new Paise(figures.Receipts.NetPaise)} · expected cash {new Paise(figures.Drawer.ExpectedPaise)}", ct2).ConfigureAwait(false);
                await tx.PublishAsync("BusinessDayClosed", a.RunId, new { propertyId = caller.PropertyId, businessDate = date, snapshotId, sha256 = Convert.ToHexString(sha) }, ct2).ConfigureAwait(false);
                return new CloseResult(snapshotId);
            }, ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.INVALID_TRANSITION && ex.Details?.ContainsKey("blockers") == true)
        {
            await runner.RunAsync(Ctx(caller, "CloseBlocked", StepKey(a, CloseStep.Close)), new { a.RunId, a.Attempt }, async (tx, ct2) =>
            {
                await store.UpsertStepAsync(tx, a.RunId, CloseStep.Close, "Blocked", a.Attempt, CanonicalJson.Serialize(new { message = ex.Message }), caller.PrincipalId, Now, ct2).ConfigureAwait(false);
                await store.UpdateRunAsync(tx, a.RunId, CloseRunStatus.Blocked, CloseSteps.Code(CloseStep.Close), null, null, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("DAY_CLOSE_STEP", Target(date), "close · Blocked · " + BillingService.Clip(ex.Message, 300), ct2).ConfigureAwait(false);
                return new StepResult(true, []);
            }, ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>NEXT_DAY (DEC-042): the only place the business date moves. The next shift's float is carried by P4 (DEC-035).</summary>
    private async Task NextDayStepAsync(Caller caller, DateOnly date, AttemptResult a, CancellationToken ct)
    {
        await observer.OnStepAsync(caller.PropertyId, date, "next-day", null, ct).ConfigureAwait(false);
        await runner.RunAsync(Ctx(caller, "OpenNextBusinessDay", $"cls:{a.RunId}:next-day"), new { a.RunId }, async (tx, ct2) =>
        {
            if (!await store.IsClosedAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Close the current business day first.");
            var advanced = await store.AdvanceBusinessDateAsync(tx, caller.PropertyId, date, ct2).ConfigureAwait(false);
            var next = date.AddDays(1);
            await store.UpsertStepAsync(tx, a.RunId, CloseStep.NextDay, "Done", a.Attempt, CanonicalJson.Serialize(new { from = Iso(date), to = Iso(next), advanced }), caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await store.UpdateRunAsync(tx, a.RunId, CloseRunStatus.Completed, CloseSteps.Done, null, null, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("BUSINESS_DATE_OPENED", Target(next), $"opened {Iso(next)} after closing {Iso(date)} · run {a.RunId}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("BusinessDateOpened", a.RunId, new { propertyId = caller.PropertyId, closedDate = date, businessDate = next }, ct2).ConfigureAwait(false);
            return new NextDayResult(next, advanced);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CloseRunResult> ResultAsync(CloseRunRow run, CancellationToken ct)
    {
        var steps = await store.StepsAsync(run.RunId, ct).ConfigureAwait(false);
        var snap = await store.ReadSnapshotAsync(null, run.PropertyId, run.BusinessDate, ct).ConfigureAwait(false);
        var order = Enum.GetValues<CloseStep>().Select(CloseSteps.Code).ToList();
        return new CloseRunResult(run.RunId, run.RunRef, run.BusinessDate, run.Status, run.CurrentStep, run.Attempts,
            run.Status == CloseRunStatus.Completed ? run.BusinessDate.AddDays(1) : null, snap?.SnapshotId,
            steps.OrderBy(s => order.IndexOf(s.Step)).Select(s => new CloseStepView(s.Step, s.Status, s.Attempt, s.Detail, s.UpdatedUtc)).ToList());
    }

    // ── Read models ──

    public async Task<PreflightResult> GetPreflightAsync(Caller caller, DateOnly? date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.PreflightView, ct: ct).ConfigureAwait(false);
        var d = date ?? await store.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await PreflightAsync(null, caller.PropertyId, d, ct).ConfigureAwait(false);
        return new PreflightResult(d, await store.IsClosedAsync(null, caller.PropertyId, d, ct).ConfigureAwait(false), CloseRules.Evaluate(data));
    }

    public async Task<CloseRunResult> GetRunAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.PreflightView, ct: ct).ConfigureAwait(false);
        var run = await store.FindRunAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "No close has been run for that date.");
        return await ResultAsync(run, ct).ConfigureAwait(false);
    }

    public async Task<ClosingSnapshotView> GetSnapshotAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        var s = await store.ReadSnapshotAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "No closing record for that date.");
        return new ClosingSnapshotView(s.SnapshotId, Convert.ToHexString(s.Sha256), ParseRecord(s.Json));
    }

    public static ClosingRecord ParseRecord(string json)
        => System.Text.Json.JsonSerializer.Deserialize<ClosingRecord>(json, CanonicalJson.SerializerOptions) ?? throw new InvalidOperationException("Empty closing record.");

    // ── Cash-book day lock (DEC-065) ──

    public async Task<CommandResult<CashBookDayResult>> LockCashBookDayAsync(Caller caller, string idempotencyKey, LockCashBookDayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.CashBookLock, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpLockCashBook, idempotencyKey), req, async (tx, ct2) =>
        {
            var closed = await store.IsClosedAsync(tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false);
            var row = closed ? await store.LockCashBookDayRowAsync(tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false) : null;
            var counters = closed ? await store.CountersAsync(tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false) : [];
            CashBookRules.EnsureCanLock(caller.PrincipalId, closed, row?.Locked == true, counters);
            var now = Now;
            await store.SetCashBookLockAsync(tx, caller.PropertyId, req.BusinessDate, true, caller.PrincipalId, now, null, ct2).ConfigureAwait(false);
            await tx.AuditAsync("CASHBOOK_LOCKED", Target(req.BusinessDate), $"cash book {Iso(req.BusinessDate)} locked", ct2).ConfigureAwait(false);
            return new CashBookDayResult(req.BusinessDate, true, true, caller.PrincipalId, now, [new CashBookEventView("lock", caller.PrincipalId, now, null)], null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CashBookDayResult>> ReopenCashBookDayAsync(Caller caller, string idempotencyKey, ReopenCashBookDayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        var reason = CashBookRules.EnsureReopenReason(req.Reason);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.CashBookReopen, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpReopenCashBook, idempotencyKey), req, async (tx, ct2) =>
        {
            var row = await store.LockCashBookDayRowAsync(tx, caller.PropertyId, req.BusinessDate, ct2).ConfigureAwait(false);
            if (row?.Locked != true) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This cash-book day is not locked.");
            var now = Now;
            await store.SetCashBookLockAsync(tx, caller.PropertyId, req.BusinessDate, false, caller.PrincipalId, now, reason, ct2).ConfigureAwait(false);
            await tx.AuditAsync("CASHBOOK_REOPENED", Target(req.BusinessDate), $"cash book {Iso(req.BusinessDate)} reopened · {reason}", ct2).ConfigureAwait(false);
            return new CashBookDayResult(req.BusinessDate, true, false, null, null, [new CashBookEventView("reopen", caller.PrincipalId, now, reason)], null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CashBookDayResult> GetCashBookDayAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ClosingPermissions.ReportFinance, ct: ct).ConfigureAwait(false);
        var closed = await store.IsClosedAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false);
        var (day, history) = await store.ReadCashBookDayAsync(caller.PropertyId, date, ct).ConfigureAwait(false);
        var snap = await store.ReadSnapshotAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false);
        return new CashBookDayResult(date, closed, day?.Locked == true, day?.LockedBy, day?.LockedUtc, history, snap is null ? null : ParseRecord(snap.Json).Figures.Drawer);
    }
}
