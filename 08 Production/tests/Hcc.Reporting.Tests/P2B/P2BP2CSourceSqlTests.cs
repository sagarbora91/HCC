// [F8D-TESTS-B] First execution of the batch-B / batch-C source SELECTs against SQL Server (the F8-RPT-P2-B and -C DONE files say they were compiled but never run):
// SqlP2BReportSource.DiscountsAsync / RefundsAsync and SqlP2CReportSource.CancelledAsync, with key totals. Disposable HccTest_* database. Fictional data. Tests only.
using Hcc.Reporting.Reports.P2B;
using Hcc.Reporting.Reports.P2C;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.P2B;

[Trait("Category", "Sql")]
public sealed class P2BP2CSourceSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        var rec = db.Users["REC"];
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise) VALUES (@p, 'DLX', 3, N'Deluxe', 200000);
            DECLARE @t INT = SCOPE_IDENTITY();
            INSERT inv.room(property_id, room_no, room_type_id, hk_status) VALUES (@p, '301', @t, 'Ready');
            DECLARE @rm INT = SCOPE_IDENTITY();
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source, nightly_rate_paise)
              VALUES (@p, 'HCC-3001', N'Source Guest (fictional)', '2026-10-22', '2026-10-24', 'Departed', @rm, @t, 1, 0, 'Direct', 200000);
            DECLARE @r BIGINT = SCOPE_IDENTITY();
            INSERT bill.folio(reservation_id) VALUES (@r);
            DECLARE @f BIGINT = SCOPE_IDENTITY();
            INSERT inv.room_night(room_id, night, reservation_id) VALUES (@rm, '2026-10-22', @r), (@rm, '2026-10-23', @r);
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
              (@f, 'room', '2026-10-22', N'Room night · 2026-10-22', 200000, 500, 10000), (@f, 'room', '2026-10-23', N'Room night · 2026-10-23', 200000, 500, 10000);

            -- discount 1: approved ₹200 (kept); discount 2: ₹100 reversed by a correction (not listed)
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, 'discount', @f, 'folio', @f, 20000, N'Loyal guest', 'Executed', {rec}, '2026-10-22 10:00', '2026-10-23 10:00', {own}, '2026-10-22 10:05');
            DECLARE @a1 BIGINT = SCOPE_IDENTITY();
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, approval_id) VALUES (@f, 'discount', '2026-10-22', N'Discount · Loyal guest', -20000, 500, -1000, @a1);
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES (@f, 'discount', '2026-10-23', N'Discount · Mistake', -10000, 500, -500);
            DECLARE @bad BIGINT = SCOPE_IDENTITY();
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, 'correction', @f, 'charge', @bad, 10000, N'Entered twice', 'Executed', {rec}, '2026-10-23 11:00', '2026-10-24 11:00', {own}, '2026-10-23 11:05');
            DECLARE @a2 BIGINT = SCOPE_IDENTITY();
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id, approval_id)
              VALUES (@f, 'correction', '2026-10-23', N'Reversal · Discount · Mistake', 10000, 500, 500, @bad, @a2);

            -- refund ₹300 of a ₹1,000 payment, approved
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', 100000, '2026-10-23');
            DECLARE @pay BIGINT = SCOPE_IDENTITY();
            INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, reason, status, requested_by, requested_utc, expires_utc, decided_by, decided_utc)
              VALUES (@p, 'refund', @f, 'receipt', @pay, 30000, N'Early departure', 'Executed', {rec}, '2026-10-23 12:00', '2026-10-24 12:00', {own}, '2026-10-23 12:05');
            DECLARE @a3 BIGINT = SCOPE_IDENTITY();
            INSERT pay.receipt(folio_id, kind, method, amount_paise, refund_of, business_date, approval_id) VALUES (@f, 'refund', 'Cash', 30000, @pay, '2026-10-23', @a3);
            SELECT 1;
            """);
    }

    [Fact]
    public async Task P2B_discounts_refunds_and_P2C_cancelled_lines_run_against_SQL_with_the_right_totals()
    {
        await SeedAsync();
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        var rec = db.Users["REC"];
        var from = new DateOnly(2026, 10, 22);
        var to = new DateOnly(2026, 10, 24);

        var b = new SqlP2BReportSource(db.Platform.Connections);
        var discounts = await b.DiscountsAsync(p, from, to, Ct);
        // Only the live discount is listed (the reversed one is not): ₹200, approved by the Owner, reason from the line label.
        var d = Assert.Single(discounts);
        Assert.Equal((new DateOnly(2026, 10, 22), "HCC-3001", "Source Guest (fictional)", "Loyal guest", 20000L, own), (d.Date, d.Ref, d.Guest, d.Reason, d.AmountPaise, d.ApprovedBy));

        var refunds = await b.RefundsAsync(p, from, to, Ct);
        var r = Assert.Single(refunds);
        Assert.Equal((new DateOnly(2026, 10, 23), 30000L, "Cash", "Early departure", own), (r.Date, r.AmountPaise, r.Tender, r.Reason, r.ApprovedBy));
        Assert.Equal("HCC-3001", r.BillNo);                       // no invoice yet: falls back to the booking number
        Assert.Empty(await b.RefundsAsync(p, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25), Ct));   // range respected

        var c = new SqlP2CReportSource(db.Platform.Connections);
        var cancelled = await c.CancelledAsync(p, from, to, Ct);
        var x = Assert.Single(cancelled);                         // the discount reversal is the only approved correction
        Assert.Equal((new DateOnly(2026, 10, 23), "HCC-3001", "301", "Discount · Mistake", 10000L, rec, "Entered twice"),
            (x.Date, x.Booking, x.Room, x.Item, x.AmountPaise, x.By, x.Reason));
        var names = await c.NamesAsync(p, Ct);
        Assert.Equal("OWN", names[own]);
    }
}
