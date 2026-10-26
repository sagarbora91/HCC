using System.Globalization;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Sql;

/// <summary>
/// A08-03 follow-up (Wave D): the masters import's room insert raises the type's sellable count, so it publishes InventoryChanged
/// {propertyId, roomTypeId, from, to} like the room masters: one event per room type for the whole file (one transaction), from the
/// business date (2026-10-26) for one year, or to the last counted night when that is later. Compiles with Wave D4's InventoryChangePublisher.
/// </summary>
[Trait("Category", "Sql")]
public class ImportInventoryChangedTests : IAsyncLifetime
{
    private ImportWorld _w = null!;

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<string[]> EventsAsync(string typeCode)
        => ((await _w.ScalarAsync($"""
            SELECT STRING_AGG(CONCAT(JSON_VALUE(o.payload, '$.from'), '>', JSON_VALUE(o.payload, '$.to'), '@', o.principal_id), '|') WITHIN GROUP (ORDER BY o.outbox_id)
            FROM plat.outbox o JOIN inv.room_type t ON t.room_type_id = o.aggregate_id AND t.code = '{typeCode}' AND t.property_id = {_w.PropertyId}
            WHERE o.event_type = 'InventoryChanged' AND JSON_VALUE(o.payload, '$.roomTypeId') = CAST(t.room_type_id AS VARCHAR(12)) AND o.property_id = {_w.PropertyId};
            """) as string) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Importing_rooms_publishes_one_event_per_room_type_from_the_business_date()
    {
        await _w.ImportSampleAsync("room-types");
        Assert.Empty(await EventsAsync("STD"));   // a new type has no rooms: nothing to sell yet
        // DLX has a counted night beyond one year: its event reaches that night.
        await _w.ExecAsync("INSERT inv.type_night_count(room_type_id, night, sellable, sold, held) SELECT room_type_id, '2028-01-01', 0, 0, 0 FROM inv.room_type WHERE code = 'DLX';");
        await _w.ImportSampleAsync("rooms");   // 101-103 STD, 201-202 DLX, 301 STE
        var owner = "@" + _w.Users["owner"].ToString(CultureInfo.InvariantCulture);
        Assert.Equal(["2026-10-26>2027-10-26" + owner], await EventsAsync("STD"));
        Assert.Equal(["2026-10-26>2028-01-02" + owner], await EventsAsync("DLX"));
        Assert.Equal(["2026-10-26>2027-10-26" + owner], await EventsAsync("STE"));
        Assert.Equal(2, await _w.CountAsync("SELECT sellable FROM inv.type_night_count WHERE night = '2028-01-01'"));
    }
}
