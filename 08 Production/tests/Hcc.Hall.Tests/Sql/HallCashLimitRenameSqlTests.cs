using System.Globalization;
using Hcc.Application.Hall;
using Hcc.Application.MoneyMasters;
using Hcc.Application.Payments;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;

namespace Hcc.Hall.Tests.Sql;

/// <summary>
/// CU-PAY-B: the statutory cash-receipt limit per hall event (rulebook B9 / DEC-260, Rs 2,00,000) counts every receipt that went into a till, not the
/// literal tender text: after the Owner renames Cash ("Front till") or adds a second till method, Rs 1.5 lakh then Rs 1 lakh of cash on one event is refused.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HallCashLimitRenameSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Hotel H, long EventId, int Till, PaymentMethodView Cash)> SetUpAsync(string date)
    {
        var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var till = Convert.ToInt32(await h.ScalarAsync($"INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES ({h.PropertyId}, 'HALL-L1', N'Hall limit till', {h.Owner}, SYSUTCDATETIME());", Ct),
            CultureInfo.InvariantCulture);
        await h.Get<CashierService>().OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, 100_000), Ct);
        var q = await HallBillingSqlTests.QuoteAsync(h, sagar, date, pax: 150);
        var ev = (await h.Get<HallBillingCommands>().AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q), Ct)).Value;
        var cash = (await h.Get<MoneyMastersQueries>().PaymentMethodsAsync(h.PropertyId, h.Owner, ct: Ct)).Items.Single(m => m.Code == "CASH");
        return (h, ev.EventId, till, cash);
    }

    private static Task AdvanceAsync(Hotel h, long ev, string method, long paise, int till, string? reference = null) =>
        h.Get<HallBillingCommands>().ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev, paise, method, reference, till), Ct);

    [Fact]
    public async Task Renamed_cash_row_counts_towards_the_event_limit_under_either_name()
    {
        var (h, ev, till, cash) = await SetUpAsync("2027-02-12");
        await using var _ = h;
        await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(cash.PaymentMethodId, "CASH", "Front till", cash.Kind, cash.SettlesTo, cash.NeedsReference, cash.UsableAt, Ledger: cash.Ledger, ExpectedVersion: cash.Version), Ct);

        await AdvanceAsync(h, ev, "Front till", 15_000_000, till); // Rs 1,50,000 stored as "Front till"
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Front till", 10_000_000, till))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Cash", 10_000_000, till))).Code); // earlier name
        await AdvanceAsync(h, ev, "UPI", 10_000_000, till, "UPI-LIM-1"); // a UPI receipt does not count
        Assert.Equal(15_000_000L, await h.LongAsync("SELECT SUM(amount_paise) FROM pay.cash_movement WHERE source_type = 'hall'", Ct));
    }

    [Fact]
    public async Task A_second_till_method_counts_towards_the_event_limit()
    {
        var (h, ev, till, cash) = await SetUpAsync("2027-02-13");
        await using var _ = h;
        await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, "BOX", "Cash box", "cash", cash.SettlesTo, false, ["frontdesk"]), Ct);

        await AdvanceAsync(h, ev, "Cash box", 15_000_000, till);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Cash box", 10_000_000, till))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Cash", 10_000_000, till))).Code); // the two till methods add up
        await AdvanceAsync(h, ev, "UPI", 10_000_000, till, "UPI-LIM-2");
    }
}
