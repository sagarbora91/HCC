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
/// CU-PAY-B (migration 4935): hall advances, balances and refunds take the tender from the Payment methods master — an Owner-added method is accepted
/// and stored under its name, an earlier name resolves to the row's current name, "settles to a till" (not the literal Cash) decides the drawer, a retired
/// method is refused except as the refund of that same row's receipt, and a 60-character Devanagari name round-trips.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HallPaymentMethodSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PaymentMethodView> AddAsync(Hotel h, string code, string name, bool reference, string place = "frontdesk") =>
        (await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, code, name, "wallet", "BANK-1", reference, [place]), Ct)).Value;

    private static Task AdvanceAsync(Hotel h, long ev, string method, string? reference, int? till = null) =>
        h.Get<HallBillingCommands>().ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev, 10_000, method, reference, till), Ct);

    private static async Task<(long EventId, int Till)> ConfirmedEventAsync(Hotel h, string date)
    {
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var till = Convert.ToInt32(await h.ScalarAsync($"INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES ({h.PropertyId}, 'HALL-T9', N'Hall pm till', {h.Owner}, SYSUTCDATETIME());", Ct),
            CultureInfo.InvariantCulture);
        await h.Get<CashierService>().OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, 100_000), Ct);
        var q = await HallBillingSqlTests.QuoteAsync(h, sagar, date);
        var acc = (await h.Get<HallBillingCommands>().AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q), Ct)).Value;
        return (acc.EventId, till);
    }

    private static Task<List<string>> TendersAsync(Hotel h, long ev) =>
        h.RowsAsync($"SELECT tender FROM hall.event_receipt WHERE event_id = {ev} ORDER BY receipt_id", r => r.GetString(0), Ct);

    [Fact]
    public async Task Owner_added_method_is_accepted_moves_no_drawer_and_the_reference_rule_follows_the_row()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (ev, till) = await ConfirmedEventAsync(h, "2027-02-10");
        await AddAsync(h, "WALLET", "Paytm wallet", true);
        await AddAsync(h, "VOUCHER", "Room voucher", false);
        await AddAsync(h, "ZOMATO", "Zomato", false, "pos");
        var moves = await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'hall'", Ct);

        await AdvanceAsync(h, ev, "Paytm wallet", "PTM-1234");
        await AdvanceAsync(h, ev, "Room voucher", null);
        Assert.Equal(["Paytm wallet", "Room voucher"], await TendersAsync(h, ev));
        Assert.Equal(moves, await h.LongAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'hall'", Ct));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Paytm wallet", "x"))).Code);
        Assert.StartsWith("Select a payment method (", (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Bitcoin", "B-1"))).Message, StringComparison.Ordinal);
        Assert.StartsWith("Select a payment method (", (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Zomato", null))).Message, StringComparison.Ordinal); // POS-only
        _ = till;
    }

    [Fact]
    public async Task Renamed_cash_row_still_moves_the_drawer_under_its_current_name_and_an_earlier_name_resolves()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (ev, till) = await ConfirmedEventAsync(h, "2027-02-10");
        var cash = (await h.Get<MoneyMastersQueries>().PaymentMethodsAsync(h.PropertyId, h.Owner, ct: Ct)).Items.Single(m => m.Code == "CASH");
        await h.Get<MoneyMastersCommands>().SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(cash.PaymentMethodId, "CASH", "Front till", cash.Kind, cash.SettlesTo, cash.NeedsReference, cash.UsableAt, Ledger: cash.Ledger, ExpectedVersion: cash.Version), Ct);

        await AdvanceAsync(h, ev, "Cash", null, till);       // earlier name
        await AdvanceAsync(h, ev, "Front till", null, till); // current name
        Assert.Equal(["Front till", "Front till"], await TendersAsync(h, ev));
        Assert.Equal(20_000L, await h.LongAsync("SELECT SUM(amount_paise) FROM pay.cash_movement WHERE source_type = 'hall'", Ct));
    }

    [Fact]
    public async Task Refund_by_a_retired_method_only_when_it_is_the_row_of_one_of_the_events_receipts()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (ev, _) = await ConfirmedEventAsync(h, "2027-02-10");
        var wallet = await AddAsync(h, "WALLET", "Paytm wallet", true);
        var voucher = await AddAsync(h, "VOUCHER", "Room voucher", false);
        await AdvanceAsync(h, ev, "Paytm wallet", "PTM-1");
        var c = h.Get<HallBillingCommands>();
        await c.CancelAsync(h.Ctx(h.Reception, HallBillingCommands.OpCancel), new CancelHallEventRequest(ev, "Date changed"), Ct);
        var cmds = h.Get<MoneyMastersCommands>();
        await cmds.RetirePaymentMethodAsync(h.Ctx(h.Owner, "RetirePaymentMethod"), new PaymentMethodIdRequest(wallet.PaymentMethodId, wallet.Version), Ct);
        await cmds.RetirePaymentMethodAsync(h.Ctx(h.Owner, "RetirePaymentMethod"), new PaymentMethodIdRequest(voucher.PaymentMethodId, voucher.Version), Ct);

        var before = await h.LongAsync($"SELECT COUNT(*) FROM hall.event_receipt WHERE event_id = {ev}", Ct);
        var other = await Assert.ThrowsAsync<HccException>(() => c.RefundAsync(h.Ctx(h.Manager, HallBillingCommands.OpRefund), new HallRefundRequest(ev, "Room voucher"), Ct));
        Assert.Equal("MASTER_RETIRED", other.Details!["reason"].GetString());
        Assert.Equal(before, await h.LongAsync($"SELECT COUNT(*) FROM hall.event_receipt WHERE event_id = {ev}", Ct));
        // Advance by a retired method is refused too.
        Assert.Equal("MASTER_RETIRED", (await Assert.ThrowsAsync<HccException>(() => AdvanceAsync(h, ev, "Paytm wallet", "PTM-2"))).Details!["reason"].GetString());

        await c.RefundAsync(h.Ctx(h.Manager, HallBillingCommands.OpRefund), new HallRefundRequest(ev, "Paytm wallet", "PTM-R"), Ct); // the original receipt's (retired) row
        Assert.Equal(["Paytm wallet", "Paytm wallet"], await TendersAsync(h, ev));
    }

    [Fact]
    public async Task A_sixty_character_devanagari_method_name_is_stored_and_read_back_intact()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (ev, _) = await ConfirmedEventAsync(h, "2027-02-10");
        var name = string.Concat(Enumerable.Repeat("भुगतान", 10)); // 60 UTF-16 characters
        Assert.Equal(60, name.Length);
        await AddAsync(h, "DEVA", name, false);
        await AdvanceAsync(h, ev, name, null);
        Assert.Equal([name], await TendersAsync(h, ev));
        var shown = (await h.Get<HallQueries>().EventsAsync(h.PropertyId, h.Owner, null, ev, Ct)).Single();
        Assert.Contains(shown.Receipts, r => r.Tender == name);
    }

    [Fact]
    public async Task Migration_4935_runs_twice_keeps_both_constraints_and_the_old_rows()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (ev, _) = await ConfirmedEventAsync(h, "2027-02-10");
        await AdvanceAsync(h, ev, "UPI", "UPI-OLD");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", "migrations", "4935_hall_receipt_tender_master.sql"))) dir = dir.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "database", "migrations", "4935_hall_receipt_tender_master.sql"), Ct);
        await h.ScalarAsync(sql, Ct);
        await h.ScalarAsync(sql, Ct);
        Assert.Equal(2L, await h.LongAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('hall.event_receipt') AND name IN ('CK_hall_event_receipt_tender','CK_hall_event_receipt_side')", Ct));
        Assert.Equal(120L, await h.LongAsync("SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('hall.event_receipt') AND name = 'tender'", Ct));
        Assert.Equal(["UPI"], await TendersAsync(h, ev));
    }
}
