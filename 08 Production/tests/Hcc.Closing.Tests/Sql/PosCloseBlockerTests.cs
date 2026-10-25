using Hcc.Application.Pos;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Pos;

namespace Hcc.Closing.Tests.Sql;

/// <summary>W3-a: POS open checks and pending void requests block RunBusinessDayClose (IPosCloseGate), with a clear blocker list.</summary>
[Trait("Category", "Sql")]
public sealed class PosCloseBlockerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);

    private sealed record PosWorld(Hotel H, PosService Pos, PosQueries Queries, PosCaller Caller, int Outlet, int Thali, int Dal);

    private static async Task<PosWorld> PosHotelAsync()
    {
        var h = await Hotel.CreateAsync(Ct);
        var pos = h.Get<PosService>();
        var caller = new PosCaller(h.PropertyId, h.Owner);
        var outlet = (await pos.CreateOutletAsync(caller, Hotel.Key(), new CreateOutletRequest("REST", "Restaurant", "table", ["T1", "T2"]), Ct)).Value.OutletId;
        var thali = (await pos.SaveMenuItemAsync(caller, Hotel.Key(), new SaveMenuItemRequest(null, "THALI", "Thali", "Main", 42000, true, [outlet], null), Ct)).Value.ItemId;
        var dal = (await pos.SaveMenuItemAsync(caller, Hotel.Key(), new SaveMenuItemRequest(null, "DAL", "Dal", "Main", 12000, true, [outlet], null), Ct)).Value.ItemId;
        return new PosWorld(h, pos, h.Get<PosQueries>(), caller, outlet, thali, dal);
    }

    private static async Task<long> SentCheckAsync(PosWorld w, string table, int item, int qty)
    {
        var o = (await w.Pos.OpenCheckAsync(w.Caller, Hotel.Key(), new OpenCheckRequest(w.Outlet, table, null, 2, "Guest"), Ct)).Value;
        await w.Pos.AddItemAsync(w.Caller, Hotel.Key(), new AddCheckItemRequest(o.OrderId, item, qty, null), Ct);
        await w.Pos.SendKotAsync(w.Caller, Hotel.Key(), new SendKotRequest(o.OrderId, null), Ct);
        return o.OrderId;
    }

    [Fact]
    public async Task An_open_check_blocks_the_close_with_a_listed_blocker_and_settling_it_allows_the_close()
    {
        var w = await PosHotelAsync();
        await using var _ = w.H;
        var id = await SentCheckAsync(w, "T2", w.Thali, 4);                       // Rs 1,680 + 5 % GST = Rs 1,764
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.H.CloseAsync(ct: Ct));
        var blocker = ex.Details!["blockers"].EnumerateArray().Single(b => b.GetProperty("id").GetString() == "pos");
        var line = blocker.GetProperty("items")[0].GetString()!;
        Assert.StartsWith("Table T2 · POS-" + id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + " · Guest · ", line, StringComparison.Ordinal);
        Assert.EndsWith(" open", line, StringComparison.Ordinal);
        Assert.Contains("1,764", line, StringComparison.Ordinal);
        var pre = await w.H.Closing.GetPreflightAsync(w.H.As(w.H.Owner), D24, Ct);
        Assert.True(pre.Checks.Single(c => c.Id == "pos").Blocking);
        Assert.Equal(("Blocked", "preflight"), ((await w.H.Closing.GetRunAsync(w.H.As(w.H.Owner), D24, Ct)).Status, (await w.H.Closing.GetRunAsync(w.H.As(w.H.Owner), D24, Ct)).CurrentStep));

        var version = (await w.Queries.GetCheckAsync(w.Caller, id, Ct)).Version;
        await w.Pos.SettleCheckAsync(w.Caller, Hotel.Key(), new SettleCheckRequest(id, "outlet", "UPI", "UTR0001", null, null, version), Ct);
        Assert.False((await w.H.Closing.GetPreflightAsync(w.H.As(w.H.Owner), D24, Ct)).Checks.Single(c => c.Id == "pos").Blocking);
        Assert.Equal("Completed", (await w.H.CloseAsync(ct: Ct)).Value.Status);
    }

    [Fact]
    public async Task A_pending_void_request_blocks_the_close_until_it_is_decided()
    {
        var w = await PosHotelAsync();
        await using var _ = w.H;
        var id = await SentCheckAsync(w, "T1", w.Thali, 2);               // Rs 840: above the Rs 500 limit, within the Manager's Rs 1,000
        var check = await w.Queries.GetCheckAsync(w.Caller, id, Ct);
        var manager = new PosCaller(w.H.PropertyId, w.H.Manager);
        var req = (await w.Pos.VoidItemAsync(manager, Hotel.Key(), new VoidCheckItemRequest(id, check.Lines[0].LineId, "Kitchen error", null), Ct)).Value;
        Assert.Equal("PendingApproval", req.Outcome);
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => w.H.CloseAsync(ct: Ct));
        var ids = ex.Details!["blockers"].EnumerateArray().Select(b => b.GetProperty("id").GetString()).ToArray();
        Assert.Contains("posVoids", ids);
        var voids = ex.Details["blockers"].EnumerateArray().Single(b => b.GetProperty("id").GetString() == "posVoids").GetProperty("items");
        Assert.StartsWith("Void request · Table T1 · POS-", voids[0].GetString(), StringComparison.Ordinal);

        // The Owner (another user) decides it (rejecting keeps the check open): the void blocker clears, the open-check blocker stays.
        var decided = await w.Pos.DecideVoidAsync(w.Caller, Hotel.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, false, "Keep"), Ct);
        Assert.Equal("Rejected", decided.Value.Status);
        Assert.DoesNotContain("posVoids", (await w.H.Closing.GetPreflightAsync(w.H.As(w.H.Owner), D24, Ct)).Checks.Where(c => c.Blocking).Select(c => c.Id));
        Assert.Contains("pos", (await w.H.Closing.GetPreflightAsync(w.H.As(w.H.Owner), D24, Ct)).Checks.Where(c => c.Blocking).Select(c => c.Id));
    }
}
