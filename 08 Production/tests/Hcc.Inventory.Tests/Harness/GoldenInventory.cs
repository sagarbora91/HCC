using System.Globalization;
using System.Text;
using System.Text.Json;
using Hcc.Domain.Dates;

namespace Hcc.Inventory.Tests.Harness;

/// <summary>
/// Raw BASE records + prototype-computed figures exported by <c>Golden/export-inventory-golden.mjs</c> from
/// <c>03 Prototype loadScenario('BASE')</c> (regenerate after a prototype fixture or rule change).
/// </summary>
public sealed record GoldenInventory(string GeneratedBy, string Source, bool FictionalDataOnly, GoldenRaw Raw, GoldenExpected Expected)
{
    public static readonly TimeZoneInfo Kolkata = CalendarDate.AssertTimeZone("Asia/Kolkata");

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static GoldenInventory Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "inventory-base.golden.json");
        return JsonSerializer.Deserialize<GoldenInventory>(File.ReadAllText(path), Options) ?? throw new InvalidOperationException("Empty golden file.");
    }

    /// <summary>Prototype simulated clock (YYYY-MM-DDTHH:mm, Asia/Kolkata) → UTC.</summary>
    public static DateTime LocalToUtc(string local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.ParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), Kolkata);

    public static string Status(string prototype) => prototype switch
    {
        "In house" => "InHouse",
        "No show" => "NoShow",
        _ => prototype,
    };

    /// <summary>Loads the raw BASE records into a migrated empty database as property 1 and rebuilds the type-night counts.</summary>
    public async Task SeedAsync(InventoryDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var r = Raw;
        var sql = new StringBuilder();
        sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.property(code, name, time_zone, business_date) VALUES ('BASE', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '{r.BusinessDate}');");
        for (var i = 0; i < r.RoomTypes.Count; i++)
        {
            var t = r.RoomTypes[i];
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_type(property_id, code, max_occupancy, rack_rate_paise, sort_order) VALUES (1, '{t.Code}', {t.MaxOccupancy}, {t.RackRatePaise}, {i});");
        }

        foreach (var room in r.Rooms)
        {
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room(property_id, room_no, room_type_id) SELECT 1, '{room.RoomNo}', room_type_id FROM inv.room_type WHERE code = '{room.Type}';");
            if (room.Blocked)
                sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_block(room_id, from_night, to_night, reason) SELECT room_id, {Lit(room.BlockFrom)}, {Lit(room.BlockTo)}, N'Out of service' FROM inv.room WHERE room_no = '{room.RoomNo}';");
        }

        foreach (var b in r.Bookings)
        {
            var hold = b.HoldUntil is null ? "NULL" : "'" + LocalToUtc(b.HoldUntil).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "'";
            sql.AppendLine(CultureInfo.InvariantCulture, $"""
                INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, hold_until_utc, room_id, room_type_id, adults, children, nightly_rate_paise)
                SELECT 1, '{b.Id}', N'{b.Guest.Replace("'", "''", StringComparison.Ordinal)}', '{b.Arrival}', '{b.Departure}', '{Status(b.Status)}', {hold}, rm.room_id, rm.room_type_id, {b.People}, {b.Children}, t.rack_rate_paise
                FROM inv.room rm JOIN inv.room_type t ON t.room_type_id = rm.room_type_id WHERE rm.room_no = '{b.Room}';
                """);
            for (var n = StayDates.Parse(b.Arrival); n < StayDates.Parse(b.Departure); n = n.AddDays(1))
                sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_night(room_id, night, reservation_id) SELECT r.room_id, '{StayDates.ToWire(n)}', r.reservation_id FROM inv.reservation r WHERE r.public_no = '{b.Id}';");
        }

        foreach (var g in r.Blocks)
        {
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.group_block(property_id, code, name, arrival, departure, cutoff, status) VALUES (1, '{g.Id}', N'{g.Name.Replace("'", "''", StringComparison.Ordinal)}', '{g.Arrival}', '{g.Departure}', '{g.Cutoff}', '{g.Status}');");
            foreach (var (type, allot) in g.Allot)
            {
                var picked = g.Picked.TryGetValue(type, out var p) ? p : 0;
                // A released block keeps allotted = picked (prototype grpSweep / GRP_RELEASE).
                var allotted = g.Status == "Released" ? picked : allot;
                sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.group_block_type(group_block_id, room_type_id, allotted, picked, released, rate_paise) SELECT g.group_block_id, t.room_type_id, {allotted}, {picked}, {allot - allotted}, {(g.Rates.TryGetValue(type, out var rate) ? rate.ToString(CultureInfo.InvariantCulture) : "NULL")} FROM inv.group_block g CROSS JOIN inv.room_type t WHERE g.code = '{g.Id}' AND t.code = '{type}';");
            }
        }

        foreach (var x in r.Restrictions)
        {
            var type = x.Type == "*" ? "NULL" : $"(SELECT room_type_id FROM inv.room_type WHERE code = '{x.Type}')";
            sql.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.stay_restriction(property_id, code, room_type_id, arrival_from, arrival_to, min_stay, closed_to_arrival, note) VALUES (1, '{x.Id}', {type}, '{x.From}', '{x.To}', {x.MinStay}, {(x.Cta ? 1 : 0)}, N'{x.Note.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        sql.AppendLine("EXEC inv.usp_rebuild_type_night_count 1, '2026-10-01', '2027-02-01';");
        await db.ExecAsync(sql.ToString());
    }

    private static string Lit(string? date) => date is null ? "NULL" : $"'{date}'";
}

public sealed record GoldenRoomType(string Code, int MaxOccupancy, long RackRatePaise);

public sealed record GoldenRoom(string RoomNo, string Type, bool Blocked, string? BlockFrom, string? BlockTo);

public sealed record GoldenBooking(string Id, string Room, string Guest, string Arrival, string Departure, string Status, string? HoldUntil, int People, int Children);

public sealed record GoldenBlock(string Id, string Name, string Arrival, string Departure, string Cutoff, string Status, Dictionary<string, int> Allot, Dictionary<string, int> Picked, Dictionary<string, long> Rates);

public sealed record GoldenRestriction(string Id, string Type, string From, string To, int MinStay, bool Cta, string Note);

public sealed record GoldenRaw(string BusinessDate, string Clock, int TaxBps, int HoldMinutes, IReadOnlyList<GoldenRoomType> RoomTypes, IReadOnlyList<GoldenRoom> Rooms,
    IReadOnlyList<GoldenBooking> Bookings, IReadOnlyList<GoldenBlock> Blocks, IReadOnlyList<GoldenRestriction> Restrictions);

public sealed record GoldenQuery(string Arrival, string Departure, int Adults, int Children, string? Type);

public sealed record GoldenRoomRow(string RoomNo, bool Available, IReadOnlyList<string> Reasons, bool AvailableRaw, bool Bookable);

public sealed record GoldenSearch(GoldenQuery Query, IReadOnlyList<GoldenRoomRow> Rooms);

public sealed record GoldenHold(string RoomNo, string Block, string Type, string From, string To);

public sealed record GoldenViolation(string Code, string Restriction, string Message);

public sealed record GoldenViolationCase(string Type, string Arrival, string Departure, IReadOnlyList<GoldenViolation> Violations);

public sealed record GoldenNight(string Date, long RatePaise, int TaxBps, long TaxPaise);

public sealed record GoldenQuoteCase(string RoomNo, string Type, string Arrival, string Departure, IReadOnlyList<GoldenNight> Nights, long NetPaise, long TaxPaise,
    long TotalPaise, IReadOnlyList<string> Violations);

public sealed record GoldenBookCommand(string Guest, string Room, string Arrival, string Departure, int People, bool? Hold, string? RestrictionOverride);

public sealed record GoldenBookResult(string Code, string? Status, string? HoldUntil);

public sealed record GoldenBookCase(string Name, string Uid, GoldenBookCommand Cmd, GoldenBookResult Result);

public sealed record GoldenExpected(IReadOnlyList<GoldenSearch> Searches, IReadOnlyList<GoldenHold> Holds, IReadOnlyList<GoldenViolationCase> ViolationCases,
    IReadOnlyList<GoldenQuoteCase> QuoteCases, IReadOnlyList<GoldenBookCase> BookCases);
