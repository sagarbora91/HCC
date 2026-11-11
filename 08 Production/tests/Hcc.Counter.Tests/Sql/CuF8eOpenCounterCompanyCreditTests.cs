using Hcc.Application.Commands;
using Hcc.Application.Counter;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>
/// CU f8e-open: the missing company-credit counter-bill test (F8E-SQL-DONE "not covered"). A counter bill settled on company credit is stored with the
/// company id, takes no money into the till, counts against the company's limit together with the other open counter bills, and an over-limit bill is
/// refused with the shared "Credit limit exceeded" text and no write anywhere. Fictional data; integer paise.
/// </summary>
public sealed class CuF8eOpenCounterCompanyCreditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] Tables = ["ctr.bill", "ctr.bill_line", "pay.cash_movement", "ivc.invoice", "plat.outbox"];

    private static async Task<long[]> CountsAsync(Hotel h) => [.. await Task.WhenAll(Tables.Select(t => h.LongAsync($"SELECT COUNT(*) FROM {t}", Ct)))];

    private static IssueCounterBillRequest OnCompany(long companyId, string who) =>
        new(new(who), [new("Dinner", 1, 40000, "food")], new("company", null, null, null, companyId));

    private static Task<CommandResult<CounterBillResult>> Issue(Hotel h, IssueCounterBillRequest r) =>
        h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue, null), r, Ct);

    [Fact]
    public async Task Company_credit_counter_bills_count_together_against_the_limit_and_the_over_limit_bill_writes_nothing()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        // limit Rs 1,000.00 = 100000 paise; each bill is 40000 + 5 % food GST (2000) = 42000
        await h.ScalarAsync($"INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) VALUES ({h.PropertyId}, N'Counter Credit Traders (fictional)', 100000, {h.Owner}, SYSUTCDATETIME());", Ct);
        var cid = await h.LongAsync($"SELECT company_id FROM grp.company_account WHERE property_id = {h.PropertyId}", Ct);

        var a = await Issue(h, OnCompany(cid, "Walk-in One (fictional)"));
        var b = await Issue(h, OnCompany(cid, "Walk-in Two (fictional)"));
        Assert.Equal(("company", 42000L), (a.Value.Settlement, a.Value.TotalPaise));
        Assert.Equal(42000L, b.Value.TotalPaise);
        // stored against the company, no tender, no till movement
        Assert.Equal(cid, await h.LongAsync($"SELECT company_id FROM ctr.bill WHERE bill_id = {a.Value.BillId}", Ct));
        Assert.Equal("company", (string)(await h.ScalarAsync($"SELECT settlement_mode FROM ctr.bill WHERE bill_id = {a.Value.BillId}", Ct))!);
        Assert.Equal(0, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'counter-bill'", Ct));
        Assert.Equal(84000L, await h.LongAsync($"SELECT SUM(total_paise) FROM ctr.bill WHERE company_id = {cid} AND status = 'Issued'", Ct));

        // 84000 already open + 42000 = 126000 > 100000: refused, nothing written
        var before = await CountsAsync(h);
        var ex = await Assert.ThrowsAsync<HccException>(() => Issue(h, OnCompany(cid, "Walk-in Three (fictional)")));
        Assert.Contains("Credit limit exceeded for Counter Credit Traders (fictional)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("this counter bill", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync(h));

        // raising the limit to exactly the three bills (126000) lets the third through to the paise; one paisa less would not
        await h.ScalarAsync($"UPDATE grp.company_account SET credit_limit_paise = 125999 WHERE company_id = {cid};", Ct);
        await Assert.ThrowsAsync<HccException>(() => Issue(h, OnCompany(cid, "Walk-in Three (fictional)")));
        await h.ScalarAsync($"UPDATE grp.company_account SET credit_limit_paise = 126000 WHERE company_id = {cid};", Ct);
        var c = await Issue(h, OnCompany(cid, "Walk-in Three (fictional)"));
        Assert.Equal(42000L, c.Value.TotalPaise);
        Assert.Equal(126000L, await h.LongAsync($"SELECT SUM(total_paise) FROM ctr.bill WHERE company_id = {cid} AND status = 'Issued'", Ct));
    }
}
