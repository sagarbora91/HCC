using Hcc.Application.Invoices;
using Hcc.Application.Stays;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF B1 / B2 / B7 through the real composition on a disposable HccTest_* hotel: Owner-only agreed rate and credit account (key = phone
/// last 10), money-role read, and the GSTR-1 'LGC' Table 13 row from lgc.bill with the 4685 cancel flag.</summary>
public sealed class LfGuestMoneySqlTests
{
    [Fact]
    public async Task Agreed_rate_and_credit_account_are_owner_only_and_read_by_money_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Hotel.CreateAsync(ct);
        var (_, stayId, _) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-26", 200000, ct);
        var guest = await h.LongAsync($"SELECT guest_id FROM gst.stay WHERE stay_id = {stayId}", ct);
        await h.ScalarAsync($"UPDATE gst.guest SET phone = '+91 98200 01041' WHERE guest_id = {guest}", ct);
        var type = (int)await h.LongAsync("SELECT TOP 1 room_type_id FROM inv.room_type ORDER BY room_type_id", ct);
        var c = h.Get<LfMoneyGuestCommands>();

        var denied = await Assert.ThrowsAsync<HccException>(() => c.SetAgreedRateAsync(h.Ctx(h.Manager, LfMoneyGuestCommands.OpSetAgreedRate), new(guest, type, 170000, null, null, null), ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, denied.Code);
        var set = await c.SetAgreedRateAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpSetAgreedRate), new(guest, type, 170000, null, new DateOnly(2026, 12, 31), "Regular"), ct);
        Assert.Equal(170000, Assert.Single(set.Value.AgreedRates).RatePaise);

        var acc = await c.SetCreditAccountAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpSetCreditAccount), new(guest, 1_000_000, true, null), ct);
        Assert.Equal("9820001041", acc.Value.CreditAccount!.CustomerKey);
        Assert.Equal("1120/UD-9820001041", acc.Value.CreditAccount.PartyLedger);
        await Assert.ThrowsAsync<HccException>(() => c.SetCreditAccountAsync(h.Ctx(h.Reception, LfMoneyGuestCommands.OpSetCreditAccount), new(guest, 1, true, null), ct));

        var read = await c.ReadAsync(h.PropertyId, h.Reception, guest, ct);
        Assert.Single(read.AgreedRates);
        await c.ClearAgreedRateAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpClearAgreedRate), new(guest, type), ct);
        Assert.Empty((await c.ReadAsync(h.PropertyId, h.Owner, guest, ct)).AgreedRates);
    }

    [Fact]
    public async Task Gstr1_has_the_legacy_row_with_the_cancel_flag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await Hotel.CreateAsync(ct);
        foreach (var (no, cancelled) in new[] { (1204, 0), (1205, 1) })
            await h.ScalarAsync($"""
                INSERT lgc.bill(property_id, legacy_fy, legacy_bill_no, legacy_transaction_id, bill_date, bill_utc, room_no, room_charge_paise, extra_paise, fb_paise, laundry_paise,
                  other_paise, discount_paise, room_gst_bps, room_gst_paise, fb_gst_paise, laundry_gst_paise, other_gst_paise, advance_paise, net_paise, balance_paise, refund_paise,
                  data_flag, paid_flag, b2b, out_of_fy, cancelled)
                VALUES ({h.PropertyId}, '2026-27', {no}, {no}, '2026-05-04', '2026-05-04', '105', 200000, 0, 0, 0, 0, 0, 500, 10000, 0, 0, 0, 0, 210000, 0, 0, 'C', 'Y', 0, 0, {cancelled});
                """, ct);
        var g = await h.Get<InvoiceQueries>().Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), ct);
        var lgc = Assert.Single(g.Documents, d => d.DocType == "LGC");
        Assert.Equal(("1204", "1205", 2, 1), (lgc.First, lgc.Last, lgc.Count, lgc.Cancelled));
        Assert.Empty(g.Sac);
        var oct = await h.Get<InvoiceQueries>().Gstr1Async(h.PropertyId, h.Owner, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ct);
        Assert.DoesNotContain(oct.Documents, d => d.DocType == "LGC");
    }
}
