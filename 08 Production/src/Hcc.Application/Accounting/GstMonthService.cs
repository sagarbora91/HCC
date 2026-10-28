using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Compliance;
using Hcc.Application.Expenses;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;

namespace Hcc.Application.Accounting;

// ACC-2 acc2-gst · GST month end, statutory remittances, the month lock and the GST-on-advances switch (02 Planning/agents/ACC2-POSTING-CONTRACT.md
// §4 switch, §6, §9; rulebook §4.12, A6, D5, E1–E5, §G1; DEC-131, DEC-142). Prototype: 03 Prototype/src/domain/streams/accounting-gst.ts and
// handlers/accounting-gst.ts (ACC_GST_SETOFF, ACC_GST_PAY, ACC_STAT_REMIT, ACC_MONTH_LOCK / _REQUEST / _UNLOCK, ACC_GST_ADVANCES_SET).

/// <summary>ACC-2 permissions of this lane (contract §9; seeded by migration 4249).</summary>
public static class AccountingGstPermissions
{
    /// <summary>accounting.month.lock / unlock — Owner ('A'); the Accountant holds it request-only ('R') and asks with RequestAccountingMonthLock.</summary>
    public const string MonthLock = "PERM-ACC-012";

    /// <summary>accounting.tax — GST set-off, GST / TDS / statutory remittance entries, TDS report: Owner, Accountant.</summary>
    public const string Tax = "PERM-ACC-013";
}

/// <summary>A posted set-off as stored in <c>acc.gst_setoff</c> (the current row of its month; a replaced one is superseded).</summary>
public sealed record StoredGstSetoff(long SetoffId, string Month, DateOnly PostedOn, string SourceKey, GstSetOffResult Figures, int PostedBy, DateTime PostedUtc);

/// <summary>One month lock row (<c>acc.month_lock</c>): unlocking stamps the row, a relock inserts a new one (history kept).</summary>
public sealed record MonthLockRow(long LockId, string Month, DateTime LockedUtc, int LockedBy, long? SetoffId, string? SetoffKey, DateTime? UnlockedUtc, int? UnlockedBy,
    string? UnlockReason);

/// <summary>Persistence of the lane (SQL adapter <c>SqlGstMonthStore</c>, migrations 4246–4248).</summary>
public interface IGstMonthStore
{
    /// <summary>Current (not superseded) set-offs of the property, month ascending.</summary>
    Task<IReadOnlyList<StoredGstSetoff>> SetoffsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Supersedes the month's current row (if any) and inserts the new one; returns its id.</summary>
    Task<long> InsertSetoffAsync(ICommandTransaction tx, int propertyId, StoredGstSetoff setoff, DateTime now, CancellationToken ct);

    /// <summary>Records an accounting source in the command's transaction (unique key per property); true when inserted.</summary>
    Task<bool> InsertSourceAsync(ICommandTransaction tx, int propertyId, AccountingSource source, DateTime now, CancellationToken ct);

    /// <summary>The stored entries of a source (line order), empty when unknown.</summary>
    Task<IReadOnlyList<SourceEntry>> SourceEntriesAsync(ICommandTransaction tx, int propertyId, string sourceKey, CancellationToken ct);

    Task<IReadOnlyList<MonthLockRow>> MonthLocksAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<long> InsertMonthLockAsync(ICommandTransaction tx, int propertyId, string month, long? setoffId, int by, DateTime now, CancellationToken ct);

    Task UnlockMonthAsync(ICommandTransaction tx, long lockId, int by, DateTime now, string reason, CancellationToken ct);

    Task InsertMonthLockRequestAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, CancellationToken ct);

    /// <summary>Months with a request newer than their last lock.</summary>
    Task<IReadOnlyList<string>> OpenMonthLockRequestsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>The firm profile's switch (<c>cmp.firm_profile.gst_on_advances</c>); false without a profile.</summary>
    Task<bool> GstOnAdvancesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Sets the switch; false when the property has no firm profile yet.</summary>
    Task<bool> SetGstOnAdvancesAsync(ICommandTransaction tx, int propertyId, bool on, int by, DateTime now, CancellationToken ct);
}

/// <summary>A manual voucher this lane asks the manual-voucher module (acc2-manual, contract §2) to save.</summary>
public sealed record GstManualVoucherDraft(DateOnly Date, string Type, string Narration, string Reference, string Template, IReadOnlyList<(string Account, long DebitPaise, long CreditPaise)> Lines,
    string? Tender = null); // ACC-2 integration: the payment method, so the tender line posts on the party it settles to (1030/<bank>)

public sealed record GstManualVoucherRef(string Id, string Status);

