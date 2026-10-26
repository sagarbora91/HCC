using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Masters.Tests.Kernel;

/// <summary>Masters rules without a database (prototype domain/streams/masters.ts parity).</summary>
public class MastersRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    [Fact]
    public void Codes_are_normalised_and_checked_against_the_pattern()
    {
        Assert.Equal(new MastersRules.CleanRow("F4", "Annexe", null), MastersRules.Validate(" f4 ", "  Annexe ", null));
        Assert.Equal("ANNEXE-2", MastersRules.Validate("annexe-2", "x", 5).Code);
        foreach (var bad in new[] { "", " ", "F 5", "F_5", "ABCDEFGHIJKLMNOPQRSTU", "FLÖOR" })
        {
            var e = Assert.Throws<HccException>(() => MastersRules.Validate(bad, "Name", null));
            Assert.Equal(HccErrorCode.VALIDATION, e.Code);
            Assert.Equal(MasterReasons.Invalid, Reason(e));
            Assert.Equal("code", e.Details!["field"].GetString());
        }
    }

    [Fact]
    public void Names_and_sort_orders_are_bounded()
    {
        Assert.Equal("name", Assert.Throws<HccException>(() => MastersRules.Validate("F1", "  ", null)).Details!["field"].GetString());
        Assert.Throws<HccException>(() => MastersRules.Validate("F1", new string('x', 61), null));
        Assert.Equal(60, MastersRules.Validate("F1", new string('x', 60), null).Name.Length);
        Assert.Equal("sortOrder", Assert.Throws<HccException>(() => MastersRules.Validate("F1", "One", -1)).Details!["field"].GetString());
        Assert.Throws<HccException>(() => MastersRules.Validate("F1", "One", 1_000_001));
    }

    [Fact]
    public void Master_errors_carry_the_frozen_wire_code_and_the_reason()
    {
        Assert.Equal(HccErrorCode.VALIDATION, MastersRules.Fail(MasterReasons.CodeTaken, "x").Code);
        Assert.Equal(HccErrorCode.VALIDATION, MastersRules.Fail(MasterReasons.Invalid, "x").Code);
        foreach (var r in new[] { MasterReasons.InUse, MasterReasons.Retired, MasterReasons.LockedByBookings })
            Assert.Equal(HccErrorCode.INVALID_TRANSITION, MastersRules.Fail(r, "x").Code);
        Assert.Equal(["MASTER_CODE_TAKEN", "MASTER_IN_USE", "MASTER_RETIRED", "MASTER_LOCKED_BY_BOOKINGS", "MASTER_INVALID"], MasterReasons.All);
    }

    [Fact]
    public void Code_is_immutable_once_used_and_used_rows_are_retired_not_deleted()
    {
        MastersRules.EnsureCodeMayChange("F1", "F1", 8, "rooms");
        MastersRules.EnsureCodeMayChange("ANNEXE", "ANX", 0, "rooms");
        var e = Assert.Throws<HccException>(() => MastersRules.EnsureCodeMayChange("F1", "G", 8, "rooms"));
        Assert.Equal(MasterReasons.InUse, Reason(e));
        Assert.Contains("retire it instead", Assert.Throws<HccException>(() => MastersRules.EnsureDeletable("Floor 1", 8, "rooms")).Message, StringComparison.Ordinal);
        MastersRules.EnsureDeletable("Annexe", 0, "rooms");
        Assert.Equal(MasterReasons.Retired, Reason(Assert.Throws<HccException>(() => MastersRules.EnsureActive(false, "Floor 2"))));
    }

    [Fact]
    public void Reorder_needs_every_row_once_and_assigns_steps_of_ten()
    {
        var order = MastersRules.Reorder([1, 2, 3], [3, 1, 2], "floor");
        Assert.Equal(10, order[3]);
        Assert.Equal(20, order[1]);
        Assert.Equal(30, order[2]);
        Assert.Throws<HccException>(() => MastersRules.Reorder([1, 2, 3], [1, 2], "floor"));
        Assert.Throws<HccException>(() => MastersRules.Reorder([1, 2, 3], [1, 1, 2], "floor"));
        Assert.Throws<HccException>(() => MastersRules.Reorder([1, 2, 3], [1, 2, 4], "floor"));
        Assert.Throws<HccException>(() => MastersRules.Reorder([1, 2, 3], null, "floor"));
        Assert.Equal(40, MastersRules.NextSort([10, 20, 30]));
        Assert.Equal(10, MastersRules.NextSort([]));
        Assert.Equal(30, MastersRules.NextSort([25]));
    }

    [Fact]
    public void Diff_and_audit_text_match_the_prototype()
    {
        var before = new MastersRules.CleanRow("F1", "Floor 1", 10);
        var after = new MastersRules.CleanRow("F1", "Ground floor", null);
        var d = MastersRules.Diff(before, after, 10);
        Assert.Single(d);
        Assert.Equal("Master Floors: name Floor 1 → Ground floor", MastersRules.AuditText("Floors", d));
        Assert.Equal(["code", "name", "sortOrder"], MastersRules.Diff(null, after, 40).Select(x => x.Field));
        Assert.Null(MastersRules.Diff(null, after, 40)[0].Old);
    }

    [Fact]
    public void Floor_of_a_room_is_the_hundreds_digit_and_codes_map_F_n()
    {
        Assert.Equal(1, MastersRules.FloorOfRoom("101"));
        Assert.Equal(3, MastersRules.FloorOfRoom("308"));
        Assert.Equal(12, MastersRules.FloorOfRoom("1205"));
        Assert.Equal(1, MastersRules.FloorOfRoom("G1"));
        Assert.Equal(1, MastersRules.FloorOfRoom("12"));
        Assert.Equal(1, MastersRules.FloorOfRoom("050"));
        Assert.Equal("F3", MastersRules.FloorCode(3));
        Assert.Equal(3, MastersRules.FloorNumber("F3"));
        Assert.Null(MastersRules.FloorNumber("ANNEXE"));
        Assert.Null(MastersRules.FloorNumber("F"));
    }

    [Fact]
    public void Paging_arguments_are_checked()
    {
        Assert.Equal(new MastersRules.PageArgs(1, 100), MastersRules.Page(null, null));
        Assert.Equal(20, MastersRules.Page(3, 10).Skip);
        Assert.Throws<HccException>(() => MastersRules.Page(0, 10));
        Assert.Throws<HccException>(() => MastersRules.Page(1, 501));
        Assert.Throws<HccException>(() => MastersRules.Page(1, 0));
    }
}
