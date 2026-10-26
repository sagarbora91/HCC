using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;

namespace Hcc.Application.Invoices;

/// <summary>
/// Invoice and credit-note commands: IssueInvoice, RequestCreditNote, DecideCreditNote. Every command goes through the business-day guarded
/// <see cref="ICommandRunner"/> from DI (DAY_CLOSED on a closed date), with idempotency, audit and outbox in one transaction. The document
/// number is allocated in the same transaction as the document (gap-free); issued documents are immutable.
/// Prototype rules (handlers/folio.ts): number once per stay, after checkout; credit note only for an invoiced (departed) stay, with a
/// reason, never above what is still on the invoice, one pending request at a time, approved by an Owner and never by the requester.
/// </summary>
public sealed class InvoiceService(ICommandRunner runner, IAccessPolicy policy, IInvoiceStore store, IEnumerable<IInvoiceSource> sources, TimeProvider clock,
    ISettingsReader? settings = null)
{
    public const string OpIssue = "IssueInvoice";
    public const string OpRequestCreditNote = "RequestCreditNote";
    public const string OpDecideCreditNote = "DecideCreditNote";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private IInvoiceSource Source(string? kind) =>
        sources.FirstOrDefault(s => string.Equals(s.Kind, kind?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw HccException.Validation("Source kind must be folio (group invoices are not available yet).");

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    // ── IssueInvoice ──

    public async Task<CommandResult<IssueInvoiceResult>> IssueInvoiceAsync(CommandContext ctx, IssueInvoiceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, InvoicePermissions.Issue, ct: ct).ConfigureAwait(false);
        var source = Source(req.SourceKind);
        return await runner.RunAsync(ctx with { Operation = OpIssue }, req, async (tx, c) =>
        {
            var doc = await source.LoadForIssueAsync(tx, ctx.PropertyId, req.SourceId, c).ConfigureAwait(false);
            if (await store.InvoiceIdForSourceAsync(tx, ctx.PropertyId, source.Kind, doc.SourceId, c).ConfigureAwait(false) is { } existingId)
            {
                var existing = (await store.ReadInvoiceAsync(tx, ctx.PropertyId, existingId, c).ConfigureAwait(false))!;
                await tx.AuditAsync("INVOICE_ALREADY_ISSUED", "invoice:" + N(existingId), $"{existing.InvoiceNo} returned for {source.Kind} {N(doc.SourceId)}", c).ConfigureAwait(false);
                return new IssueInvoiceResult(existingId, existing.InvoiceNo, InvoiceNumbering.Code(existing.DocType), existing.InvoiceDate, existing.Figures.Total.Value, false);
            }

            var supplier = await SupplierAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            // Decision (W4 D1): every document here is a GST tax invoice / bill of supply, B2C included, so the hotel must be GST-registered
            // with a valid GSTIN before the first one. Nothing is numbered without it; the checkout itself is not undone.
            if (string.IsNullOrWhiteSpace(supplier.Gstin)) throw new HccException(HccErrorCode.INVALID_TRANSITION, MissingGstinMessage);
            if (Gstin.Problem(supplier.Gstin, "hotel") is { } gstinProblem) throw HccException.Validation($"{gstinProblem} {GstinSettingHint}");
            var supplierGstin = Gstin.Validate(supplier.Gstin, "hotel");
            var supplierState = Gstin.StateCode(supplierGstin);
            var gross = InvoiceRules.Totals(InvoiceRules.BuildLines(doc.Lines, interState: false)).Total;
            var recipient = InvoiceRules.Recipient(req.SupplyType, req.BuyerName ?? (req.SupplyType is null or "" or "B2C" or "b2c" ? doc.DefaultRecipientName : null),
                req.BuyerGstin, req.BuyerAddress, req.PlaceOfSupply, supplierState, gross);
            var lines = InvoiceRules.BuildLines(doc.Lines, recipient.InterState);
            var figures = InvoiceRules.Totals(lines);
            if (figures.Total < Paise.Zero) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The folio total is negative; there is nothing to invoice.");
            var type = InvoiceRules.DocTypeFor(lines);
            var date = supplier.BusinessDate;
            var fy = InvoiceNumbering.FyCode(date);
            var prefix = type == InvoiceDocType.TaxInvoice ? await PrefixAsync(tx, ctx.PropertyId, c).ConfigureAwait(false) : InvoiceNumbering.DefaultPrefix(type);
            var (seriesPrefix, seq) = await store.NextNumberAsync(tx, ctx.PropertyId, fy, type, prefix, Now, c).ConfigureAwait(false);
            var number = InvoiceNumbering.Format(seriesPrefix, fy, seq);
            var record = new InvoiceRecord(0, ctx.PropertyId, type, number, fy, seq, date, source.Kind, doc.SourceId, doc.Reference, doc.PeriodFrom, doc.PeriodTo,
                recipient, supplier.Name, supplierGstin, supplierState, supplier.Address, lines, figures, InvoiceRules.Words(figures.Total), ctx.PrincipalId, Now);
            var id = await store.InsertInvoiceAsync(tx, record, c).ConfigureAwait(false);
            await tx.AuditAsync("INVOICE_ISSUED", "invoice:" + N(id),
                $"{InvoiceNumbering.Label(type)} {number} · {source.Kind} {N(doc.SourceId)} · {recipient.Type} · taxable {figures.Taxable} + tax {figures.Tax} = {figures.Total}", c).ConfigureAwait(false);
            await tx.PublishAsync("InvoiceIssued", id, new
            {
                invoiceId = id, invoiceNo = number, docType = InvoiceNumbering.Code(type), invoiceDate = date, sourceKind = source.Kind, sourceId = doc.SourceId,
                supplyType = recipient.Type.ToString(), interState = recipient.InterState, taxablePaise = figures.Taxable.Value, cgstPaise = figures.Cgst.Value,
                sgstPaise = figures.Sgst.Value, igstPaise = figures.Igst.Value, totalPaise = figures.Total.Value,
            }, c).ConfigureAwait(false);
            return new IssueInvoiceResult(id, number, InvoiceNumbering.Code(type), date, figures.Total.Value, true);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Where the hotel GSTIN is recorded (named in IssueInvoice refusals).</summary>
    public const string GstinSettingHint = "An Owner records it in Master configuration › Property and branding › GSTIN on documents (brand.gstin); then issue the invoice from the departed stay's folio.";

    public const string MissingGstinMessage = "No tax invoice can be issued: the hotel's GSTIN is not recorded. " + GstinSettingHint;

    /// <summary>The property row (as registered at setup). The GSTIN on documents is the Master configuration value (brand.gstin, seeded
    /// from setup and corrected there later); the property row's GSTIN is the fallback while that setting is empty. The address falls back
    /// to brand.address.</summary>
    private async Task<SupplierInfo> SupplierAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        var s = await store.SupplierAsync(tx, propertyId, ct).ConfigureAwait(false);
        if (settings is null) return s;
        var configured = (await settings.GetAsync(tx, propertyId, "brand.gstin", null, ct).ConfigureAwait(false)).Text;
        if (!string.IsNullOrWhiteSpace(configured)) s = s with { Gstin = configured.Trim() };
        if (string.IsNullOrWhiteSpace(s.Address)) s = s with { Address = (await settings.GetAsync(tx, propertyId, "brand.address", null, ct).ConfigureAwait(false)).Text };
        return s with { Gstin = string.IsNullOrWhiteSpace(s.Gstin) ? null : s.Gstin, Address = string.IsNullOrWhiteSpace(s.Address) ? null : s.Address };
    }

    private async Task<string> PrefixAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        if (settings is null) return InvoiceNumbering.DefaultPrefix(InvoiceDocType.TaxInvoice);
        var v = await settings.GetAsync(tx, propertyId, InvoicePermissions.PrefixSetting, null, ct).ConfigureAwait(false);
        return InvoiceNumbering.ValidatePrefix(string.IsNullOrWhiteSpace(v.Text) ? InvoiceNumbering.DefaultPrefix(InvoiceDocType.TaxInvoice) : v.Text);
    }

    // ── RequestCreditNote ──

    public async Task<CommandResult<CreditNoteRequestResult>> RequestCreditNoteAsync(CommandContext ctx, RequestCreditNoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var owner = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, InvoicePermissions.Approve, ct: ct).ConfigureAwait(false);
        if (!owner) await RequireRequesterAsync(ctx).ConfigureAwait(false);
        var reason = BillingText.Required(req.Reason, 200, "A reason is required.");
        var mode = CreditNoteRules.ParseMode(req.Mode);
        Paise? amount = null;
        IReadOnlyList<RequestedCreditLine> requested = [];
        switch (mode)
        {
            case CreditMode.Amount:
                if (req.AmountPaise is not { } a) throw HccException.Validation("Enter the taxable amount to credit.");
                amount = Paise.FromWire(a).EnsureReceiptAmount();
                break;
            case CreditMode.Lines:
                if (req.Lines is not { Count: > 0 } ls) throw HccException.Validation("Choose the invoice lines to credit.");
                if (ls.Count > 50) throw HccException.Validation("Name at most 50 lines.");
                requested = ls.Select(l => new RequestedCreditLine(l.InvoiceLineNo, Paise.FromWire(l.TaxablePaise).EnsureReceiptAmount())).ToList();
                break;
        }

        return await runner.RunAsync(ctx with { Operation = OpRequestCreditNote }, req, async (tx, c) =>
        {
            var inv = await store.ReadInvoiceAsync(tx, ctx.PropertyId, req.InvoiceId, c).ConfigureAwait(false) ?? throw NotFound("Invoice not found.");
            await Source(inv.SourceKind).LockAsync(tx, ctx.PropertyId, inv.SourceId, c).ConfigureAwait(false);
            var now = Now;
            if (await store.PendingRequestAsync(tx, inv.InvoiceId, c).ConfigureAwait(false) is { } pending)
            {
                if (now <= pending.ExpiresUtc) throw Invalid("A credit-note request is already pending for this invoice.");
                await store.UpdateRequestAsync(tx, pending.RequestId, pending.Version, "Expired", null, null, null, null, c).ConfigureAwait(false);
            }

            // Validate the plan now (limits, rounding); it is planned again at approval against the then-current credits.
            CreditNoteRules.Plan(await CreditableAsync(tx, inv, c).ConfigureAwait(false), mode, amount, requested, inv.Recipient.InterState);
            var row = await store.InsertRequestAsync(tx, new NewCreditNoteRequest(ctx.PropertyId, inv.InvoiceId, mode, amount, requested, reason, owner, ctx.PrincipalId,
                now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            if (owner)
            {
                var (note, version) = await IssueCoreAsync(tx, inv, row, ctx.PrincipalId, c).ConfigureAwait(false);
                return new CreditNoteRequestResult("Issued", row.RequestId, version, note.CreditNoteId, note.CreditNoteNo, note.Figures.Total.Value);
            }

            await tx.AuditAsync("CREDIT_NOTE_REQUESTED", "invoice:" + N(inv.InvoiceId),
                $"{inv.InvoiceNo} · {CreditNoteRules.Code(mode)}{(amount is { } x ? " " + x : "")} · {reason} · request {N(row.RequestId)}", c).ConfigureAwait(false);
            await tx.PublishAsync("CreditNoteRequested", row.RequestId, new { requestId = row.RequestId, invoiceId = inv.InvoiceId, invoiceNo = inv.InvoiceNo, mode = CreditNoteRules.Code(mode) }, c).ConfigureAwait(false);
            return new CreditNoteRequestResult("PendingApproval", row.RequestId, row.Version, null, null, null);
        }, ct).ConfigureAwait(false);
    }

    // ── DecideCreditNote ──

    public async Task<CommandResult<CreditNoteRequestResult>> DecideCreditNoteAsync(CommandContext ctx, DecideCreditNoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, InvoicePermissions.Approve, ct: ct).ConfigureAwait(false);
        VersionToken.Validate(req.ExpectedVersion);
        var decisionReason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(ctx with { Operation = OpDecideCreditNote }, req, async (tx, c) =>
        {
            var peek = await store.PeekRequestAsync(tx, ctx.PropertyId, req.RequestId, c).ConfigureAwait(false) ?? throw NotFound("Credit-note request not found.");
            var inv = (await store.ReadInvoiceAsync(tx, ctx.PropertyId, peek.InvoiceId, c).ConfigureAwait(false))!;
            await Source(inv.SourceKind).LockAsync(tx, ctx.PropertyId, inv.SourceId, c).ConfigureAwait(false); // lock order: source, then request
            var r = (await store.LockRequestAsync(tx, req.RequestId, c).ConfigureAwait(false))!;
            BillingService.EnsureVersion(r.Version, req.ExpectedVersion, "The credit-note request");
            if (r.Status != "Pending") throw Invalid("This request is no longer pending.");
            if (r.RequestedBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve your own request (DEC-063).");
            var now = Now;
            if (now > r.ExpiresUtc)
            {
                if (req.Approve) throw Invalid("This request expired (24 hours). Dismiss it and request again.");
                var v = await store.UpdateRequestAsync(tx, r.RequestId, r.Version, "Expired", ctx.PrincipalId, now, decisionReason.Length > 0 ? decisionReason : null, null, c).ConfigureAwait(false);
                await tx.AuditAsync("CREDIT_NOTE_EXPIRED", "invoice:" + N(inv.InvoiceId), $"request {N(r.RequestId)} dismissed after expiry", c).ConfigureAwait(false);
                return new CreditNoteRequestResult("Expired", r.RequestId, v, null, null, null);
            }

            if (!req.Approve)
            {
                var v = await store.UpdateRequestAsync(tx, r.RequestId, r.Version, "Rejected", ctx.PrincipalId, now, decisionReason.Length > 0 ? decisionReason : null, null, c).ConfigureAwait(false);
                await tx.AuditAsync("CREDIT_NOTE_REJECTED", "invoice:" + N(inv.InvoiceId), $"request {N(r.RequestId)}{(decisionReason.Length > 0 ? " · " + decisionReason : "")}", c).ConfigureAwait(false);
                return new CreditNoteRequestResult("Rejected", r.RequestId, v, null, null, null);
            }

            var (note, version) = await IssueCoreAsync(tx, inv, r, ctx.PrincipalId, c, decisionReason.Length > 0 ? decisionReason : null).ConfigureAwait(false);
            return new CreditNoteRequestResult("Issued", r.RequestId, version, note.CreditNoteId, note.CreditNoteNo, note.Figures.Total.Value);
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CreditableLine>> CreditableAsync(ICommandTransaction tx, InvoiceRecord inv, CancellationToken ct)
    {
        var credited = await store.CreditedAsync(tx, inv.InvoiceId, ct).ConfigureAwait(false);
        return inv.Lines.Select(l => credited.TryGetValue(l.LineNo, out var x) ? new CreditableLine(l, x.Taxable, x.Tax) : new CreditableLine(l, Paise.Zero, Paise.Zero)).ToList();
    }

    /// <summary>Plans against the current credits, numbers the note (CRN series of the business date's FY), posts it to the source, stores it.</summary>
    private async Task<(CreditNoteRecord Note, string RequestVersion)> IssueCoreAsync(ICommandTransaction tx, InvoiceRecord inv, CreditNoteRequestRow r, int approvedBy,
        CancellationToken ct, string? decisionReason = null)
    {
        var lines = CreditNoteRules.Plan(await CreditableAsync(tx, inv, ct).ConfigureAwait(false), r.Mode, r.Amount, r.Lines, inv.Recipient.InterState);
        var figures = CreditNoteRules.Totals(lines);
        var supplier = await store.SupplierAsync(tx, inv.PropertyId, ct).ConfigureAwait(false);
        var date = supplier.BusinessDate;
        var fy = InvoiceNumbering.FyCode(date);
        var (prefix, seq) = await store.NextNumberAsync(tx, inv.PropertyId, fy, InvoiceDocType.CreditNote, InvoiceNumbering.DefaultPrefix(InvoiceDocType.CreditNote), Now, ct).ConfigureAwait(false);
        var number = InvoiceNumbering.Format(prefix, fy, seq);
        var byLine = inv.Lines.ToDictionary(l => l.LineNo);
        var postings = lines.Select(l => new CreditPosting(l.LineNo, byLine[l.InvoiceLineNo].SourceLineId, l.Category, l.Bps, l.Taxable, l.Tax)).ToList();
        var chargeIds = await Source(inv.SourceKind).PostCreditAsync(tx, inv.PropertyId, inv.SourceId, number, r.Reason, date, postings, ct).ConfigureAwait(false);
        var now = Now;
        var note = new CreditNoteRecord(0, inv.PropertyId, number, fy, seq, date, inv.InvoiceId, r.RequestId, r.Mode, r.Reason, lines, chargeIds, figures,
            InvoiceRules.Words(figures.Total), tx.Context.PrincipalId, approvedBy, now);
        var id = await store.InsertCreditNoteAsync(tx, note, ct).ConfigureAwait(false);
        note = note with { CreditNoteId = id };
        var version = await store.UpdateRequestAsync(tx, r.RequestId, r.Version, "Issued", approvedBy, now, decisionReason, id, ct).ConfigureAwait(false);
        await tx.AuditAsync("CREDIT_NOTE_ISSUED", "invoice:" + N(inv.InvoiceId),
            $"{number} against {inv.InvoiceNo} · {CreditNoteRules.Code(r.Mode)} · taxable {figures.Taxable} + tax {figures.Tax} = {figures.Total} · {r.Reason}{(r.Direct ? " · issued by the Owner" : $" · approved, request {N(r.RequestId)}")}", ct).ConfigureAwait(false);
        await tx.PublishAsync("CreditNoteIssued", id, new
        {
            creditNoteId = id, creditNoteNo = number, noteDate = date, invoiceId = inv.InvoiceId, invoiceNo = inv.InvoiceNo, sourceKind = inv.SourceKind, sourceId = inv.SourceId,
            taxablePaise = figures.Taxable.Value, cgstPaise = figures.Cgst.Value, sgstPaise = figures.Sgst.Value, igstPaise = figures.Igst.Value, totalPaise = figures.Total.Value,
            folioChargeIds = chargeIds,
        }, ct).ConfigureAwait(false);
        return (note, version);
    }

    /// <summary>A request-only ("R") or limited holder is exactly who a request is for: APPROVAL_REQUIRED from the policy is accepted.</summary>
    private async Task RequireRequesterAsync(CommandContext ctx)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, InvoicePermissions.Request).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // Request-only holder: the request goes to the Owner.
        }
    }

    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    private static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);
}
