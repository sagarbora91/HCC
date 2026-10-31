using System.Globalization;
using Hcc.Application.Groups;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Groups;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// ACC-2 lane acc2-flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §6; DEC-157): company retire on <c>grp.company_account.active</c>
/// (migration 4253) — retiring refuses an open receivable / open stays / open group masters (MASTER_IN_USE), the book flags the company, a retired
/// company takes no new billing, and reactivation brings it back. Mirrors the prototype's [ACC2-FLOWS] company retire tests.
/// </summary>
[Trait("Category", "Sql")]
public sealed class CompanyRetireSqlTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private CompanyRetireService Retire => new(_w.Platform.Commands, _w.CompanyStore, new SqlCompanyCreditStore(_w.Platform.Connections),
        new SqlFlowsStore(_w.Platform.Connections), _w.Policy, _w.Clock);

    private async Task<long> NewCompanyAsync(string name) => (await _w.Companies.SaveAsync(_w.Ctx("owner"), new SaveCompanyRequest(null, null, name, null, "Asha Rao", "9876543210",
        "ap@example.test", "Pune", new Dictionary<string, long>(), 50_000_000))).Value.CompanyId;

    [Fact]
    public async Task Retire_hides_the_company_and_reactivation_brings_it_back()
    {
        var id = await NewCompanyAsync("Flows Retire Co");
        var r = (await Retire.SetActiveAsync(_w.Ctx("manager"), new SetCompanyActiveRequest(id, false, "No longer a client"))).Value;
        Assert.Equal((false, "Flows Retire Co retired (hidden from pickers) · No longer a client"), (r.Active, r.Message));
        var c = (await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies.Single(x => x.CompanyId == id);
        Assert.False(c.Active);
        Assert.Equal(1L, await _w.LongAsync($"SELECT COUNT(*) FROM grp.company_account WHERE company_id = {id} AND active = 0 AND retired_by IS NOT NULL AND retire_reason = N'No longer a client';"));
        // A retired company takes no new billing (CompanyCreditCheck.NewAsync).
        var row = (await _w.CompanyStore.ListAsync(_w.PropertyId)).Single(x => x.CompanyId == id);
        var ex = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => CompanyCreditCheck.NewAsync(_w.Credit, null!, row, "stay", 0, "booking", CancellationToken.None));
        Assert.Equal("MASTER_RETIRED", ex.Details!["reason"].GetString());
        Assert.True((await Retire.SetActiveAsync(_w.Ctx("manager"), new SetCompanyActiveRequest(id, true))).Value.Active);
        Assert.True((await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies.Single(x => x.CompanyId == id).Active);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => Retire.SetActiveAsync(_w.Ctx("housekeeper"), new SetCompanyActiveRequest(id, false)));
    }

    [Fact]
    public async Task Retire_refuses_a_company_that_still_owes()
    {
        var id = await NewCompanyAsync("Owing Co");
        var folio = _w.Folio.Values.First();
        await _w.ExecAsync($"""
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date, reference, company_id)
            VALUES ({folio.ToString(CultureInfo.InvariantCulture)}, 'company-credit', N'Company credit', 620000, '2026-10-24', N'Owing Co · test', {id.ToString(CultureInfo.InvariantCulture)});
            """);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => Retire.SetActiveAsync(_w.Ctx("manager"), new SetCompanyActiveRequest(id, false)));
        Assert.Equal("MASTER_IN_USE", ex.Details!["reason"].GetString());
        Assert.Contains("still owes ₹6,200 on company credit", ex.Message, StringComparison.Ordinal);
        Assert.True((await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies.Single(x => x.CompanyId == id).Active);
    }
}
