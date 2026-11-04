using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>One requested change (LF-CONTRACT §4 BLCChange). Kind room-rate {Night, NewRatePaise} | remove-line {ChargeId} | add-line {Label, AmountPaise, GstCategory, ServiceDate}.</summary>
public sealed record BillChangeIn(string Kind, DateOnly? Night = null, long? NewRatePaise = null, long? ChargeId = null, string? Label = null, long? AmountPaise = null,
    string? GstCategory = null, DateOnly? ServiceDate = null);

/// <summary>POST /api/v1/invoices/{no}/corrections.</summary>
public sealed record RequestBillCorrectionRequest(string InvoiceNo, IReadOnlyList<BillChangeIn> Changes, string Reason);

public sealed record BillCorrectionRequestResult(long CorrectionId, long ApprovalId, string ApprovalVersion, long OldTotalPaise, long NewTotalPaise, long DeltaPaise, string Settlement);

public sealed record BillCorrectionRow(long CorrectionId, int PropertyId, long FolioId, long OldInvoiceId, long ApprovalId, string Reason, int RequestedBy, string Status,
    long OldTotalPaise, long NewTotalPaise, long DeltaPaise);

/// <summary>A stored change (ivc.bill_correction_change) with what the request saw (old rate).</summary>
public sealed record StoredBillChange(int Seq, string Kind, DateOnly? Night, long? OldRatePaise, long? NewRatePaise, long? ChargeId, string? Label, long? AmountPaise,
    string? GstCategory, DateOnly? ServiceDate);

/// <summary>The correction register (A2; W7 "Bill updation report").</summary>
public sealed record BillCorrectionListRow(long CorrectionId, string OldInvoiceNo, string? CreditNoteNo, string? NewInvoiceNo, string Status, long OldTotalPaise,
    long NewTotalPaise, long DeltaPaise, string? Settlement, string Reason, DateTime RequestedUtc, int RequestedBy, long ApprovalId, int? DecidedBy, DateTime? DecidedUtc,
    IReadOnlyList<StoredBillChange> Changes);

public interface IBillCorrectionStore
{
    Task<long?> InvoiceIdByNoAsync(ICommandTransaction tx, int propertyId, string invoiceNo, CancellationToken ct);

    Task<bool> SupersededAsync(ICommandTransaction tx, long invoiceId, CancellationToken ct);

    Task<bool> PendingForFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    Task<long> InsertAsync(ICommandTransaction tx, BillCorrectionRow row, DateTime utc, IReadOnlyList<StoredBillChange> changes, CancellationToken ct);

    Task<(BillCorrectionRow Row, IReadOnlyList<StoredBillChange> Changes)?> ByApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct);

    Task DecideAsync(ICommandTransaction tx, long correctionId, string status, int decidedBy, DateTime utc, string? creditNoteNo, long? newInvoiceId, string? settlement,
        CancellationToken ct);

    Task InsertSupersessionAsync(ICommandTransaction tx, long oldInvoiceId, long newInvoiceId, long correctionId, DateTime utc, CancellationToken ct);

    Task<IReadOnlyList<BillCorrectionListRow>> ListAsync(int propertyId, CancellationToken ct);
}

