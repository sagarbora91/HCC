using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;
using Hcc.Domain.MoneyMasters;
using Hcc.Domain.Payments;

namespace Hcc.Application.Expenses;

// ACC-2 · lane acc2-purchase — vendor payments / advances / voids, returns (credit notes), creditor ageing and the fixed-asset register
// (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3.2, §3.4, §3.5, §9; rulebook C3, C4, C5, C10). Ports the prototype handlers in
// services/mock/handlers/expenses-vendor.ts and the selectors in domain/streams/expenses-vendor.ts / accounting-assets.ts.

/// <summary>PERM-EXP-004 expenses.vendor.pay (Owner, Manager, Accountant; migration 4234).</summary>
public static class VendorPaymentPermissions
{
    public const string Pay = "PERM-EXP-004";

    /// <summary>Reads (payments, ageing, the asset register): the vendor-pay holders or the accounting viewers.</summary>
    public const string AccountingView = "PERM-ACC-001";
}

/// <summary>One stored payment (ops.vendor_payment + its allocations).</summary>
public sealed record VendorPaymentRecord(long PaymentId, int PropertyId, string VendorCode, string VendorName, DateOnly BusinessDate, long AmountPaise, string Method,
    string? Reference, int? TillId, long? CashMovementId, int RecordedBy, DateTime RecordedUtc, bool Voided, string? VoidReason, int? VoidedBy, DateOnly? VoidedDate,
    IReadOnlyList<VendorAllocationDto> Allocations)
{
    public long Allocated => Allocations.Sum(a => a.AmountPaise);

    /// <summary>The payment stands on <paramref name="asOf"/>: dated on or before it and not void — or voided on a later business day.</summary>
    public bool LiveOn(DateOnly asOf) => BusinessDate <= asOf && (!Voided || (VoidedDate is { } v && v > asOf && v != BusinessDate));
}

public sealed record NewVendorPayment(int PropertyId, string VendorCode, string VendorName, DateOnly BusinessDate, long AmountPaise, string Method, string? Reference,
    int? TillId, int RecordedBy, DateTime NowUtc, IReadOnlyList<VendorAllocationDto> Allocations);

/// <summary>A counted expense record of a vendor (bill, reversal or return; Posted / Reversed), for open bills and ageing.</summary>
public sealed record VendorBillRow(long ExpenseId, DateOnly BusinessDate, string Category, string? Description, string Method, string Status, long AmountPaise, long? TdsPaise,
    long? GstPaise, long? ReversalOf, long? ReturnOf);

/// <summary>Persistence of vendor payments (SQL adapter Infrastructure/Expenses/SqlVendorPaymentStore).</summary>
public interface IVendorPaymentStore
{
    Task<long> InsertAsync(ICommandTransaction tx, NewVendorPayment p, CancellationToken ct = default);

    Task SetCashMovementAsync(ICommandTransaction tx, long paymentId, long movementId, CancellationToken ct = default);

    Task<VendorPaymentRecord?> LockAsync(ICommandTransaction tx, int propertyId, long paymentId, CancellationToken ct = default);

    Task VoidAsync(ICommandTransaction tx, long paymentId, string reason, int by, DateOnly date, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Payments of the property (one vendor when <paramref name="vendorCode"/> is set), oldest first.</summary>
    Task<IReadOnlyList<VendorPaymentRecord>> ListAsync(ICommandTransaction? tx, int propertyId, string? vendorCode, CancellationToken ct = default);

    /// <summary>Counted records (Posted / Reversed) whose vendor is <paramref name="vendorName"/> or <paramref name="vendorCode"/> (any case).</summary>
    Task<IReadOnlyList<VendorBillRow>> BillsAsync(ICommandTransaction? tx, int propertyId, string vendorName, string vendorCode, CancellationToken ct = default);

    /// <summary>Σ returned (amount, GST) against a bill by return records that are not void / rejected (positive paise).</summary>
    Task<(long Amount, long Gst)> ReturnedAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default);
}

