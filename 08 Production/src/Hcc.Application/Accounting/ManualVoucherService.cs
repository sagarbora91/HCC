using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

// ACC-2 · Manual vouchers and loan party ledgers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2, §7, §9; rulebook §1 principle 2, B5–B7,
// B11, A18, §6). Prototype twin: 03 Prototype/src/domain/streams/accounting-manual.ts + services/mock/handlers/accounting-manual.ts —
// the same guards, in the same order, with the same messages.

/// <summary>Manual voucher PERM ids (migration 4241): enter = Owner, Accountant; approve / reject / void = Owner.</summary>
public static class AccountingManualPermissions
{
    public const string Enter = "PERM-ACC-010";   // accounting.manual.enter
    public const string Approve = "PERM-ACC-011"; // accounting.manual.approve (and void)
}

/// <summary>One stored manual line (paise, one side only). <see cref="Account"/> = ledger code or <c>&lt;control&gt;/&lt;party&gt;</c>.</summary>
public sealed record ManualLine(string Account, long Debit, long Credit, string? Note = null);

/// <summary>A stored manual voucher (<c>acc.manual_voucher</c> + <c>acc.manual_line</c>).</summary>
public sealed record ManualVoucher(long Id, int PropertyId, DateOnly Date, string Type, string Narration, string? Reference, string? Template, string Status,
    IReadOnlyList<ManualLine> Lines, int EnteredBy, DateTime EnteredUtc, int? DecidedBy, DateTime? DecidedUtc, string? Reason, long? VoidOf, int? VoidedBy,
    DateTime? VoidedUtc, DateOnly? BookedOn, string Version);

/// <summary>What a save / a reversal writes.</summary>
public sealed record ManualVoucherDraft(DateOnly Date, string Type, string Narration, string? Reference, string? Template, string Status,
    IReadOnlyList<ManualLine> Lines, int EnteredBy, DateTime EnteredUtc, int? DecidedBy, DateTime? DecidedUtc, string? Reason, long? VoidOf, DateOnly? BookedOn);

/// <summary>A loan party of 2400 (<c>acc.loan</c>): <see cref="PartyId"/> = the code (e.g. <c>LN-HDFC</c>); <see cref="Kind"/> = secured | unsecured.</summary>
public sealed record LoanRow(string PartyId, string Name, string Lender, string? AccountLast4, string? Note, bool Active, string Kind = "secured")
{
    public string Account => ManualVoucherRules.LoanControl + "/" + PartyId;
}

/// <summary>Persistence of manual vouchers and loans (migrations 4239 / 4240).</summary>
public interface IManualVoucherStore
{
    /// <summary>Transaction-owned application lock <c>acc:manual:&lt;property&gt;</c>.</summary>
    Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<ManualVoucher?> GetAsync(ICommandTransaction? tx, int propertyId, long voucherId, CancellationToken ct);

    /// <summary>Every voucher of the property, newest first.</summary>
    Task<IReadOnlyList<ManualVoucher>> ListAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Inserts a voucher with its lines; returns its id and version.</summary>
    Task<(long Id, string Version)> InsertAsync(ICommandTransaction tx, int propertyId, ManualVoucherDraft draft, CancellationToken ct);

    /// <summary>Replaces a pending / rejected voucher (lines rewritten); STALE_VERSION when <paramref name="expectedVersion"/> is given and not the row's.</summary>
    Task<string> ReplaceAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, ManualVoucherDraft draft, CancellationToken ct);

    /// <summary>Pending → approved (booked on its date) or rejected (reason); STALE_VERSION as <see cref="ReplaceAsync"/>.</summary>
    Task<string> DecideAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, bool approve, string? reason, int actor, DateTime now,
        CancellationToken ct);

    /// <summary>Approved → void (reason); STALE_VERSION as <see cref="ReplaceAsync"/>.</summary>
    Task<string> VoidAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, string reason, int actor, DateTime now, CancellationToken ct);

    /// <summary>Party ledger ids that exist for the property (contract §3: tills, banks, companies, OTAs, Udhaar customers, staff, vendors) + its active loans.</summary>
    Task<IReadOnlySet<string>> PartyIdsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<IReadOnlyList<LoanRow>> LoansAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task InsertLoanAsync(ICommandTransaction tx, int propertyId, LoanRow loan, int actor, DateTime now, CancellationToken ct);
}

