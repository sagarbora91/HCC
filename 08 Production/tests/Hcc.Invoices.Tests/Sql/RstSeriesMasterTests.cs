using Hcc.Application.FrontDeskMasters;
using Hcc.Application.Pos;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Pos;

namespace Hcc.Invoices.Tests.Sql;

/// <summary>
/// MA-11 review (MA-5 vs Wave B): restaurant tax invoices (RST, DEC-072) are numbered from a row of the numbering-series master
/// (bill.number_series, migration 2891) exactly like the tax-invoice series; once the Owner stores the list, outlet bills keep their invoices
/// (before: "No numbering series is in force for the RST").
/// </summary>
[Trait("Category", "Sql")]
public sealed class RstSeriesMasterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(PosService Pos, PosCaller Caller, int Outlet, int Item)> OutletAsync(Hotel h)
    {
        var pos = h.Get<PosService>();
        var caller = new PosCaller(h.PropertyId, h.Owner);
        var outlet = (await pos.CreateOutletAsync(caller, Hotel.Key(), new CreateOutletRequest("REST", "Restaurant", "table", ["T1", "T2", "T3"]), Ct)).Value.OutletId;
        var item = (await pos.SaveMenuItemAsync(caller, Hotel.Key(), new SaveMenuItemRequest(null, "TEA", "Masala chai", "Mains", 6000, true, [outlet], null), Ct)).Value.ItemId;
        return (pos, caller, outlet, item);
    }

    private static async Task<SettleResult> SellAsync(Hotel h, PosService pos, PosCaller caller, int outlet, int item, string table)
    {
        var o = (await pos.OpenCheckAsync(caller, Hotel.Key(), new OpenCheckRequest(outlet, table, null, 1, "Guest"), Ct)).Value;
        await pos.AddItemAsync(caller, Hotel.Key(), new AddCheckItemRequest(o.OrderId, item, 1, null), Ct);
        await pos.SendKotAsync(caller, Hotel.Key(), new SendKotRequest(o.OrderId, null), Ct);
        var v = (await h.Get<PosQueries>().GetCheckAsync(caller, o.OrderId, Ct)).Version;
        return (await pos.SettleCheckAsync(caller, Hotel.Key(), new SettleCheckRequest(o.OrderId, "outlet", "UPI", "UPI-" + o.OrderId, null, null, v), Ct)).Value;
    }

    private static Task SaveSeriesAsync(Hotel h, string code, string doc, string prefix, int padding, string reset, string from) =>
        h.Get<FrontDeskMastersCommands>().SaveNumberSeriesAsync(h.Ctx(h.Owner, "SaveNumberSeries"),
            new SaveNumberSeriesRequest(null, code, code + " series", doc, prefix, 1, padding, reset, from), Ct);

    [Fact]
    public async Task A_stored_numbering_list_holds_the_RST_row_and_outlet_bills_keep_their_RST_numbers()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        await SaveSeriesAsync(h, "KOT-2", "KOT", "K", 4, "NEVER", "2026-10-24");   // stores the defaults first (incl. RST)
        Assert.Equal(1, await h.LongAsync($"SELECT COUNT(*) FROM bill.number_series WHERE property_id = {h.PropertyId} AND document = 'RST'", Ct));
        var (pos, caller, outlet, item) = await OutletAsync(h);
        var a = await SellAsync(h, pos, caller, outlet, item, "T1");
        Assert.Equal(("RST/2627/0000001", (string?)null), (a.InvoiceNo, a.InvoiceNote));
    }

    [Fact]
    public async Task An_owner_RST_series_from_today_numbers_the_next_outlet_bill_and_a_list_without_RST_falls_back_to_the_built_in_series()
    {
        await using var w = await InvoiceWorld.CreateAsync(ct: Ct);
        var h = w.H;
        await SaveSeriesAsync(h, "RST-OUT", "RST", "RS", 5, "FY", "2026-10-24");
        var (pos, caller, outlet, item) = await OutletAsync(h);
        Assert.Equal("RS/2627/00001", (await SellAsync(h, pos, caller, outlet, item, "T1")).InvoiceNo);

        // A list stored before migration 2891 (no RST row at all): the invoices module keeps its built-in RST numbering.
        await using var w2 = await InvoiceWorld.CreateAsync(ct: Ct);
        await SaveSeriesAsync(w2.H, "KOT-2", "KOT", "K", 4, "NEVER", "2026-10-24");
        await w2.H.ScalarAsync($"DELETE bill.number_series WHERE property_id = {w2.H.PropertyId} AND document = 'RST'", Ct);
        var (pos2, caller2, outlet2, item2) = await OutletAsync(w2.H);
        Assert.Equal("RST/2627/0000001", (await SellAsync(w2.H, pos2, caller2, outlet2, item2, "T2")).InvoiceNo);
    }
}
