using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Application.Invoices;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Audit;
using Hcc.Domain.Billing;
using Hcc.Domain.Counter;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Domain.Stays;
using Hcc.Domain.Tax;

namespace Hcc.Application.Counter;

// ── contracts (LF-CONTRACT §6) ──

public sealed record CounterPartyIn(string Name, string? Phone = null, string? Gstin = null, string? Address = null, long? GuestId = null);

/// <summary>A line: free text with a GST category (food | laundry | banquet | other | room), or an extras / POS-menu item id with the price given.</summary>
public sealed record CounterLineIn(string Label, int Qty, long UnitPaise, string? GstCategory = null, string? ExtraId = null, string? MenuItemId = null);

/// <summary><see cref="Mode"/> paid | udhaar | company. Paid: <see cref="Method"/> (Cash needs <see cref="TillId"/>; UPI / Card / Bank transfer need a reference).</summary>
public sealed record CounterSettlementIn(string Mode, string? Method = null, string? Reference = null, int? TillId = null, long? CompanyId = null);

public sealed record IssueCounterBillRequest(CounterPartyIn Party, IReadOnlyList<CounterLineIn> Lines, CounterSettlementIn Settlement, long DiscountPaise = 0,
    string? DiscountReason = null, string? Remark = null);

public sealed record CounterBillResult(long BillId, string InvoiceNo, long NetPaise, long TaxPaise, long CgstPaise, long SgstPaise, long TotalPaise, string Settlement,
    long? UdhaarId, string Status);

public sealed record RequestCounterBillVoidRequest(long BillId, string Reason);

public sealed record CounterBillVoidRequestResult(long ApprovalId, string ApprovalVersion);

/// <summary>Accounting payload of CounterBillIssued / CounterBillCredited (SourceRules.Counter / CounterCredit).</summary>
public sealed record CounterBillPostedEvent(long BillId, string InvoiceNo, DateOnly BusinessDate, string Mode, string? Tender, string PartyName, string? PartyPhone,
    long? CompanyId, long FoodPaise, long RoomPaise, long ServicePaise, long DiscountPaise, long CgstPaise, long SgstPaise, long TotalPaise, string? CreditNoteNo = null);

// ── store port ──

public sealed record CounterBillHeader(long BillId, int PropertyId, string InvoiceNo, long? InvoiceId, DateOnly BusinessDate, string PartyName, string? PartyPhone,
    string? PartyGstin, string? PartyAddress, long? GuestId, long? CompanyId, Paise Discount, CounterTotals Totals, string Mode, string? Method, string? Reference,
    int? TillId, long? UdhaarId, string Status, long? VoidApprovalId);

public sealed record NewCounterBill(int PropertyId, string TempNo, DateOnly BusinessDate, DateTime Utc, int By, CounterPartyIn Party, long? CompanyId, Paise Discount,
    string? DiscountReason, CounterTotals Totals, string? Remark, string Mode, string? Method, string? Reference, string IdemKey);

public interface ICounterBillStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<long> InsertAsync(ICommandTransaction tx, NewCounterBill bill, IReadOnlyList<CounterLine> lines, CancellationToken ct);

    Task SetInvoiceAsync(ICommandTransaction tx, long billId, long invoiceId, string invoiceNo, CancellationToken ct);

    Task SetSettlementAsync(ICommandTransaction tx, long billId, int? tillId, long? movementId, long? udhaarId, CancellationToken ct);

    /// <summary>The bill (UPDLOCK) with its lines; null when not on the property.</summary>
    Task<(CounterBillHeader Bill, IReadOnlyList<CounterLine> Lines)?> LockAsync(ICommandTransaction tx, int propertyId, long billId, CancellationToken ct);

    Task SetVoidApprovalAsync(ICommandTransaction tx, long billId, long approvalId, CancellationToken ct);

    Task MarkCreditedAsync(ICommandTransaction tx, long billId, string creditNoteNo, string reason, DateOnly date, long approvalId, CancellationToken ct);

    Task<long> InsertUdhaarAsync(ICommandTransaction tx, int propertyId, string name, string? mobile, Paise amount, DateOnly date, string kind, int gstBps, long billId,
        string note, int by, DateTime utc, CancellationToken ct);

    /// <summary>Voids the bill's Udhaar receivable; INVALID_TRANSITION when money was already received against it.</summary>
    Task VoidUdhaarAsync(ICommandTransaction tx, long udhaarId, string reason, int by, DateOnly date, CancellationToken ct);

    /// <summary>Open (Issued) counter bills on the company's account (its credit exposure besides the stays / groups / receivable).</summary>
    Task<Paise> OpenCompanyBillsAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct);
}

