using Hcc.Contracts.Pos;
using Hcc.Pos.Tests.Harness;

namespace Hcc.Pos.Tests.Sql;

/// <summary>W3-a: the void limit is the Master-configuration setting <c>pos.voidLimit</c> (dated, money, Owner-only), not a constant.</summary>
public class PosVoidLimitSettingTests(PosDb db) : IClassFixture<PosDb>
{
    private async Task<string> VoidDalAsync(string table)
    {
        var id = await db.SentTableAsync(table, ("THALI", 4));                                // Rs 1,680 line
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines[0].LineId;
        return (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, line, "Kitchen error", null))).Value.Outcome;
    }

    [Fact]
    public async Task Changing_the_void_limit_changes_behaviour_from_its_effective_date()
    {
        await db.SetBusinessDateAsync(new DateOnly(2026, 10, 24));
        Assert.Equal("PendingApproval", await VoidDalAsync("T21"));                          // default Rs 500
        await db.SetSettingAsync("pos.voidLimit", "200000", "2026-10-25");                    // Rs 2,000 from tomorrow
        Assert.Equal("PendingApproval", await VoidDalAsync("T22"));                          // still Rs 500 today
        await db.SetBusinessDateAsync(new DateOnly(2026, 10, 25));
        Assert.Equal("Voided", await VoidDalAsync("T23"));                                   // Rs 1,680 is now within the limit
        await db.SetSettingAsync("pos.voidLimit", "10000", "2026-10-26");                     // Rs 100 from the 26th
        await db.SetBusinessDateAsync(new DateOnly(2026, 10, 26));
        Assert.Equal("PendingApproval", await VoidDalAsync("T24"));
    }
}

public class PosSettingDefinitionTests
{
    [Fact]
    public void Void_limit_is_a_dated_money_setting_so_only_the_Owner_changes_it_DEC_058()
    {
        var d = Assert.Single(Hcc.Application.Pos.PosSettings.Provider.Definitions);
        Assert.Equal("pos.voidLimit", d.Key);
        Assert.True(d.Money);
        Assert.True(d.Dated);
        Assert.Equal(Hcc.Domain.Pos.PosRules.VoidLimitPaise, d.Default.Number);
        Assert.Equal("pos", Hcc.Application.Pos.PosSettings.Provider.Group);
    }
}
