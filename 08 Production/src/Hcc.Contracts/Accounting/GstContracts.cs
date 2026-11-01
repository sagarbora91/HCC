namespace Hcc.Contracts.Accounting;

// ACC-2 acc2-gst · GST month end, statutory remittances, the month lock and GST on booking advances
// (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 switch, §6, §9; rulebook §4.12, A6, D5, E1–E5, §G1; DEC-131, DEC-142).
// Prototype: 03 Prototype/src/domain/streams/accounting-gst.ts, handlers/accounting-gst.ts. Money = paise; month = "YYYY-MM".

/// <summary>GST by head (paise).</summary>
public sealed record GstHeadsView(long IgstPaise, long CgstPaise, long SgstPaise);

/// <summary>PostGstSetoff (ACC_GST_SETOFF, PERM-ACC-013): posts the month's set-off journal on the business date; idempotent per month.</summary>
public sealed record PostGstSetoffRequest(string? Month);

public sealed record GstSetoffResult(string Month, DateOnly PostedOn, string SourceKey, long PayablePaise, long ChallanPaise, bool Unchanged, string? Replaced);

/// <summary>RecordGstPayment (ACC_GST_PAY, PERM-ACC-013): the challan through the manual-voucher template gst-payment (Dr 1340 · Cr tender);
/// <see cref="RcmPaise"/> &gt; 0 also books Dr 2140 · Cr 1340 (E3).</summary>
public sealed record RecordGstPaymentRequest(string? Month, long AmountPaise, string? Method, string? Challan, DateOnly? Date, long? RcmPaise);

/// <summary>RecordStatutoryRemittance (ACC_STAT_REMIT, PERM-ACC-013): kind pf | esic | pt, template statutory-remittance
/// (Dr 2310 / 2320 / 2330 · Cr tender); the month's EPF / ESIC / PT compliance card is marked filed with the challan.</summary>
public sealed record RecordStatutoryRemittanceRequest(string? Kind, string? Month, long AmountPaise, string? Method, string? Challan, DateOnly? Date);

public sealed record GstRemittanceResult(string Month, string Template, string VoucherId, string VoucherStatus, string? ComplianceCard, string ComplianceNote);

/// <summary>LockAccountingMonth (ACC_MONTH_LOCK, PERM-ACC-012 Owner) / RequestAccountingMonthLock (Accountant, PERM-ACC-013).</summary>
public sealed record LockAccountingMonthRequest(string? Month);

/// <summary>UnlockAccountingMonth (ACC_MONTH_UNLOCK, PERM-ACC-012 Owner), reason 3–200 characters (DEC-131 mirror).</summary>
public sealed record UnlockAccountingMonthRequest(string? Month, string? Reason);

public sealed record AccountingMonthLockResult(string Month, string Status, int DaysLocked, string? SetoffKey, bool ExpenseMonthLocked);

/// <summary>SetGstOnAdvances (Owner, PERM-CFG-003 money settings): the firm profile's "GST on advances" switch (A6, DEC-142 default off).</summary>
/// ACC-3 C10: From = the first advance date the switch applies to (default the business date); ignored when off.
public sealed record SetGstOnAdvancesRequest(bool On, string? From = null);

public sealed record GstOnAdvancesResult(bool On, string? From = null);

/// <summary>A posted set-off as stored (acc.gst_setoff).</summary>
public sealed record GstSetoffView(string Month, DateOnly PostedOn, string SourceKey, GstHeadsView Output, GstHeadsView Credit, GstHeadsView CreditUsed, GstHeadsView Cash,
    long PayablePaise, long CashAvailablePaise, long ChallanPaise, int PostedBy, DateTime PostedUtc);

/// <summary>
/// GET /accounting/gst/month?month=YYYY-MM (PERM-ACC-001): the month's position from the books (prototype <c>accGstMonth</c>) — output by head,
/// ITC of the month and brought forward, RCM and its balance, TCS, the 1340 balance after earlier set-offs, GST held on advances, the computed
/// set-off (s.49 order), the posted one (if any), whether the month is locked and an open lock request.
/// </summary>
public sealed record GstMonthView(string Month, DateOnly From, DateOnly To, GstHeadsView Output, GstHeadsView Input, GstHeadsView BroughtForward, GstHeadsView Credit,
    long RcmPaise, long RcmBalancePaise, long TcsPaise, long CashBalancePaise, long AdvanceGstPaise,
    GstHeadsView SetoffCash, GstHeadsView CreditLeft, GstHeadsView UseIgst, GstHeadsView UseCgst, GstHeadsView UseSgst, long PayablePaise, long ChallanPaise,
    GstSetoffView? Posted, bool Locked, string? LockBlocker, bool LockRequested);

/// <summary>One month lock row (current or lifted).</summary>
public sealed record AccountingMonthLockView(string Month, DateTime LockedUtc, int LockedBy, string? SetoffKey, DateTime? UnlockedUtc, int? UnlockedBy, string? UnlockReason);

/// <summary>GET /accounting/gst/locks: every month lock with its history, the open requests, the switch.</summary>
public sealed record AccountingMonthLocksView(IReadOnlyList<AccountingMonthLockView> Locks, IReadOnlyList<string> Requested, IReadOnlyList<GstSetoffView> Setoffs, bool GstOnAdvances,
    string? GstOnAdvancesFrom = null); // ACC-3 C10
