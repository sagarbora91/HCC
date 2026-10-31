using System.Globalization;
using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Application.Ota;
using Hcc.Contracts.Accounting;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Ota.Tests.Sql;

/// <summary>
/// ACC-2 lane acc2-flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4; rulebook A13, DEC-150): an OTA payout from the statement settles the channel's
/// open receivable (OTA credits on the folios of its reservations, migration 4250) — figures add up, gross ≤ the open receivable, an active bank account,
/// a statement once per channel, PERM-OTA-004 holders only — and publishes OtaPayoutRecorded with the channel code and the bank (the 1115 / 1030 parties).
/// Mirrors the prototype's [ACC2-FLOWS] CHN_PAYOUT tests.
/// </summary>
[Trait("Category", "Sql")]
public sealed class OtaPayoutSqlTests : OtaSqlTest
{
    private OtaPayoutService Payouts => new(F.Db.Platform.Commands, new SqlFlowsStore(F.Db.Platform.Connections), F.Policy, F.Clock);

    /// <summary>F1 (BookNest, Kiran Joshi) accepted, its folio opened and the stay's balance moved to BookNest (an ota-credit receipt of ₹7,080).</summary>
    private async Task<long> CreditedBookNestAsync()
    {
        var accepted = await F.AcceptAsync("F1");
        var reservation = accepted.ReservationId!.Value;
        await ExecAsync($"""
            INSERT bill.folio(reservation_id) VALUES ({reservation});
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date, reference)
              SELECT folio_id, 'ota-credit', 'OTA credit', 708000, '2026-10-24', N'BookNest · paid by the OTA' FROM bill.folio WHERE reservation_id = {reservation};
            IF NOT EXISTS (SELECT 1 FROM mst.bank_account WHERE property_id = 1 AND code = 'BANK-1')
              INSERT mst.bank_account(property_id, code, name, sort_order, kind, bank, updated_by, updated_utc) VALUES (1, 'BANK-1', N'Current account (fictional)', 1, 'bank', N'Demo Bank', 1, '2026-01-01');
            IF NOT EXISTS (SELECT 1 FROM mst.bank_account WHERE property_id = 1 AND code = 'BANK-OLD')
              INSERT mst.bank_account(property_id, code, name, sort_order, active, retired_on, kind, bank, updated_by, updated_utc) VALUES (1, 'BANK-OLD', N'Closed account (fictional)', 2, 0, '2026-10-01', 'bank', N'Demo Bank', 1, '2026-01-01');
            """);
        return reservation;
    }

    private RecordOtaPayoutRequest Statement(long gross, long reservation, string reference = "BN-STMT-OCT-01", string bank = "BANK-1", long? net = null)
        => new(F.ChannelIds["BOOKNEST"], reference, gross, 10_000, 1_800, 500, net ?? gross - 12_300, bank, [reservation]);

    [Fact]
    public async Task Payout_settles_the_open_receivable_and_publishes_the_parties()
    {
        var reservation = await CreditedBookNestAsync();
        var r = (await Payouts.RecordAsync(F.Ctx(F.Accountant, OtaPayoutService.OpRecord), Statement(708_000, reservation), Ct)).Value;
        Assert.Equal((708_000L, 708_000L, 0L), (r.GrossPaise, r.OpenBeforePaise, r.OpenAfterPaise));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM ota.payout_booking WHERE payout_id = {r.PayoutId} AND reservation_id = {reservation};"));
        var json = (string)(await F.Db.ScalarAsync($"SELECT TOP (1) payload FROM plat.outbox WHERE event_type = '{AccountingEvents.OtaPayoutRecorded}' ORDER BY outbox_id DESC;", Ct))!;
        var e = JsonSerializer.Deserialize<OtaPayoutRecordedEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(("BOOKNEST", "BookNest", "BANK-1", 695_700L, false), (e.ChannelCode, e.Channel, e.BankAccount, e.NetPaise, e.ItcAllowed));
        var v = Assert.Single(Hcc.Domain.Accounting.VoucherBuilder.Build(e.BusinessDate, [AccountingEventMapper.OtaPayout(e)], Hcc.Domain.Accounting.ChartOfAccounts.Effective(null)));
        Assert.Equal([("1030/BANK-1", 695_700L, 0L), ("5070", 11_800L, 0L), ("1340", 500L, 0L), ("1115/BOOKNEST", 0L, 708_000L)],
            v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToList());
        var list = await Payouts.ListAsync(1, F.Manager, Ct);
        Assert.Equal([reservation], Assert.Single(list).ReservationIds);
    }

    [Fact]
    public async Task Payout_guards_figures_receivable_bank_statement_and_roles()
    {
        var reservation = await CreditedBookNestAsync();
        Assert.Equal("FORBIDDEN", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Reception, OtaPayoutService.OpRecord), Statement(708_000, reservation), Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Cashier, OtaPayoutService.OpRecord), Statement(708_000, reservation), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord), Statement(708_000, reservation, net: 708_000), Ct)));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord), Statement(709_000, reservation), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord), Statement(708_000, reservation, bank: "NOPE"), Ct)));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord), Statement(708_000, reservation, bank: "BANK-OLD"), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord),
            Statement(708_000, reservation) with { ReservationIds = [reservation + 999] }, Ct)));
        await Payouts.RecordAsync(F.Ctx(F.Manager, OtaPayoutService.OpRecord), Statement(400_000, reservation), Ct);
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => Payouts.RecordAsync(F.Ctx(F.Manager, OtaPayoutService.OpRecord), Statement(300_000, reservation), Ct))); // same statement
        await Payouts.RecordAsync(F.Ctx(F.Manager, OtaPayoutService.OpRecord), Statement(308_000, reservation, "BN-STMT-OCT-02"), Ct);
        Assert.Equal(0L, Convert.ToInt64(await F.Db.ScalarAsync($"""
            SELECT ISNULL((SELECT SUM(amount_paise) FROM pay.receipt WHERE kind = 'ota-credit'), 0) - ISNULL((SELECT SUM(gross_paise) FROM ota.payout), 0);
            """, Ct), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Channel_itc_is_the_owners_setting_and_travels_with_the_payout()
    {
        var reservation = await CreditedBookNestAsync();
        var ch = F.ChannelIds["BOOKNEST"];
        Assert.Equal("FORBIDDEN", await CodeAsync(() => Payouts.SetItcAsync(F.Ctx(F.Manager, OtaPayoutService.OpSetItc), new SetChannelItcRequest(ch, true), Ct)));
        await Payouts.SetItcAsync(F.Ctx(F.Owner, OtaPayoutService.OpSetItc), new SetChannelItcRequest(ch, true), Ct);
        await Payouts.RecordAsync(F.Ctx(F.Owner, OtaPayoutService.OpRecord), Statement(708_000, reservation), Ct);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ota.payout WHERE itc_allowed = 1;"));
    }
}
