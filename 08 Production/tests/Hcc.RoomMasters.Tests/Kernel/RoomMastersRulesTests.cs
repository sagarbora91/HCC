using Hcc.Application.RoomMasters;
using Hcc.Contracts.Masters;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.RoomMasters;

namespace Hcc.RoomMasters.Tests.Kernel;

/// <summary>Rooms-masters rules without a database (prototype domain/streams/roommasters.ts parity).</summary>
public class RoomMastersRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    [Fact]
    public void Range_add_numbers_are_upper_cased_distinct_valid_and_at_most_50()
    {
        Assert.Equal(["101", "A12"], RoomMastersRules.RoomNos([" 101 ", "a12"]));
        Assert.Equal(MasterReasons.Invalid, Reason(Assert.Throws<HccException>(() => RoomMastersRules.RoomNos([]))));
        Assert.Equal(MasterReasons.Invalid, Reason(Assert.Throws<HccException>(() => RoomMastersRules.RoomNos(["101", "101"]))));
        Assert.Equal(MasterReasons.Invalid, Reason(Assert.Throws<HccException>(() => RoomMastersRules.RoomNos(["10#1"]))));
        Assert.Contains("50", Assert.Throws<HccException>(() => RoomMastersRules.RoomNos(Enumerable.Range(100, 51).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_type_fields_occupancy_bed_size_description_and_amenity_codes()
    {
        var f = RoomMastersRules.ValidateRoomType(2, 1, true, " King ", 240, ["wifi", "WIFI", "desk"], "  Spacious ");
        Assert.Equal((3, "King", 240, "Spacious"), (f.MaxOccupancy, f.BedType, f.SizeSqft, f.Description));
        Assert.Equal(["WIFI", "DESK"], f.Amenities);
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoomType(0, 0, false, null, null, null, null));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoomType(8, 6, false, null, null, null, null));
        Assert.Equal("bedType", Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoomType(2, 0, false, "Bunk", null, null, null)).Details!["field"].GetString());
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoomType(2, 0, false, null, 10, null, null));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoomType(2, 0, false, null, null, ["A B"], null));
    }

    [Fact]
    public void Dated_amounts_one_per_date_sorted_and_past_amounts_are_frozen()
    {
        var bd = new DateOnly(2026, 10, 24);
        var l = RoomMastersRules.Dated([new DatedAmount(320000, "2026-11-01"), new DatedAmount(300000, "2000-01-01")], "rackRates", "Rack rate");
        Assert.Equal([(new DateOnly(2000, 1, 1), 300000L), (new DateOnly(2026, 11, 1), 320000L)], l);
        Assert.Throws<HccException>(() => RoomMastersRules.Dated([new DatedAmount(1, "2026-11-01"), new DatedAmount(2, "2026-11-01")], "x", "X"));
        Assert.Throws<HccException>(() => RoomMastersRules.Dated([new DatedAmount(-1, "2026-11-01")], "x", "X"));
        Assert.Throws<HccException>(() => RoomMastersRules.Dated([new DatedAmount(1, "01/11/2026")], "x", "X"));
        RoomMastersRules.EnsurePastUnchanged(l, [l[0], (new DateOnly(2026, 12, 1), 330000L)], bd, "rackRates", "Rack rate");
        var e = Assert.Throws<HccException>(() => RoomMastersRules.EnsurePastUnchanged(l, [(new DateOnly(2000, 1, 1), 310000L)], bd, "rackRates", "Rack rate"));
        Assert.Equal(MasterReasons.Invalid, Reason(e));
        Assert.Equal("₹3,000 from 2000-01-01; ₹3,200 from 2026-11-01", RoomMastersRules.DatedText(l));
    }

    [Fact]
    public void Room_fields_connecting_room_extension_notes_and_ages()
    {
        var r = RoomMastersRules.ValidateRoom("105", ["ac"], true, " 106 ", "2105", " Garden ");
        Assert.Equal(["AC"], r.Features);
        Assert.Equal((true, "106", "2105", "Garden"), (r.Smoking, r.ConnectingRoomNo, r.Extension, r.Notes));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoom("105", null, false, "105", null, null));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateRoom("105", null, false, null, "21a", null));
        RoomMastersRules.ValidateAges(6, 12);
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateAges(13, 12));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateAges(null, 18));
        Assert.Equal("wifi", RoomMastersRules.ValidateIcon(" wifi "));
        Assert.Throws<HccException>(() => RoomMastersRules.ValidateIcon("rocket"));
    }

    [Fact]
    public void Rack_list_is_the_base_rate_from_2000_then_the_dated_tariffs()
    {
        var l = RoomMastersCommands.RackList(300000, [(new DateOnly(2026, 11, 1), 320000L)]);
        Assert.Equal([(new DateOnly(2000, 1, 1), 300000L), (new DateOnly(2026, 11, 1), 320000L)], l);
        Assert.Equal([(new DateOnly(2026, 11, 1), 320000L)], RoomMastersCommands.RackList(0, [(new DateOnly(2026, 11, 1), 320000L)]));
        Assert.Equal("2000-01-01", RoomMasterIds.BaseFrom);
    }
}