/// <summary>
/// LF A7 — counter bill (legacy "Direct Bill", F11). <c>IssueCounterBill</c> (PERM-CTR-001): lines priced in paise, GST per line at the dated master
/// (by category on the business date; CGST + SGST per line half away from zero), an optional discount within the caller's PERM-FOL-003 limit (refused
/// above: no approval queue), one tax invoice in the INV series (source 'counter', B2B when a GSTIN is given), settled paid (cash into the open shift,
/// non-cash with a reference) / on Udhaar (receivable linked to the bill, 4691) / on company credit (limit incl. open counter bills). Outbox
/// <c>CounterBillIssued</c> → accounting Sales voucher (balanced). <c>RequestCounterBillVoid</c> (PERM-CTR-002) → approval 'counter-credit' (Owner) →
/// a full credit note (CRN), cash back out of the drawer / the receivable voided, status Credited, <c>CounterBillCredited</c> (the mirror voucher).
/// </summary>
public sealed class CounterBillService(ICommandRunner runner, IAccessPolicy policy, ICounterBillStore store, IBillingStore billing, IGstRateResolver gst,
    InvoiceService invoices, TimeProvider clock, ICashDrawerPort? drawer = null, ICompanyCreditGuard? credit = null,
    Accounting.CashGuardService? cashGuard = null, Catalogue.ICatalogueStore? catalogue = null, Pos.IPosStore? pos = null, MoneyMasters.IPaymentMethodSource? methods = null) : ILfApprovalHandler
{
    private async Task<IReadOnlyList<Hcc.Domain.MoneyMasters.TenderMethod>?> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        methods is null ? null : await methods.MethodsAsync(tx, propertyId, ct).ConfigureAwait(false);

    public const string OpIssue = "IssueCounterBill";
    public const string OpRequestVoid = "RequestCounterBillVoid";
    public const string PermIssue = "PERM-CTR-001";
    public const string PermVoid = "PERM-CTR-002";
    public const string EventIssued = "CounterBillIssued";
    public const string EventCredited = "CounterBillCredited";
    public const string DrawerSource = "counter-bill";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public AdjustmentAction Action => AdjustmentAction.CounterCredit;

    public async Task<CommandResult<CounterBillResult>> IssueAsync(CommandContext ctx, IssueCounterBillRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, PermIssue, ct: ct).ConfigureAwait(false);
        var party = req.Party ?? throw HccException.Validation("Name the customer.");
        var name = (party.Name ?? "").Trim();
        if (name.Length is 0 or > 80) throw HccException.Validation("Name the customer (up to 80 characters).");
        var phone = Digits(party.Phone);
        var gstin = string.IsNullOrWhiteSpace(party.Gstin) ? null : Gstin.Validate(party.Gstin, "customer");
        var lines = req.Lines ?? [];
        if (lines.Count is 0 or > CounterBillRules.MaxLines) throw HccException.Validation($"Add 1 to {CounterBillRules.MaxLines} lines.");
        var s = req.Settlement ?? throw HccException.Validation("Choose how the bill is settled.");
        var mode = (s.Mode ?? "").Trim().ToLowerInvariant();
        if (!CounterBillRules.Settlements.Contains(mode)) throw HccException.Validation("Settle the bill paid, on Udhaar or on company credit.");
        if (mode == CounterBillRules.Udhaar && phone is null && party.GuestId is null) throw HccException.Validation("An Udhaar bill needs the customer's mobile number.");
        if (mode == CounterBillRules.Company && s.CompanyId is null) throw HccException.Validation("Choose the company account.");
        Hcc.Domain.MoneyMasters.TenderMethod? method = null;
        var cash = false;
        if (mode == CounterBillRules.Paid)
        {
            var stored = await MethodsAsync(null, ctx.PropertyId, ct).ConfigureAwait(false);
            // AF-CTR-SVC (BIL-02 / MA-11): the counter uses the payment-methods master exactly as the folio receipt does (AF-DEC-2: front-desk place).
            method = Hcc.Domain.MoneyMasters.PaymentMethodBook.Pick(stored, s.Method, Hcc.Contracts.MoneyMasters.MoneyMasterValues.FrontDesk, s.Reference);
            cash = method.SettlesToTill;
            if (cash && s.TillId is null) throw HccException.Validation("Choose the till that receives the cash.");
        }

        var tender = method?.Name; // the master's canonical name is what gets stored ("upi" -> "UPI")

        var discount = new Paise(req.DiscountPaise);
        if (discount.Value < 0) throw HccException.Validation("The discount cannot be negative.");
        if (discount.Value > 0 && string.IsNullOrWhiteSpace(req.DiscountReason)) throw HccException.Validation("Give the reason for the discount.");

        return await runner.RunAsync(ctx with { Operation = OpIssue }, req, async (tx, c) =>
        {
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var priced = new List<CounterLine>();
            var seq = 0;
            IReadOnlyList<Catalogue.CatRow>? extras = null;
            foreach (var raw in lines)
            {
                seq++;
                // LF-int (LF-money open point): a line that names a catalogue extra or a POS menu item takes its price (and label / GST class) from the
                // master on the business date when the request gives no unit price; a given price is kept (prototype ctrLineFrom parity).
                var l = raw;
                if (Blank(l.ExtraId) is { } xid && catalogue is not null)
                {
                    extras ??= await catalogue.LockRowsAsync(tx, Catalogue.CatalogueMaster.Extras, ctx.PropertyId, c).ConfigureAwait(false);
                    var x = extras.FirstOrDefault(r => r.Id.ToString(CultureInfo.InvariantCulture) == xid || string.Equals(r.Code, xid, StringComparison.OrdinalIgnoreCase))
                        ?? throw HccException.Validation($"Line {N(seq)}: choose an item from the catalogue.");
                    if (!x.Active) throw HccException.Validation($"Line {N(seq)}: {x.Name} is retired — choose another item.");
                    var unit = l.UnitPaise > 0 ? l.UnitPaise : Hcc.Domain.Catalogue.CatalogueRules.AmountOn(x.Prices, bd)
                        ?? throw HccException.Validation($"Line {N(seq)}: {x.Name} has no price on {bd:yyyy-MM-dd} — enter the unit price.");
                    l = l with { Label = string.IsNullOrWhiteSpace(l.Label) ? x.Name : l.Label, UnitPaise = unit, GstCategory = l.GstCategory ?? x.F("gst_category") };
                }
                else if (Blank(l.MenuItemId) is { } mid && pos is not null)
                {
                    var m = int.TryParse(mid, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                        ? await pos.MenuItemAsync(tx, ctx.PropertyId, itemId, false, c).ConfigureAwait(false) : null;
                    if (m is null || !m.Active) throw HccException.Validation($"Line {N(seq)}: choose an item from the menu.");
                    var unit = l.UnitPaise > 0 ? l.UnitPaise : m.Priced && m.Price.Value > 0 ? m.Price.Value
                        : throw HccException.Validation($"Line {N(seq)}: {m.Name} has no price — enter the unit price.");
                    l = l with { Label = string.IsNullOrWhiteSpace(l.Label) ? m.Name : l.Label, UnitPaise = unit, GstCategory = l.GstCategory ?? m.GstClass };
                }

                if (CounterBillRules.LineRefusal(seq, l.Label ?? "", l.Qty, new Paise(l.UnitPaise)) is { } bad) throw HccException.Validation(bad);
                var cat = GstCategories.ForCharge(l.GstCategory, l.Label!);
                var g = cat == GstCategory.Room
                    ? await gst.RoomNightAsync(tx, ctx.PropertyId, bd, new Paise(l.UnitPaise), c).ConfigureAwait(false)
                    : await gst.ChargeAsync(tx, ctx.PropertyId, bd, cat, c).ConfigureAwait(false);
                priced.Add(new CounterLine(seq, l.Label!.Trim(), Blank(l.ExtraId), Blank(l.MenuItemId), l.Qty, new Paise(l.UnitPaise), cat, g.Bps));
            }

            if (CounterBillRules.BillRefusal(priced) is { } tooBig) throw HccException.Validation(tooBig);
            var gross = priced.Aggregate(Paise.Zero, (a, l) => a + l.Amount);
            if (discount.Value > 0)
            {
                var (may, limit) = await DiscountLimitAsync(ctx, discount, c).ConfigureAwait(false);
                if (CounterBillRules.DiscountRefusal(may, limit, discount, gross) is { } no) throw HccException.Validation(no);
            }

            var totals = CounterBillRules.Totals(priced, discount);
            if (totals.Total.Value <= 0) throw HccException.Validation("The bill total must be more than zero.");
            // LF-int (LF-money open point; ACC-2 contract §7, B9): one counter bill of the receipt limit or more in cash is refused (CASH_LIMIT), as a POS bill.
            if (cash && cashGuard is not null)
                await cashGuard.EnsureReceiptAsync(tx, ctx.PropertyId, new Accounting.CashParty(Accounting.CashParty.Bill, 0), totals.Total.Value, bd, "this counter bill", c).ConfigureAwait(false);
            long? companyId = null;
            if (mode == CounterBillRules.Company)
            {
                companyId = s.CompanyId!.Value;
                if (credit is not null)
                {
                    var company = await credit.LockAsync(tx, ctx.PropertyId, companyId.Value, c).ConfigureAwait(false);
                    var exposure = await credit.ExposureAsync(tx, ctx.PropertyId, companyId.Value, c).ConfigureAwait(false);
                    var open = await store.OpenCompanyBillsAsync(tx, ctx.PropertyId, companyId.Value, c).ConfigureAwait(false);
                    CompanyCreditRules.Ensure(company.CompanyId, company.Name, new Paise(company.CreditLimitPaise), exposure.Total + open, totals.Total, "counter bill on company credit");
                }
            }

            var now = Now;
            var temp = "CTR-" + Guid.NewGuid().ToString("N")[..12];
            var billId = await store.InsertAsync(tx, new NewCounterBill(ctx.PropertyId, temp, bd, now, ctx.PrincipalId, party with { Name = name, Phone = phone, Gstin = gstin },
                companyId, discount, Blank(req.DiscountReason), totals, Blank(req.Remark), mode, tender, Blank(s.Reference),
                ctx.IdempotencyKey), priced, c).ConfigureAwait(false);

            var issued = await invoices.IssueInTransactionAsync(tx, new IssueInvoiceRequest(CounterInvoiceSource.SourceKind, billId, gstin is null ? null : "B2B",
                name, gstin, Blank(party.Address)), c).ConfigureAwait(false);
            await store.SetInvoiceAsync(tx, billId, issued.InvoiceId, issued.InvoiceNo, c).ConfigureAwait(false);

            long? udhaarId = null;
            long? movementId = null;
            if (cash)
            {
                if (drawer is null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The cash drawer is not available.");
                movementId = await drawer.RecordMovementAsync(tx, s.TillId!.Value, CashMovementKind.PosSale, DrawerSource, billId, totals.Total,
                    $"Counter bill {issued.InvoiceNo} · {name}", issued.InvoiceNo, c).ConfigureAwait(false);
            }
            else if (mode == CounterBillRules.Udhaar)
            {
                udhaarId = await store.InsertUdhaarAsync(tx, ctx.PropertyId, name, phone is { Length: >= 10 } p ? p[^10..] : null, totals.Total, bd, CounterBillRules.UdhaarKind(priced),
                    CounterBillRules.Dominant(priced)!.Bps, billId, $"Counter bill {issued.InvoiceNo}", ctx.PrincipalId, now, c).ConfigureAwait(false);
            }

            await store.SetSettlementAsync(tx, billId, cash ? s.TillId : null, movementId, udhaarId, c).ConfigureAwait(false);
            await tx.AuditAsync("COUNTER_BILL_ISSUED", "counter-" + N(billId),
                $"{issued.InvoiceNo} · {PhoneDigitsScrub.Scrub(name)} · {priced.Count} line(s) · net {totals.Net.Value} + tax {totals.Tax.Value} = {totals.Total.Value} · {mode}" + (discount.Value > 0 ? $" · discount {discount.Value}" : ""), c).ConfigureAwait(false);
            await tx.PublishAsync(EventIssued, billId, Posted(billId, issued.InvoiceNo, bd, mode, tender, name, phone, companyId, priced, discount, totals), c).ConfigureAwait(false);
            return new CounterBillResult(billId, issued.InvoiceNo, totals.Net.Value, totals.Tax.Value, totals.Cgst.Value, totals.Sgst.Value, totals.Total.Value, mode, udhaarId, "Issued");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CounterBillVoidRequestResult>> RequestVoidAsync(CommandContext ctx, RequestCounterBillVoidRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is 0 or > 200) throw HccException.Validation("A reason is required (up to 200 characters).");
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, PermVoid, ct: ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // request-only holder (4689 'R'): exactly who the request is for
        }

        return await runner.RunAsync(ctx with { Operation = OpRequestVoid }, req, async (tx, c) =>
        {
            var (b, _) = await store.LockAsync(tx, ctx.PropertyId, req.BillId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Counter bill not found.");
            if (b.Status != "Issued") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This bill is already credited.");
            var now = Now;
            if (b.VoidApprovalId is { } prior && await billing.PeekApprovalAsync(tx, prior, c).ConfigureAwait(false) is { } pa
                && pa.Status is ApprovalStatus.Pending && !ApprovalRules.IsExpired(pa.Status, pa.Action, pa.ExpiresUtc, now))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A void request is already pending for this bill.");
            var (id, version) = await billing.InsertApprovalAsync(tx, new NewApproval(ctx.PropertyId, AdjustmentAction.CounterCredit, null, null, "counter", b.BillId,
                b.Totals.Total, null, null, null, reason, ctx.PrincipalId, now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            await store.SetVoidApprovalAsync(tx, b.BillId, id, c).ConfigureAwait(false);
            await tx.AuditAsync("COUNTER_BILL_VOID_REQUESTED", "counter-" + N(b.BillId), $"{b.InvoiceNo} · {b.Totals.Total.Value} · {reason} · approval {id}", c).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", id, new { approvalId = id, action = "counter-credit", billId = b.BillId, amountPaise = b.Totals.Total.Value }, c).ConfigureAwait(false);
            return new CounterBillVoidRequestResult(id, version);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>'counter-credit' decision (Owner only: PERM-INV-003 credit-note approval; never the requester).</summary>
    public async Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(approval);
        var (b, lines) = await store.LockAsync(tx, caller.PropertyId, approval.TargetId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Counter bill not found.");
        var a = (await billing.LockApprovalAsync(tx, approval.ApprovalId, ct).ConfigureAwait(false))!;
        BillingService.EnsureVersion(a.Version, expectedVersion, "The approval request");
        var now = Now;
        var expired = ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now);
        if (a.Status != ApprovalStatus.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
        if (a.RequestedBy == caller.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, InvoicePermissions.Approve, ct: ct).ConfigureAwait(false);
        if (!approve || expired)
        {
            if (approve) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request expired (24 hours). Dismiss it and request again.");
            var status = expired ? ApprovalStatus.Expired : ApprovalStatus.Rejected;
            var v0 = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(status, caller.PrincipalId, now, reason, null, null, null), ct).ConfigureAwait(false);
            await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"counter-credit · {status}", ct).ConfigureAwait(false);
            return new ApprovalResult(a.ApprovalId, "counter-credit", status.ToString(), a.Amount.Value, v0, null, null, null);
        }

        if (b.Status != "Issued") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This bill is already credited.");
        var bd = await store.BusinessDateAsync(tx, caller.PropertyId, ct).ConfigureAwait(false);
        if (b.UdhaarId is { } ud) await store.VoidUdhaarAsync(tx, ud, "Counter bill voided · " + a.Reason, caller.PrincipalId, bd, ct).ConfigureAwait(false);
        // Cash back out of the drawer the same way it came in (needs an open shift on the bill's till).
        if (b.Mode == CounterBillRules.Paid && b.TillId is { } till && drawer is not null)
            await drawer.RecordMovementAsync(tx, till, CashMovementKind.PosVoid, DrawerSource, b.BillId, -b.Totals.Total, $"Counter bill {b.InvoiceNo} voided", b.InvoiceNo, ct).ConfigureAwait(false);
        await store.MarkCreditedAsync(tx, b.BillId, "PENDING", a.Reason, bd, a.ApprovalId, ct).ConfigureAwait(false); // the source must read Credited before the note posts
        var note = await invoices.CreditOnSourceReversalAsync(tx, CounterInvoiceSource.SourceKind, b.BillId, a.RequestedBy, a.Reason, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The counter bill has no invoice to credit.");
        await store.MarkCreditedAsync(tx, b.BillId, note.CreditNoteNo, a.Reason, bd, a.ApprovalId, ct).ConfigureAwait(false);
        var v = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(ApprovalStatus.Executed, caller.PrincipalId, now, reason, caller.PrincipalId, now, note.CreditNoteId), ct).ConfigureAwait(false);
        await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"counter-credit · approved · {b.InvoiceNo} credited by {note.CreditNoteNo}", ct).ConfigureAwait(false);
        await tx.PublishAsync(EventCredited, b.BillId, Posted(b.BillId, b.InvoiceNo, bd, b.Mode, b.Method, b.PartyName, b.PartyPhone, b.CompanyId, lines, b.Discount, b.Totals) with { CreditNoteNo = note.CreditNoteNo }, ct).ConfigureAwait(false);
        return new ApprovalResult(a.ApprovalId, "counter-credit", nameof(ApprovalStatus.Executed), a.Amount.Value, v, note.CreditNoteId, null, null);
    }

    private async Task<(bool May, Paise? Limit)> DiscountLimitAsync(CommandContext ctx, Paise discount, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BillingPermissions.Discount, amountPaise: discount.Value, ct: ct).ConfigureAwait(false);
            return (true, null);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            throw HccException.Validation("This discount is above your limit. A counter bill has no approval queue: ask a Manager or the Owner to issue it.");
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.FORBIDDEN)
        {
            return (false, null);
        }
    }

    private static CounterBillPostedEvent Posted(long billId, string no, DateOnly date, string mode, string? tender, string name, string? phone, long? companyId,
        IReadOnlyList<CounterLine> lines, Paise discount, CounterTotals t)
    {
        long Sum(Func<CounterLine, bool> f) => lines.Where(f).Sum(l => l.Amount.Value);
        // CU digits: the outbox never carries the raw number. The phone travels as the "••••••1234" display mask and the name is scrubbed; the accounting
        // projector reads the real Udhaar party (UD-<mobile>) from ctr.bill itself (see AccountingVoucherProjector, counter cases).
        return new CounterBillPostedEvent(billId, no, date, mode, tender, PhoneDigitsScrub.Scrub(name), string.IsNullOrEmpty(phone) ? null : GuestRules.MaskPhone(phone), companyId, Sum(l => l.Category == GstCategory.Food), Sum(l => l.Category == GstCategory.Room),
            Sum(l => l.Category is not (GstCategory.Food or GstCategory.Room)), discount.Value, t.Cgst.Value, t.Sgst.Value, t.Total.Value);
    }

    private static string? Digits(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length == 0 ? null : d.Length > 20 ? d[^20..] : d;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The counter bill as an invoice source (INV series, source kind 'counter', 4687); credited only by its void (approval 'counter-credit').</summary>
public sealed class CounterInvoiceSource(ICounterBillStore store) : IInvoiceSource
{
    public const string SourceKind = "counter";

    public string Kind => SourceKind;

    public bool CreditNoteRequests => false;

    public string CreditNoteRefusal => "A counter bill is credited by its void request (Cash & Billing › Counter bills › Void); the Owner approves it.";

    public async Task LockAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct) =>
        _ = await store.LockAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Counter bill not found.");

    public async Task<InvoiceSourceDocument> LoadForIssueAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct)
    {
        var (b, lines) = await store.LockAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Counter bill not found.");
        if (b.Status != "Issued") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This counter bill was credited; there is nothing to invoice.");
        var src = lines.Select(l =>
        {
            var split = GstMath.SplitLine(l.Amount, l.Bps);
            return new SourceLine(l.Seq, "counter", b.BusinessDate, $"{l.Label} ×{l.Qty.ToString(CultureInfo.InvariantCulture)}", l.Category, l.Amount, l.Bps, split.Tax, split.Cgst, split.Sgst);
        }).ToList();
        if (b.Discount.Value > 0 && CounterBillRules.Dominant(lines) is { } d)
        {
            var split = GstMath.SplitLine(-b.Discount, d.Bps);
            src.Add(new SourceLine(null, "discount", b.BusinessDate, "Discount", d.Category, -b.Discount, d.Bps, split.Tax, split.Cgst, split.Sgst));
        }

        return new InvoiceSourceDocument(SourceKind, b.BillId, b.InvoiceNo, b.PartyName, b.BusinessDate, b.BusinessDate, src, InvoiceDocType.TaxInvoice);
    }

    public async Task<IReadOnlyList<long?>> PostCreditAsync(ICommandTransaction tx, int propertyId, long sourceId, string creditNoteNo, string reason, DateOnly date,
        IReadOnlyList<CreditPosting> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var (b, _) = await store.LockAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Counter bill not found.");
        if (b.Status != "Credited") throw new InvalidOperationException("A counter-bill invoice is credited only by its approved void.");
        return lines.Select(_ => (long?)null).ToList(); // CounterBillCredited books the reversal
    }
}