/// <summary>
/// The manual-voucher API of acc2-manual (ManualVoucherService): payments and remittances are manual vouchers with a template (contract §2,
/// §5, §6). Wired by the integrator; until then <see cref="UnwiredGstManualVouchers"/> answers "not configured" and reports no pending voucher.
/// </summary>
public interface IGstManualVouchers
{
    /// <summary>Saves the voucher inside the caller's transaction with the manual guards (Owner → approved, Accountant → pending).</summary>
    Task<GstManualVoucherRef> SaveAsync(ICommandTransaction tx, Caller caller, GstManualVoucherDraft draft, CancellationToken ct);

    /// <summary>Manual vouchers pending approval dated in [from, to] (the month lock waits for them).</summary>
    Task<int> PendingAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Stand-in until acc2-manual is merged (ACC2-GST-DONE.md "Integration needed").</summary>
public sealed class UnwiredGstManualVouchers : IGstManualVouchers
{
    public Task<GstManualVoucherRef> SaveAsync(ICommandTransaction tx, Caller caller, GstManualVoucherDraft draft, CancellationToken ct)
        => throw HccException.NotConfigured("manual-vouchers", "Manual vouchers are not wired on this server yet (acc2-manual): the payment was not recorded.");

    public Task<int> PendingAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>The SCC expense month lock (EXP_MONTH_LOCK / DEC-131 unlock) driven by the accounting month lock (contract §6).</summary>
public interface IExpenseMonthLockPort
{
    Task LockAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, CancellationToken ct);

    /// <summary>Unlocks the month when it is locked; true when it was.</summary>
    Task<bool> UnlockAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, string reason, CancellationToken ct);
}

