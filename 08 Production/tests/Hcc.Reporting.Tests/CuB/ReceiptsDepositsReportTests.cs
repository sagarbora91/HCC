// CU-RPT-B · rpt.money.receipts-deposits on a disposable HccTest_* database: tender columns from the Payment methods master (retired method left out, an unlisted
// tender appended), payments minus refunds per tender and day, deposits in / out, company credit and deposits held now, rule V / M through the real access policy,
// screen = Excel = PDF = print.
using Hcc.Contracts;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Reporting;
using Hcc.Reporting.Reports.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.CuB;

[Trait("Category", "Sql")]
public sealed class ReceiptsDepositsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await SeedMethods(db);
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Folio 1: 21 Oct Cash 9.99 (outside the range); 22 Oct Cash 1,000 + UPI 500 + deposit Cash 300; 23 Oct Cash 200, refund Cash 50, Wallet 70 (not in the master),
    // deposit returned 100, company credit 400; 24 Oct Card 250. Folio 2: 22 Oct deposit UPI 600; 24 Oct deposit applied 200 (Cash) and 50 carried in the opening balance.
    // Held now = 300 + 600 - 100 - 200 = 600.00 (the opening advance never sat in a deposit receipt).
    private async Task Seed()
    {
        var own = db.Users["OWN"];
        await db.ScalarAsync(RoomsSql(db, "101", "102") + $"""
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101'), @r102 INT = (SELECT room_id FROM inv.room WHERE room_no = '102');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source) VALUES
              (@p, 'HCC-3001', N'Ananya Rao', '2026-10-21', '2026-10-26', 'InHouse', @r101, @t, 1, 0, 'Direct'), (@p, 'HCC-3002', N'Claire Dupont', '2026-10-22', '2026-10-26', 'InHouse', @r102, @t, 1, 0, 'Direct');
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-3001','HCC-3002');
            DECLARE @f1 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-3001'),
                    @f2 BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-3002');
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES
              (@f1, 'payment', 'Cash', 999, '2026-10-21'),
              (@f1, 'payment', 'Cash', 100000, '2026-10-22'), (@f1, 'payment', 'UPI', 50000, '2026-10-22'), (@f1, 'deposit', 'Cash', 30000, '2026-10-22'),
              (@f1, 'payment', 'Cash', 20000, '2026-10-23'), (@f1, 'payment', 'Wallet', 7000, '2026-10-23'),
              (@f1, 'deposit-return', 'Cash', 10000, '2026-10-23'), (@f1, 'company-credit', 'Company credit', 40000, '2026-10-23'),
              (@f1, 'payment', 'Card', 25000, '2026-10-24'),
              (@f2, 'deposit', 'UPI', 60000, '2026-10-22'), (@f2, 'deposit-apply', 'Cash', 20000, '2026-10-24'), (@f2, 'deposit-apply', 'Opening advance applied', 5000, '2026-10-24');
            -- split stay: folio B (seq 2) of the same reservation takes a Card payment; a method once named "Paytm" (renamed to UPI in the master) keeps its own column, listed after Wallet (first seen)
            INSERT bill.folio(reservation_id, folio_seq) SELECT reservation_id, 2 FROM inv.reservation WHERE public_no = 'HCC-3001';
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date)
              SELECT folio_id, 'payment', 'Card', 3000, '2026-10-24' FROM bill.folio WHERE reservation_id = (SELECT reservation_id FROM inv.reservation WHERE public_no = 'HCC-3001') AND folio_seq = 2;
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f1, 'payment', 'Paytm', 10000, '2026-10-22');
            INSERT pay.receipt(folio_id, kind, method, amount_paise, refund_of, business_date)
              SELECT @f1, 'refund', 'Cash', 5000, receipt_id, '2026-10-23' FROM pay.receipt WHERE folio_id = @f1 AND kind = 'payment' AND method = 'Cash' AND amount_paise = 20000;
            SELECT 1;
            """);
        _ = own;
    }

    private ReportRunner Runner() => CuBRig.Runner(db, new ReceiptsDepositsReport(new SqlReportSource(db.Platform.Connections), db.Platform));

    [Fact]
    public async Task Tender_columns_follow_the_master_and_figures_are_exact_to_the_paisa()
    {
        var r = await Run(db, Runner(), "OWN", ReceiptsDepositsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var days = ReportRunner.Tables(r)[0];
        // master desk methods in list order (the retired "Old wallet" is out), then the tender found in the range
        Assert.Equal(["date", "t:Cash", "t:UPI", "t:Card", "t:Bank transfer", "t:Wallet", "t:Paytm", "net", "depositsIn", "depositsOut"], days.Columns.Select(c => c.Key));
        Assert.All(days.Columns.Skip(1), c => Assert.True(c.Money));
        var d = Data(days);
        Assert.Equal(["2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => S(x, "date")));
        Assert.Equal((100000L, 50000L, 0L, 0L, 0L, 10000L, 160000L, 90000L, 0L), Cells(d[0]));
        Assert.Equal((15000L, 0L, 0L, 0L, 7000L, 0L, 22000L, 0L, -10000L), Cells(d[1])); // Cash 200 - refund 50; the deposit returned is negative, as the prototype
        Assert.Equal((0L, 0L, 28000L, 0L, 0L, 0L, 28000L, 0L, 0L), Cells(d[2])); // Card 250 on folio A + 30 on folio B of the split stay: counted once each
        var total = days.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal((115000L, 50000L, 28000L, 0L, 7000L, 10000L, 210000L, 90000L, -10000L), Cells(total));
        Assert.Equal(d.Sum(x => L(x, "net")), L(total, "net"));
    }

    private static (long, long, long, long, long, long, long, long, long) Cells(ReportRow x) =>
        (L(x, "t:Cash"), L(x, "t:UPI"), L(x, "t:Card"), L(x, "t:Bank transfer"), L(x, "t:Wallet"), L(x, "t:Paytm"), L(x, "net"), L(x, "depositsIn"), L(x, "depositsOut"));

    [Fact]
    public async Task Second_block_has_company_credit_in_the_range_and_deposits_held_now()
    {
        var r = await Run(db, Runner(), "ACC", ReceiptsDepositsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var now = ReportRunner.Tables(r)[1];
        Assert.Equal("now", now.Key);
        Assert.Equal(["item", "amount"], now.Columns.Select(c => c.Key));
        Assert.Equal(("Company credit (city ledger) in the range", 40000L), (S(now.Rows[0], "item"), L(now.Rows[0], "amount")));
        Assert.Equal(("Deposits held now · all open folios", 60000L), (S(now.Rows[1], "item"), L(now.Rows[1], "amount")));
        // the held figure is a point in time: a narrower range leaves it unchanged, the company credit follows the range
        var one = await Run(db, Runner(), "MGR", ReceiptsDepositsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22"));
        var n1 = ReportRunner.Tables(one)[1];
        Assert.Equal((0L, 60000L), (L(n1.Rows[0], "amount"), L(n1.Rows[1], "amount")));
    }

    [Fact]
    public async Task Only_roles_with_the_finance_report_right_may_run_it()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ReceiptsDepositsReport.Id)));
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, ReceiptsDepositsReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, runner, "OWN", ReceiptsDepositsReport.Id, ("from", "2026-10-25"), ("to", "2026-10-26"))));
    }
}