public sealed record NewFixedAsset(int PropertyId, string AssetNo, string Ledger, string Name, long CostPaise, long ItcPaise, DateOnly PurchaseDate, string Vendor, long ExpenseId,
    int CreatedBy, DateTime NowUtc);

/// <summary>The fixed-asset register (acc.fixed_asset, migration 4233).</summary>
public interface IFixedAssetStore
{
    Task<IReadOnlyList<string>> NumbersAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<long> InsertAsync(ICommandTransaction tx, NewFixedAsset a, CancellationToken ct = default);

    /// <summary>A reversed purchase marks its row disposed (amount, voucher) — never deleted.</summary>
    Task DisposeByExpenseAsync(ICommandTransaction tx, long expenseId, DateOnly date, long amountPaise, string voucher, CancellationToken ct = default);

    Task<IReadOnlyList<FixedAssetView>> ListAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// RecordVendorPayment, VoidVendorPayment, RecordExpenseReturn (contract §3.2, §3.5). Cash moves the shared drawer through
/// <see cref="ICashDrawerPort"/> (source type <c>vendor-payment</c>; a reversing void <c>vendor-pay-void</c>) inside the same transaction; every
/// payment / reversing void publishes <see cref="PurchaseEvents"/> for Accounting.
/// </summary>
public sealed class VendorPaymentCommands(ICommandRunner runner, IAccessPolicy policy, IVendorPaymentStore payments, IExpenseStore expenses, ICashDrawerPort drawer,
    TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null, MoneyMasters.IPaymentMethodSource? methods = null, IExpenseMasterSource? masters = null,
    IExpenseRegisterStore? registers = null)
{
    public const string OpPay = "RecordVendorPayment";
    public const string OpVoid = "VoidVendorPayment";
    public const string OpReturn = "RecordExpenseReturn";
    public const string DrawerSourcePay = "vendor-payment";
    public const string DrawerSourceVoid = "vendor-pay-void";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private async Task<IReadOnlyList<TenderMethod>?> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        methods is null ? null : await methods.MethodsAsync(tx, propertyId, ct).ConfigureAwait(false);

    private Task<bool> IsOwnerAsync(CommandContext ctx, CancellationToken ct) => policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct);

