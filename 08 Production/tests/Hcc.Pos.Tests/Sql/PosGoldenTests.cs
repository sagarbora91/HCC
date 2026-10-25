using System.Data;
using Hcc.Contracts.Pos;
using Hcc.Pos.Tests.Harness;

namespace Hcc.Pos.Tests.Sql;

/// <summary>The prototype BASE POS fixture (POS-F01..F04) rebuilt through the commands in a database of its own, so the day figures are global.</summary>
public class PosGoldenTests(PosDb db) : IClassFixture<PosDb>
{
    [Fact]
    public async Task Prototype_fixture_figures_reproduce_and_cash_enters_the_shared_drawer_once()
    {
        var (till, shift) = await db.OpenTillAsync(1_000_000);
        // POS-F01: breakfast at T1, cash.
        var f1 = await db.OpenTableAsync("T1");
        await db.AddAsync(f1.OrderId, "POHA", 2);
        await db.AddAsync(f1.OrderId, "TEA", 2);
        await db.AddAsync(f1.OrderId, "COFFEE", 1);
        await db.SendAsync(f1.OrderId);
        var s1 = (await db.SettleOutletAsync(f1.OrderId, "Cash", till)).Value;
        Assert.Equal((50_000L, 2_500L, 52_500L, 500), (s1.NetPaise, s1.TaxPaise, s1.TotalPaise, s1.TaxBps));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pos.bill WHERE bill_id = @b AND cgst_paise = 1250 AND sgst_paise = 1250", ("@b", SqlDbType.BigInt, s1.BillId))); // GST master split
        // POS-F02: T4, UPI.
        var f2 = await db.OpenTableAsync("T4", covers: 3);
        await db.AddAsync(f2.OrderId, "DOSA", 2);
        await db.AddAsync(f2.OrderId, "IDLI");
        await db.AddAsync(f2.OrderId, "COFFEE");
        await db.SendAsync(f2.OrderId);
        var s2 = (await db.SettleOutletAsync(f2.OrderId, "UPI", reference: "DEMO-UPI-POS-7731")).Value;
        Assert.Equal(71_400L, s2.TotalPaise);
        // POS-F03 / F04: open checks that block the day close.
        var f3 = await db.OpenTableAsync("T2", covers: 4);
        await db.AddAsync(f3.OrderId, "THALI", 2);
        await db.AddAsync(f3.OrderId, "PANEER");
        await db.SendAsync(f3.OrderId);
        var (_, _, _) = await db.NewInHouseAsync("201");
        var f4 = (await db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "201", 1, null))).Value;
        await db.AddAsync(f4.OrderId, "PARATHA");

        var sum = await db.Queries.DaySummaryAsync(db.As(Who.Owner), null);
        Assert.Equal((2, 118_000L, 5_900L, 123_900L), (sum.Checks, sum.NetPaise, sum.TaxPaise, sum.GrossPaise));
        Assert.Equal((52_500L, 71_400L, 52_500L), (sum.CashPaise, sum.UpiPaise, sum.OutletCashPaise));
        Assert.Equal(2, sum.OpenCount);
        Assert.Equal(5, sum.Covers);
        // Shared drawer (DEC-052): only the cash bill, exactly once.
        Assert.Equal(52_500L, await db.DrawerSumAsync(shift.ShiftId, "pos-sale"));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'pos-bill'"));
        var blockers = await db.Queries.BlockersAsync(db.As(Who.Owner));
        Assert.Equal((2, 0, true), (blockers.OpenChecks, blockers.PendingVoids, blockers.Blocked));
    }
}
