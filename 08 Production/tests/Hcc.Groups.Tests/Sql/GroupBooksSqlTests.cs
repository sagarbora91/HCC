using System.Data;
using System.Text.Json.Nodes;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Accounting;
using Microsoft.Data.SqlClient;

namespace Hcc.Groups.Tests.Sql;

/// <summary>
/// DEC-074 (wave B3, A03b-02, A03a-06): a group's master folio reaches the books through Accounting's real outbox consumer — transfers,
/// a deposit and its refund, attrition on a ₹9,000 block (18 %, the contracted rate's slab), the payment and the settlement — into the day
/// book, the trial balance, the reconciliation and the (simulated) Tally XML. Payloads published before DEC-074 still map.
/// </summary>
[Trait("Category", "Sql")]
public sealed class GroupBooksSqlTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private GroupWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await GroupWorld.CreateAsync();

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private AccountingVoucherProjector Projector => new(_w.Platform.Connections, _w.Platform.Outbox, new StoredChargeGstReader(), _w.Clock);

    private AccountingQueries Books => new(new SqlAccountingStore(_w.Platform.Connections), _w.Policy);

    private RecordGroupReceiptRequest R(long groupId, string kind, string tender, long amount, long? of = null) => new(groupId, kind, tender, amount, "NEFT-DEMO-" + kind, of);

    [Fact]
    public async Task Group_receipts_attrition_and_settlement_reach_the_day_book_trial_balance_and_tally_and_old_payloads_still_map()
    {
        // GB-1 contracted at ₹9,000 a night (Classic, 4 contracted, 2 picked): 50 % attrition on the 2 unpicked rooms for 23–26 Oct.
        await _w.ExecAsync($"UPDATE inv.group_block_type SET rate_paise = 900000 WHERE group_block_id = {_w.Gb1};");
        var g = await _w.CreateGb1Async(attritionBps: 5000);

        // Member transfers: each master share ₹2,100 (₹2,000 room + ₹100 GST) leaves the guest folio as company credit (Dr 1110 · Cr 1100).
        await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1044"]));
        await _w.Commands.TransferAsync(_w.Ctx("reception"), new TransferGroupMemberRequest(g.GroupId, _w.Res["HCC-1048"]));
        // Deposit ₹5,000 by bank transfer (balance ₹4,200 − ₹5,000 = ₹800 credit), the ₹800 credit refunded from that deposit.
        var dep = (await _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), R(g.GroupId, "deposit", "Bank transfer", 500000))).Value;
        var refund = (await _w.Commands.RecordReceiptAsync(_w.Ctx("manager"), R(g.GroupId, "refund", "Bank transfer", 80000, dep.EntryId))).Value;

        // A03a-06 / DEC-074: retention ₹4,500 per room-night, slab of the ₹9,000 contracted night = 18 %. Per night 2 × ₹4,500 = ₹9,000 net,
        // GST ₹1,620 (CGST ₹810 + SGST ₹810); 3 nights: net ₹27,000, GST ₹4,860 → ₹31,860, above the Manager's ₹10,000: the Owner approves.
        var a = (await _w.Commands.RequestAttritionAsync(_w.Ctx("reception"), new RequestGroupAttritionRequest(g.GroupId, await _w.VersionAsync(g.GroupId), "Clause 7: 50 % retention"))).Value;
        Assert.Equal((2_700_000L, 486_000L), (a.NetPaise, a.TaxPaise));
        await _w.Commands.DecideAttritionAsync(_w.Ctx("owner"), new DecideGroupAttritionRequest(a.AttritionId, true, "Agreed with the organiser"));
        var lines = (await _w.StatementAsync(g.GroupId)).Master.Where(m => m.Kind == "attrition").ToList();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal((900_000L, 1800, 162_000L, 81_000L, 81_000L), (l.AmountPaise, l.TaxBps!.Value, l.TaxPaise!.Value, l.CgstPaise!.Value, l.SgstPaise!.Value)));
        Assert.Contains("\"slabBasisPaise\":900000", (string)(await _w.ScalarAsync($"SELECT detail FROM grp.attrition_request WHERE attrition_id = {a.AttritionId};"))!, StringComparison.Ordinal);

        // Payment of the balance ₹4,200 + ₹31,860 − (₹5,000 − ₹800) = ₹31,860 by UPI; members checked out; settled at 0.
        await _w.Commands.RecordReceiptAsync(_w.Ctx("cashier"), R(g.GroupId, "payment", "UPI", 3_186_000));
        await _w.ExecAsync($"UPDATE inv.reservation SET status = 'Departed' WHERE property_id = {_w.PropertyId} AND group_block_id = {_w.Gb1};");
        await _w.Commands.SettleAsync(_w.Ctx("reception"), new SettleGroupRequest(g.GroupId, await _w.VersionAsync(g.GroupId)));

        // The publishers carry the figures accounting needs (additive fields).
        Assert.Contains("\"refundOfKind\":\"deposit\"", await PayloadAsync(AccountingEvents.GroupReceiptRecorded, refund.EntryId), StringComparison.Ordinal);
        var settledJson = await PayloadAsync(AccountingEvents.GroupSettled, null);
        Assert.Contains("\"depositsHeldPaise\":420000", settledJson, StringComparison.Ordinal);
        Assert.Contains("\"businessDate\":\"2026-10-24\"", settledJson, StringComparison.Ordinal);

        // Payloads published before DEC-074 (no refundOfKind, no depositsHeld / businessDate): the consumer reads them from the master folio.
        await ApplyOldAsync(AccountingEvents.GroupReceiptRecorded, refund.EntryId, "refundOfKind");
        await ApplyOldAsync(AccountingEvents.GroupSettled, null, "depositsHeldPaise", "businessDate");
        await Projector.RunOnceAsync(ct: Ct);
        Assert.Equal(0, await Projector.RunOnceAsync(ct: Ct));

        var day = await Books.DayAsync(_w.Caller("owner"), Today, Ct);
        Assert.DoesNotContain(day.Exceptions, x => x.Kind is "pending" or "unbalanced" or "unmapped");
        VoucherView V(string key) => day.Vouchers.Single(v => v.SourceKey == key);
        static IEnumerable<(string, long, long)> E(VoucherView v) => v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise));
        var attrition = V("group-attrition:" + a.AttritionId);
        Assert.Equal(("Sales", "group-attrition"), (attrition.Type, attrition.Sub));
        // ACC-1: attrition re-pointed 4000 → 4300 (contract §2); 2000 named as the chart (Tally name in the XML below).
        Assert.Equal([("1110", 3_186_000L, 0L), ("4300", 0L, 2_700_000L), ("2110", 0L, 243_000L), ("2120", 0L, 243_000L)], E(attrition));
        Assert.Equal([("1030", 500_000L, 0L), ("2000", 0L, 500_000L)], E(V("group-receipt:" + dep.EntryId)));
        var refundVoucher = V("group-receipt:" + refund.EntryId);
        Assert.Equal(("Payment", "group-refund"), (refundVoucher.Type, refundVoucher.Sub));
        Assert.Equal([("2000", 80_000L, 0L), ("1030", 0L, 80_000L)], E(refundVoucher));   // read back: the refunded receipt was a deposit
        var settle = V("group-settle:" + g.GroupId);
        Assert.Equal(("Journal", "group-settlement"), (settle.Type, settle.Sub));
        Assert.Equal([("2000", 420_000L, 0L), ("1110", 0L, 420_000L)], E(settle));      // read back: deposits held ₹5,000 − ₹800
        Assert.Equal(2, day.Vouchers.Count(v => v.Sub == "company-credit"));
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.SourceKey));
        var checks = day.Reconciliation.ToDictionary(c => c.Id);
        foreach (var id in new[] { "group-attrition", "group-receipts", "tax", "company-credit" })
            Assert.True(checks[id].Ok, $"{id}: report {checks[id].ReportPaise} ledger {checks[id].LedgerPaise}");
        Assert.Equal((2_700_000L, 3_606_000L, 486_000L), (checks["group-attrition"].LedgerPaise, checks["group-receipts"].LedgerPaise, checks["tax"].LedgerPaise));

        // Trial balance: the group's receivable and its deposits are both cleared; revenue and GST stay.
        var tb = await Books.TrialBalanceAsync(_w.Caller("owner"), Today, true, Ct);
        Assert.True(tb.Balanced);
        long B(string acc) => tb.Rows.SingleOrDefault(r => r.Account == acc)?.BalancePaise ?? 0;
        Assert.Equal((0L, 0L), (B("1110"), B("2000")));
        Assert.Equal((-2_700_000L, -243_000L, -243_000L, 3_186_000L, 420_000L, -420_000L), (B("4300"), B("2110"), B("2120"), B("1010"), B("1030"), B("1100")));

        // Tally (simulated) carries the vouchers with their Tally voucher types.
        var tally = await Books.TallyAsync(_w.Caller("owner"), Today, Today, Ct);
        Assert.Equal((7, true), (tally.VoucherCount, tally.DebitPaise == tally.CreditPaise));
        Assert.Contains(" VCHTYPE=\"Sales\" ACTION=\"Create\"", tally.Xml, StringComparison.Ordinal);
        Assert.Matches("<VOUCHER REMOTEID=\"HCC-[0-9a-f]{32}-group-attrition:[0-9]+\" VCHTYPE=\"Sales\"", tally.Xml); // A03b-05: stable voucher id
        Assert.Contains("<NARRATION>Group attrition · 3 line(s)", tally.Xml, StringComparison.Ordinal);
        Assert.Contains("<NARRATION>Group settled · deposits applied to the company receivable", tally.Xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Advance deposits (guest and group)</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-4200.00</AMOUNT>", tally.Xml, StringComparison.Ordinal);
    }

    private async Task<string> PayloadAsync(string eventType, long? entryId)
    {
        var all = new List<string>();
        await using var conn = await _w.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new SqlCommand("SELECT payload FROM plat.outbox WHERE event_type = @e AND property_id = @p ORDER BY outbox_id;", conn);
        cmd.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = eventType;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = _w.PropertyId;
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        while (await rd.ReadAsync(Ct)) all.Add(rd.GetString(0));
        return entryId is { } id ? all.Single(p => p.Contains($"\"entryId\":{id},", StringComparison.Ordinal)) : all.Single();
    }

    /// <summary>Delivers the stored event to the consumer's handler with the named (DEC-074) fields removed — the shape published before.</summary>
    private async Task ApplyOldAsync(string eventType, long? entryId, params string[] drop)
    {
        var payload = await PayloadAsync(eventType, entryId);
        var node = JsonNode.Parse(payload)!.AsObject();
        foreach (var f in drop) Assert.True(node.Remove(f), f);
        await using var conn = await _w.Platform.Connections.OpenAsync(Ct);
        long outboxId, aggregate;
        DateTime created;
        await using (var q = new SqlCommand("SELECT outbox_id, aggregate_id, created_utc FROM plat.outbox WHERE payload = @j;", conn))
        {
            q.Parameters.Add("@j", SqlDbType.NVarChar, -1).Value = payload;
            await using var rd = await q.ExecuteReaderAsync(Ct);
            Assert.True(await rd.ReadAsync(Ct));
            (outboxId, aggregate, created) = (rd.GetInt64(0), rd.GetInt64(1), rd.GetDateTime(2));
        }

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(Ct);
        await Projector.ApplyAsync(new OutboxEvent(outboxId, eventType, aggregate, node.ToJsonString(), created), conn, tx, Ct);
        await tx.CommitAsync(Ct);
    }
}