    /// <summary>Open bills of a vendor as of a date (credit-funded posted bills; payable less posted returns and live allocations).</summary>
    internal static IReadOnlyList<ExpensePurchase.OpenBill> OpenBills(IReadOnlyList<VendorBillRow> rows, IReadOnlyList<VendorPaymentRecord> pays, IReadOnlyList<TenderMethod>? methods,
        DateOnly asOf)
    {
        var returned = rows.Where(r => r.ReturnOf is not null && r.BusinessDate <= asOf).GroupBy(r => r.ReturnOf!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => -(r.AmountPaise - (r.TdsPaise ?? 0))));
        var alloc = pays.Where(p => p.LiveOn(asOf)).SelectMany(p => p.Allocations).GroupBy(a => a.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(a => a.AmountPaise));
        return rows.Where(r => r.Status == ExpenseState.Posted && r.ReversalOf is null && r.ReturnOf is null && r.AmountPaise > 0 && r.BusinessDate <= asOf
                && PaymentMethodBook.KindOf(methods, r.Method) == MoneyMasterValues.Credit)
            .OrderBy(r => r.BusinessDate).ThenBy(r => r.ExpenseId)
            .Select(r => new ExpensePurchase.OpenBill(r.ExpenseId, r.BusinessDate, r.AmountPaise - (r.TdsPaise ?? 0), returned.GetValueOrDefault(r.ExpenseId), alloc.GetValueOrDefault(r.ExpenseId)))
            .ToList();
    }

    /// <summary>The party ledger 2200/[vendor] balance (positive = the hotel owes): credit-funded counted records net of TDS − live payments.</summary>
    internal static long Balance(IReadOnlyList<VendorBillRow> rows, IReadOnlyList<VendorPaymentRecord> pays, IReadOnlyList<TenderMethod>? methods, DateOnly asOf) =>
        rows.Where(r => r.BusinessDate <= asOf && PaymentMethodBook.KindOf(methods, r.Method) == MoneyMasterValues.Credit).Sum(r => r.AmountPaise - (r.TdsPaise ?? 0))
        - pays.Where(p => p.LiveOn(asOf)).Sum(p => p.AmountPaise);

    public async Task<CommandResult<VendorPaymentResult>> PayAsync(CommandContext ctx, RecordVendorPaymentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, VendorPaymentPermissions.Pay, null, null, ct).ConfigureAwait(false);
        if (req.AmountPaise is <= 0 or > 10_000_000_000) throw HccException.Validation("Enter the amount paid (more than zero, at most ₹10,00,00,000).");
        var reference = ExpenseScc.Optional(req.Reference, 120, "reference");
        DateOnly? chosen = string.IsNullOrWhiteSpace(req.BusinessDate) ? null : StayDates.Parse(req.BusinessDate, "businessDate");

        return await runner.RunAsync(ctx with { Operation = OpPay }, req, async (tx, c) =>
        {
            var ms = await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var m = PaymentMethodBook.Pick(ms, req.Method, MoneyMasterValues.Expenses, reference, requireReference: false);
            if (m.IsCredit) throw HccException.Validation("A vendor payment moves money: choose cash, UPI or a bank transfer — not a credit method.");
            if (!m.SettlesToTill && reference is null) throw HccException.Validation($"Enter the {m.Name} reference (UTR / UPI reference) of the payment.");
            var vendors = masters is null ? [] : await masters.VendorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var v = ExpenseCommands.VendorRow(vendors, req.Vendor) ?? throw HccException.Validation("Choose a vendor from the list (Master configuration › Expense vendors).");
            if (!v.Active) throw Hcc.Domain.Masters.MastersRules.Fail(Hcc.Contracts.Masters.MasterReasons.Retired, $"Vendor {v.Name} is retired — choose an active vendor.", "vendor");
            var bd = await expenses.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var date = chosen ?? bd;
            if (days is not null && date == bd) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await ExpenseCommands.DateGateAsync(days, tx, ctx.PropertyId, bd, date, m.SettlesToTill, c).ConfigureAwait(false);
            int? till = null;
            if (m.SettlesToTill) till = req.TillId ?? throw HccException.Validation("Choose the till that pays this vendor in cash (open cash shift required).");
            // acc2-flows lane: the B10 cash guard (accCashGuard / CashGuard.cs) warns above ₹10,000 to one person in a day — wired at integration.

            var rows = await payments.BillsAsync(tx, ctx.PropertyId, v.Name, v.Code, c).ConfigureAwait(false);
            var pays = await payments.ListAsync(tx, ctx.PropertyId, v.Code, c).ConfigureAwait(false);
            var open = OpenBills(rows, pays, ms, date);
            IReadOnlyList<VendorAllocationDto> allocations;
            if (req.Allocations is { } sent)
            {
                var list = sent.Select(a => (a.ExpenseId, a.AmountPaise)).ToList();
                if (ExpensePurchase.AllocationRefusal(open, req.AmountPaise, list) is { } why) throw HccException.Validation(why);
                allocations = sent;
            }
            else
            {
                allocations = ExpensePurchase.Allocate(open, req.AmountPaise).Select(a => new VendorAllocationDto(a.ExpenseId, a.Amount)).ToList();
            }

            var now = UtcNow;
            var id = await payments.InsertAsync(tx, new NewVendorPayment(ctx.PropertyId, v.Code, v.Name, date, req.AmountPaise, m.Name, reference, till, ctx.PrincipalId, now, allocations), c)
                .ConfigureAwait(false);
            long? movement = null;
            if (till is { } t)
            {
                movement = await drawer.RecordMovementAsync(tx, t, CashMovementKind.Expense, DrawerSourcePay, id, new Paise(-req.AmountPaise), $"Vendor payment · {v.Name}",
                    reference ?? string.Empty, c).ConfigureAwait(false);
                await payments.SetCashMovementAsync(tx, id, movement.Value, c).ConfigureAwait(false);
            }

            var allocated = allocations.Sum(a => a.AmountPaise);
            await tx.AuditAsync("VENDOR_PAYMENT", Target(id), $"{v.Name} · {new Paise(req.AmountPaise)} · {m.Name}{(reference is null ? "" : " · " + reference)}"
                + (req.AmountPaise > allocated ? " · advance " + new Paise(req.AmountPaise - allocated) : string.Empty), c).ConfigureAwait(false);
            await tx.PublishAsync(PurchaseEvents.VendorPaymentRecorded, id, new
            {
                paymentId = id, businessDate = StayDates.ToWire(date), vendorCode = v.Code, vendor = v.Name, tender = m.Name, amountPaise = req.AmountPaise,
                allocatedPaise = allocated, reference, tillId = till, cashMovementId = movement,
            }, c).ConfigureAwait(false);
            var p = (await payments.LockAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false))!;
            return new VendorPaymentResult(ToView(p), req.AmountPaise - allocated);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<VendorPaymentResult>> VoidAsync(CommandContext ctx, VoidVendorPaymentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await IsOwnerAsync(ctx, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: only the Owner voids a vendor payment.");
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required to void a vendor payment.");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");

        return await runner.RunAsync(ctx with { Operation = OpVoid }, req, async (tx, c) =>
        {
            var p = await payments.LockAsync(tx, ctx.PropertyId, req.PaymentId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Vendor payment not found.");
            if (p.Voided) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This vendor payment is already void.");
            var bd = await expenses.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var cash = PaymentMethodBook.SettlesToTill(await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), p.Method);
            var now = UtcNow;
            await payments.VoidAsync(tx, p.PaymentId, reason, ctx.PrincipalId, bd, now, c).ConfigureAwait(false);
            if (cash)
            {
                var till = req.TillId ?? throw HccException.Validation("Choose the till the cash of the voided payment comes back into.");
                await drawer.RecordMovementAsync(tx, till, CashMovementKind.ExpenseReversal, DrawerSourceVoid, p.PaymentId, new Paise(p.AmountPaise), $"Vendor payment void · {p.VendorName}",
                    reason, c).ConfigureAwait(false);
            }

            var sameDay = ExpensePurchase.SameDayVoid(p.BusinessDate, bd);
            await tx.AuditAsync("VENDOR_PAYMENT_VOID", Target(p.PaymentId), $"{p.VendorName} · {new Paise(p.AmountPaise)} · {reason} · " + (sameDay ? "same day: nothing posts" : "reversing voucher"), c)
                .ConfigureAwait(false);
            await tx.PublishAsync(PurchaseEvents.VendorPaymentVoided, p.PaymentId, new
            {
                paymentId = p.PaymentId, businessDate = StayDates.ToWire(p.BusinessDate), vendorCode = p.VendorCode, vendor = p.VendorName, tender = p.Method,
                amountPaise = p.AmountPaise, allocatedPaise = p.Allocated, reference = p.Reference, voidDate = StayDates.ToWire(bd), voidReason = reason,
            }, c).ConfigureAwait(false);
            var u = (await payments.LockAsync(tx, ctx.PropertyId, p.PaymentId, c).ConfigureAwait(false))!;
            return new VendorPaymentResult(ToView(u), 0);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExpenseResult>> ReturnAsync(CommandContext ctx, RecordExpenseReturnRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, VendorPaymentPermissions.Pay, null, null, ct).ConfigureAwait(false);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required for a return.");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        var owner = await IsOwnerAsync(ctx, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpReturn }, req, async (tx, c) =>
        {
            var o = await expenses.LockAsync(tx, ctx.PropertyId, req.ExpenseId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Bill not found.");
            if (o.Status != ExpenseState.Posted || o.IsReversal || o.ReturnOf is not null || o.Amount.Value <= 0)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a posted bill (not reversed) can be returned.");
            if (string.IsNullOrWhiteSpace(o.Vendor)) throw HccException.Validation("This bill names no vendor — a return needs the vendor.");
            var done = await payments.ReturnedAsync(tx, o.ExpenseId, c).ConfigureAwait(false);
            if (ExpensePurchase.ReturnRefusal(o.Amount.Value, o.GstPaise, done.Amount, done.Gst, req.AmountPaise, req.GstPaise) is { } why) throw HccException.Validation(why);
            var ms = await MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var method = PaymentMethodBook.KindOf(ms, o.Method) == MoneyMasterValues.Credit ? o.Method
                : PaymentMethodBook.NamesAt(ms, MoneyMasterValues.Expenses).FirstOrDefault(n => PaymentMethodBook.KindOf(ms, n) == MoneyMasterValues.Credit)
                  ?? throw HccException.Validation("No credit method is set up for expenses (Master configuration › Payment methods, kind Credit).");
            var bd = await expenses.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await ExpenseCommands.DateGateAsync(days, tx, ctx.PropertyId, bd, bd, false, c).ConfigureAwait(false);
            await ExpenseCommands.MonthGateAsync(registers, tx, ctx.PropertyId, bd, false, null, UtcNow, c).ConfigureAwait(false);
            var now = UtcNow;
            long? tds = o.TdsPaise is { } t ? -(long)Math.Round((decimal)t * req.AmountPaise / o.Amount.Value, MidpointRounding.AwayFromZero) : null;
            var r = await expenses.InsertAsync(tx, new NewExpense(ctx.PropertyId, o.Vendor, o.Category, new Paise(-req.AmountPaise), method, bd,
                owner ? ExpenseState.Posted : ExpenseState.Pending, ctx.PrincipalId, null, null, reason, owner ? ctx.PrincipalId : null, null, null, now,
                $"Return to {o.Vendor} · {reason}", req.GstPaise > 0 ? o.GstRateBps : null, req.GstPaise > 0 ? -req.GstPaise : null, null, "ret:" + o.ExpenseId.ToString(CultureInfo.InvariantCulture),
                ItcAllowed: o.ItcAllowed && (req.GstPaise > 0 || o.Rcm), GstKind: o.GstKind, Rcm: o.Rcm, RcmBps: o.Rcm ? o.RcmBps ?? 0 : null, Capital: o.Capital,
                TdsPaise: tds is 0 ? null : tds, TdsSection: tds is null or 0 ? null : o.TdsSection, ReturnOf: o.ExpenseId), c).ConfigureAwait(false);
            await tx.AuditAsync(owner ? "EXPENSE_RETURN_POSTED" : "EXPENSE_RETURN_REQUESTED", ExpenseCommands.Target(r.ExpenseId),
                $"return against {ExpenseMapping.Ref(o.ExpenseId)} · {o.Vendor} · {new Paise(req.AmountPaise)}{(req.GstPaise > 0 ? " · GST " + new Paise(req.GstPaise) : "")} · {reason}", c).ConfigureAwait(false);
            if (owner) await ExpenseCommands.PublishReturnAsync(tx, r, ctx.PrincipalId, c).ConfigureAwait(false);
            return new ExpenseResult(ExpenseMapping.ToView(r), [], null);
        }, ct).ConfigureAwait(false);
    }

    internal static string Target(long id) => "vendor-payment:" + id.ToString(CultureInfo.InvariantCulture);

    internal static VendorPaymentView ToView(VendorPaymentRecord p) => new(p.PaymentId, p.VendorCode, p.VendorName, StayDates.ToWire(p.BusinessDate), p.AmountPaise, p.Method,
        p.Reference, p.RecordedBy, DateTime.SpecifyKind(p.RecordedUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture), p.Allocations, p.Voided, p.VoidReason,
        p.VoidedBy, p.VoidedDate is { } d ? StayDates.ToWire(d) : null, p.CashMovementId);
}

/// <summary>Reads: GET /expenses/vendor-payments, /expenses/vendor-ageing?asOf=&amp;vendor=, /accounting/assets (PERM-EXP-004 or PERM-ACC-001).</summary>
public sealed class VendorPaymentQueries(IAccessPolicy policy, IVendorPaymentStore payments, IExpenseStore expenses, IFixedAssetStore? assets = null,
    MoneyMasters.IPaymentMethodSource? methods = null, IExpenseMasterSource? masters = null)
{
    private async Task RequireAsync(int propertyId, int principalId, CancellationToken ct)
    {
        if (await policy.HasAsync(propertyId, principalId, VendorPaymentPermissions.Pay, null, ct).ConfigureAwait(false)) return;
        await policy.RequireAsync(propertyId, principalId, VendorPaymentPermissions.AccountingView, null, null, ct).ConfigureAwait(false);
    }

    public async Task<VendorPaymentList> ListAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, ct).ConfigureAwait(false);
        return new VendorPaymentList((await payments.ListAsync(null, propertyId, null, ct).ConfigureAwait(false)).Select(VendorPaymentCommands.ToView).ToList());
    }

    public async Task<VendorAgeingView> AgeingAsync(int propertyId, int principalId, string? asOf = null, string? vendor = null, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var date = string.IsNullOrWhiteSpace(asOf) ? await expenses.ReadBusinessDateAsync(propertyId, ct).ConfigureAwait(false) : StayDates.Parse(asOf, "asOf");
        var ms = methods is null ? null : await methods.MethodsAsync(null, propertyId, ct).ConfigureAwait(false);
        var vendors = masters is null ? [] : await masters.VendorsAsync(null, propertyId, ct).ConfigureAwait(false);
        var all = await payments.ListAsync(null, propertyId, null, ct).ConfigureAwait(false);
        var want = string.IsNullOrWhiteSpace(vendor) ? null : ExpenseCommands.VendorRow(vendors, vendor)?.Code ?? vendor.Trim();
        var rows = new List<VendorAgeingRow>();
        foreach (var v in vendors.Where(v => want is null || v.Code == want))
        {
            var bills = await payments.BillsAsync(null, propertyId, v.Name, v.Code, ct).ConfigureAwait(false);
            var pays = all.Where(p => p.VendorCode == v.Code).ToList();
            var open = VendorPaymentCommands.OpenBills(bills, pays, ms, date).Where(b => b.Open > 0).ToList();
            var balance = VendorPaymentCommands.Balance(bills, pays, ms, date);
            var openTotal = open.Sum(b => b.Open);
            if (openTotal == 0 && balance >= 0) continue;
            var buckets = ExpensePurchase.AgeBuckets.ToDictionary(b => b, _ => 0L);
            var views = new List<VendorOpenBillView>();
            foreach (var b in open)
            {
                var d = date.DayNumber - b.Date.DayNumber;
                var bucket = ExpensePurchase.AgeBucket(d);
                buckets[bucket] += b.Open;
                var row = bills.First(x => x.ExpenseId == b.ExpenseId);
                views.Add(new VendorOpenBillView(b.ExpenseId, StayDates.ToWire(b.Date), row.Category, row.Description, b.Payable, b.Returned, b.Allocated, b.Open, d, bucket));
            }

            rows.Add(new VendorAgeingRow(v.Code, v.Name, openTotal, Math.Max(0, openTotal - balance), balance, buckets, views));
        }

        return new VendorAgeingView(StayDates.ToWire(date), rows);
    }

    public async Task<FixedAssetList> AssetsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, ct).ConfigureAwait(false);
        return new FixedAssetList(assets is null ? [] : await assets.ListAsync(propertyId, ct).ConfigureAwait(false));
    }
}
