using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;
using Hcc.Infrastructure.SqlServer.Accounting;
using Microsoft.Data.SqlClient;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// DEC-077 (resolves DEC-028) on SQL with the real composition: the Owner reopens a closed day with a reason, corrections and missed
/// charges post on that day, issued invoices never change, the cash-book lock (DEC-065) and the accounting period lock are respected,
/// exported Tally days are flagged for re-export, and closing the day again replaces its closing record while keeping the old one.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReopenDayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D23 = new(2026, 10, 23);
    private static readonly DateOnly D24 = new(2026, 10, 24);
    private static readonly DateOnly D25 = new(2026, 10, 25);
    private static readonly DateOnly D26 = new(2026, 10, 26);

    private sealed record World(long Folio101, long Folio102, long Folio103, long Invoice103);

    private static Task SetBusinessDateAsync(Hotel h, string date) => h.ScalarAsync($"UPDATE inv.property SET business_date = '{date}' WHERE property_id = {h.PropertyId};", Ct);

    /// <summary>
    /// 24 Oct closed, 25 Oct open with the next shift. Rooms 101 and 102 in house 23 → 30 Oct at ₹2,000 (5 % GST = ₹100 a night); the close
    /// posts their 24 Oct nights. Room 103 stayed 22 → 24 Oct: nights 22 and 23 Oct (₹2,000 + ₹100 each), laundry ₹500 + 18 % (₹90) on 24 Oct,
    /// paid by UPI ₹4,790 (2 × 2,100 + 590), checked out on 24 Oct, invoice INV/2627/0000001 issued that day.
    /// 24 Oct at close: rooms 2 × ₹2,000 + laundry ₹500 = net ₹4,500 (450000 paise); tax 2 × ₹100 + ₹90 = ₹290 (29000 paise).
    /// </summary>
    private static async Task<World> ClosedDay24Async(Hotel h)
    {
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        await SetBusinessDateAsync(h, "2026-10-22");
        var (_, stay103, folio103) = await h.SeedInHouseAsync("103", "2026-10-22", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay103, new DateOnly(2026, 10, 22)), Ct);
        await SetBusinessDateAsync(h, "2026-10-23");
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay103, D23), Ct);
        await SetBusinessDateAsync(h, "2026-10-24");
        await h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(folio103, "Laundry service", 50000, "laundry"), Ct);
        var balance = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio103, Ct)).Totals.BalancePaise;
        Assert.Equal(479000, balance);
        await h.PayAsync(folio103, balance, "UPI", ct: Ct);
        var version = await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay103}", Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay103, version), Ct);
        var invoice = (await h.Get<InvoiceService>().IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", folio103), Ct)).Value;
        Assert.Equal(("INV/2627/0000001", 479000L), (invoice.InvoiceNo, invoice.TotalPaise));

        var f101 = (await h.SeedInHouseAsync("101", "2026-10-23", "2026-10-30", ct: Ct)).FolioId;
        var f102 = (await h.SeedInHouseAsync("102", "2026-10-23", "2026-10-30", ct: Ct)).FolioId;
        var (till, shift) = await h.OpenTillAsync(500000, Ct);
        await h.CountExactAsync(shift.ShiftId, Ct);
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
        await h.Till.OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, null), Ct);
        return new World(f101, f102, folio103, invoice.InvoiceId);
    }

    private static Task<CommandResult<ReopenBusinessDayResult>> ReopenAsync(Hotel h, int who, DateOnly date, string reason = "Room 101 night keyed on the wrong folio")
        => h.Closing.ReopenBusinessDayAsync(h.As(who), Hotel.Key(), new ReopenBusinessDayRequest(date, reason), Ct);

    private static Task<CommandResult<ChargeResult>> ChargeAsync(Hotel h, long folio, DateOnly? on, long amount = 30000)
        => h.Billing.PostChargeAsync(h.As(h.Reception), Hotel.Key(), new PostChargeRequest(folio, "Laundry service (missed)", amount, "laundry", on), Ct);

    private static Task<long> NightAsync(Hotel h, long folio, string date)
        => h.LongAsync($"SELECT charge_id FROM bill.charge WHERE folio_id = {folio} AND kind = 'room' AND service_date = '{date}' AND reverses_charge_id IS NULL", Ct);

    private static async Task<AdjustmentResult> RequestCorrectionAsync(Hotel h, long folio, long chargeId, long amount)
    {
        var v = (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Version;
        return (await h.Billing.RequestAdjustmentAsync(h.As(h.Manager), Hotel.Key(), new RequestAdjustmentRequest("correction", folio, chargeId, amount, "Night belongs to another guest", v), Ct)).Value;
    }

    private static async Task<ApprovalResult> ApproveAsync(Hotel h, AdjustmentResult r)
        => (await h.Billing.DecideApprovalAsync(h.As(h.Owner), Hotel.Key(), new DecideApprovalRequest(r.ApprovalId!.Value, true, null, r.ApprovalVersion!), Ct)).Value;

    private static async Task<(DateOnly Date, long Amount, long Tax)> ChargeRowAsync(Hotel h, long chargeId)
        => (await h.RowsAsync($"SELECT service_date, amount_paise, tax_paise FROM bill.charge WHERE charge_id = {chargeId}",
            r => (DateOnly.FromDateTime(r.GetDateTime(0)), r.GetInt64(1), r.GetInt64(2)), Ct)).Single();

    private static async Task<int> SqlErrorAsync(Hotel h, string sql)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => h.ScalarAsync(sql, Ct));
        return ex.Number;
    }

    [Fact]
    public async Task Reopened_day_takes_its_corrections_and_closing_it_again_replaces_the_record_and_keeps_the_old_one()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var w = await ClosedDay24Async(h);
        var first = await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct);
        Assert.Equal((450000L, 29000L), (first.Record.Figures.Revenue.NetPaise, first.Record.Figures.Revenue.TaxPaise));
        var invoiceBefore = await h.Get<InvoiceQueries>().GetInvoiceAsync(h.PropertyId, h.Accountant, w.Invoice103, Ct);

        // Before the reopen the closed day is adjustment-only (DEC-028 as built): nothing can be dated 24 Oct.
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ChargeAsync(h, w.Folio102, D24));

        var reopen = (await ReopenAsync(h, h.Owner, D24)).Value;
        Assert.Equal(("Reopened", first.SnapshotId, false, false), (reopen.Reopen.Status, reopen.Reopen.PriorSnapshotId, reopen.Reopen.CashBookLocked, reopen.Reopen.TallyReexport));
        Assert.Contains("does not change cash-book locks", reopen.Message, StringComparison.Ordinal);
        Assert.Equal((D25, D24), ((await h.Closing.GetBusinessDateAsync(h.As(h.Reception), Ct)).BusinessDate, (await h.Closing.GetBusinessDateAsync(h.As(h.Reception), Ct)).ReopenedDate));
        Assert.Equal(D25, await h.BusinessDateAsync(Ct));

        // Today's close waits until 24 Oct is closed again.
        var today = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(date: D25, ct: Ct));
        Assert.Contains("reopened day closed again", today.Message, StringComparison.OrdinalIgnoreCase);

        // A correction of Room 101's 24 Oct night, requested by the Manager: while pending it blocks closing 24 Oct again.
        var night101 = await NightAsync(h, w.Folio101, "2026-10-24");
        var corr = await RequestCorrectionAsync(h, w.Folio101, night101, 200000);
        Assert.Equal("PendingApproval", corr.Outcome);
        var pending = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(date: D24, ct: Ct));
        Assert.Contains("corrections of 2026-10-24 decided", pending.Message, StringComparison.OrdinalIgnoreCase);
        // The Owner approves: the reversal posts ON 24 Oct (−₹2,000, tax −₹100), not on the open 25 Oct.
        var approved = await ApproveAsync(h, corr);
        Assert.Equal((D24, -200000L, -10000L), await ChargeRowAsync(h, approved.ResultId!.Value));

        // A missed laundry charge on Room 102 dated 24 Oct: ₹300 + 18 % = ₹54 (5400 paise).
        var missed = (await ChargeAsync(h, w.Folio102, D24)).Value;
        Assert.Equal((D24, 1800, 5400L), (missed.ServiceDate, missed.TaxBps, missed.TaxPaise));
        // Only the reopened day: 23 Oct stays closed, a later date is refused.
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ChargeAsync(h, w.Folio102, D23));
        await Expect.Fails(HccErrorCode.VALIDATION, () => ChargeAsync(h, w.Folio102, D26));
        // The checked-out, invoiced stay takes neither a charge nor a correction (a credit note fixes an invoice).
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ChargeAsync(h, w.Folio103, D24));
        var laundry103 = await h.LongAsync($"SELECT charge_id FROM bill.charge WHERE folio_id = {w.Folio103} AND kind = 'service'", Ct);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => RequestCorrectionAsync(h, w.Folio103, laundry103, 50000));

        // Closed again by the Manager: net ₹4,500 − ₹2,000 + ₹300 = ₹2,800 (280000); tax ₹290 − ₹100 + ₹54 = ₹244 (24400). The business date stays.
        var reclosed = await h.CloseAsync(who: h.Manager, date: D24, ct: Ct);
        Assert.Equal(("Completed", false), (reclosed.Value.Status, reclosed.Replayed));
        var second = await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct);
        Assert.Equal(reclosed.Value.SnapshotId, second.SnapshotId);
        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
        Assert.Equal((280000L, 24400L, -200000L), (second.Record.Figures.Revenue.NetPaise, second.Record.Figures.Revenue.TaxPaise, second.Record.Figures.Revenue.CorrectionsPaise));
        Assert.Equal((reopen.Reopen.ReopenId, h.Manager), (second.ReopenId!.Value, second.Record.ClosedBy));
        Assert.Equal(first.Record.DepositsHeldAtClosePaise, second.Record.DepositsHeldAtClosePaise);
        Assert.Contains(second.Record.Exceptions, e => e.StartsWith("Reopened ", StringComparison.Ordinal) && e.Contains("Room 101 night keyed", StringComparison.Ordinal)
            && e.Contains("closed again", StringComparison.Ordinal));
        Assert.Contains(second.Record.Exceptions, e => e.Contains($"reverses charge {night101}", StringComparison.Ordinal));
        Assert.Equal(D25, await h.BusinessDateAsync(Ct));
        Assert.Null((await h.Closing.GetBusinessDateAsync(h.As(h.Reception), Ct)).ReopenedDate);
        Assert.False((await h.Closing.GetPreflightAsync(h.As(h.Owner), D25, Ct)).Checks.Single(c => c.Id == "reopened").Blocking);

        // History: the first record is kept exactly as closed and marked replaced; the reopen shows Closed with the record it wrote.
        var records = await h.Closing.GetRecordsAsync(h.As(h.Owner), D24, D25, Ct);
        Assert.Equal([second.SnapshotId], records.Items.Select(x => x.SnapshotId));
        var old = Assert.Single(records.Replaced!);
        Assert.Equal((first.SnapshotId, first.Sha256, 450000L), (old.SnapshotId, old.Sha256, old.Record.Figures.Revenue.NetPaise));
        Assert.NotNull(old.SupersededUtc);
        var r = Assert.Single(records.Reopens!);
        Assert.Equal(("Closed", (int?)h.Manager, (long?)second.SnapshotId), (r.Status, r.ReclosedBy, r.SnapshotId));

        // The invoice number and its GST never changed; no new number was taken.
        var invoiceAfter = await h.Get<InvoiceQueries>().GetInvoiceAsync(h.PropertyId, h.Accountant, w.Invoice103, Ct);
        Assert.Equal((invoiceBefore.InvoiceNo, invoiceBefore.TaxablePaise, invoiceBefore.CgstPaise, invoiceBefore.SgstPaise, invoiceBefore.TotalPaise),
            (invoiceAfter.InvoiceNo, invoiceAfter.TaxablePaise, invoiceAfter.CgstPaise, invoiceAfter.SgstPaise, invoiceAfter.TotalPaise));
        Assert.Equal(invoiceBefore.Lines.Select(l => (l.TaxablePaise, l.TaxBps, l.TaxPaise)), invoiceAfter.Lines.Select(l => (l.TaxablePaise, l.TaxBps, l.TaxPaise)));
        Assert.Equal(1, await h.LongAsync("SELECT last_seq FROM ivc.series WHERE doc_type = 'INV'", Ct));

        // Closed again = adjustment-only again: a later correction of a 24 Oct line posts on 25 Oct; a charge on 24 Oct is DAY_CLOSED.
        var later = await ApproveAsync(h, await RequestCorrectionAsync(h, w.Folio102, await NightAsync(h, w.Folio102, "2026-10-24"), 200000));
        Assert.Equal(D25, (await ChargeRowAsync(h, later.ResultId!.Value)).Date);
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ChargeAsync(h, w.Folio102, D24));
        Assert.Equal(second.SnapshotId, (await h.Closing.GetSnapshotAsync(h.As(h.Owner), D24, Ct)).SnapshotId);

        // Stored records stay immutable: no figure change, no second replacement, no delete; the reopen history is append-only.
        Assert.Equal(51501, await SqlErrorAsync(h, $"UPDATE cls.closing_snapshot SET net_revenue_paise = 0 WHERE snapshot_id = {old.SnapshotId};"));
        Assert.Equal(51501, await SqlErrorAsync(h, $"UPDATE cls.closing_snapshot SET superseded_utc = SYSUTCDATETIME() WHERE snapshot_id = {old.SnapshotId};"));
        Assert.Contains(await SqlErrorAsync(h, "DELETE cls.closing_snapshot;"), new[] { 547, 51501 }); // the reopen references both records (FK) before the trigger
        Assert.Equal(2, await h.LongAsync("SELECT COUNT(*) FROM cls.closing_snapshot WHERE business_date = '2026-10-24'", Ct));
        Assert.Equal(51503, await SqlErrorAsync(h, "UPDATE cls.day_reopen SET reason = N'changed';"));
        Assert.Equal(51503, await SqlErrorAsync(h, "UPDATE cls.day_reopen SET reclosed_utc = SYSUTCDATETIME();"));
        Assert.Equal((1L, 1L, 1L, 1L), (await h.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'BUSINESS_DAY_REOPENED' AND target = 'day:2026-10-24'", Ct),
            await h.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'DAY_RECLOSED'", Ct),
            await h.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'BusinessDayReopened'", Ct),
            await h.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'BusinessDayReclosed'", Ct)));
    }

    [Fact]
    public async Task Reopen_is_Owner_only_with_a_reason_refused_for_open_unclosed_locked_or_reopened_days_and_never_unlocks_the_cash_book()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var w = await ClosedDay24Async(h);

        await Expect.Fails(HccErrorCode.FORBIDDEN, () => ReopenAsync(h, h.Manager, D24));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => ReopenAsync(h, h.Accountant, D24));
        await Expect.Fails(HccErrorCode.VALIDATION, () => ReopenAsync(h, h.Owner, D24, "   "));
        Assert.Contains("not closed", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenAsync(h, h.Owner, D25))).Message, StringComparison.Ordinal);
        Assert.Contains("not closed", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenAsync(h, h.Owner, new DateOnly(2026, 10, 20)))).Message, StringComparison.Ordinal);

        // A day locked in Accounting is refused until the Owner unlocks that period.
        await h.Get<AccountingVoucherProjector>().RunOnceAsync(ct: Ct);
        var lk = (await h.Get<AccountingService>().LockAsync(h.As(h.Accountant), Hotel.Key(), new LockPeriodRequest(D24), Ct)).Value;
        Assert.Contains("locked in Accounting", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenAsync(h, h.Owner, D24))).Message, StringComparison.Ordinal);
        await h.Get<AccountingService>().UnlockAsync(h.As(h.Owner), Hotel.Key(), new UnlockPeriodRequest(D24, "Owner reopens 24 Oct for a correction", lk.Version), Ct);

        // The checker locks the 24 Oct cash book (DEC-065); reopening the business day leaves it locked and says so.
        await h.Closing.LockCashBookDayAsync(h.As(h.Accountant), Hotel.Key(), new LockCashBookDayRequest(D24), Ct);
        var reopen = (await ReopenAsync(h, h.Owner, D24)).Value;
        Assert.True(reopen.Reopen.CashBookLocked);
        Assert.Contains("cash book of 2026-10-24 stays locked", reopen.Message, StringComparison.Ordinal);
        Assert.True((await h.Closing.GetCashBookDayAsync(h.As(h.Owner), D24, Ct)).Locked);
        Assert.Contains("cash book stays locked", (string)(await h.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'BUSINESS_DAY_REOPENED'", Ct))!, StringComparison.Ordinal);
        // While the cash book is locked nothing touching 24 Oct posts: neither a charge nor a correction request.
        Assert.Contains("Cash book 2026-10-24 is locked", (await Expect.Fails(HccErrorCode.DAY_CLOSED, () => ChargeAsync(h, w.Folio102, D24))).Message, StringComparison.Ordinal);
        var night101 = await NightAsync(h, w.Folio101, "2026-10-24");
        await Expect.Fails(HccErrorCode.DAY_CLOSED, () => RequestCorrectionAsync(h, w.Folio101, night101, 200000));

        // One reopened day at a time; the reopened day cannot be locked in Accounting.
        Assert.Contains("already reopened", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => ReopenAsync(h, h.Owner, D24))).Message, StringComparison.Ordinal);
        Assert.Contains("Close the business day", (await Expect.Fails(HccErrorCode.INVALID_TRANSITION,
            () => h.Get<AccountingService>().LockAsync(h.As(h.Accountant), Hotel.Key(), new LockPeriodRequest(D24), Ct))).Message, StringComparison.Ordinal);
        Assert.Equal("Open", (await h.Get<AccountingQueries>().DayAsync(h.As(h.Accountant), D24, Ct)).Status);

        // The Owner reopens the cash-book day itself (DEC-065 unchanged); then the charge posts on 24 Oct.
        await h.Closing.ReopenCashBookDayAsync(h.As(h.Owner), Hotel.Key(), new ReopenCashBookDayRequest(D24, "Correction on the reopened day"), Ct);
        Assert.Equal(D24, (await ChargeAsync(h, w.Folio102, D24)).Value.ServiceDate);
    }

    [Fact]
    public async Task An_exported_day_that_is_reopened_is_flagged_and_carried_again_by_Tally_exports_until_one_after_it_is_closed_again()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var w = await ClosedDay24Async(h);
        var projector = h.Get<AccountingVoucherProjector>();
        var tally = h.Get<AccountingQueries>();
        await projector.RunOnceAsync(ct: Ct);

        var exported = await tally.TallyAsync(h.As(h.Accountant), D24, D24, Ct);
        Assert.NotNull(exported.ExportId);
        Assert.Empty(exported.ReexportDates!);
        Assert.Equal((1L, 1L), (await h.LongAsync("SELECT COUNT(*) FROM acc.tally_export", Ct), await h.LongAsync("SELECT COUNT(*) FROM acc.tally_export_day WHERE business_date = '2026-10-24'", Ct)));
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'TALLY_EXPORTED'", Ct));

        var reopen = (await ReopenAsync(h, h.Owner, D24)).Value;
        Assert.True(reopen.Reopen.TallyReexport);
        Assert.Contains("exported to Tally", reopen.Message, StringComparison.Ordinal);
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM acc.tally_reexport WHERE business_date = '2026-10-24' AND cleared_export_id IS NULL", Ct));

        // An export of 25 Oct while 24 Oct is still reopened carries 24 Oct as re-export AND provisional; the flag stays.
        var during = await tally.TallyAsync(h.As(h.Owner), D25, D25, Ct);
        Assert.Equal([D24], during.ReexportDates!);
        Assert.Equal([D24], during.ProvisionalDates!);
        Assert.Contains("RE-EXPORT · 2026-10-24 changed after it was exported (day reopened: Room 101 night keyed on the wrong folio)", during.Xml, StringComparison.Ordinal);
        Assert.Contains("PROVISIONAL · 2026-10-24", during.Xml, StringComparison.Ordinal);
        Assert.Contains("<DATE>20261024</DATE>", during.Xml, StringComparison.Ordinal);
        Assert.Equal(1, await h.LongAsync("SELECT COUNT(*) FROM acc.tally_reexport WHERE cleared_export_id IS NULL", Ct));

        // The correction lands on 24 Oct; after the day is closed again the next export carries it (not provisional) and clears the flag.
        var night101 = await NightAsync(h, w.Folio101, "2026-10-24");
        await ApproveAsync(h, await RequestCorrectionAsync(h, w.Folio101, night101, 200000));
        await h.CloseAsync(who: h.Manager, date: D24, ct: Ct);
        await projector.RunOnceAsync(ct: Ct);
        var after = await tally.TallyAsync(h.As(h.Accountant), D25, D25, Ct);
        Assert.Equal([D24], after.ReexportDates!);
        Assert.Empty(after.ProvisionalDates!);
        Assert.Contains("RE-EXPORT · 2026-10-24", after.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("PROVISIONAL", after.Xml, StringComparison.Ordinal);
        Assert.Contains($"charge {await h.LongAsync($"SELECT charge_id FROM bill.charge WHERE reverses_charge_id = {night101}", Ct)}", after.Xml, StringComparison.Ordinal);
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM acc.tally_reexport WHERE cleared_export_id IS NULL", Ct));
        Assert.Equal(after.ExportId, await h.LongAsync("SELECT cleared_export_id FROM acc.tally_reexport", Ct));

        // Once re-exported, 24 Oct is no longer carried by an export of another range.
        var next = await tally.TallyAsync(h.As(h.Accountant), D25, D25, Ct);
        Assert.Empty(next.ReexportDates!);
        Assert.DoesNotContain("<DATE>20261024</DATE>", next.Xml, StringComparison.Ordinal);
        Assert.Equal(4, await h.LongAsync("SELECT COUNT(*) FROM acc.tally_export", Ct));
        Assert.Equal(51105, await SqlErrorAsync(h, "UPDATE acc.tally_export SET voucher_count = 0;"));
        Assert.Equal(51106, await SqlErrorAsync(h, "DELETE acc.tally_export_day;"));
        Assert.Equal(51107, await SqlErrorAsync(h, "UPDATE acc.tally_reexport SET reason = N'changed';"));
    }
}
