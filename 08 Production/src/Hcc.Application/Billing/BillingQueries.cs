using Hcc.Application.Access;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Application.Billing;

/// <summary>GetFolio (PERM-FOL-001), GetShift (PERM-SHF-001, own-shift scope), collections (PERM-RPT-002).</summary>
public sealed class BillingQueries(IBillingStore store, IAccessPolicy policy, TimeProvider clock)
{
    // ETP-REUSE: folio / sample invoice / credit note PDF and Excel via ETP exporters (Hcc.Reporting) when V:\ETP is available.
    public async Task<FolioView> GetFolioAsync(Caller caller, long folioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.FolioView, ct: ct).ConfigureAwait(false);
        var snap = await store.ReadFolioAsync(folioId, ct).ConfigureAwait(false);
        if (snap is null || snap.Header.PropertyId != caller.PropertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        var figures = FolioStatement.Compute(snap.Lines, snap.Receipts);
        var lines = snap.Lines.Select(l => new FolioLineView(l.ChargeId, BillingCodes.Code(l.Kind), l.ServiceDate, l.Label, l.Amount.Value, l.TaxBps, l.Tax.Value,
            l.ReversesChargeId, snap.Lines.Any(x => x.ReversesChargeId == l.ChargeId))).ToList();
        var receipts = snap.Receipts.Select(r =>
        {
            var refunded = FolioStatement.RefundedAgainst(snap.Receipts, r.ReceiptId);
            var outstanding = snap.Approvals.Where(a => a.Action == AdjustmentAction.Refund && a.TargetId == r.ReceiptId && ApprovalRules.IsOutstanding(a.Status, a.Action, a.ExpiresUtc, now))
                .Aggregate(Paise.Zero, (s, a) => s + a.Amount);
            var refundable = r.Kind == ReceiptKind.Payment ? RefundRules.Remainder(r.Amount, refunded, outstanding) : Paise.Zero;
            return new FolioReceiptView(r.ReceiptId, FolioTotals.KindCode(r.Kind), r.Tender, r.Amount.Value, r.BusinessDate, r.Reference, r.RefundOf, refunded.Value, refundable.Value);
        }).ToList();
        var approvals = snap.Approvals.Select(a => ToView(a, now)).ToList();
        var h = snap.Header;
        return new FolioView(h.FolioId, h.ReservationId, h.ReservationStatus, h.BusinessDate, h.Version, lines, receipts, approvals,
            new FolioTotalsView(figures.Net.Value, figures.Tax.Value, figures.Total.Value, figures.Paid.Value, figures.Deposit.Value, figures.CompanyCredit.Value, figures.Balance.Value));
    }

    public async Task<ShiftView> GetShiftAsync(Caller caller, long shiftId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftCount, new AccessScope(BillingPermissions.ShiftScope, shiftId), ct: ct).ConfigureAwait(false);
        var read = await store.ReadShiftAsync(shiftId, ct).ConfigureAwait(false);
        if (read is not { } r || r.Shift.PropertyId != caller.PropertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Shift not found.");
        var e = CashDrawer.Expected(r.Shift.OpeningFloat, r.Movements.Select(m => new CashMovement(m.Kind, m.Amount)).ToList());
        return new ShiftView(CashierService.ToResult(r.Shift),
            new ExpectedCashView(e.Opening.Value, e.Payments.Value, e.Deposits.Value, e.Refunds.Value, e.Returns.Value, e.Expenses.Value, e.Other.Value,
                e.Restaurant.Value, e.Transfers.Value, e.Expected.Value, e.Consistent),
            r.Movements.Select(m => new CashMovementView(m.MovementId, CashDrawer.Code(m.Kind), m.SourceType, m.SourceId, m.Amount.Value, m.Label, m.Reference, m.BusinessDate)).ToList());
    }

