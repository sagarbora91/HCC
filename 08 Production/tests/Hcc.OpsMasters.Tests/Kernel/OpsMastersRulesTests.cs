using Hcc.Contracts.Masters;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Housekeeping;
using Hcc.Domain.OpsMasters;

namespace Hcc.OpsMasters.Tests.Kernel;

/// <summary>Rules of the operations masters without a database (mirrors tests/stream-ops-master.mjs of the prototype).</summary>
public class OpsMastersRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static string Field(HccException e) => e.Details!["field"].GetString()!;

    private static readonly string[] Three = ["Linen", "Bath", "Final"];

    [Fact]
    public void Task_type_is_normalised_kinds_in_a_fixed_order_items_trimmed()
    {
        var t = OpsMastersRules.ValidateTaskType([" Move ", "checkout", "checkout"], 45, true, [" Linen ", "", "Bath"]);
        Assert.Equal(("checkout,move", 45, true), (t.AppliesTo, t.Minutes, t.NeedsInspection));
        Assert.Equal(["Linen", "Bath"], t.Checklist);
        Assert.Equal("""["Linen","Bath"]""", OpsMastersRules.ChecklistJson(t.Checklist));
        Assert.Equal(["Linen", "Bath"], OpsMastersRules.ParseChecklist("""["Linen","Bath"]"""));
        Assert.Empty(OpsMastersRules.ParseChecklist("not json"));
    }

    [Theory]
    [InlineData(null, 40, "appliesTo")]
    [InlineData("bogus", 40, "appliesTo")]
    [InlineData("checkout", null, "standardMinutes")]
    [InlineData("checkout", 4, "standardMinutes")]
    [InlineData("checkout", 241, "standardMinutes")]
    public void Task_type_refuses_missing_kind_unknown_kind_and_minutes_out_of_range(string? kind, int? minutes, string field)
    {
        var e = Assert.Throws<HccException>(() => OpsMastersRules.ValidateTaskType(kind is null ? [] : [kind], minutes, false, Three));
        Assert.Equal(("MASTER_INVALID", field), (Reason(e), Field(e)));
    }

    [Fact]
    public void Task_type_checklist_is_one_to_eight_items_of_sixty_characters_each_once()
    {
        void Bad(string[] items)
        {
            var e = Assert.Throws<HccException>(() => OpsMastersRules.ValidateTaskType(["checkout"], 40, false, items));
            Assert.Equal("checklist", Field(e));
        }

        Bad([]);
        Bad(["", "  "]);
        Bad(Enumerable.Range(0, 9).Select(i => "Item " + i).ToArray());
        Bad([new string('x', 61)]);
        Bad(["Same", "same"]);
        Assert.Equal(8, OpsMastersRules.ValidateTaskType(["checkout"], 40, false, Enumerable.Range(0, 8).Select(i => "Item " + i).ToArray()).Checklist.Count);
    }

    [Fact]
    public void A_stayover_type_is_never_inspected_and_a_kind_belongs_to_one_active_type()
    {
        var e = Assert.Throws<HccException>(() => OpsMastersRules.ValidateTaskType(["stayover"], 20, true, Three));
        Assert.Equal("needsInspection", Field(e));
        OpsMastersRules.ValidateTaskType(["stayover", "inspection"], 20, false, Three);
        var others = new[] { (1L, "Departure clean", true, "checkout,move"), (2L, "Old", false, "stayover") };
        var clash = Assert.Throws<HccException>(() => OpsMastersRules.EnsureKindsFree("move", true, others, null));
        Assert.Contains("already handled by “Departure clean”", clash.Message, StringComparison.Ordinal);
        OpsMastersRules.EnsureKindsFree("stayover", true, others, null); // a retired type does not count
        OpsMastersRules.EnsureKindsFree("checkout", true, others, 1);    // the row itself does not count
        OpsMastersRules.EnsureKindsFree("checkout", false, others, null); // a retired row may overlap
    }

    [Fact]
    public void Zone_needs_a_floor_or_a_room_and_a_floor_or_room_belongs_to_one_active_zone()
    {
        Assert.Throws<HccException>(() => OpsMastersRules.ValidateZone([], []));
        Assert.Throws<HccException>(() => OpsMastersRules.ValidateZone(["f 1"], []));
        Assert.Throws<HccException>(() => OpsMastersRules.ValidateZone([], ["room!"]));
        var z = OpsMastersRules.ValidateZone(["f2", "F2", "F1"], ["201", "201", "202"]);
        Assert.Equal(("F2,F1", "201,202"), (z.Floors, z.Rooms));
        var others = new[] { (1L, "First", true, "F1", "101"), (2L, "Retired", false, "F3", "") };
        Assert.Equal("floors", Field(Assert.Throws<HccException>(() => OpsMastersRules.EnsureZoneFree(new("F1", ""), true, others, null))));
        Assert.Equal("rooms", Field(Assert.Throws<HccException>(() => OpsMastersRules.EnsureZoneFree(new("", "101"), true, others, null))));
        OpsMastersRules.EnsureZoneFree(new("F3", ""), true, others, null);
        OpsMastersRules.EnsureZoneFree(new("F1", "101"), true, others, 1);
    }

    [Fact]
    public void Category_department_and_sla_are_checked()
    {
        Assert.Equal("Front desk", OpsMastersRules.ValidateDepartment(" front desk "));
        Assert.Equal("department", Field(Assert.Throws<HccException>(() => OpsMastersRules.ValidateDepartment("Kitchen"))));
        Assert.Equal("department", Field(Assert.Throws<HccException>(() => OpsMastersRules.ValidateDepartment(null))));
        Assert.Equal(720, OpsMastersRules.ValidateSla(720));
        foreach (var bad in new int?[] { null, 0, 721 }) Assert.Equal("slaHours", Field(Assert.Throws<HccException>(() => OpsMastersRules.ValidateSla(bad))));
        Assert.Equal("name", Field(Assert.Throws<HccException>(() => OpsMastersRules.EnsureNameFree("noise", true, [(1L, "Noise", true)], null, "complaint category"))));
        OpsMastersRules.EnsureNameFree("noise", true, [(1L, "Noise", false)], null, "complaint category");
    }

    [Fact]
    public void History_values_read_like_the_prototype()
    {
        Assert.Equal("yes", OpsMastersRules.Show("needsInspection", "1"));
        Assert.Equal("no", OpsMastersRules.Show("valuable", "0"));
        Assert.Equal("A / B", OpsMastersRules.Show("checklist", """["A","B"]"""));
        Assert.Equal("checkout, move", OpsMastersRules.Show("appliesTo", "checkout,move"));
        Assert.Equal("40", OpsMastersRules.Show("standardMinutes", "40"));
        Assert.Equal("—", OpsMastersRules.Show("rooms", null));
        Assert.Equal((40, true, 3), OpsMastersRules.Fallback(HkTypeKinds.Checkout));
        Assert.Equal((20, false, 3), OpsMastersRules.Fallback(HkTypeKinds.Stayover));
    }

    [Fact]
    public void Task_machine_uses_the_checklist_length_of_the_type_and_defaults_to_three()
    {
        var start = new HkTaskState(HkTaskKind.Checkout, HkTaskStatus.InProgress, 0, 6, null);
        Assert.Throws<HccException>(() => HkTaskMachine.Apply(start, HkAction.Check, true, index: 4, checklistItems: 4));
        var four = HkTaskMachine.Apply(start, HkAction.Check, true, index: 3, checklistItems: 4);
        Assert.Equal(8, four.Task.ChecklistMask);
        Assert.Throws<HccException>(() => HkTaskMachine.Apply(start with { ChecklistMask = 7 }, HkAction.Complete, true, checklistItems: 4));
        var done = HkTaskMachine.Apply(start with { ChecklistMask = 15 }, HkAction.Complete, true, checklistItems: 4);
        Assert.Equal((HkTaskStatus.Inspection, RoomHk.Inspection), (done.Task.Status, done.RoomHk));
        var direct = HkTaskMachine.Apply(start with { ChecklistMask = 15 }, HkAction.Complete, false, checklistItems: 4);
        Assert.Equal((HkTaskStatus.Done, RoomHk.Ready), (direct.Task.Status, direct.RoomHk));
        // default: the built-in three items, as before MA-7
        Assert.Throws<HccException>(() => HkTaskMachine.Apply(start, HkAction.Check, true, index: 3));
        Assert.Equal(HkTaskStatus.Inspection, HkTaskMachine.Apply(start with { ChecklistMask = 7 }, HkAction.Complete, true).Task.Status);
    }

    [Fact]
    public void Contracts_carry_no_money_field()
    {
        var names = typeof(HkTaskTypeView).Assembly.GetTypes().Where(t => t.Namespace == "Hcc.Contracts.OpsMasters")
            .SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n => n.Contains("Rate", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Paise", StringComparison.Ordinal)
            || n.Contains("Price", StringComparison.Ordinal) || n.Contains("Balance", StringComparison.Ordinal));
    }
}
