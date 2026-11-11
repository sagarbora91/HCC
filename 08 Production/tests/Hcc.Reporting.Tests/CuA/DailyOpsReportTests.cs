// CU-RPT-A · rpt.guests.daily-ops on the server: stays, charges, receipts and expenses seeded on a disposable HccTest_* database; per-day figures and the total row to the paise,
// day status (Closed / Open / Future), refusals for Cashier, Reception and Housekeeping, DEC-061 guard of the hub service, screen = Excel = PDF = print.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

[Trait("Category", "Sql")]
public sealed class DailyOpsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakePolicy _policy = new();

    public async ValueTask InitializeAsync()
    {
        await CuARig.SeedCash(db);      // closes 22 and 23 Oct
        await CuARig.SeedExpenses(db);
        await CuARig.SeedOnce(db, "stays", async () =>
        {
            var (p, own) = (db.PropertyId, db.Users["OWN"]);
            await db.ScalarAsync($"""
                DECLARE @p INT = {p};
                INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES (@p, 'F1', N'Floor 1', 1, {own}, '2026-01-01');
                DECLARE @fl INT = SCOPE_IDENTITY();
                INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic');
                DECLARE @cls INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS');
                INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, '101', @cls, 'Ready', @fl), (@p, '102', @cls, 'Ready', @fl);
                DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = '101'), @r102 INT = (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = '102');
                INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc) VALUES
                  (@p, N'Ananya Rao', '+919820001041', N'Indian', N'Aadhaar', '4101', N'10 Demo Street, Bengaluru, Karnataka (fictional)', 'manual', '2026-10-01');
                DECLARE @g1 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Ananya Rao');
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source, phone) VALUES
                  (@p, 'HCC-2001', N'Ananya Rao', '2026-10-22', '2026-10-24', 'InHouse', @r101, @cls, 1, 0, 'Direct', NULL),
                  (@p, 'HCC-2002', N'Ananya Rao', '2026-10-23', '2026-10-25', 'Departed', @r102, @cls, 1, 0, 'Direct', NULL);
                INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-2001', 'HCC-2002');
                INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) SELECT reservation_id, @g1, '2026-10-20', {own} FROM inv.reservation WHERE public_no IN ('HCC-2001', 'HCC-2002');
                INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
                  SELECT @p, r.reservation_id, @g1, f.folio_id, CASE r.public_no WHEN 'HCC-2002' THEN 'CheckedOut' ELSE 'InHouse' END, r.arrival, r.departure, r.arrival,
                         DATEADD(MINUTE, 510, CAST(r.arrival AS DATETIME2)), {own},
                         CASE r.public_no WHEN 'HCC-2002' THEN '2026-10-24' END, CASE r.public_no WHEN 'HCC-2002' THEN '2026-10-24 05:30' END, CASE r.public_no WHEN 'HCC-2002' THEN {own} END
                    FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id WHERE r.public_no IN ('HCC-2001', 'HCC-2002');
                DECLARE @f1 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2001'),
                        @f2 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2002');
                INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
                  (@f1, 'room', '2026-10-22', N'Room night · 2026-10-22', 200000, 500, 10000), (@f1, 'room', '2026-10-23', N'Room night · 2026-10-23', 200000, 500, 10000),
                  (@f2, 'room', '2026-10-23', N'Room night · 2026-10-23', 300000, 500, 15000);
                INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f1, 'payment', 'Cash', 150000, '2026-10-23');
                SELECT 1;
                """);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Per_day_figures_status_and_the_total_row_to_the_paise()
    {
        var r = await CuARig.Run(db, _policy, db.Users["MGR"], DailyOpsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var d = CuARig.Data(r);
        Assert.Equal(["2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => CuARig.S(x, "date")));
        Assert.Equal(["Closed", "Closed", "Open"], d.Select(x => CuARig.S(x, "status")));
        Assert.Equal([1L, 1, 0], d.Select(x => CuARig.L(x, "arrivals")));
        Assert.Equal([0L, 0, 1], d.Select(x => CuARig.L(x, "departures")));
        Assert.Equal([1L, 2, 0], d.Select(x => CuARig.L(x, "occupied")));
        Assert.Equal([200000L, 500000, 0], d.Select(x => CuARig.L(x, "netRevenue")));
        Assert.Equal([0L, 150000, 0], d.Select(x => CuARig.L(x, "netReceipts")));
        Assert.Equal([130000L, 1430000, 0], d.Select(x => CuARig.L(x, "expenses"))); // 22: 1,000 + 300; 23: 4,500 + 10,100 - 300
        var t = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 2L, 1L, 3L, 700000L, 150000L, 1560000L),
            (CuARig.S(t, "date"), CuARig.L(t, "arrivals"), CuARig.L(t, "departures"), CuARig.L(t, "occupied"), CuARig.L(t, "netRevenue"), CuARig.L(t, "netReceipts"), CuARig.L(t, "expenses")));
    }

    [Fact]
    public void Expenses_exclude_capital_and_unposted_records_and_status_names_closed_open_future()
    {
        static Hcc.Domain.Expenses.LedgerRow l(DateOnly d, long amt, string status, bool capital, bool rev) => new(1, d, "C", "Cash", "V", null, amt, null, null, status, null, "x", null, null, rev,
            DateTime.UtcNow, null, false, capital);
        var day = new DateOnly(2026, 10, 22);
        var sums = DailyOpsReport.ExpensesByDate([l(day, 1000, "Posted", false, false), l(day, 500, "Posted", true, false), l(day, 700, "Rejected", false, false), l(day, 300, "Reversed", false, false),
            l(day.AddDays(1), -300, "Posted", false, true)]);
        Assert.Equal((1300L, -300L), (sums[day], sums[day.AddDays(1)]));
        var closed = new HashSet<DateOnly> { day };
        Assert.Equal(["Closed", "—", "Open", "Future"], new[] { day, day.AddDays(1), day.AddDays(2), day.AddDays(3) }.Select(x => DailyOpsReport.Status(x, day.AddDays(2), closed)));
    }

    [Fact]
    public async Task Cashier_reception_and_housekeeping_are_refused_and_the_hub_enforces_DEC_061_even_with_a_mis_seeded_grant()
    {
        foreach (var who in new[] { db.Users["CSH"], db.Users["REC"], 7 })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, who, DailyOpsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22")))).Code);
        foreach (var perm in new[] { "PERM-RPT-001", "PERM-RPT-002", "PERM-RPT-003" }) _policy.Grants[perm].Add("HKP");
        var s = CuARig.Service(db, _policy);
        Assert.DoesNotContain(await s.CatalogueAsync(Rig.C(7), Ct), e => e.Definition.MoneyFlag);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.RunAsync(Rig.C(7), DailyOpsReport.Id, Rig.NoParams, null, 100, Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ExportAsync(Rig.C(7), DailyOpsReport.Id, "csv", Rig.NoParams, Ct))).Code);
        Assert.Contains(await s.CatalogueAsync(Rig.C(1), Ct), e => e.Definition.Id == DailyOpsReport.Id && e.Money);
    }
}