/// <summary>The pure rules of a manual voucher (contract §2): every guard, the reversal, the loan plan. Prototype <c>accManualProblems</c>.</summary>
public static partial class ManualVoucherRules
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Void = "void";
    public const int MinLines = 2;
    public const int MaxLines = 100;
    public const long MaxPaise = 10_000_000_000;
    public const string LoanControl = "2400";

    public static readonly IReadOnlyList<string> Types = ["Journal", "Contra", "Payment", "Receipt"];

    public static readonly IReadOnlyList<string> Templates = ["journal", "contra", "capital-introduced", "owner-drawing-bank", "loan-received", "loan-emi",
        "bank-charges", "interest-income", "bad-debt", "statutory-remittance", "gst-payment", "tds-remittance"];

    /// <summary>Ledgers never posted by hand: Suspense and the opening-balance difference.</summary>
    public static readonly IReadOnlyList<string> Never = ["9999", "1400"];

    // ── Messages (prototype ACC_MANUAL_MSG, same text) ──
    public const string MsgType = "Choose the voucher type: Journal, Contra, Payment or Receipt.";
    public const string MsgTemplate = "Choose a voucher template from the list.";
    public const string MsgNarration = "Enter the narration (3–400 characters).";
    public const string MsgReference = "Keep the reference to 60 characters.";
    public const string MsgDate = "Enter the voucher date (YYYY-MM-DD).";
    public const string MsgFuture = "The voucher date cannot be after the business date.";
    public const string MsgLines = "Enter 2 to 100 lines.";
    public const string MsgBalance = "The voucher does not balance: total debits must equal total credits.";
    public const string MsgContra = "A Contra voucher moves money only between cash and bank ledgers (Cash-in-Hand, Bank Accounts).";
    public const string MsgPayment = "A Payment voucher credits cash or bank and debits no cash or bank ledger (money between cash and bank is a Contra).";
    public const string MsgReceipt = "A Receipt voucher debits cash or bank and credits no cash or bank ledger (money between cash and bank is a Contra).";
    public const string MsgBadDebt = "Only the Owner writes off a bad debt (rulebook A18).";
    public const string MsgReason = "Enter a reason (3–200 characters).";

    public static string MsgLocked(DateOnly d) => $"{Iso(d)} is locked — unlock the day first (the Owner reopens a locked day with a reason, DEC-077).";

    public static string MsgLineAccount(int n) => $"Line {n}: choose a ledger code or a party ledger (code/party).";

    public static string MsgLineAmount(int n) => $"Line {n}: enter either a debit or a credit, in whole paise more than zero.";

    public static string MsgLineLarge(int n) => $"Line {n}: the amount is too large.";

    public static string MsgLineNever(int n, string code) => $"Line {n}: {code} is never posted by hand (9999 Suspense, 1400 Opening-balance difference).";

    public static string MsgLineControl(int n, string code) => $"Line {n}: {code} is a control ledger — post to one of its parties ({code}/<party>).";

    public static string MsgLineUnknown(int n, string account) => $"Line {n}: {account} is not an active ledger of the chart of accounts or a party ledger.";

    [GeneratedRegex(@"^\d{4}(/[A-Za-z0-9][A-Za-z0-9._-]{0,39})?$")]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex Last4Pattern();

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True when the ledger of <paramref name="account"/> sits in Cash-in-Hand or Bank Accounts (or below them).</summary>
    public static bool IsCashBank(EffectiveChart chart, string account)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Find(account) is { } l && (chart.IsUnder(l.GroupId, "cash") || chart.IsUnder(l.GroupId, "bank"));
    }

    /// <summary>A bad-debt write-off (template, or a debit on 5300): Owner only (rulebook A18).</summary>
    public static bool IsBadDebt(string? template, IReadOnlyList<ManualLineRequest>? lines)
        => template == "bad-debt" || (lines ?? []).Any(l => (l?.Account ?? "").Trim() == "5300" && l!.DebitPaise > 0);

    /// <summary>
    /// Every guard of contract §2 in order (the first is the refusal): type, template, narration, reference, date (≤ business date, not a
    /// locked day), 2–100 lines, each line, balanced, Contra only cash / bank, Payment / Receipt one cash-or-bank side.
    /// </summary>
    public static IReadOnlyList<string> Problems(string? type, string? template, string? narration, string? reference, DateOnly? date,
        IReadOnlyList<ManualLineRequest>? lines, DateOnly businessDate, Func<DateOnly, bool> locked, EffectiveChart chart, IReadOnlySet<string> parties)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(parties);
        var out_ = new List<string>();
        if (type is null || !Types.Contains(type)) out_.Add(MsgType);
        if (!string.IsNullOrEmpty(template) && !Templates.Contains(template)) out_.Add(MsgTemplate);
        var narr = (narration ?? "").Trim();
        if (narr.Length < 3 || narr.Length > 400) out_.Add(MsgNarration);
        if ((reference ?? "").Trim().Length > 60) out_.Add(MsgReference);
        if (date is not { } d) out_.Add(MsgDate);
        else if (d > businessDate) out_.Add(MsgFuture);
        else if (locked(d)) out_.Add(MsgLocked(d));
        if (lines is null || lines.Count < MinLines || lines.Count > MaxLines)
        {
            out_.Add(MsgLines);
            return out_;
        }

        var ok = true;
        for (var i = 0; i < lines.Count; i++)
        {
            var n = i + 1;
            var account = (lines[i]?.Account ?? "").Trim();
            long dr = lines[i]?.DebitPaise ?? 0, cr = lines[i]?.CreditPaise ?? 0;
            if (!AccountPattern().IsMatch(account))
            {
                out_.Add(MsgLineAccount(n));
                ok = false;
                continue;
            }

            if (dr < 0 || cr < 0 || (dr > 0) == (cr > 0))
            {
                out_.Add(MsgLineAmount(n));
                ok = false;
            }
            else if (dr > MaxPaise || cr > MaxPaise)
            {
                out_.Add(MsgLineLarge(n));
                ok = false;
            }

            var code = PartyLedgers.ControlOf(account);
            if (Never.Contains(code))
            {
                out_.Add(MsgLineNever(n, code));
                ok = false;
                continue;
            }

            if (!PartyLedgers.IsParty(account))
            {
                if (PartyLedgers.IsControl(account))
                {
                    out_.Add(MsgLineControl(n, account));
                    ok = false;
                }
                else if (chart.Find(account) is not { Active: true })
                {
                    out_.Add(MsgLineUnknown(n, account));
                    ok = false;
                }

                continue;
            }

            if (!PartyLedgers.IsControl(code) || chart.Find(code) is not { Active: true } || !parties.Contains(account))
            {
                out_.Add(MsgLineUnknown(n, account));
                ok = false;
            }
        }

        if (!ok) return out_;
        var ls = lines.Select(l => (Account: l!.Account!.Trim(), l.DebitPaise, l.CreditPaise)).ToList();
        long debit = ls.Sum(l => l.DebitPaise), credit = ls.Sum(l => l.CreditPaise);
        if (debit != credit || debit <= 0)
        {
            out_.Add(MsgBalance);
            return out_;
        }

        var cb = ls.Select(l => IsCashBank(chart, l.Account)).ToList();
        if (type == "Contra" && cb.Any(v => !v)) out_.Add(MsgContra);
        if (type == "Payment" && (!ls.Where((l, i) => cb[i] && l.CreditPaise > 0).Any() || ls.Where((l, i) => cb[i] && l.DebitPaise > 0).Any())) out_.Add(MsgPayment);
        if (type == "Receipt" && (!ls.Where((l, i) => cb[i] && l.DebitPaise > 0).Any() || ls.Where((l, i) => cb[i] && l.CreditPaise > 0).Any())) out_.Add(MsgReceipt);
        return out_;
    }

    /// <summary>The error code of a refusal: "unlock the day first" is a state conflict, everything else a validation.</summary>
    public static HccException Refusal(string message)
        => message.Contains("unlock the day first", StringComparison.Ordinal) ? new HccException(HccErrorCode.INVALID_TRANSITION, message) : HccException.Validation(message);

    /// <summary>Lines as stored (trimmed accounts, note ≤ 120). Call after <see cref="Problems"/>.</summary>
    public static IReadOnlyList<ManualLine> Normalise(IReadOnlyList<ManualLineRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(l =>
        {
            var note = (l.Note ?? "").Trim();
            return new ManualLine(l.Account!.Trim(), l.DebitPaise, l.CreditPaise, note.Length == 0 ? null : note.Length > 120 ? note[..120] : note);
        }).ToList();
    }

    /// <summary>The lines of a reversing voucher: debit and credit swapped (prototype <c>accManualSwap</c>).</summary>
    public static IReadOnlyList<ManualLine> Swap(IReadOnlyList<ManualLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToList();
    }

    public static IReadOnlyList<ManualLineRequest> AsRequests(IReadOnlyList<ManualLine> lines)
        => lines.Select(l => new ManualLineRequest(l.Account, l.Debit, l.Credit, l.Note)).ToList();

    /// <summary>The ManualVoucherApproved payload of a stored voucher.</summary>
    public static ManualVoucherApprovedEvent ApprovedEvent(long id, ManualVoucherDraft d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new(id, d.Date, d.Type, d.Narration, d.Reference, d.Template, d.Lines.Select(l => new ManualLinePayload(l.Account, l.Debit, l.Credit)).ToList(), d.VoidOf);
    }

    /// <summary>Plan of AddLoan (prototype <c>accLoanAddPlan</c>): the row, or the refusal (code, message).</summary>
    public static (LoanRow? Row, HccException? Refusal) LoanPlan(AddLoanRequest req, EffectiveChart chart, IReadOnlyList<LoanRow> loans)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(loans);
        static string T(string? v) => (v ?? "").Trim();
        string name = T(req.Name), lender = T(req.Lender), last4 = T(req.AccountLast4), note = T(req.Note);
        if (name.Length < 3 || name.Length > 60) return (null, HccException.Validation("Enter the loan name (3–60 characters)."));
        if (lender.Length < 2 || lender.Length > 60) return (null, HccException.Validation("Enter the lender (2–60 characters)."));
        if (last4.Length > 0 && !Last4Pattern().IsMatch(last4)) return (null, HccException.Validation("The loan account number ends in 4 digits (or leave it empty)."));
        if (note.Length > 200) return (null, HccException.Validation("Keep the note to 200 characters."));
        var kind = T(req.Kind).Length == 0 ? "secured" : T(req.Kind);
        if (kind is not ("secured" or "unsecured")) return (null, HccException.Validation("Choose the loan kind: secured or unsecured."));
        // Coordinator ruling (28 Oct): the code as given, upper-case (e.g. LN-HDFC); without one, LN-<lender>.
        var party = T(req.Code).Length > 0 ? PartyLedgers.Slug(T(req.Code)) : "LN-" + PartyLedgers.Slug(lender);
        if (party.Length > 36) party = party[..36].TrimEnd('-');
        var account = LoanControl + "/" + party;
        if (loans.Any(l => l.PartyId == party))
            return (null, new HccException(HccErrorCode.INVALID_TRANSITION, $"Loan {account} already exists — choose another code."));
        foreach (var l in chart.Ledgers)
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(l.TallyName, name, StringComparison.OrdinalIgnoreCase))
                return (null, new HccException(HccErrorCode.INVALID_TRANSITION, $"{l.Code} is already called “{l.Name}”."));
        foreach (var l in loans)
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                return (null, new HccException(HccErrorCode.INVALID_TRANSITION, $"{l.Account} is already called “{l.Name}”."));
        return (new LoanRow(party, name, lender, last4.Length == 0 ? null : last4, note.Length == 0 ? null : note, true, kind), null);
    }
}

