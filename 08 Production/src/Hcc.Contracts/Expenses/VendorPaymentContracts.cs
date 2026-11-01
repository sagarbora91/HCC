namespace Hcc.Contracts.Expenses;

// ACC-2 · lane acc2-purchase — vendor payments, advances, voids, returns, ageing and the fixed-asset register (02 Planning/agents/
// ACC2-POSTING-CONTRACT.md §3, §9). Prototype: services/mock/handlers/expenses-vendor.ts, sync clients/sync/expenses-vendor.ts.

/// <summary>One allocation of a payment to a bill — or, with <c>Opening</c> (ExpenseId 0), to the vendor's opening balance (coordinator ruling,
/// rulebook T11: the opening is the oldest open item; prototype <c>expense:'opening'</c>).</summary>
public sealed record VendorAllocationDto(long ExpenseId, long AmountPaise, bool Opening = false);

/// <summary>RecordVendorPayment K (PERM-EXP-004): vendor (code or name of the Expense vendors master), amount, method (cash / UPI / bank —
/// never a credit method), reference (required except cash), business date (default the business date; never later), allocations (default:
/// oldest open bill first; the rest is an advance). Cash needs <c>TillId</c> (the till with the open shift).</summary>
public sealed record RecordVendorPaymentRequest(string Vendor, long AmountPaise, string Method, string? Reference = null, string? BusinessDate = null,
    IReadOnlyList<VendorAllocationDto>? Allocations = null, int? TillId = null);

/// <summary>VoidVendorPayment K (Owner, PERM-MST-002): a reason of 3–200 characters; a cash payment needs <c>TillId</c> (the cash comes back).</summary>
public sealed record VoidVendorPaymentRequest(long PaymentId, string Reason, int? TillId = null);

/// <summary>RecordExpenseReturn K (PERM-EXP-004): a goods return / vendor credit note against a posted bill; the Owner's own posts at once,
/// anyone else's waits for the Owner's DecideExpense.</summary>
public sealed record RecordExpenseReturnRequest(long ExpenseId, long AmountPaise, long GstPaise, string Reason);

public sealed record VendorPaymentView(long PaymentId, string VendorCode, string Vendor, string BusinessDate, long AmountPaise, string Method, string? Reference,
    int RecordedBy, string RecordedUtc, IReadOnlyList<VendorAllocationDto> Allocations, bool Voided, string? VoidReason, int? VoidedBy, string? VoidedDate, long? CashMovementId);

public sealed record VendorPaymentList(IReadOnlyList<VendorPaymentView> Items);

/// <summary>ACC-3 C4: <c>Warning</c> = the vendor's GSTIN does not start with its state code (DEC-136: a warning, never a refusal).</summary>
public sealed record VendorPaymentResult(VendorPaymentView Payment, long AdvancePaise, string? Warning = null);

/// <summary>ACC-3 C3 ReallocateVendorPayment K (PERM-EXP-004 Owner / Manager / Accountant): the payment's new allocation list (open bills of the
/// vendor, its own current allocations counted as open; the opening balance with <c>Opening</c>); the rest stays an advance. Audit row in
/// ops.vendor_payment_realloc (migration 4271); the voucher does not change.</summary>
public sealed record ReallocateVendorPaymentRequest(long PaymentId, IReadOnlyList<VendorAllocationDto> Allocations);

/// <summary>ACC-3 C1 SetAssetPutToUse K (PERM-ACC-010 Owner / Accountant): the date the asset was first used (default the business date).</summary>
public sealed record SetAssetPutToUseRequest(long AssetId, string? Date = null);

/// <summary>ACC-3 C1 DisposeFixedAsset K (PERM-ACC-010 Owner / Accountant; rulebook F4): sale value (0 = scrapped, nothing received), the
/// tender it came in by (needed when the value is above 0), date (default the business date), reason.</summary>
public sealed record DisposeFixedAssetRequest(long AssetId, long ProceedsPaise, string? Method = null, string? Date = null, string? Reason = null);

/// <summary>An open item of a vendor: a bill, or the opening balance (<c>Opening</c>, ExpenseId 0, dated the opening entry's asOf).</summary>
public sealed record VendorOpenBillView(long ExpenseId, string BusinessDate, string Category, string? Description, long PayablePaise, long ReturnedPaise, long AllocatedPaise,
    long OpenPaise, int Days, string Bucket, bool Opening = false);

/// <summary>Creditor ageing row: open bills by bucket (0–30 / 31–60 / 61–90 / 90+ days), the advance and the net (= the party ledger 2200/[vendor]).</summary>
public sealed record VendorAgeingRow(string VendorCode, string Vendor, long OpenPaise, long AdvancePaise, long NetPaise, IReadOnlyDictionary<string, long> Buckets,
    IReadOnlyList<VendorOpenBillView> Bills);

public sealed record VendorAgeingView(string AsOf, IReadOnlyList<VendorAgeingRow> Rows);

/// <summary>A register row. ACC-3: <c>CostPaise</c> is net of the posted returns against the purchase (C2; <c>OriginalCostPaise</c> − <c>ReturnedPaise</c>);
/// <c>DisposalMethod</c> / <c>DisposalFlag</c> (rulebook F4 "short-term capital gain / loss — CA") come with a disposal (C1).</summary>
public sealed record FixedAssetView(long AssetId, string AssetNo, string Ledger, string Name, long CostPaise, long ItcPaise, string PurchaseDate, string Vendor, long ExpenseId,
    string? PutToUse, string? DisposedDate, long? DisposedAmountPaise, string? DisposedVoucher, long? OriginalCostPaise = null, long ReturnedPaise = 0, string? DisposalMethod = null,
    string? DisposalFlag = null);

public sealed record FixedAssetResult(FixedAssetView Asset, string? ManualVoucherId, string? Flag);

public sealed record FixedAssetList(IReadOnlyList<FixedAssetView> Items);
