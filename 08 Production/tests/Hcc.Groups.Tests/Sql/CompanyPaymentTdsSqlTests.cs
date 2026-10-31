using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// ACC-2 · lane acc2-tds (contract §4 A9; rulebook T7b; migration 4242): a company pays its company-credit receivable net of the TDS it deducted.
/// Amount + TDS settle the receivable (never more than it; TDS at most 20 % of the sum); the payment stores tds_paise, the event carries it and the
/// accounting consumer books Receipt Dr tender · Dr 1360 TDS receivable · Cr 1110/&lt;company&gt; (gross); the Form 168 reconciliation lists it.
/// World as CompanyCreditSqlTests: HCC-1048's Rs 100 balance put on company credit (receivable Rs 100 = 10000 paise).
/// </summary>
[Trait("Category", "Sql")]
public sealed class CompanyPaymentTdsSqlTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private GroupWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private const string Deccan = "Deccan Agro (fictional)";

    private static string Key() => Guid.NewGuid().ToString("N");

    private AccountingVoucherProjector Projector => new(_w.Platform.Connections, _w.Platform.Outbox, new StoredChargeGstReader(), _w.Clock);

    private AccountingQueries Books => new(new SqlAccountingStore(_w.Platform.Connections), _w.Policy);

    [Fact]
    public async Task A_payment_net_of_tds_settles_amount_plus_tds_and_books_1360()
    {
        var id = (await _w.Companies.SaveAsync(_w.Ctx("owner"), new SaveCompanyRequest(null, null, Deccan, CreditLimitPaise: 1_000_000))).Value.CompanyId;
        await _w.ExecAsync($"UPDATE inv.reservation SET company_id = {id}, company_name = N'{Deccan}' WHERE reservation_id = {_w.Res["HCC-1048"]};");
        var version = (await _w.Folios.GetFolioAsync(_w.Caller("owner"), _w.Folio["HCC-1048"])).Version;
        var r = (await _w.CreditBilling.RequestAdjustmentAsync(_w.Caller("reception"), Key(), new RequestAdjustmentRequest("credit", _w.Folio["HCC-1048"], null, 10_000,
            "Bill to " + Deccan, version))).Value;
        var d = (await _w.CreditBilling.DecideApprovalAsync(_w.Caller("owner"), Key(), new DecideApprovalRequest(r.ApprovalId!.Value, true, null, r.ApprovalVersion!))).Value;
        await _w.CreditBilling.ExecuteAdjustmentAsync(_w.Caller("reception"), Key(), new ExecuteAdjustmentRequest(r.ApprovalId.Value, d.Version, null, null, null));

        // Rs 90 received + Rs 10 TDS = the Rs 100 receivable. Guards: TDS above 20 % of the sum; amount + TDS above the receivable; negative TDS.
        var pay = new RecordCompanyPaymentRequest(id, "Bank transfer", 9_000, "NEFT-DEMO-0430", "inv/2627/0031", TdsPaise: 1_000);
        var tooMuch = await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { TdsPaise = 3_000 }));
        Assert.Contains("more than 20 %", tooMuch.Message, StringComparison.Ordinal);
        var over = await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { AmountPaise = 9_500 }));
        Assert.Equal($"Payment ₹95 + TDS ₹10 cannot exceed what {Deccan} owes on company credit (₹100).", over.Message);
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { TdsPaise = -1 }));
        var paid = (await _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay)).Value;
        Assert.Equal((0L, 9_000L, 1_000L), (paid.ReceivablePaise, paid.AmountPaise, paid.TdsPaise));
        Assert.Equal(1_000L, await _w.LongAsync($"SELECT tds_paise FROM grp.company_payment WHERE payment_id = {paid.PaymentId};"));
        Assert.Contains("\"tdsPaise\":1000", (string)(await _w.ScalarAsync("SELECT payload FROM plat.outbox WHERE event_type = 'CompanyPaymentRecorded';"))!, StringComparison.Ordinal);
        var book = await _w.CreditBook.BookAsync(_w.PropertyId, _w.Users["owner"]);
        Assert.Equal(1_000L, Assert.Single(book.Payments!).TdsPaise);
        Assert.Equal(0L, book.Companies.Single(c => c.CompanyId == id).Credit!.ReceivablePaise);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Credit.RecordPaymentAsync(_w.Ctx("cashier"), pay with { Reference = "NEFT-DEMO-0431" })); // owes nothing

        // The books: Dr tender 90 · Dr 1360 10 · Cr 1110/CMP-<id> 100.
        Assert.True(await Projector.RunOnceAsync(ct: Ct) > 0);
        Assert.Equal(0, await Projector.RunOnceAsync(ct: Ct)); // everything consumed, nothing parked
        var day = await Books.DayAsync(_w.Caller("owner"), Today, Ct);
        var v = day.Vouchers.Single(x => x.SourceKey == "company-payment:" + paid.PaymentId);
        Assert.Equal(("Receipt", "company-payment", true), (v.Type, v.Sub, v.Balanced));
        Assert.Equal((9_000L, 0L), v.Entries.Where(e => e.Account.Split('/')[0] == "1030").Select(e => (e.DebitPaise, e.CreditPaise)).Single());
        Assert.Equal(("1360", 1_000L, 0L), v.Entries.Where(e => e.Account == "1360").Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).Single());
        Assert.Equal(("1110/CMP-" + id, 0L, 10_000L), v.Entries.Where(e => e.Account.StartsWith("1110", StringComparison.Ordinal)).Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).Single());
        Assert.EndsWith("· TDS ₹10.00 deducted", v.Narration, StringComparison.Ordinal);
        Assert.DoesNotContain(day.Exceptions, x => x.Kind is "unbalanced" or "unmapped");

        // Form 168 reconciliation (PERM-ACC-013: Owner, Accountant; the Cashier is refused).
        var tds = new TdsService(_w.Platform.Commands, new SqlTdsStore(_w.Platform.Connections), _w.Policy, _w.Clock);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => tds.ReceivableAsync(_w.Caller("cashier"), "2026-27", Ct));
        var rep = await tds.ReceivableAsync(_w.Caller("owner"), "2026-27", Ct);
        Assert.Equal([(id, Deccan, "2026-10", 1, 9_000L, 1_000L, 10_000L)], rep.Rows.Select(x => (x.CompanyId, x.Company, x.Month, x.Payments, x.ReceivedPaise, x.TdsPaise, x.SettledPaise)));
        Assert.Equal(1_000L, rep.TdsPaise);
    }
}