/// <summary>
/// Cash guards of contract §7 for a manual Payment / Receipt that moves cash (1000 / 1005) with a party on the other side. LOCAL STUB until the
/// acc2-flows lane merges its <c>CashGuard</c> (receipt ≥ ₹2,00,000 refused, payment &gt; ₹10,000 warned): allows everything. The integrator
/// replaces <see cref="Check"/>'s body with the call (ACC2-MANUAL-DONE.md "Integration needed").
/// </summary>
public static class ManualCashGuard
{
    public static (string? Refused, string? Warning) Check(string type, DateOnly date, IReadOnlyList<ManualLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _ = (type, date);
        return (null, null);
    }
}

/// <summary>SaveManualVoucher / DecideManualVoucher / VoidManualVoucher / AddLoan (prototype ACC_MANUAL_SAVE / _DECIDE / _VOID, ACC_LOAN_ADD).</summary>
public sealed class ManualVoucherService(ICommandRunner runner, IManualVoucherStore store, IAccountingStore accounting, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpSave = "SaveManualVoucher";
    public const string OpDecide = "DecideManualVoucher";
    public const string OpVoid = "VoidManualVoucher";
    public const string OpAddLoan = "AddLoan";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static string Rs(long p) => "₹" + SourceRules.Rupees(p);

    private static ManualVoucherResult Result(long id, string status, DateOnly date, long? reversal, string version) => new(id, status, date, reversal, version);

    private async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
        => await accounting.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");

    private async Task<bool> LockedAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct)
    {
        await accounting.SerializeAsync(tx, AccountingDays.PeriodResource(propertyId, date), ct).ConfigureAwait(false);
        return await accounting.CurrentLockAsync(tx, propertyId, date, ct).ConfigureAwait(false) is not null;
    }

    /// <summary>Every guard against the database (chart, parties, the business date, the lock of the voucher date).</summary>
    private async Task GuardAsync(ICommandTransaction tx, int propertyId, string? type, string? template, string? narration, string? reference, DateOnly? date,
        IReadOnlyList<ManualLineRequest>? lines, CancellationToken ct)
    {
        var businessDate = await BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false);
        var locked = date is { } d && d <= businessDate && await LockedAsync(tx, propertyId, d, ct).ConfigureAwait(false);
        var chart = await accounting.ChartAsync(tx, propertyId, ct).ConfigureAwait(false);
        var parties = await store.PartyIdsAsync(tx, propertyId, ct).ConfigureAwait(false);
        var problems = ManualVoucherRules.Problems(type, template, narration, reference, date, lines, businessDate, _ => locked, chart, parties);
        if (problems.Count > 0) throw ManualVoucherRules.Refusal(problems[0]);
    }

    public async Task<CommandResult<ManualVoucherResult>> SaveAsync(Caller caller, string idempotencyKey, SaveManualVoucherRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingManualPermissions.Enter, ct: ct).ConfigureAwait(false);
        var owner = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, AccountingManualPermissions.Approve, ct: ct).ConfigureAwait(false);
        if (ManualVoucherRules.IsBadDebt(req.Template, req.Lines) && !owner) throw new HccException(HccErrorCode.FORBIDDEN, ManualVoucherRules.MsgBadDebt);
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpSave, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            if (req.VoucherId is { } vid)
            {
                var old = await store.GetAsync(tx, caller.PropertyId, vid, ct2).ConfigureAwait(false)
                    ?? throw new HccException(HccErrorCode.NOT_FOUND, "This manual voucher was not found.");
                if (old.Status is not (ManualVoucherRules.Pending or ManualVoucherRules.Rejected))
                    throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a pending or rejected voucher can be changed — void an approved one instead.");
                if (old.EnteredBy != caller.PrincipalId && !owner)
                    throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: only the voucher’s maker or the Owner changes it.");
            }

            await GuardAsync(tx, caller.PropertyId, req.VoucherType, req.Template, req.Narration, req.Reference, req.Date, req.Lines, ct2).ConfigureAwait(false);
            var lines = ManualVoucherRules.Normalise(req.Lines!);
            var date = req.Date!.Value;
            var (refused, warning) = ManualCashGuard.Check(req.VoucherType!, date, lines);
            if (refused is not null) throw HccException.Validation(refused);
            var reference = (req.Reference ?? "").Trim();
            var status = owner ? ManualVoucherRules.Approved : ManualVoucherRules.Pending;
            var draft = new ManualVoucherDraft(date, req.VoucherType!, req.Narration!.Trim(), reference.Length == 0 ? null : reference, string.IsNullOrEmpty(req.Template) ? null : req.Template,
                status, lines, caller.PrincipalId, Now, owner ? caller.PrincipalId : null, owner ? Now : null, null, null, owner ? date : null);
            long id;
            string version;
            if (req.VoucherId is { } existing)
            {
                id = existing;
                version = await store.ReplaceAsync(tx, caller.PropertyId, id, req.ExpectedVersion, draft, ct2).ConfigureAwait(false);
            }
            else
            {
                (id, version) = await store.InsertAsync(tx, caller.PropertyId, draft, ct2).ConfigureAwait(false);
            }

            var total = lines.Sum(l => l.Debit);
            await tx.AuditAsync(owner ? "ACC_MANUAL_APPROVED" : "ACC_MANUAL_SAVED", "acc-manual:" + id.ToString(CultureInfo.InvariantCulture),
                $"Manual {draft.Type} {id.ToString(CultureInfo.InvariantCulture)} dated {ManualVoucherRules.Iso(date)} {(req.VoucherId is null ? "saved" : "changed")} · {Rs(total)} · "
                + (owner ? "approved (Owner)" : "pending the Owner’s approval") + (warning is null ? "" : " · " + warning), ct2).ConfigureAwait(false);
            if (owner) await tx.PublishAsync(AccountingEvents.ManualVoucherApproved, id, ManualVoucherRules.ApprovedEvent(id, draft), ct2).ConfigureAwait(false);
            return Result(id, status, date, null, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ManualVoucherResult>> DecideAsync(Caller caller, string idempotencyKey, DecideManualVoucherRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingManualPermissions.Approve, ct: ct).ConfigureAwait(false);
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpDecide, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var v = await store.GetAsync(tx, caller.PropertyId, req.VoucherId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "This manual voucher was not found.");
            if (v.Status != ManualVoucherRules.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This manual voucher is no longer pending.");
            var key = "acc-manual:" + v.Id.ToString(CultureInfo.InvariantCulture);
            if (req.Approve)
            {
                await GuardAsync(tx, caller.PropertyId, v.Type, v.Template, v.Narration, v.Reference, v.Date, ManualVoucherRules.AsRequests(v.Lines), ct2).ConfigureAwait(false);
                var version = await store.DecideAsync(tx, caller.PropertyId, v.Id, req.ExpectedVersion, true, null, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("ACC_MANUAL_APPROVED", key, $"Manual {v.Type} {v.Id.ToString(CultureInfo.InvariantCulture)} dated {ManualVoucherRules.Iso(v.Date)} approved · {Rs(v.Lines.Sum(l => l.Debit))}", ct2).ConfigureAwait(false);
                var draft = new ManualVoucherDraft(v.Date, v.Type, v.Narration, v.Reference, v.Template, ManualVoucherRules.Approved, v.Lines, v.EnteredBy, v.EnteredUtc,
                    caller.PrincipalId, Now, null, null, v.Date);
                await tx.PublishAsync(AccountingEvents.ManualVoucherApproved, v.Id, ManualVoucherRules.ApprovedEvent(v.Id, draft), ct2).ConfigureAwait(false);
                return Result(v.Id, ManualVoucherRules.Approved, v.Date, null, version);
            }

            var reason = (req.Reason ?? "").Trim();
            if (reason.Length < 3 || reason.Length > 200) throw HccException.Validation(ManualVoucherRules.MsgReason);
            var rv = await store.DecideAsync(tx, caller.PropertyId, v.Id, req.ExpectedVersion, false, reason, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MANUAL_REJECTED", key, $"Manual {v.Type} {v.Id.ToString(CultureInfo.InvariantCulture)} rejected · {reason}", ct2).ConfigureAwait(false);
            return Result(v.Id, ManualVoucherRules.Rejected, v.Date, null, rv);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ManualVoucherResult>> VoidAsync(Caller caller, string idempotencyKey, VoidManualVoucherRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingManualPermissions.Approve, ct: ct).ConfigureAwait(false);
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, OpVoid, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var v = await store.GetAsync(tx, caller.PropertyId, req.VoucherId, ct2).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "This manual voucher was not found.");
            if (v.Status != ManualVoucherRules.Approved) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only an approved manual voucher can be voided.");
            if (v.VoidOf is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A reversing voucher cannot be voided — enter a new voucher instead.");
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length < 3 || reason.Length > 200) throw HccException.Validation(ManualVoucherRules.MsgReason);
            var key = "acc-manual:" + v.Id.ToString(CultureInfo.InvariantCulture);
            var id = v.Id.ToString(CultureInfo.InvariantCulture);
            if (!await LockedAsync(tx, caller.PropertyId, v.Date, ct2).ConfigureAwait(false))
            {
                var version = await store.VoidAsync(tx, caller.PropertyId, v.Id, req.ExpectedVersion, reason, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("ACC_MANUAL_VOIDED", key, $"Manual {v.Type} {id} dated {ManualVoucherRules.Iso(v.Date)} voided (unlocked day: posts nothing) · {reason}", ct2).ConfigureAwait(false);
                await tx.PublishAsync(AccountingEvents.ManualVoucherVoided, v.Id, new ManualVoucherVoidedEvent(v.Id, v.Date, reason, null), ct2).ConfigureAwait(false);
                return Result(v.Id, ManualVoucherRules.Void, v.Date, null, version);
            }

            // Locked day: the original stays in its snapshot; a reversing voucher dated the business date undoes it (DEC-077 / rulebook G5).
            var businessDate = await BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            if (await LockedAsync(tx, caller.PropertyId, businessDate, ct2).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, ManualVoucherRules.MsgLocked(businessDate));
            var voided = await store.VoidAsync(tx, caller.PropertyId, v.Id, req.ExpectedVersion, reason, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var narration = $"Reversal of {id} · {v.Narration}";
            if (narration.Length > 400) narration = narration[..400];
            var draft = new ManualVoucherDraft(businessDate, v.Type, narration, v.Reference, v.Template, ManualVoucherRules.Approved, ManualVoucherRules.Swap(v.Lines),
                caller.PrincipalId, Now, caller.PrincipalId, Now, reason, v.Id, businessDate);
            var (rid, _) = await store.InsertAsync(tx, caller.PropertyId, draft, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_MANUAL_VOIDED", key,
                $"Manual {v.Type} {id} dated {ManualVoucherRules.Iso(v.Date)} voided · {ManualVoucherRules.Iso(v.Date)} is locked: reversing voucher {rid.ToString(CultureInfo.InvariantCulture)} dated {ManualVoucherRules.Iso(businessDate)} · {reason}",
                ct2).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.ManualVoucherApproved, rid, ManualVoucherRules.ApprovedEvent(rid, draft), ct2).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.ManualVoucherVoided, v.Id, new ManualVoucherVoidedEvent(v.Id, v.Date, reason, rid), ct2).ConfigureAwait(false);
            return Result(v.Id, ManualVoucherRules.Void, v.Date, rid, voided);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LoanResult>> AddLoanAsync(Caller caller, string idempotencyKey, AddLoanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ChartPermissions.Edit, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpAddLoan, idempotencyKey), req, async (tx, ct2) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var chart = await accounting.ChartAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var loans = await store.LoansAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var (row, refusal) = ManualVoucherRules.LoanPlan(req, chart, loans);
            if (refusal is not null) throw refusal;
            await store.InsertLoanAsync(tx, caller.PropertyId, row!, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_LOAN_ADDED", "acc-loan:" + row!.Account, $"Loan {row.Account} {row.Name} added under 2400 ({row.Kind}) · lender {row.Lender}", ct2).ConfigureAwait(false);
            return new LoanResult(row.Account, row.Name);
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>GET /accounting/manual (PERM-ACC-001): the property's manual vouchers (newest first) and loans.</summary>
public sealed class ManualVoucherQueries(IManualVoucherStore store, IAccountingStore accounting, IAccessPolicy policy)
{
    public async Task<ManualVouchersView> GetAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var chart = await accounting.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var loans = await store.LoansAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var loanNames = loans.ToDictionary(l => l.Account, l => l.Name, StringComparer.Ordinal);
        string Name(string account) => PartyLedgers.IsParty(account)
            ? chart.NameOf(PartyLedgers.ControlOf(account)) + " · " + (loanNames.TryGetValue(account, out var n) ? n : PartyLedgers.PartyOf(account))
            : chart.NameOf(account);
        var vouchers = (await store.ListAsync(null, caller.PropertyId, ct).ConfigureAwait(false)).Select(v => new ManualVoucherView(v.Id, v.Date, v.Type, v.Narration, v.Reference,
            v.Template, v.Status, v.Lines.Select(l => new ManualLineView(l.Account, Name(l.Account), l.Debit, l.Credit, l.Note)).ToList(), v.EnteredBy, v.EnteredUtc,
            v.DecidedBy, v.DecidedUtc, v.Reason, v.VoidOf, v.VoidedBy, v.BookedOn, v.Version)).ToList();
        return new ManualVouchersView(vouchers, loans.Select(l => new LoanView(l.Account, l.Name, l.Lender, l.AccountLast4, l.Active, l.Note, l.Kind)).ToList());
    }
}