/// <summary>
/// LF A2 — correct a bill after check-out (legacy "Bill Updation", GST-safe form). <c>RequestBillCorrection</c> (PERM-FOL-010): the latest invoice
/// of a departed folio, not superseded, no other correction pending; the changes are validated and previewed (BillCorrectionRules) → approval
/// 'bill-correction' (Owner: PERM-INV-003; never the requester). On approval, in one transaction: (a) a full credit note of the invoice (CRN; it
/// posts its correction lines on the folio, kind 'correction'), (b) the corrected lines posted on the business date (kind 'correction', GST at the
/// slab of the night / category they belong to, DEC-069 / DEC-077), (c) a new tax invoice (INV, revision + 1) of exactly the corrected lines, (d) the
/// supersession row and the correction marked Issued with its settlement (refund-due → the refund flow; balance-due → a payment on the departed
/// folio for exactly that balance). Refused after the s.34(2) window (30 Nov after the invoice's FY — CA to confirm). Ledgers carry the delta only:
/// the credit note and the corrected lines both post to the adjustments ledger, every voucher balanced.
/// </summary>
public sealed class BillCorrectionService(ICommandRunner runner, IAccessPolicy policy, IBillCorrectionStore store, IInvoiceStore invoicesStore, InvoiceService invoices,
    IBillingStore billing, IGstRateResolver gst, IGstChargeSnapshots snapshots, TimeProvider clock) : ILfApprovalHandler
{
    public const string OpRequest = "RequestBillCorrection";
    public const string PermRequest = "PERM-FOL-010";
    public const string EventCorrected = "BillCorrected";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public AdjustmentAction Action => AdjustmentAction.BillCorrection;

    public async Task<CommandResult<BillCorrectionRequestResult>> RequestAsync(CommandContext ctx, RequestBillCorrectionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is 0 or > 200) throw HccException.Validation("A reason is required (up to 200 characters).");
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, PermRequest, ct: ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // request-only holders are who the request is for
        }

        return await runner.RunAsync(ctx with { Operation = OpRequest }, req, async (tx, c) =>
        {
            var invoiceId = await store.InvoiceIdByNoAsync(tx, ctx.PropertyId, (req.InvoiceNo ?? "").Trim(), c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Invoice not found.");
            var inv = (await invoicesStore.ReadInvoiceAsync(tx, ctx.PropertyId, invoiceId, c).ConfigureAwait(false))!;
            if (inv.SourceKind != FolioInvoiceSource.FolioKind) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a stay's tax invoice is corrected here.");
            var f = await billing.LockFolioAsync(tx, inv.SourceId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
            if (f.ReservationStatus != "Departed") throw new HccException(HccErrorCode.INVALID_TRANSITION, "A bill is corrected after check-out.");
            if (await store.SupersededAsync(tx, invoiceId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Invoice {inv.InvoiceNo} was already superseded: correct its latest revision.");
            if (await store.PendingForFolioAsync(tx, f.FolioId, c).ConfigureAwait(false)
                || await invoicesStore.PendingRequestAsync(tx, invoiceId, c).ConfigureAwait(false) is not null)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A correction or credit note is already pending on this bill.");
            var bd = await BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (BillCorrectionRules.WindowRefusal(inv.InvoiceDate, bd, inv.InvoiceNo) is { } late) throw new HccException(HccErrorCode.INVALID_TRANSITION, late);

            var (changes, stored) = Parse(inv, req.Changes ?? []);
            var plan = await PlanAsync(tx, ctx.PropertyId, inv, changes, c).ConfigureAwait(false);
            var now = Now;
            var amount = new Paise(Math.Max(1, Math.Abs(plan.Preview.Delta.Value)));
            var (approvalId, version) = await billing.InsertApprovalAsync(tx, new NewApproval(ctx.PropertyId, AdjustmentAction.BillCorrection, f.FolioId, null, "folio", f.FolioId,
                amount, f.Version, null, null, reason, ctx.PrincipalId, now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            var id = await store.InsertAsync(tx, new BillCorrectionRow(0, ctx.PropertyId, f.FolioId, invoiceId, approvalId, reason, ctx.PrincipalId, "Pending",
                plan.Preview.OldTotal.Value, plan.Preview.NewTotal.Value, plan.Preview.Delta.Value), now, stored, c).ConfigureAwait(false);
            await tx.AuditAsync("BILL_CORRECTION_REQUESTED", "invoice:" + N(invoiceId),
                $"{inv.InvoiceNo} · {stored.Count} change(s) · {plan.Preview.OldTotal.Value} → {plan.Preview.NewTotal.Value} ({plan.Preview.Settlement}) · {reason} · approval {approvalId}", c).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", approvalId, new { approvalId, action = "bill-correction", folioId = f.FolioId, amountPaise = amount.Value }, c).ConfigureAwait(false);
            return new BillCorrectionRequestResult(id, approvalId, version, plan.Preview.OldTotal.Value, plan.Preview.NewTotal.Value, plan.Preview.Delta.Value, plan.Preview.Settlement);
        }, ct).ConfigureAwait(false);
    }

    public async Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(approval);
        var f = await billing.LockFolioAsync(tx, approval.FolioId!.Value, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        var a = (await billing.LockApprovalAsync(tx, approval.ApprovalId, ct).ConfigureAwait(false))!;
        BillingService.EnsureVersion(a.Version, expectedVersion, "The approval request");
        var now = Now;
        var expired = ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now);
        if (a.Status != ApprovalStatus.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
        if (a.RequestedBy == caller.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, InvoicePermissions.Approve, ct: ct).ConfigureAwait(false);
        var (row, stored) = await store.ByApprovalAsync(tx, a.ApprovalId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "The correction request is missing.");
        if (!approve || expired)
        {
            if (approve) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request expired (24 hours). Dismiss it and request again.");
            var status = expired ? ApprovalStatus.Expired : ApprovalStatus.Rejected;
            await store.DecideAsync(tx, row.CorrectionId, status.ToString(), caller.PrincipalId, now, null, null, null, ct).ConfigureAwait(false);
            var v0 = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(status, caller.PrincipalId, now, reason, null, null, null), ct).ConfigureAwait(false);
            await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"bill-correction · {status}", ct).ConfigureAwait(false);
            return new ApprovalResult(a.ApprovalId, "bill-correction", status.ToString(), a.Amount.Value, v0, null, f.Version, null);
        }

        if (a.TargetVersion != f.Version) throw new HccException(HccErrorCode.STALE_VERSION, "The folio changed after this request. Reject it and request again.");
        var inv = (await invoicesStore.ReadInvoiceAsync(tx, caller.PropertyId, row.OldInvoiceId, ct).ConfigureAwait(false))!;
        var bd = await BusinessDateAsync(tx, caller.PropertyId, ct).ConfigureAwait(false);
        if (BillCorrectionRules.WindowRefusal(inv.InvoiceDate, bd, inv.InvoiceNo) is { } late) throw new HccException(HccErrorCode.INVALID_TRANSITION, late);
        var changes = stored.Select(ToChange).ToList();
        var plan = await PlanAsync(tx, caller.PropertyId, inv, changes, ct).ConfigureAwait(false);

        // (a) the whole invoice credited (CRN; posts its correction lines on the folio)
        var note = await invoices.CreditOnSourceReversalAsync(tx, FolioInvoiceSource.FolioKind, f.FolioId, a.RequestedBy, $"Bill correction {N(row.CorrectionId)} · {row.Reason}", ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The bill has no invoice to correct.");
        // (b) the corrected lines, posted on the business date
        var source = new List<SourceLine>();
        foreach (var l in plan.Lines)
        {
            var tax = GstMath.LineTax(l.Amount, l.Bps);
            var label = $"Bill correction {N(row.CorrectionId)} · {l.Description}";
            var id = await billing.InsertChargeAsync(tx, new NewCharge(f.FolioId, ChargeKind.Correction, bd, label.Length <= 200 ? label : label[..200], l.Amount, l.Bps, tax,
                null, a.ApprovalId, caller.PrincipalId, now), ct).ConfigureAwait(false);
            var split = GstMath.Split(tax);
            await snapshots.RecordAsync(tx, new GstChargeSnapshot(id, l.Category, l.Bps, false, tax, split.Cgst, split.Sgst, "Bill correction " + N(row.CorrectionId)), ct).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId = id, kind = "correction", serviceDate = bd, amountPaise = l.Amount.Value, taxPaise = tax.Value }, ct).ConfigureAwait(false);
            source.Add(new SourceLine(id, l.Kind, l.Night, l.Description, l.Category, l.Amount, l.Bps, tax, split.Cgst, split.Sgst));
        }

        await billing.TouchFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        // (c) the new tax invoice, revision + 1, of exactly the corrected lines
        var issued = await invoices.IssueRevisionAsync(tx, new InvoiceSourceDocument(FolioInvoiceSource.FolioKind, f.FolioId, inv.SourceRef, inv.Recipient.Name, inv.PeriodFrom,
            inv.PeriodTo, source), inv, ct).ConfigureAwait(false);
        // (d) supersession + the correction record
        await store.InsertSupersessionAsync(tx, inv.InvoiceId, issued.InvoiceId, row.CorrectionId, now, ct).ConfigureAwait(false);
        await store.DecideAsync(tx, row.CorrectionId, "Issued", caller.PrincipalId, now, note.CreditNoteNo, issued.InvoiceId, plan.Preview.Settlement, ct).ConfigureAwait(false);
        var v = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(ApprovalStatus.Executed, caller.PrincipalId, now, reason, caller.PrincipalId, now, issued.InvoiceId), ct).ConfigureAwait(false);
        await tx.AuditAsync("BILL_CORRECTED", "invoice:" + N(inv.InvoiceId),
            $"{inv.InvoiceNo} superseded by {issued.InvoiceNo} (credit note {note.CreditNoteNo}) · {plan.Preview.OldTotal.Value} → {plan.Preview.NewTotal.Value} · {plan.Preview.Settlement}", ct).ConfigureAwait(false);
        await tx.PublishAsync(EventCorrected, row.CorrectionId, new { correctionId = row.CorrectionId, folioId = f.FolioId, oldInvoiceNo = inv.InvoiceNo, creditNoteNo = note.CreditNoteNo,
            newInvoiceNo = issued.InvoiceNo, deltaPaise = plan.Preview.Delta.Value, settlement = plan.Preview.Settlement }, ct).ConfigureAwait(false);
        var fv = (await billing.LockFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false))!.Version;
        return new ApprovalResult(a.ApprovalId, "bill-correction", nameof(ApprovalStatus.Executed), a.Amount.Value, v, issued.InvoiceId, fv, null);
    }

    public Task<IReadOnlyList<BillCorrectionListRow>> ListAsync(int propertyId, int principalId, CancellationToken ct) => ListCoreAsync(propertyId, principalId, ct);

    private async Task<IReadOnlyList<BillCorrectionListRow>> ListCoreAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, InvoicePermissions.View, ct: ct).ConfigureAwait(false);
        return await store.ListAsync(propertyId, ct).ConfigureAwait(false);
    }

    private sealed record PlannedLine(string Kind, DateOnly Night, string Description, GstCategory Category, Paise Amount, int Bps);

    private sealed record Plan(BillCorrectionPreview Preview, IReadOnlyList<PlannedLine> Lines);

    /// <summary>The preview (BillCorrectionRules) on the invoice's lines, plus each corrected line's category and description (same order as the preview).</summary>
    private async Task<Plan> PlanAsync(ICommandTransaction tx, int propertyId, InvoiceRecord inv, IReadOnlyList<BillChange> changes, CancellationToken ct)
    {
        var live = inv.Lines.Select(l => new BillLine(l.SourceLineId ?? -l.LineNo, l.Kind, l.ServiceDate, l.Taxable, l.Bps)).ToList();
        var roomBps = new Dictionary<(DateOnly, long), int>();
        var chargeBps = new Dictionary<(DateOnly, GstCategory), int>();
        foreach (var ch in changes)
        {
            if (ch is BillChange.RoomRate r && r.NewRate.Value >= 0)
                roomBps[(r.Night, r.NewRate.Value)] = (await gst.RoomNightAsync(tx, propertyId, r.Night, r.NewRate, ct).ConfigureAwait(false)).Bps;
            if (ch is BillChange.AddLine a)
                chargeBps[(a.ServiceDate, a.Category)] = (await gst.ChargeAsync(tx, propertyId, a.ServiceDate, a.Category, ct).ConfigureAwait(false)).Bps;
        }

        var (preview, refusal) = BillCorrectionRules.Preview(inv.PeriodFrom ?? DateOnly.MinValue, inv.PeriodTo ?? DateOnly.MaxValue, live, changes,
            (d, p) => roomBps.TryGetValue((d, p.Value), out var b) ? b : 0, (d, c) => chargeBps.TryGetValue((d, c), out var b) ? b : 0);
        if (refusal is not null) throw HccException.Validation(refusal);
        var removed = changes.OfType<BillChange.RemoveLine>().Select(x => x.ChargeId).ToHashSet();
        var meta = inv.Lines.Where(l => !removed.Contains(l.SourceLineId ?? -l.LineNo)).Select(l => (l.Description, l.Category))
            .Concat(changes.OfType<BillChange.AddLine>().Select(x => (x.Label.Trim(), x.Category))).ToList();
        var lines = preview!.NewLines.Select((n, i) => new PlannedLine(n.Kind, n.Night, meta[i].Item1, n.Kind == "room" ? GstCategory.Room : meta[i].Item2, n.Amount, n.Bps)).ToList();
        return new Plan(preview, lines);
    }

    private static (IReadOnlyList<BillChange> Changes, IReadOnlyList<StoredBillChange> Stored) Parse(InvoiceRecord inv, IReadOnlyList<BillChangeIn> input)
    {
        if (input.Count is 0 or > 40) throw HccException.Validation("Add 1 to 40 changes to the bill.");
        var changes = new List<BillChange>();
        var stored = new List<StoredBillChange>();
        var seq = 0;
        foreach (var c in input)
        {
            seq++;
            switch ((c.Kind ?? "").Trim())
            {
                case "room-rate":
                {
                    var night = c.Night ?? throw HccException.Validation("Choose the night whose room rent changes.");
                    var rate = c.NewRatePaise ?? throw HccException.Validation("Enter the corrected room rent.");
                    var old = inv.Lines.FirstOrDefault(l => l.Kind == "room" && l.Taxable.Value > 0 && l.ServiceDate == night)?.Taxable.Value ?? 0;
                    changes.Add(new BillChange.RoomRate(night, new Paise(rate)));
                    stored.Add(new StoredBillChange(seq, "room-rate", night, old, rate, null, null, null, null, null));
                    break;
                }

                case "remove-line":
                {
                    var id = c.ChargeId ?? throw HccException.Validation("Choose the line to remove.");
                    changes.Add(new BillChange.RemoveLine(id));
                    stored.Add(new StoredBillChange(seq, "remove-line", null, null, null, id, null, null, null, null));
                    break;
                }

                case "add-line":
                {
                    var label = (c.Label ?? "").Trim();
                    if (label.Length > 120) throw HccException.Validation("Keep the description to 120 characters.");
                    var cat = GstCategories.ForCharge(c.GstCategory, label);
                    var date = c.ServiceDate ?? throw HccException.Validation("Give the service date of the added charge.");
                    changes.Add(new BillChange.AddLine(label, new Paise(c.AmountPaise ?? 0), cat, date));
                    stored.Add(new StoredBillChange(seq, "add-line", null, null, null, null, label, c.AmountPaise, GstCategories.Code(cat), date));
                    break;
                }

                default:
                    throw HccException.Validation("A change is room-rate, remove-line or add-line.");
            }
        }

        return (changes, stored);
    }

    private static BillChange ToChange(StoredBillChange s) => s.Kind switch
    {
        "room-rate" => new BillChange.RoomRate(s.Night!.Value, new Paise(s.NewRatePaise!.Value)),
        "remove-line" => new BillChange.RemoveLine(s.ChargeId!.Value),
        _ => new BillChange.AddLine(s.Label!, new Paise(s.AmountPaise!.Value), GstCategories.Parse(s.GstCategory!), s.ServiceDate!.Value),
    };

    private async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct) =>
        await billing.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}
