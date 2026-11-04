using Hcc.Application.Counter;
using Hcc.Application.Stays;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Counter.Tests.Sql;

/// <summary>LF-finish list reads on a disposable HccTest_* hotel: counter bills (PERM-CTR-001 roles), credit guests with the open Udhaar balance (PERM-FOL-010
/// roles) and applied rate changes (PERM-STY-010 roles); a role without the permission (the Accountant) is refused with FORBIDDEN, no money leaves.</summary>
public sealed class LfMoneyReadsTests
{
    private const string HotelGstin = "27AAAAA0000A1Z2"; // fictional, valid checksum
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counter_bills_credit_guests_and_rate_changes_are_listed_to_money_roles_only()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var reads = h.Get<LfMoneyReadService>();

        var bill = await h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new("Walk-in R (fictional)"),
            [new("Laundry · shirts", 3, 5000, "laundry"), new("Tea", 2, 2500, "food")], new("paid", "UPI", "UPI-REF-R")), Ct);
        var bills = await reads.CounterBillsAsync(h.PropertyId, h.Reception, ct: Ct);
        var row = Assert.Single(bills);
        Assert.Equal((bill.Value.BillId, bill.Value.InvoiceNo, "Issued", bill.Value.TotalPaise, 2), (row.BillId, row.InvoiceNo, row.Status, row.TotalPaise, row.Lines.Count));
        Assert.Equal(bill.Value.TotalPaise, (await reads.CounterBillsAsync(h.PropertyId, h.Owner, ct: Ct)).Single().TotalPaise);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => reads.CounterBillsAsync(h.PropertyId, h.Accountant, ct: Ct))).Code);

        var (_, stay, _) = await h.SeedInHouseAsync("101", "2026-10-24", "2026-10-27", 200000, Ct);
        var guest = await h.LongAsync($"SELECT guest_id FROM gst.stay WHERE stay_id = {stay}", Ct);
        await h.ScalarAsync($"UPDATE gst.guest SET phone = '+91 98200 01051' WHERE guest_id = {guest}", Ct);
        Assert.Empty(await reads.CreditGuestsAsync(h.PropertyId, h.Owner, Ct));
        await h.Get<LfMoneyGuestCommands>().SetCreditAccountAsync(h.Ctx(h.Owner, LfMoneyGuestCommands.OpSetCreditAccount), new(guest, 1_000_000, true, "Regular"), Ct);
        var cg = Assert.Single(await reads.CreditGuestsAsync(h.PropertyId, h.Reception, ct: Ct));
        Assert.Equal((guest, 1_000_000L, true, "9820001051", 0L), (cg.GuestId, cg.LimitPaise, cg.Active, cg.CustomerKey, cg.OpenPaise));

        Assert.Empty(await reads.RateChangesAsync(h.PropertyId, h.Owner, Ct));
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new Hcc.Contracts.Billing.PostRoomNightRequest(stay, Hotel.Day1), Ct);
        await h.Get<StayRateCommands>().ChangeAsync(h.Ctx(h.Owner, StayRateCommands.OpChangeStayRate), new(stay, 180000, null, null, "today-next", "Regular guest"), Ct);
        var rc = Assert.Single(await reads.RateChangesAsync(h.PropertyId, h.Manager, Ct));
        Assert.Equal((stay, "101", 200000L, 180000L, "today-next"), (rc.StayId, rc.RoomNo, rc.OldRatePaise, rc.NewRatePaise, rc.Scope));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => reads.RateChangesAsync(h.PropertyId, h.Accountant, Ct))).Code);
    }

    // AF-CTR-READS (CON-01 / HIS-01)
    private static async Task<List<(long Id, string No)>> IssueManyAsync(Hotel h, int count)
    {
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '{HotelGstin}', address = N'12 Station Road, Latur (fictional)' WHERE property_id = {h.PropertyId};", Ct);
        var list = new List<(long, string)>();
        for (var i = 1; i <= count; i++)
        {
            var b = await h.Get<CounterBillService>().IssueAsync(h.Ctx(h.Reception, CounterBillService.OpIssue), new IssueCounterBillRequest(new($"Walk-in {i} (fictional)"),
                [new("Laundry · shirts", i, 5000, "laundry"), new("Tea", 2, 2500, "food")], new("paid", "UPI", $"UPI-REF-{i}")), Ct);
            list.Add((b.Value.BillId, b.Value.InvoiceNo));
        }

        return list;
    }

    [Fact]
    public async Task Counter_list_every_returned_header_has_its_lines_when_take_is_smaller_than_the_bill_count()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var issued = await IssueManyAsync(h, 5);
        var rows = await h.Get<LfMoneyReadService>().CounterBillsAsync(h.PropertyId, h.Reception, take: 3, ct: Ct);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, r.Lines.Count));
        Assert.Equal(issued.Skip(2).Reverse().Select(x => x.Id), rows.Select(r => r.BillId));
    }

    [Fact]
    public async Task Counter_lookup_by_invoice_no_finds_a_bill_outside_the_newest_take()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var issued = await IssueManyAsync(h, 4);
        var reads = h.Get<LfMoneyReadService>();
        Assert.DoesNotContain(issued[0].Id, (await reads.CounterBillsAsync(h.PropertyId, h.Reception, take: 2, ct: Ct)).Select(r => r.BillId));
        var found = Assert.Single(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: issued[0].No, take: 2, ct: Ct));
        Assert.Equal(issued[0].Id, found.BillId);
        Assert.Equal(2, found.Lines.Count);
    }

    [Fact]
    public async Task Counter_lookup_by_date_range()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var issued = await IssueManyAsync(h, 3);
        await h.ScalarAsync($"UPDATE ctr.bill SET business_date = '2026-01-10' WHERE bill_id = {issued[0].Id}; UPDATE ctr.bill SET business_date = '2026-01-20' WHERE bill_id = {issued[1].Id}; UPDATE ctr.bill SET business_date = '2026-02-01' WHERE bill_id = {issued[2].Id};", Ct);
        var reads = h.Get<LfMoneyReadService>();
        var mid = await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: new DateOnly(2026, 1, 15), to: new DateOnly(2026, 1, 31), ct: Ct);
        Assert.Equal(issued[1].Id, Assert.Single(mid).BillId);
        var all = await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: new DateOnly(2026, 1, 10), to: new DateOnly(2026, 2, 1), ct: Ct);
        Assert.Equal(3, all.Count);
        var open = await reads.CounterBillsAsync(h.PropertyId, h.Reception, from: new DateOnly(2026, 1, 20), ct: Ct);
        Assert.Equal(2, open.Count);
    }

    [Fact]
    public async Task Counter_lookup_from_after_to_is_VALIDATION()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var ex = await Assert.ThrowsAsync<HccException>(() => h.Get<LfMoneyReadService>().CounterBillsAsync(h.PropertyId, h.Reception, from: new DateOnly(2026, 2, 1), to: new DateOnly(2026, 1, 1), ct: Ct));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
    }

    [Fact]
    public async Task Counter_lookup_invoice_no_is_trimmed_and_exact()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var issued = await IssueManyAsync(h, 2);
        var reads = h.Get<LfMoneyReadService>();
        Assert.Equal(issued[0].Id, Assert.Single(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: issued[0].No + " ", ct: Ct)).BillId);
        Assert.Empty(await reads.CounterBillsAsync(h.PropertyId, h.Reception, invoiceNo: issued[0].No[..^1], ct: Ct));
    }
}
