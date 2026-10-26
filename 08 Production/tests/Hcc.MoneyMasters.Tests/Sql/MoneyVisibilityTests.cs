using Hcc.Contracts.MoneyMasters;
using Hcc.MoneyMasters.Tests.Harness;

namespace Hcc.MoneyMasters.Tests.Sql;

/// <summary>MA-11 review (DEC-061): money values of the money-master lists are never sent to Housekeeping, Maintenance, Supervisor or Tech admin.</summary>
[Trait("Category", "Sql")]
public class MoneyVisibilityTests : IAsyncLifetime
{
    private MoneyWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MoneyWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [Fact]
    public async Task Surcharge_and_approval_limit_are_null_for_roles_without_money_visibility()
    {
        var cash = (await _w.Queries.PaymentMethodsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(m => m.Code == "CARD");
        await _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s1"), new SavePaymentMethodRequest(cash.PaymentMethodId, cash.Code, cash.Name, cash.Kind, cash.SettlesTo,
            cash.NeedsReference, cash.UsableAt, 150, cash.Ledger, cash.SortOrder, cash.Version), Ct);
        await _w.Commands.SaveExpenseCategoryAsync(_w.Ctx("owner", "c1"), new SaveExpenseCategoryRequest(null, "GAS", "Kitchen gas", "5050", true, 100000), Ct);
        foreach (var u in new[] { "owner", "manager", "reception", "cashier", "accountant" })
        {
            Assert.Equal(150, (await _w.Queries.PaymentMethodsAsync(_w.PropertyId, _w.Users[u], ct: Ct)).Items.Single(m => m.Code == "CARD").SurchargeBps);
            Assert.Equal(100000, (await _w.Queries.ExpenseCategoriesAsync(_w.PropertyId, _w.Users[u], ct: Ct)).Items.Single(c => c.Code == "GAS").ApprovalLimitPaise);
        }

        foreach (var u in new[] { "housekeeper", "maint", "supervisor", "techadmin" })
        {
            var m = (await _w.Queries.PaymentMethodsAsync(_w.PropertyId, _w.Users[u], ct: Ct)).Items.Single(x => x.Code == "CARD");
            Assert.Null(m.SurchargeBps);
            Assert.Equal("Card", m.Name);   // the list itself stays readable (names for pick lists)
            Assert.Null((await _w.Queries.ExpenseCategoriesAsync(_w.PropertyId, _w.Users[u], ct: Ct)).Items.Single(c => c.Code == "GAS").ApprovalLimitPaise);
            Assert.Null((await _w.Queries.BankAccountsAsync(_w.PropertyId, _w.Users[u], ct: Ct)).Items[0].OpeningBalancePaise);
        }
    }
}
