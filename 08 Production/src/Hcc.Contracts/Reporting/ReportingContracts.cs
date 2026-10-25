namespace Hcc.Contracts.Reporting;

// Wire contracts for owner reports (contracts package §3 GetReport; Financial Rulebook §6 report dictionary). Money = integer
// paise; dates YYYY-MM-DD. The same records are the figures of the immutable closing snapshot (cls.closing_snapshot), so a
// report for a closed date and its snapshot are produced by one calculation over the same transactions.

/// <summary>Occupancy (report dictionary): sellable = rooms not blocked; occupied = stays whose [arrival, departure) covers the date.</summary>
/// <param name="OccupancyPct">Occupied ÷ sellable × 100, half-up to 1 dp ("34.8%"); "—" when nothing is sellable.</param>
/// <param name="AdrPaise">Room revenue ÷ occupied room-nights (half away from zero); null when no night was occupied.</param>
/// <param name="RevParPaise">Room revenue ÷ sellable room-nights; null when nothing was sellable.</param>
public sealed record OccupancyFigures(int SellableRoomNights, int OccupiedRoomNights, string OccupancyPct, long RoomRevenuePaise, long? AdrPaise,
    long? RevParPaise);

/// <summary>Revenue by service date (excl. tax) and the tax on it. Tax is a liability, not revenue.</summary>
public sealed record RevenueFigures(long RoomPaise, long ServicePaise, long DiscountsPaise, long CorrectionsPaise, long NetPaise, long TaxPaise,
    long CgstPaise, long SgstPaise, long GrossPaise);

/// <summary>
/// One GST line of the report: lines of one category at one snapshot rate. CGST/SGST are split per line (SGST = half rounded toward
/// zero, CGST takes the odd paisa) and then added, so the report agrees with every document to the paisa. Illustrative — CA to confirm.
/// </summary>
public sealed record GstCategoryRow(string Category, int TaxBps, int Lines, long TaxablePaise, long CgstPaise, long SgstPaise, long TaxPaise);

/// <summary>Payments − refunds of one tender by receipt date.</summary>
public sealed record TenderRow(string Tender, long PaymentsPaise, long RefundsPaise, long NetPaise);

/// <summary>Receipts by receipt date: net = payments − refunds (all tenders); deposits and company credit separately.</summary>
public sealed record ReceiptFigures(IReadOnlyList<TenderRow> ByTender, long NetPaise, long DepositsInPaise, long DepositsOutPaise, long CompanyCreditPaise);

/// <summary>Expenses by business date. Wave 1: drawer-cash expenses moved through the shared till (DEC-052); Wave 2 adds the expense module.</summary>
public sealed record ExpenseFigures(long CashPaise, long ReversalsPaise, long TotalPaise, string Note);

/// <summary>One till on one business date (all its shifts): opening float of the first shift, cash in/out, expected and counted of the last.</summary>
/// <param name="DifferencePaise">Σ counted − expected over the day's counted shifts (cash over/short).</param>
public sealed record DrawerDayRow(string TillCode, DateOnly BusinessDate, int Shifts, string Status, long OpeningPaise, long CashInPaise, long CashOutPaise,
    long ExpensesPaise, long RestaurantPaise, long ExpectedPaise, long? CountedPaise, long DifferencePaise, int? CountedBy, int? ApprovedBy, string? Reason);

/// <summary>Drawer cash (Financial Rulebook §4): opening + cash in − cash out = expected, before banking; counted and difference from the counts.</summary>
public sealed record DrawerFigures(IReadOnlyList<DrawerDayRow> Tills, long OpeningPaise, long CashInPaise, long CashOutPaise, long ExpectedPaise,
    long? CountedPaise, long DifferencePaise);

/// <summary>Per business date figures (same definitions as the totals; the rows add up to the range totals).</summary>
public sealed record ReportDayRow(DateOnly Date, int Arrivals, int Departures, int Sellable, int Occupied, long NetRevenuePaise, long TaxPaise,
    long NetReceiptsPaise, long CashExpensesPaise);

/// <summary>The owner's daily pack for an inclusive business-date range.</summary>
public sealed record DailyReport(DateOnly From, DateOnly To, OccupancyFigures Occupancy, RevenueFigures Revenue, IReadOnlyList<GstCategoryRow> Gst,
    ReceiptFigures Receipts, ExpenseFigures Expenses, DrawerFigures Drawer, IReadOnlyList<ReportDayRow> Days);

/// <summary>Day status in a report: Open (current business date), Closed (snapshot exists), Future, or Past (before HCC closes existed).</summary>
/// <param name="MatchesSnapshot">For a closed date: whether today's recalculation equals the closing snapshot's figures (integrity alarm when false).</param>
public sealed record ReportDayStatus(DateOnly Date, string Status, bool? MatchesSnapshot);

/// <summary>GET /api/v1/reports/daily?from=&amp;to= (PERM-RPT-002).</summary>
public sealed record DailyReportView(DailyReport Report, IReadOnlyList<ReportDayStatus> DayStatus, DateOnly BusinessDate, string Note);