    // ETP-REUSE: Tally export of receipts / cash book from ETP when V:\ETP is available.
    public async Task<CollectionsView> GetCollectionsAsync(Caller caller, DateOnly businessDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.FinanceReport, ct: ct).ConfigureAwait(false);
        var receipts = await store.ReceiptsOnAsync(caller.PropertyId, businessDate, ct).ConfigureAwait(false);
        var byTender = receipts.Where(r => r.Kind is ReceiptKind.Payment or ReceiptKind.Refund)
            .GroupBy(r => r.Tender, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Aggregate(0L, (s, r) => checked(s + (r.Kind == ReceiptKind.Payment ? r.Amount.Value : -r.Amount.Value))), StringComparer.Ordinal);
        long Sum(ReceiptKind k) => receipts.Where(r => r.Kind == k).Aggregate(0L, (s, r) => checked(s + r.Amount.Value));
        return new CollectionsView(businessDate, DailyReceipts.Collections(receipts, businessDate).Value, new SortedDictionary<string, long>(byTender, StringComparer.Ordinal),
            Sum(ReceiptKind.Deposit), Sum(ReceiptKind.DepositReturn));
    }

    /// <summary>W4-3: the reservation's folio (null before check-in) with its stay id/version (PERM-FOL-001).</summary>
    public async Task<ReservationFolioView> GetReservationFolioAsync(Caller caller, long reservationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.FolioView, ct: ct).ConfigureAwait(false);
        var r = await store.FindReservationFolioAsync(caller.PropertyId, reservationId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
        var folio = r.FolioId is { } fid ? await GetFolioAsync(caller, fid, ct).ConfigureAwait(false) : null;
        return new ReservationFolioView(r.ReservationId, r.StayId, r.StayVersion, folio);
    }

    /// <summary>W4-3: the cash desk — every till with its active shift (expected cash) and last closed shift (PERM-SHF-001, any shift; DEC-052 shared till).</summary>
    public async Task<CashDeskView> GetCashDeskAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftCount, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var desk = await store.ReadCashDeskAsync(caller.PropertyId, ct).ConfigureAwait(false);
        if (desk.BusinessDate is not { } date) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        CashDeskShiftView? Shift(CashDeskShiftRead? s) => s is null ? null
            : new CashDeskShiftView(CashierService.ToResult(s.Shift), s.Shift.SubmittedBy, s.Reason, s.ClosedBy, s.Approval is { } a ? ToView(a, now) : null);
        var tills = new List<CashDeskTillView>();
        foreach (var t in desk.Tills)
        {
            ExpectedCashView? expected = null;
            if (t.Active is { } active && await store.ReadShiftAsync(active.Shift.ShiftId, ct).ConfigureAwait(false) is { } read)
            {
                var e = CashDrawer.Expected(read.Shift.OpeningFloat, read.Movements.Select(m => new CashMovement(m.Kind, m.Amount)).ToList());
                expected = new ExpectedCashView(e.Opening.Value, e.Payments.Value, e.Deposits.Value, e.Refunds.Value, e.Returns.Value, e.Expenses.Value, e.Other.Value,
                    e.Restaurant.Value, e.Transfers.Value, e.Expected.Value, e.Consistent);
            }

            tills.Add(new CashDeskTillView(new TillResult(t.Till.TillId, t.Till.Code, t.Till.Name, t.Till.Version), Shift(t.Active), expected, Shift(t.LastClosed)));
        }

        return new CashDeskView(date, tills);
    }

    /// <summary>
    /// D5: the cashier's folio list (PERM-FOL-001, property scope) — in-house stays, today's confirmed arrivals and today's departures with
    /// folio id, version and balance. Lets a role without the reservations read (Cashier, DEC-063: "Cashier works from folios") take payments.
    /// </summary>
    public async Task<DeskFolioListView> GetDeskFoliosAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.FolioView, ct: ct).ConfigureAwait(false);
        var (date, rows) = await store.ListDeskFoliosAsync(caller.PropertyId, ct).ConfigureAwait(false);
        if (date is not { } businessDate) throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var items = new List<DeskFolioItem>(rows.Count);
        foreach (var r in rows)
        {
            string? version = null;
            long balance = 0;
            if (r.FolioId is { } fid && await store.ReadFolioAsync(fid, ct).ConfigureAwait(false) is { } snap && snap.Header.PropertyId == caller.PropertyId)
            {
                version = snap.Header.Version;
                balance = FolioStatement.Compute(snap.Lines, snap.Receipts).Balance.Value;
            }

            items.Add(new DeskFolioItem(r.ReservationId, r.PublicNo, r.Status, r.GuestName, r.RoomId, r.RoomNo, r.RoomType,
                Hcc.Domain.Dates.StayDates.ToWire(r.Arrival), Hcc.Domain.Dates.StayDates.ToWire(r.Departure), r.Adults, r.Children, r.Source, r.NightlyRatePaise,
                r.StayId, r.FolioId, version, balance, r.CompanyName, r.CompanyGstin));
        }

        return new DeskFolioListView(businessDate, items);
    }

    internal static ApprovalView ToView(ApprovalRow a, DateTime now) => new(a.ApprovalId, BillingCodes.Code(a.Action), a.TargetKind, a.TargetId, a.Amount.Value,
        ApprovalRules.Effective(a.Status, a.Action, a.ExpiresUtc, now).ToString(), a.Reason, a.RequestedBy, a.DecidedBy, a.ExpiresUtc, a.ResultId, a.Version);
}
