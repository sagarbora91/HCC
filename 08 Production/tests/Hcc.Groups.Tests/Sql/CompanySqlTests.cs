using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Groups.Tests.Sql;

/// <summary>W4 N1: company accounts (GSTIN, credit limit) with the maker-checker limit, on a disposable HccTest_* database and the real access policy.</summary>
[Trait("Category", "Sql")]
public sealed class CompanySqlTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static SaveCompanyRequest New(string name, long limit = 0) => new(null, null, name, "27ZZFIC0009F1Z5", "Asha Rao", "9876543210", "ap@example.test", "Pune",
        new Dictionary<string, long> { ["CLS"] = 350000 }, limit);

    [Fact]
    public async Task Owner_creates_a_company_with_a_direct_limit_and_the_book_lists_it()
    {
        var r = (await _w.Companies.SaveAsync(_w.Ctx("owner"), New("Deccan Pharma Distributors", 50_000_000))).Value;
        Assert.Equal(("set", null), (r.LimitStatus, r.RequestId));
        var book = await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["accountant"]);
        var c = Assert.Single(book.Companies);
        Assert.Equal(("Deccan Pharma Distributors", "27ZZFIC0009F1Z5", 50_000_000L, 350000L), (c.Name, c.Gstin, c.CreditLimitPaise, c.Rates["CLS"]));
        Assert.Empty(book.Requests);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["housekeeper"]));
    }

    [Fact]
    public async Task Reception_limit_becomes_a_request_that_only_another_approver_can_decide()
    {
        var r = (await _w.Companies.SaveAsync(_w.Ctx("reception"), New("Kale Textiles", 20_000_000))).Value;
        Assert.Equal("requested", r.LimitStatus);
        Assert.NotNull(r.RequestId);
        Assert.Equal(0, (await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies[0].CreditLimitPaise);
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Companies.DecideAsync(_w.Ctx("reception"), new DecideCompanyLimitRequest(r.RequestId!.Value, true)));
        // Manager's decision limit is Rs 10,000: a Rs 2,00,000 limit needs the Owner.
        await Expect.Fails(HccErrorCode.APPROVAL_REQUIRED, () => _w.Companies.DecideAsync(_w.Ctx("manager"), new DecideCompanyLimitRequest(r.RequestId!.Value, true)));
        var d = (await _w.Companies.DecideAsync(_w.Ctx("owner"), new DecideCompanyLimitRequest(r.RequestId!.Value, true))).Value;
        Assert.Equal("set", d.LimitStatus);
        var book = await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"]);
        Assert.Equal(20_000_000L, book.Companies[0].CreditLimitPaise);
        Assert.Equal("Approved", Assert.Single(book.Requests).Status);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Companies.DecideAsync(_w.Ctx("owner"), new DecideCompanyLimitRequest(r.RequestId!.Value, true)));
    }

    [Fact]
    public async Task Manager_own_request_cannot_be_decided_by_the_manager_and_rules_hold()
    {
        var id = (await _w.Companies.SaveAsync(_w.Ctx("manager"), New("Patil Logistics"))).Value.CompanyId;
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Companies.SetLimitAsync(_w.Ctx("manager"), new SetCompanyLimitRequest(id, 30_000_000)));
        var req = (await _w.Companies.SetLimitAsync(_w.Ctx("manager"), new SetCompanyLimitRequest(id, 30_000_000, "Annual contract"))).Value;
        Assert.Equal("requested", req.LimitStatus);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Companies.SetLimitAsync(_w.Ctx("manager"), new SetCompanyLimitRequest(id, 40_000_000, "Again")));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Companies.DecideAsync(_w.Ctx("manager"), new DecideCompanyLimitRequest(req.RequestId!.Value, true)));
        // The Owner sets it directly; the pending request is superseded.
        var set = (await _w.Companies.SetLimitAsync(_w.Ctx("owner"), new SetCompanyLimitRequest(id, 10_000_000))).Value;
        Assert.Equal("set", set.LimitStatus);
        Assert.Equal("Rejected", (await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Requests[0].Status);
        await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => _w.Companies.SaveAsync(_w.Ctx("owner"), New("patil logistics")));
        await Expect.Fails(HccErrorCode.VALIDATION, () => _w.Companies.SaveAsync(_w.Ctx("owner"), New("Bad GSTIN") with { Gstin = "123" }));
        await Expect.Fails(HccErrorCode.FORBIDDEN, () => _w.Companies.SaveAsync(_w.Ctx("cashier"), New("Nope")));
    }

    [Fact]
    public async Task Edit_is_versioned_and_never_changes_the_limit()
    {
        var c = (await _w.Companies.SaveAsync(_w.Ctx("owner"), New("Joshi Agro", 10_000_000))).Value;
        var e = (await _w.Companies.SaveAsync(_w.Ctx("manager"), New("Joshi Agro Ltd", 99_000_000) with { CompanyId = c.CompanyId, ExpectedVersion = c.Version })).Value;
        Assert.Equal("none", e.LimitStatus);
        var row = (await _w.CompanyBook.BookAsync(_w.PropertyId, _w.Users["owner"])).Companies[0];
        Assert.Equal(("Joshi Agro Ltd", 10_000_000L), (row.Name, row.CreditLimitPaise));
        await Expect.Fails(HccErrorCode.STALE_VERSION, () => _w.Companies.SaveAsync(_w.Ctx("manager"), New("Joshi Agro Ltd") with { CompanyId = c.CompanyId, ExpectedVersion = c.Version }));
        // Replay with the same key returns the stored result.
        var ctx = _w.Ctx("owner", "same-key");
        var a = await _w.Companies.SaveAsync(ctx, New("Replay Co"));
        var b = await _w.Companies.SaveAsync(_w.Ctx("owner", "same-key"), New("Replay Co"));
        Assert.True(b.Replayed);
        Assert.Equal(a.Value.CompanyId, b.Value.CompanyId);
    }
}
