using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Application.MoneyMasters;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.MoneyMasters;
using Hcc.Groups.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Groups;
using Hcc.Infrastructure.SqlServer.Tax;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// CU-PAY-B (DEC-MA-11a): a master-folio refund may go back by a RETIRED method only when it is the same master row as the refunded receipt's
/// tender; any other retired method is refused with MASTER_RETIRED and nothing is written.
/// </summary>
[Trait("Category", "Sql")]
public sealed class GroupRefundRetiredMethodTests : IAsyncLifetime
{
    private GroupWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private sealed class Switchable : IPaymentMethodSource
    {
        public bool CardActive = true;

        public Task<IReadOnlyList<TenderMethod>> MethodsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenderMethod>>(
            [
                new("UPI", "UPI", "upi", false, true, true, true, true, true, "1010"),
                new("CARD", "Card", "card", false, true, true, true, false, CardActive, "1020"),
                new("BANK", "Bank transfer", "bank", false, true, true, false, true, true, "1030"),
            ]);
    }

    [Fact]
    public async Task Refund_by_a_retired_method_only_when_it_is_the_refunded_receipts_row()
    {
        var methods = new Switchable();
        var gst = new GstRateResolver(new SqlGstMasterStore(_w.Platform.Connections));
        var credit = new CompanyCredit(_w.Platform.Commands, _w.CompanyStore, new SqlCompanyCreditStore(_w.Platform.Connections), _w.Store, _w.Policy, _w.Clock, new SqlBusinessDayGuard(), methods);
        var groups = new GroupCommands(_w.Platform.Commands, _w.Store, _w.Policy, new BillingGroupFolioPort(_w.BillingStore), gst, _w.Clock, new SqlBusinessDayGuard(), credit, methods);
        await _w.ExecAsync($"UPDATE inv.group_block_type SET rate_paise = 900000 WHERE group_block_id = {_w.Gb1};");
        var g = await _w.CreateGb1Async(attritionBps: 5000);
        await groups.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"]));
        await groups.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1048"]));

        var byCard = (await groups.RecordReceiptAsync(_w.Ctx("cashier"), new RecordGroupReceiptRequest(g.GroupId, "deposit", "Card", 500_000, "CARD-DEMO-1"))).Value;
        var byBank = (await groups.RecordReceiptAsync(_w.Ctx("cashier"), new RecordGroupReceiptRequest(g.GroupId, "deposit", "Bank transfer", 10_000, "NEFT-DEMO-1"))).Value;
        methods.CardActive = false; // the Owner retires Card
        var before = await _w.LongAsync("SELECT COUNT(*) FROM grp.master_entry;");

        // Retired Card against the BANK deposit: another row than the refunded receipt's -> refused.
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => groups.RecordReceiptAsync(_w.Ctx("manager"),
            new RecordGroupReceiptRequest(g.GroupId, "refund", "Card", 20_000, "CARD-R-1", byBank.EntryId)));
        Assert.Equal("MASTER_RETIRED", ex.Details!["reason"].GetString());
        Assert.Equal(before, await _w.LongAsync("SELECT COUNT(*) FROM grp.master_entry;"));

        // Retired Card against the CARD deposit: the original receipt's row -> accepted.
        var ok = (await groups.RecordReceiptAsync(_w.Ctx("manager"), new RecordGroupReceiptRequest(g.GroupId, "refund", "Card", 20_000, "CARD-R-2", byCard.EntryId))).Value;
        Assert.Equal("Card", ok.Tender);
        Assert.Equal(before + 1, await _w.LongAsync("SELECT COUNT(*) FROM grp.master_entry;"));
    }
}
