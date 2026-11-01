namespace Hcc.Contracts.Accounting;

// ACC-2 · lane acc2-tds — wire shapes of the TDS endpoints (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §5, §9). Money in paise.

/// <summary>POST /commands/SetTdsSettings (Owner, PERM-CFG-003): the firm's two TDS switches; null = unchanged. DeductorType = individual | other.</summary>
public sealed record SetTdsSettingsRequest(bool? HotelDeductsTds, string? DeductorType);

public sealed record TdsSettingsResult(bool HotelDeductsTds, string DeductorType);

/// <summary>POST /commands/SetTdsRate (Owner, PERM-CFG-003): a dated override of one TDS-table key (rates in bps, thresholds in paise).</summary>
public sealed record SetTdsRateRequest(string Key, long Value, string? EffectiveFrom, string? Reason);

public sealed record TdsRateChangeResult(long RateId, string Key, string EffectiveFrom, long Before, long After, string Status);

/// <summary>
/// POST /commands/RecordTdsRemittance (PERM-ACC-013 Owner / Accountant; rulebook E5): TDS deposited for a row of the table and the month it was
/// held back in. Tender = Bank transfer | UPI; Date = payment date (default the business date). Booked as the manual template tds-remittance.
/// </summary>
/// ACC-3 C6: Section may be a sub-section code (rent:plant = old 194-I(a), professional:technical = old 194J(a)); C7: the Form 168 figures.
public sealed record RecordTdsRemittanceRequest(string Month, string Section, long AmountPaise, string Tender, string Challan, string? Date = null,
    IReadOnlyList<Form168FigureDto>? Form168 = null);

/// <summary>ACC-3 C7: one Form 168 figure — the TDS a company deducted from the hotel as the annual tax-credit statement shows it.</summary>
public sealed record Form168FigureDto(long CompanyId, long AmountPaise);

/// <summary>ACC-3 C7 POST /commands/SetTdsForm168 (PERM-ACC-013): replaces the Form 168 figures kept on a remittance (acc.tds_remit.form168_json).</summary>
public sealed record SetTdsForm168Request(long RemitId, IReadOnlyList<Form168FigureDto> Figures);

public sealed record TdsForm168Result(long RemitId, IReadOnlyList<Form168FigureDto> Figures);

public sealed record TdsRemittanceResult(long RemitId, string Month, string Section, long AmountPaise, DateOnly BusinessDate, string? ManualVoucherId, long OutstandingPaise);

public sealed record TdsTableRowView(string Code, string Label, string Section, string Basis, long ThresholdPaise, long? YearThresholdPaise, int IndividualBps, int OtherBps,
    string Source, string Note);

public sealed record TdsRateView(long RateId, string Key, string EffectiveFrom, long Value, string Reason);

public sealed record TdsRemittanceView(long RemitId, string Month, string Section, long AmountPaise, string Tender, string Challan, DateOnly BusinessDate, string? ManualVoucherId,
    IReadOnlyList<Form168FigureDto>? Form168 = null, string? Old = null); // ACC-3 C6 / C7

/// <summary>GET /accounting/tds (PERM-ACC-013): the switches, the table in force on the business date, the Owner's overrides, the remittances.</summary>
public sealed record TdsStateView(bool HotelDeductsTds, string DeductorType, DateOnly BusinessDate, IReadOnlyList<TdsTableRowView> Table, IReadOnlyList<TdsRateView> Rates,
    IReadOnlyList<TdsRemittanceView> Remittances);

public sealed record TdsDeducteeRowView(string Vendor, string? Pan, string Section, int Bills, long TaxablePaise, long TdsPaise, long RemittedPaise, long PendingPaise);

/// <summary>GET /accounting/tds/deductees?period=2026-27|2026-04 (PERM-ACC-013): deductee-wise TDS for the CA (DEC-146).</summary>
public sealed record TdsDeducteeReportView(string Period, DateOnly From, DateOnly To, IReadOnlyList<TdsDeducteeRowView> Rows, long TaxablePaise, long TdsPaise,
    long RemittedPaise, long PendingPaise);

public sealed record TdsReceivableRowView(long CompanyId, string Company, string Month, int Payments, long ReceivedPaise, long TdsPaise, long SettledPaise);

/// <summary>GET /accounting/tds/receivable?fy=2026-27 (PERM-ACC-013): Form 168 reconciliation (A9 / E6) — TDS companies deducted, by company and month.</summary>
public sealed record TdsReceivableReportView(string Period, DateOnly From, DateOnly To, IReadOnlyList<TdsReceivableRowView> Rows, long TdsPaise,
    IReadOnlyList<Form168FigureDto>? Form168 = null); // ACC-3 C7: the stored figures of the year (latest remittance per company)