/// <summary><see cref="IExpenseMonthLockPort"/> over the expense stores (same figures as ExpenseRegisterCommands.LockMonthAsync).</summary>
public sealed class ExpenseMonthLockAdapter(IExpenseStore store, IExpenseRegisterStore registers) : IExpenseMonthLockPort
{
    public async Task LockAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, CancellationToken ct)
    {
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        await registers.MonthLockAsync(tx, propertyId, month, ct).ConfigureAwait(false); // key-range lock: writers of the month wait
        var sum = ExpenseScc.Summary(await store.LedgerAsync(tx, propertyId, first, first.AddMonths(1).AddDays(-1), ct).ConfigureAwait(false), month);
        await registers.SaveMonthLockAsync(tx, propertyId, new MonthLockRecord(month, sum.ExpensePaise, sum.Count, sum.GstPaidPaise, JsonSerializer.Serialize(sum.ByCategory),
            by, now, false, null, sum.GstItcPaise), ct).ConfigureAwait(false);
    }

    public async Task<bool> UnlockAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime now, string reason, CancellationToken ct)
    {
        if (await registers.MonthLockAsync(tx, propertyId, month, ct).ConfigureAwait(false) is not { Unlocked: false }) return false;
        await registers.UnlockMonthAsync(tx, propertyId, month, by, now, reason, ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary>The compliance card hook (contract §6 D5): a remittance marks the month's EPF / ESIC / PT card filed with the challan as evidence.</summary>
public interface IComplianceCardHook
{
    /// <summary>Returns (card id or null, a note for the audit text).</summary>
    Task<(string? ItemId, string Note)> MarkFiledAsync(ICommandTransaction tx, int propertyId, string obligationKind, string month, DateOnly filedOn, string evidence, string notes,
        int by, DateTime now, CancellationToken ct);
}

/// <summary><see cref="IComplianceCardHook"/> over the compliance store (the rules of ComplianceCommands.MarkDoneAsync; a filed / NA card is left).</summary>
public sealed class ComplianceCardHook(IComplianceStore store) : IComplianceCardHook
{
    public async Task<(string? ItemId, string Note)> MarkFiledAsync(ICommandTransaction tx, int propertyId, string obligationKind, string month, DateOnly filedOn, string evidence,
        string notes, int by, DateTime now, CancellationToken ct)
    {
        if (ComplianceRules.LegacyCard(obligationKind, month) is not { } w) return (null, "no compliance card for that month");
        await store.LockSettingsAsync(tx, propertyId, ct).ConfigureAwait(false);
        var snap = await store.SnapshotAsync(tx, propertyId, ct).ConfigureAwait(false);
        if (snap.Profile is null) return (null, "compliance card not marked (set the firm profile first)");
        var cal = snap.Calendar();
        if (cal.FindCard(w.ItemId, w.FyStart) is not { } card) return (null, "no compliance card for that month");
        var cur = cal.Record(w.FyStart, card.Id);
        if (cur?.Na == true) return (card.Id, $"compliance card {card.Id} is marked not applicable (left as it is)");
        if (cur?.Done == true) return (card.Id, $"compliance card {card.Id} was already filed (left as it is)");
        var due = SccTaxRules.ParseDue(card.Due, w.FyStart) ?? cur?.DueDate;
        var ev = evidence.Length <= ComplianceRules.MaxEvidenceRef ? evidence : evidence[..ComplianceRules.MaxEvidenceRef];
        var nt = notes.Length <= ComplianceRules.MaxNotes ? notes : notes[..ComplianceRules.MaxNotes];
        await store.UpsertRecordAsync(tx, propertyId, new CmpRecord(w.FyStart, card.Id, true, false, filedOn, nt, ev, string.Empty, due, by, now, null, null, null), ct)
            .ConfigureAwait(false);
        await tx.AuditAsync("COMPLIANCE_DONE", $"compliance:{w.FyStart.ToString(CultureInfo.InvariantCulture)}:{card.Id}",
            $"{card.Id} · filed on {filedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · evidence {ev} · from the accounting remittance", ct).ConfigureAwait(false);
        return (card.Id, $"compliance card {card.Id} marked filed (evidence {ev})");
    }
}

/// <summary>The month's position (prototype <c>ACCGstMonth</c>).</summary>
public sealed record GstMonthFigures(string Month, DateOnly From, DateOnly To, GstHeads Output, GstHeads Input, GstHeads BroughtForward, GstHeads Credit, long Rcm, long RcmBalance,
    long Tcs, long CashBalance, long AdvanceGst, GstSetOffResult Setoff);

/// <summary>Pure month arithmetic over vouchers (prototype <c>accGstMonth</c>): flows by month with the set-off journals left out, credit and cash
/// carried from earlier months net of what their posted set-offs used.</summary>
public static class GstMonthRules
{
    public static bool IsMonth(string? m) => m is { Length: 7 } && DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static (DateOnly From, DateOnly To) Range(string month)
    {
        var from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (from, from.AddMonths(1).AddDays(-1));
    }

    public static string MonthOf(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private sealed class Flow
    {
        public long OI, OC, OS, II, IC, IS, Cash, Rcm, Tcs, Adv;
    }

    public static GstMonthFigures Compute(string month, IEnumerable<Voucher> vouchers, IReadOnlyList<StoredGstSetoff> posted)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(posted);
        var (from, to) = Range(month);
        var flows = new SortedDictionary<string, Flow>(StringComparer.Ordinal);
        foreach (var v in vouchers.Where(v => v.Date <= to))
        {
            if (v.SourceKind == SourceKinds.Tax && v.Sub is SourceRules.GstSetoffSub or SourceRules.GstSetoffReversalSub) continue;
            var key = MonthOf(v.Date);
            if (!flows.TryGetValue(key, out var f)) flows[key] = f = new Flow();
            foreach (var e in v.Entries)
            {
                var a = PartyLedgers.ControlOf(e.Account);
                long cr = e.CreditPaise - e.DebitPaise, dr = -cr;
                switch (a)
                {
                    case GstSetOff.LedgerOutputIgst: f.OI += cr; break;
                    case GstSetOff.LedgerOutputCgst: f.OC += cr; break;
                    case GstSetOff.LedgerOutputSgst: f.OS += cr; break;
                    case GstSetOff.LedgerInputIgst: f.II += dr; break;
                    case GstSetOff.LedgerInputCgst: f.IC += dr; break;
                    case GstSetOff.LedgerInputSgst: f.IS += dr; break;
                    case GstSetOff.LedgerCash:
                        f.Cash += dr;
                        if (v.SourceKind == SourceKinds.Ota) f.Tcs += dr;
                        break;
                    case GstSetOff.LedgerRcm: f.Rcm += cr; break;
                    case GstSetOff.LedgerAdvance: f.Adv += dr; break;
                }
            }
        }

        var postedBy = posted.ToDictionary(p => p.Month, StringComparer.Ordinal);
        GstHeads credit = GstHeads.Zero;
        long cash = 0, rcm = 0, adv = 0;
        var first = flows.Keys.FirstOrDefault(k => string.CompareOrdinal(k, month) < 0);
        if (first is not null)
            for (var m = first; string.CompareOrdinal(m, month) < 0; m = MonthOf(Range(m).From.AddMonths(1)))
            {
                var f = flows.GetValueOrDefault(m) ?? new Flow();
                credit = credit.Plus(new GstHeads(f.II, f.IC, f.IS));
                cash += f.Cash;
                rcm += f.Rcm;
                adv += f.Adv;
                if (postedBy.TryGetValue(m, out var p))
                {
                    credit = credit.Minus(p.Figures.Used);
                    cash -= p.Figures.Payable;
                }
            }

        var t = flows.GetValueOrDefault(month) ?? new Flow();
        var input = new GstHeads(t.II, t.IC, t.IS);
        var avail = credit.Plus(input);
        var cashBal = cash + t.Cash;
        var output = new GstHeads(t.OI, t.OC, t.OS);
        return new GstMonthFigures(month, from, to, output, input, credit, avail, t.Rcm, rcm + t.Rcm, t.Tcs, cashBal, adv + t.Adv, GstSetOff.Compute(output, avail, cashBal));
    }

    public static string SourceKey(string month, DateOnly date) => $"gst-setoff:{month}@{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}

/// <summary>Commands and the month query of the lane (prototype handlers/accounting-gst.ts).</summary>
public sealed class GstMonthService(ICommandRunner runner, IAccountingStore accounting, IGstMonthStore store, IAccessPolicy policy, TimeProvider clock,
    IGstManualVouchers? manual = null, IExpenseMonthLockPort? expenseMonths = null, IComplianceCardHook? cards = null, IOpeningEntryStore? openingEntries = null)
{
    public const string OpSetoff = "PostGstSetoff";
    public const string OpPay = "RecordGstPayment";
    public const string OpRemit = "RecordStatutoryRemittance";
    public const string OpLock = "LockAccountingMonth";
    public const string OpRequest = "RequestAccountingMonthLock";
    public const string OpUnlock = "UnlockAccountingMonth";
    public const string OpAdvances = "SetGstOnAdvances";

    private static readonly DateOnly Beginning = new(2000, 1, 1);

    private readonly IGstManualVouchers manualVouchers = manual ?? new UnwiredGstManualVouchers();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string op, string key) => new(c.PropertyId, c.PrincipalId, op, key);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Rs(long p) => "₹" + SourceRules.Rupees(p);

    private static string Month(string? m)
    {
        var x = (m ?? "").Trim();
        return GstMonthRules.IsMonth(x) ? x : throw HccException.Validation("Choose a month (YYYY-MM).");
    }

    /// <summary>Every voucher up to <paramref name="to"/> (lock snapshots for locked dates), inside <paramref name="tx"/> when given.</summary>
    private async Task<IReadOnlyList<Voucher>> VouchersAsync(ICommandTransaction? tx, int propertyId, DateOnly to, CancellationToken ct)
    {
        var chart = await accounting.ChartAsync(tx, propertyId, ct).ConfigureAwait(false);
        var mapping = await accounting.EffectiveMappingAsync(tx, propertyId, chart, ct).ConfigureAwait(false);
        var sources = await accounting.SourcesAsync(tx, propertyId, Beginning, to, ct).ConfigureAwait(false);
        var locks = (await accounting.LocksAsync(tx, propertyId, Beginning, to, ct).ConfigureAwait(false)).ToDictionary(l => l.BusinessDate);
        var dates = sources.Select(s => s.BusinessDate).Concat(locks.Keys).Distinct().Order();
        return dates.SelectMany(d => locks.TryGetValue(d, out var l) ? l.Vouchers : VoucherBuilder.Build(d, sources, mapping, chart)).ToList();
    }

    private async Task<GstMonthFigures> FiguresAsync(ICommandTransaction? tx, int propertyId, string month, IReadOnlyList<StoredGstSetoff> posted, CancellationToken ct)
        => GstMonthRules.Compute(month, await VouchersAsync(tx, propertyId, GstMonthRules.Range(month).To, ct).ConfigureAwait(false), posted);

    private static MonthLockRow? Current(IEnumerable<MonthLockRow> rows, string month) => rows.LastOrDefault(r => r.Month == month && r.UnlockedUtc is null);

    private static string LockedMessage(string month) => $"Month {month} is locked (GST set-off posted, books closed) — the Owner unlocks the month first (DEC-131).";

    // ── Query ──

    public async Task<GstMonthView> MonthAsync(Caller caller, string? month, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var m = Month(month);
        var posted = await store.SetoffsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var f = await FiguresAsync(null, caller.PropertyId, m, posted, ct).ConfigureAwait(false);
        var locks = await store.MonthLocksAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var p = posted.FirstOrDefault(x => x.Month == m);
        var requested = await store.OpenMonthLockRequestsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        string? blocker = null;
        if (Current(locks, m) is null)
        {
            var bd = await accounting.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? f.To;
            blocker = f.To >= bd ? $"The month {m} is not over: its last day {D(f.To)} must be a closed business day." : p is null ? $"Post the GST set-off for {m} first (rulebook E1)." : null;
        }

        static GstHeadsView H(GstHeads h) => new(h.Igst, h.Cgst, h.Sgst);
        var s = f.Setoff;
        return new GstMonthView(m, f.From, f.To, H(f.Output), H(f.Input), H(f.BroughtForward), H(f.Credit), f.Rcm, f.RcmBalance, f.Tcs, f.CashBalance, f.AdvanceGst,
            H(s.Cash), H(s.CreditLeft), H(s.UseIgst), H(s.UseCgst), H(s.UseSgst), s.Payable, s.Challan, p is null ? null : View(p), Current(locks, m) is not null, blocker,
            requested.Contains(m, StringComparer.Ordinal));
    }

    public async Task<AccountingMonthLocksView> LocksAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var locks = await store.MonthLocksAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        return new AccountingMonthLocksView(
            locks.Select(l => new AccountingMonthLockView(l.Month, l.LockedUtc, l.LockedBy, l.SetoffKey, l.UnlockedUtc, l.UnlockedBy, l.UnlockReason)).ToList(),
            await store.OpenMonthLockRequestsAsync(null, caller.PropertyId, ct).ConfigureAwait(false),
            (await store.SetoffsAsync(null, caller.PropertyId, ct).ConfigureAwait(false)).Select(View).ToList(),
            await store.GstOnAdvancesAsync(null, caller.PropertyId, ct).ConfigureAwait(false));
    }

    private static GstSetoffView View(StoredGstSetoff p)
    {
        static GstHeadsView H(GstHeads h) => new(h.Igst, h.Cgst, h.Sgst);
        var x = p.Figures;
        return new GstSetoffView(p.Month, p.PostedOn, p.SourceKey, H(x.Output), H(x.Credit), H(x.Used), H(x.Cash), x.Payable, x.CashAvailable, x.Challan, p.PostedBy, p.PostedUtc);
    }

    // ── ACC_GST_SETOFF ──

    public async Task<CommandResult<GstSetoffResult>> SetoffAsync(Caller caller, string idempotencyKey, PostGstSetoffRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.Tax, ct: ct).ConfigureAwait(false);
        var month = Month(req.Month);
        return await runner.RunAsync(Ctx(caller, OpSetoff, idempotencyKey), req, async (tx, ct2) =>
        {
            await accounting.SerializeAsync(tx, $"acc:gst-month:{caller.PropertyId.ToString(CultureInfo.InvariantCulture)}", ct2).ConfigureAwait(false);
            var bd = await accounting.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
            var (_, to) = GstMonthRules.Range(month);
            if (to >= bd) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"The month {month} is not over: the set-off is posted after its last day ({D(to)}).");
            if (Current(await store.MonthLocksAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false), month) is not null)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, LockedMessage(month));
            var posted = await store.SetoffsAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var cur = posted.FirstOrDefault(p => p.Month == month);
            var f = await FiguresAsync(tx, caller.PropertyId, month, posted, ct2).ConfigureAwait(false);
            var key = GstMonthRules.SourceKey(month, bd);
            var x = f.Setoff;
            var text = $"GST set-off {month} · output {Rs(x.Output.Cgst)} CGST + {Rs(x.Output.Sgst)} SGST + {Rs(x.Output.Igst)} IGST · ITC used {Rs(x.Output.Total - x.Payable)} · cash ledger {Rs(x.Payable)} · challan {Rs(x.Challan)} · posted {D(bd)}";
            if (cur is not null && cur.PostedOn == bd && cur.Figures == x)
            {
                await tx.AuditAsync("ACC_GST_SETOFF_POSTED", "acc-gst:" + month, text + " · unchanged", ct2).ConfigureAwait(false);
                return new GstSetoffResult(month, bd, key, x.Payable, x.Challan, true, null);
            }

            string? replaced = null;
            if (cur is not null)
            {
                if (await accounting.CurrentLockAsync(tx, caller.PropertyId, cur.PostedOn, ct2).ConfigureAwait(false) is not null)
                    throw new HccException(HccErrorCode.INVALID_TRANSITION, $"The set-off for {month} posted on {D(cur.PostedOn)}, which is locked — the Owner unlocks that day first.");
                var original = await store.SourceEntriesAsync(tx, caller.PropertyId, cur.SourceKey, ct2).ConfigureAwait(false);
                if (original.Count > 0)
                    await store.InsertSourceAsync(tx, caller.PropertyId, SourceRules.GstSetoffReversal(cur.SourceKey + "/reversed@" + D(bd) + "#" + cur.SetoffId.ToString(CultureInfo.InvariantCulture), bd, original,
                        $"GST set-off for {month} of {D(cur.PostedOn)} reversed (replaced by the set-off of {D(bd)})"), Now, ct2).ConfigureAwait(false);
                replaced = cur.SourceKey;
            }

            // A source key is never reused (acc.source is append-only): a second set-off on the same day gets a "#n" suffix.
            var sourceKey = key;
            for (var n = 2; (await store.SourceEntriesAsync(tx, caller.PropertyId, sourceKey, ct2).ConfigureAwait(false)).Count > 0; n++)
                sourceKey = key + "#" + n.ToString(CultureInfo.InvariantCulture);
            var entries = GstSetOff.Entries(x);
            if (entries.Count > 0)
                await store.InsertSourceAsync(tx, caller.PropertyId, SourceRules.GstSetoff(sourceKey, bd, x,
                    $"GST set-off for {month} (s.49 order) · output {Rs(x.Output.Total)} · ITC used {Rs(x.Output.Total - x.Payable)} · cash ledger {Rs(x.Payable)} · challan {Rs(x.Challan)}"),
                    Now, ct2).ConfigureAwait(false);
            await store.InsertSetoffAsync(tx, caller.PropertyId, new StoredGstSetoff(0, month, bd, sourceKey, x, caller.PrincipalId, Now), Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_GST_SETOFF_POSTED", "acc-gst:" + month, text + (replaced is null ? "" : " · replaces " + replaced), ct2).ConfigureAwait(false);
            return new GstSetoffResult(month, bd, sourceKey, x.Payable, x.Challan, false, replaced);
        }, ct).ConfigureAwait(false);
    }

    // ── ACC_GST_PAY / ACC_STAT_REMIT ──

    private sealed record PaymentFields(string Month, long Amount, string Method, string Challan, DateOnly Date);

    private static PaymentFields Fields(string? month, long amount, string? method, string? challan, DateOnly? date, DateOnly businessDate)
    {
        var m = Month(month);
        if (amount <= 0) throw HccException.Validation("Enter the amount paid (whole paise, more than zero).");
        var meth = (method ?? "").Trim();
        if (meth.Length == 0) throw HccException.Validation("Choose how it was paid (a payment method mapped to a cash or bank ledger).");
        var ch = (challan ?? "").Trim();
        if (ch.Length is < 3 or > 40) throw HccException.Validation("Enter the challan / CIN reference (3–40 characters).");
        var d = date ?? businessDate;
        if (d > businessDate) throw HccException.Validation("The payment date must be a valid date on or before the business date.");
        if (string.CompareOrdinal(GstMonthRules.MonthOf(d), m) <= 0)
            throw HccException.Validation($"The payment for {m} is dated after the month (the challan is paid in the following month).");
        return new PaymentFields(m, amount, meth, ch, d);
    }

    private async Task<(string Account, PaymentFields F)> PrepareAsync(ICommandTransaction tx, Caller caller, string? month, long amount, string? method, string? challan, DateOnly? date,
        CancellationToken ct)
    {
        var bd = await accounting.BusinessDateAsync(tx, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var f = Fields(month, amount, method, challan, date, bd);
        if (Current(await store.MonthLocksAsync(tx, caller.PropertyId, ct).ConfigureAwait(false), GstMonthRules.MonthOf(f.Date)) is not null)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, LockedMessage(GstMonthRules.MonthOf(f.Date)));
        var chart = await accounting.ChartAsync(tx, caller.PropertyId, ct).ConfigureAwait(false);
        var mapping = await accounting.EffectiveMappingAsync(tx, caller.PropertyId, chart, ct).ConfigureAwait(false);
        var code = ChartOfAccounts.Resolve(mapping, "method:" + f.Method, chart)
            ?? throw HccException.Validation("Choose how it was paid (a payment method mapped to a cash or bank ledger).");
        return (code, f);
    }

    public async Task<CommandResult<GstRemittanceResult>> PayAsync(Caller caller, string idempotencyKey, RecordGstPaymentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.Tax, ct: ct).ConfigureAwait(false);
        var rcm = req.RcmPaise ?? 0;
        if (rcm < 0 || rcm > req.AmountPaise) throw HccException.Validation("The reverse-charge part is whole paise, zero or more, at most the amount paid.");
        return await runner.RunAsync(Ctx(caller, OpPay, idempotencyKey), req, async (tx, ct2) =>
        {
            var (tender, f) = await PrepareAsync(tx, caller, req.Month, req.AmountPaise, req.Method, req.Challan, req.Date, ct2).ConfigureAwait(false);
            var lines = new List<(string, long, long)> { (GstSetOff.LedgerCash, f.Amount, 0), (tender, 0, f.Amount) };
            if (rcm > 0)
            {
                lines.Add((GstSetOff.LedgerRcm, rcm, 0));
                lines.Add((GstSetOff.LedgerCash, 0, rcm));
            }

            var v = await manualVouchers.SaveAsync(tx, caller, new GstManualVoucherDraft(f.Date, "Payment", $"GST paid for {f.Month} · challan {f.Challan}{(rcm > 0 ? " · RCM " + Rs(rcm) : "")}",
                f.Challan, "gst-payment", lines, f.Method), ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_GST_PAYMENT", "acc-gst:" + f.Month,
                $"GST payment {f.Month} · {Rs(f.Amount)} · {f.Method} · challan {f.Challan}{(rcm > 0 ? " · RCM " + Rs(rcm) : "")} · manual voucher {v.Id} ({v.Status})", ct2).ConfigureAwait(false);
            return new GstRemittanceResult(f.Month, "gst-payment", v.Id, v.Status, null, "");
        }, ct).ConfigureAwait(false);
    }

    private static readonly IReadOnlyDictionary<string, (string Ledger, string Label, string Card)> Stat = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
    {
        ["pf"] = ("2310", "PF", "EPF"),
        ["esic"] = ("2320", "ESIC", "ESIC"),
        ["pt"] = ("2330", "Professional tax", "PT"),
    };

    public async Task<CommandResult<GstRemittanceResult>> RemitAsync(Caller caller, string idempotencyKey, RecordStatutoryRemittanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.Tax, ct: ct).ConfigureAwait(false);
        if (!Stat.TryGetValue((req.Kind ?? "").Trim(), out var k)) throw HccException.Validation("Choose PF, ESIC or professional tax.");
        return await runner.RunAsync(Ctx(caller, OpRemit, idempotencyKey), req, async (tx, ct2) =>
        {
            var (tender, f) = await PrepareAsync(tx, caller, req.Month, req.AmountPaise, req.Method, req.Challan, req.Date, ct2).ConfigureAwait(false);
            var v = await manualVouchers.SaveAsync(tx, caller, new GstManualVoucherDraft(f.Date, "Payment", $"{k.Label} remitted for {f.Month} · challan {f.Challan}", f.Challan,
                "statutory-remittance", [(k.Ledger, f.Amount, 0), (tender, 0, f.Amount)], f.Method), ct2).ConfigureAwait(false);
            var (item, note) = cards is null ? ((string?)null, "compliance card not marked (the compliance module is not composed)")
                : await cards.MarkFiledAsync(tx, caller.PropertyId, k.Card, f.Month, f.Date, f.Challan, $"{k.Label} {Rs(f.Amount)} remitted · {f.Method} · manual voucher {v.Id}",
                    caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_STAT_REMITTED", "acc-stat:" + f.Month,
                $"{k.Label} remittance {f.Month} · {Rs(f.Amount)} · {f.Method} · challan {f.Challan} · manual voucher {v.Id} ({v.Status}) · {note}", ct2).ConfigureAwait(false);
            return new GstRemittanceResult(f.Month, "statutory-remittance", v.Id, v.Status, item, note);
        }, ct).ConfigureAwait(false);
    }

    // ── Month lock (G1) ──

    /// <summary>Why the month cannot be locked inside <paramref name="tx"/> (null = it can); closed days not locked yet are listed to lock.</summary>
    private async Task<(string? Why, IReadOnlyList<DateOnly> ToLock, StoredGstSetoff? Setoff)> BlockerAsync(ICommandTransaction tx, int propertyId, string month, CancellationToken ct)
    {
        var bd = await accounting.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var (from, to) = GstMonthRules.Range(month);
        if (to >= bd) return ($"The month {month} is not over: its last day {D(to)} must be a closed business day.", [], null);
        if (Current(await store.MonthLocksAsync(tx, propertyId, ct).ConfigureAwait(false), month) is not null) return ($"The month {month} is already locked.", [], null);
        var setoff = (await store.SetoffsAsync(tx, propertyId, ct).ConfigureAwait(false)).FirstOrDefault(p => p.Month == month);
        if (setoff is null) return ($"Post the GST set-off for {month} first (rulebook E1).", [], null);
        var pending = await manualVouchers.PendingAsync(tx, propertyId, from, to, ct).ConfigureAwait(false);
        if (pending > 0) return ($"{pending} manual voucher{(pending == 1 ? " is" : "s are")} pending approval in {month} — the Owner decides {(pending == 1 ? "it" : "them")} first.", [], null);
        var toLock = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            await accounting.SerializeAsync(tx, AccountingDays.PeriodResource(propertyId, d), ct).ConfigureAwait(false);
            var day = await AccountingDays.EvaluateAsync(accounting, tx, propertyId, d, ct).ConfigureAwait(false);
            if (day.Lock is not null) continue;
            if (day.Status == DayStatus.Open) return ($"{D(d)} is not closed — close every business day of {month} first.", [], null);
            try
            {
                await OpeningBalanceGuard.RequireLockableAsync(openingEntries, tx, propertyId, d, ct).ConfigureAwait(false);
            }
            catch (HccException ex) when (ex.Code == HccErrorCode.INVALID_TRANSITION)
            {
                return ($"{D(d)}: {ex.Message}", [], null);
            }

            if (day.Errors.Count > 0)
                return ($"{D(d)}: Resolve {day.Errors.Count} exception{(day.Errors.Count == 1 ? "" : "s")} first ({day.Errors[0].Title}).", [], null);
            toLock.Add(d);
        }

        return (null, toLock, setoff);
    }

    public async Task<CommandResult<AccountingMonthLockResult>> LockMonthAsync(Caller caller, string idempotencyKey, LockAccountingMonthRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.MonthLock, ct: ct).ConfigureAwait(false);
        var month = Month(req.Month);
        return await runner.RunAsync(Ctx(caller, OpLock, idempotencyKey), req, async (tx, ct2) =>
        {
            await accounting.SerializeAsync(tx, $"acc:gst-month:{caller.PropertyId.ToString(CultureInfo.InvariantCulture)}", ct2).ConfigureAwait(false);
            var (why, toLock, setoff) = await BlockerAsync(tx, caller.PropertyId, month, ct2).ConfigureAwait(false);
            if (why is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, why);
            foreach (var d in toLock)
            {
                var day = await AccountingDays.EvaluateAsync(accounting, tx, caller.PropertyId, d, ct2).ConfigureAwait(false);
                var (debit, credit) = VoucherBuilder.Totals(day.Live);
                if (debit != credit) throw HccException.Validation($"{D(d)}: debits and credits do not agree.");
                var lk = await accounting.InsertLockAsync(tx, caller.PropertyId, d, day.Live, Now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("ACC_PERIOD_LOCKED", "acc-period:" + D(d),
                    $"{day.Live.Count} voucher(s) · ₹{SourceRules.Rupees(debit)} Dr = ₹{SourceRules.Rupees(credit)} Cr · lock {lk.LockId} · month lock {month}", ct2).ConfigureAwait(false);
                await tx.PublishAsync(AccountingEvents.AccountingPeriodLocked, lk.LockId,
                    new { lockId = lk.LockId, businessDate = d, voucherCount = day.Live.Count, debitPaise = debit, creditPaise = credit }, ct2).ConfigureAwait(false);
            }

            await store.InsertMonthLockAsync(tx, caller.PropertyId, month, setoff!.SetoffId, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var exp = false;
            if (expenseMonths is not null)
            {
                await expenseMonths.LockAsync(tx, caller.PropertyId, month, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
                exp = true;
            }

            await tx.AuditAsync("ACC_MONTH_LOCKED", "acc-month:" + month,
                $"Month {month} locked · {toLock.Count.ToString(CultureInfo.InvariantCulture)} day(s) locked now · set-off {setoff.SourceKey}{(exp ? " · expense month locked" : "")}", ct2).ConfigureAwait(false);
            return new AccountingMonthLockResult(month, "Locked", toLock.Count, setoff.SourceKey, exp);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<AccountingMonthLockResult>> RequestMonthLockAsync(Caller caller, string idempotencyKey, LockAccountingMonthRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.Tax, ct: ct).ConfigureAwait(false);
        var month = Month(req.Month);
        return await runner.RunAsync(Ctx(caller, OpRequest, idempotencyKey), req, async (tx, ct2) =>
        {
            var (why, toLock, setoff) = await BlockerAsync(tx, caller.PropertyId, month, ct2).ConfigureAwait(false);
            if (why is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, why);
            await store.InsertMonthLockRequestAsync(tx, caller.PropertyId, month, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MONTH_LOCK_REQUESTED", "acc-month:" + month, $"Month lock requested · {month} · for the Owner to lock", ct2).ConfigureAwait(false);
            return new AccountingMonthLockResult(month, "Requested", toLock.Count, setoff?.SourceKey, false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<AccountingMonthLockResult>> UnlockMonthAsync(Caller caller, string idempotencyKey, UnlockAccountingMonthRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingGstPermissions.MonthLock, ct: ct).ConfigureAwait(false);
        var month = Month(req.Month);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required to unlock a month.");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        return await runner.RunAsync(Ctx(caller, OpUnlock, idempotencyKey), req, async (tx, ct2) =>
        {
            await accounting.SerializeAsync(tx, $"acc:gst-month:{caller.PropertyId.ToString(CultureInfo.InvariantCulture)}", ct2).ConfigureAwait(false);
            var cur = Current(await store.MonthLocksAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false), month)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Month {month} is not locked.");
            await store.UnlockMonthAsync(tx, cur.LockId, caller.PrincipalId, Now, reason, ct2).ConfigureAwait(false);
            var exp = expenseMonths is not null && await expenseMonths.UnlockAsync(tx, caller.PropertyId, month, caller.PrincipalId, Now, reason, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MONTH_UNLOCKED", "acc-month:" + month,
                $"Month {month} unlocked · {reason}{(exp ? " · expense month unlocked" : "")} · day locks stay (unlock a day with its reason to correct it)", ct2).ConfigureAwait(false);
            return new AccountingMonthLockResult(month, "Unlocked", 0, cur.SetoffKey, exp);
        }, ct).ConfigureAwait(false);
    }

    // ── GST on advances switch (A6, DEC-142) ──

    public async Task<CommandResult<GstOnAdvancesResult>> SetGstOnAdvancesAsync(Caller caller, string idempotencyKey, SetGstOnAdvancesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpAdvances, idempotencyKey), req, async (tx, ct2) =>
        {
            if (await store.GstOnAdvancesAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false) == req.On)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"GST on advances is already {(req.On ? "on" : "off")}.");
            if (!await store.SetGstOnAdvancesAsync(tx, caller.PropertyId, req.On, caller.PrincipalId, Now, ct2).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Set the compliance firm profile first (Owner: Compliance calendar › Firm profile).");
            await tx.AuditAsync("ACC_GST_ON_ADVANCES_SET", "cmp-profile:gst-on-advances",
                req.On ? "GST on booking advances switched ON — deposits received post Dr 1350 · Cr 2110 · Cr 2120 at the booked room's rate (A6)"
                    : "GST on booking advances switched OFF — no GST on advances (DEC-142)", ct2).ConfigureAwait(false);
            return new GstOnAdvancesResult(req.On);
        }, ct).ConfigureAwait(false);
    }
}
