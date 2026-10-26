using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.PeopleLists.Tests.Harness;

namespace Hcc.PeopleLists.Tests.Sql;

/// <summary>
/// People lists commands and reads on SQL Server with the real access policy (PERM-MST-001 Owner + Tech admin, PERM-MST-002 Owner): first-use seeds,
/// idempotency, STALE_VERSION, in-use rules, audit, outbox and history. Every test runs in its own hotel (property + users).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PeopleListsSqlTests(PeopleDb db) : IClassFixture<PeopleDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Reason(Hcc.Domain.Errors.HccException e) => e.Details!["reason"].GetString()!;

    private static Task<Hcc.Application.Commands.CommandResult<ShiftPatternView>> SaveShift(Hotel h, string user, string code, string name, string start, string end, string key, int? id = null, string? version = null,
        int brk = 30, bool night = false, string? rule = null) =>
        h.Lists.SaveShiftPatternAsync(h.As(user, "SaveShiftPattern", key), new SaveShiftPatternRequest(id, code, name, start, end, null, brk, night, rule, version), Ct);

    private static async Task<ShiftPatternView> ShiftOf(Hotel h, string code) => (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == code);

    private static async Task<LeaveTypeView> LeaveOf(Hotel h, string code) => (await h.Queries.LeaveTypesAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == code);

    [Fact]
    public async Task Migration_creates_the_tables_and_widens_staff_roster_and_leave_codes()
    {
        foreach (var t in new[] { "designation", "shift_pattern", "leave_type", "holiday", "people_list_seed" })
            Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM sys.tables WHERE name = @n AND schema_id = SCHEMA_ID('hr');", ("@n", t)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('hr.staff') AND name = 'designation_code';"));
        var h = await db.NewHotelAsync();
        // A custom shift code and leave type are accepted by the tables (the lists, not a fixed CHECK, decide); OFF as a default shift and malformed codes are not.
        var sid = await db.LongAsync("""
            INSERT hr.staff(property_id, name, dept_code, designation, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, active, phone, created_by, created_utc)
            OUTPUT INSERTED.staff_id VALUES (@p, N'Custom', 'FO', N'Clerk', 'monthly', 1000000, 0, 'S1', 2, '2025-04-01', 1, '90000', @by, '2026-01-01');
            """, ("@p", h.PropertyId), ("@by", h.U("owner")));
        await db.ScalarAsync("INSERT hr.roster(staff_id, work_date, shift_code, set_by, set_utc) VALUES (@s, '2026-10-26', 'OFF', @by, '2026-10-24');", ("@s", sid), ("@by", h.U("owner")));
        await db.ScalarAsync("""
            INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, days_x2, reason, status, requested_by, requested_utc)
            VALUES (@p, @s, 'BL', '2026-10-27', '2026-10-27', 2, N'Family', 'Pending', @by, '2026-10-24');
            """, ("@p", h.PropertyId), ("@s", sid), ("@by", h.U("owner")));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("UPDATE hr.staff SET default_shift = 'OFF' WHERE staff_id = @s;", ("@s", sid)));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("UPDATE hr.staff SET default_shift = 'm' WHERE staff_id = @s;", ("@s", sid)));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("UPDATE hr.leave_request SET leave_type = 'b l' WHERE staff_id = @s;", ("@s", sid)));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("INSERT hr.holiday(property_id, code, name, kind, sort_order, updated_by, updated_utc) VALUES (@p, '2026-02-30', N'x', 'paid', 1, 1, '2026-10-24');", ("@p", h.PropertyId)));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("INSERT hr.shift_pattern(property_id, code, name, start_time, end_time, break_minutes, night, weekly_off_rule, sort_order, updated_by, updated_utc) VALUES (@p, 'OFF', N'x', '09:00', '17:00', 0, 0, 'fixed', 1, 1, '2026-10-24');", ("@p", h.PropertyId)));
    }

    [Fact]
    public async Task First_read_seeds_an_empty_property_once_from_todays_constants_the_stored_policy_and_the_staff_titles()
    {
        var h = await db.NewHotelAsync();
        await db.ScalarAsync("INSERT hr.leave_policy(property_id, cl_days, sl_days, el_days, default_cap, updated_by, updated_utc) VALUES (@p, 10, 5, 20, 2, @by, '2026-10-01');", ("@p", h.PropertyId), ("@by", h.U("owner")));
        await h.AddStaffAsync("Asha Cook", dept: "KIT", designation: "Cook");
        await h.AddStaffAsync("Bala Cook", dept: "KIT", designation: "cook");
        await h.AddStaffAsync("Imran", dept: "KIT", designation: "Kitchen helper (daily wage)", payType: "daily", monthly: 0, daily: 60_000);

        var shifts = await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("housekeeper"), ct: Ct);
        Assert.Equal(["M", "E", "N", "G"], shifts.Items.Select(x => x.Code));
        Assert.Equal(("07:00", "15:00", 30, false, "fixed"), (shifts.Items[0].Start, shifts.Items[0].End, shifts.Items[0].BreakMinutes, shifts.Items[0].Night, shifts.Items[0].WeeklyOffRule));
        Assert.True(shifts.Items[2].Night);
        var leave = await h.Queries.LeaveTypesAsync(h.PropertyId, h.U("housekeeper"), ct: Ct);
        Assert.Equal(["CL", "SL", "EL", "CO", "ML", "LOP"], leave.Items.Select(x => x.Code));
        Assert.Equal([10, 5, 20], leave.Items.Take(3).Select(x => x.YearlyQuota!.Value));
        Assert.Equal((true, false, true), (leave.Items[2].Encashment, leave.Items[5].Paid, leave.Items[3].YearlyQuota is null));
        var desig = await h.Queries.DesignationsAsync(h.PropertyId, h.U("owner"), ct: Ct);
        Assert.Equal(["COOK", "KITCHEN-HELPER-DAILY"], desig.Items.Select(x => x.Code));
        Assert.Equal(["monthly-structured", "daily"], desig.Items.Select(x => x.DefaultPayRule));
        Assert.Equal((2, 1), (desig.Items[0].Used, desig.Items[1].Used));
        Assert.Equal(0, (await h.Queries.HolidaysAsync(h.PropertyId, h.U("owner"), ct: Ct)).Total);   // no holidays are assumed: the Owner adds the calendar

        await db.ScalarAsync("DELETE hr.designation WHERE property_id = @p;", ("@p", h.PropertyId));   // an emptied list is not seeded again
        Assert.Equal(0, (await h.Queries.DesignationsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Total);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM hr.people_list_seed WHERE property_id = @p;", ("@p", h.PropertyId)));
        Assert.Equal(4, (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Total);
    }

    [Fact]
    public async Task Pay_rule_of_a_designation_is_shown_to_the_Owner_and_payroll_roles_only()
    {
        var h = await db.NewHotelAsync();
        await h.AddStaffAsync("Asha", dept: "KIT", designation: "Cook");
        foreach (var (user, sees) in new[] { ("owner", true), ("accountant", true), ("housekeeper", false), ("manager", false), ("reception", false) })
        {
            var d = (await h.Queries.DesignationsAsync(h.PropertyId, h.U(user), ct: Ct)).Items.Single();
            Assert.Equal(sees ? "monthly-structured" : null, d.DefaultPayRule);
            Assert.Equal("COOK", d.Code);
        }
    }

    [Fact]
    public async Task Save_shift_pattern_audits_writes_history_and_the_outbox_and_replays_idempotently()
    {
        var h = await db.NewHotelAsync();
        var a = await SaveShift(h, "owner", " s ", " Split ", "10:00", "20:00", "k1", brk: 60);
        Assert.False(a.Replayed);
        Assert.Equal(("S", "Split", 50, true, "10:00", "20:00", 60, 0), (a.Value.Code, a.Value.Name, a.Value.SortOrder, a.Value.Active, a.Value.Start, a.Value.End, a.Value.BreakMinutes, a.Value.Used));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MST_SHIFT_PATTERN' AND detail = N'Master Shift patterns: added S · Split' AND property_id = @p;", ("@p", h.PropertyId)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MasterChanged' AND payload LIKE '%shiftPatterns%' AND aggregate_id = @i;", ("@i", a.Value.ShiftPatternId)));
        var again = await SaveShift(h, "owner", " s ", " Split ", "10:00", "20:00", "k1", brk: 60);
        Assert.True(again.Replayed);
        Assert.Equal(a.Value.ShiftPatternId, again.Value.ShiftPatternId);
        Assert.Equal(5, (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Total);

        var edited = (await SaveShift(h, "techadmin", "S", "Split", "10:30", "20:00", "k2", a.Value.ShiftPatternId, a.Value.Version, 60)).Value;
        Assert.Equal("10:30", edited.Start);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = N'Master Shift patterns: start 10:00 → 10:30' AND property_id = @p;", ("@p", h.PropertyId)));
        var hist = await h.Queries.HistoryAsync(Hcc.Domain.PeopleLists.PeopleList.ShiftPatterns, h.PropertyId, h.U("owner"), a.Value.ShiftPatternId, ct: Ct);
        Assert.Equal(("start", "10:00", "10:30", h.U("techadmin")), (hist.Items[0].Field, hist.Items[0].OldValue, hist.Items[0].NewValue, hist.Items[0].ChangedBy));
        Assert.Equal(9, hist.Total);   // 8 fields of the new row (code, name, sortOrder, start, end, breakMinutes, night, weeklyOffRule) + the edit
    }

    [Fact]
    public async Task Stale_version_duplicate_code_duplicate_name_duplicate_times_nothing_changed_and_bad_input_are_refused()
    {
        var h = await db.NewHotelAsync();
        var s = (await SaveShift(h, "owner", "S", "Split", "10:00", "20:00", "a1")).Value;
        await SaveShift(h, "owner", "S", "Split", "10:00", "21:00", "a2", s.ShiftPatternId, s.Version);
        var stale = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => SaveShift(h, "owner", "S", "Split", "10:00", "22:00", "a3", s.ShiftPatternId, s.Version));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "m", "Another", "10:00", "11:00", "a4"), HccErrorCode.VALIDATION, MasterReasons.CodeTaken);
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "S2", "morning", "10:00", "11:00", "a5"), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "S3", "Early", "07:00", "15:00", "a6"), HccErrorCode.VALIDATION, MasterReasons.Invalid);   // same times as Morning
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "OFF", "Off", "07:00", "15:30", "a7"), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "S4", "Overnight", "20:00", "04:00", "a8"), HccErrorCode.VALIDATION, MasterReasons.Invalid);   // night flag missing
        var cur = await ShiftOf(h, "S");
        var same = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => SaveShift(h, "owner", "S", "Split", "10:00", "21:00", "a9", cur.ShiftPatternId, cur.Version));
        Assert.Contains("Nothing changed", same.Message, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => h.Lists.SaveShiftPatternAsync(h.As("owner", "SaveShiftPattern", "a10"), new SaveShiftPatternRequest(9999, "S9", "x", "10:00", "11:00", null, 0, false, null, cur.Version), Ct), HccErrorCode.NOT_FOUND);
    }

    [Fact]
    public async Task A_used_code_cannot_change_a_used_row_is_retired_not_deleted_and_a_default_shift_cannot_be_retired()
    {
        var h = await db.NewHotelAsync();
        var s = (await SaveShift(h, "owner", "S", "Split", "10:00", "20:00", "u1")).Value;
        var staff = await h.AddStaffAsync("Asha", shift: "M");
        await h.AddStaffAsync("Bala", shift: "M");
        var m = await ShiftOf(h, "M");
        Assert.Equal(2, m.Used);
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "MO", "Morning", "07:00", "15:00", "u2", m.ShiftPatternId, m.Version), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        await Hotel.FailsAsync(() => h.Lists.DeleteShiftPatternAsync(h.As("owner", "DeleteShiftPattern", "u3"), new DeleteShiftPatternRequest(m.ShiftPatternId, m.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        var retire = await Hotel.FailsAsync(() => h.Lists.RetireShiftPatternAsync(h.As("owner", "RetireShiftPattern", "u4"), new RetireShiftPatternRequest(m.ShiftPatternId, m.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        Assert.Contains("Asha", retire.Message, StringComparison.Ordinal);

        // A roster cell on a future date counts as use; an unused pattern is deleted, a used one only retired.
        await h.AddStaffAsync("Chitra", shift: "G");
        await db.ScalarAsync("INSERT hr.roster(staff_id, work_date, shift_code, set_by, set_utc) VALUES (@s, '2026-10-30', 'S', @by, '2026-10-24');", ("@s", staff), ("@by", h.U("owner")));
        Assert.Equal(1, (await ShiftOf(h, "S")).Used);
        await Hotel.FailsAsync(() => h.Lists.DeleteShiftPatternAsync(h.As("owner", "DeleteShiftPattern", "u5"), new DeleteShiftPatternRequest(s.ShiftPatternId, s.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        var retired = (await h.Lists.RetireShiftPatternAsync(h.As("techadmin", "RetireShiftPattern", "u6"), new RetireShiftPatternRequest(s.ShiftPatternId, s.Version), Ct)).Value;
        Assert.Equal((false, "2026-10-24", 1), (retired.Active, retired.RetiredOn, retired.Used));
        await Hotel.FailsAsync(() => SaveShift(h, "owner", "S", "Split", "10:00", "20:30", "u7", s.ShiftPatternId, retired.Version), HccErrorCode.INVALID_TRANSITION, MasterReasons.Retired);
        var back = (await h.Lists.ReactivateShiftPatternAsync(h.As("owner", "ReactivateShiftPattern", "u8"), new ReactivateShiftPatternRequest(s.ShiftPatternId, retired.Version), Ct)).Value;
        Assert.True(back.Active);
        var temp = (await SaveShift(h, "owner", "T", "Temp", "12:00", "13:00", "u9")).Value;
        Assert.True((await h.Lists.DeleteShiftPatternAsync(h.As("owner", "DeleteShiftPattern", "u10"), new DeleteShiftPatternRequest(temp.ShiftPatternId, temp.Version), Ct)).Value.Deleted);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM mst.master_history WHERE row_id = @r AND master = 'shiftPatterns' AND field = 'deleted';", ("@r", temp.ShiftPatternId)));
    }

    [Fact]
    public async Task Reorder_needs_every_row_once_and_the_lists_page_and_filter()
    {
        var h = await db.NewHotelAsync();
        var rows = (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items;
        var ids = new[] { rows[3].ShiftPatternId, rows[0].ShiftPatternId, rows[1].ShiftPatternId, rows[2].ShiftPatternId };
        var list = (await h.Lists.ReorderShiftPatternsAsync(h.As("owner", "ReorderShiftPatterns", "o1"), new ReorderShiftPatternsRequest(ids), Ct)).Value;
        Assert.Equal(["G", "M", "E", "N"], list.Items.Select(x => x.Code));
        Assert.Equal([10, 20, 30, 40], list.Items.Select(x => x.SortOrder));
        await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => h.Lists.ReorderShiftPatternsAsync(h.As("owner", "ReorderShiftPatterns", "o2"), new ReorderShiftPatternsRequest([ids[0]]), Ct));
        var same = await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => h.Lists.ReorderShiftPatternsAsync(h.As("owner", "ReorderShiftPatterns", "o3"), new ReorderShiftPatternsRequest(ids), Ct));
        Assert.Contains("did not change", same.Message, StringComparison.Ordinal);
        var p1 = await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("maint"), 1, 3, null, Ct);
        var p2 = await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("maint"), 2, 3, null, Ct);
        Assert.Equal((4, 3, 1), (p1.Total, p1.Items.Count, p2.Items.Count));
        var g = await ShiftOf(h, "G");
        await h.Lists.RetireShiftPatternAsync(h.As("owner", "RetireShiftPattern", "o4"), new RetireShiftPatternRequest(g.ShiftPatternId, g.Version), Ct);
        Assert.Equal(["G"], (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("maint"), status: "retired", ct: Ct)).Items.Select(x => x.Code));
        Assert.Equal(3, (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("maint"), status: "active", ct: Ct)).Total);
        await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("maint"), status: "gone", ct: Ct));
    }

    [Fact]
    public async Task Permissions_Owner_and_Tech_admin_change_operational_lists_pay_fields_and_holidays_are_Owner_only_everyone_else_is_refused()
    {
        var h = await db.NewHotelAsync();
        foreach (var u in new[] { "manager", "reception", "cashier", "supervisor", "housekeeper", "accountant", "maint" })
        {
            await Hotel.FailsAsync(() => SaveShift(h, u, "S", "Split", "10:00", "20:00", "p-" + u), HccErrorCode.FORBIDDEN);
            await Hotel.FailsAsync(() => h.Queries.HistoryAsync(Hcc.Domain.PeopleLists.PeopleList.Holidays, h.PropertyId, h.U(u), ct: Ct), HccErrorCode.FORBIDDEN);
        }

        // Tech admin: shift patterns and designations without a pay rule yes; holidays, new leave types and pay rules no (PERM-MST-002).
        Assert.Equal("S", (await SaveShift(h, "techadmin", "S", "Split", "10:00", "20:00", "t1")).Value.Code);
        Assert.Equal("CHEF", (await h.Lists.SaveDesignationAsync(h.As("techadmin", "SaveDesignation", "t2"), new SaveDesignationRequest(null, "CHEF", "Chef", null, "KIT", "G3"), Ct)).Value.Code);
        await Hotel.FailsAsync(() => h.Lists.SaveDesignationAsync(h.As("techadmin", "SaveDesignation", "t3"), new SaveDesignationRequest(null, "COOK", "Cook", null, "KIT", null, "daily"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("techadmin", "SaveHoliday", "t4"), new SaveHolidayRequest(null, "2026-11-08", "Diwali", "paid"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Lists.SaveLeaveTypeAsync(h.As("techadmin", "SaveLeaveType", "t5"), new SaveLeaveTypeRequest(null, "BL", "Bereavement", true, 3), Ct), HccErrorCode.FORBIDDEN);
        var cl = await LeaveOf(h, "CL");
        await Hotel.FailsAsync(() => h.Lists.SaveLeaveTypeAsync(h.As("techadmin", "SaveLeaveType", "t6"), new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual leave", true, 20, 0, false, null, cl.Version), Ct), HccErrorCode.FORBIDDEN);
        var renamed = (await h.Lists.SaveLeaveTypeAsync(h.As("techadmin", "SaveLeaveType", "t7"), new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual days", true, 12, 0, false, null, cl.Version), Ct)).Value;
        Assert.Equal("Casual days", renamed.Name);   // the name alone is not a pay field
        // Owner: everything; the Manager can be granted the lists later (DEC-MA-1).
        Assert.Equal("2026-11-08", (await h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "t8"), new SaveHolidayRequest(null, "2026-11-08", "Diwali", "paid"), Ct)).Value.Code);
        await db.ScalarAsync("INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES ('MGR', 'PERM-MST-001', 'A', 'P', NULL, NULL);");
        Assert.Equal("E2", (await SaveShift(h, "manager", "E2", "Evening two", "16:00", "23:30", "t9")).Value.Code);
        await db.ScalarAsync("DELETE plat.role_grant WHERE role_code = 'MGR' AND permission_id = 'PERM-MST-001';");
    }

    [Fact]
    public async Task Designation_rules_department_must_exist_codes_are_unique_and_used_designations_are_retired_not_deleted()
    {
        var h = await db.NewHotelAsync();
        await Hotel.FailsAsync(() => h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d1"), new SaveDesignationRequest(null, "CHEF", "Chef", null, "ZZ"), Ct), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        var chef = (await h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d2"), new SaveDesignationRequest(null, "chef", "Chef", null, "kit", "G3", "monthly-basic"), Ct)).Value;
        Assert.Equal(("CHEF", "KIT", "G3", "monthly-basic"), (chef.Code, chef.Dept, chef.Grade, chef.DefaultPayRule));
        await Hotel.FailsAsync(() => h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d3"), new SaveDesignationRequest(null, "CHEF", "Head chef"), Ct), HccErrorCode.VALIDATION, MasterReasons.CodeTaken);
        await Hotel.FailsAsync(() => h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d4"), new SaveDesignationRequest(null, "HEAD", "Chef"), Ct), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        var staff = await h.AddStaffAsync("Asha", dept: "KIT", designation: "x", designationCode: "CHEF");
        var view = (await h.Queries.DesignationsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "CHEF");
        Assert.Equal(1, view.Used);
        await Hotel.FailsAsync(() => h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d5"), new SaveDesignationRequest(view.DesignationId, "COOKCHEF", "Chef", null, "KIT", "G3", "monthly-basic", view.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        var renamed = (await h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation", "d6"), new SaveDesignationRequest(view.DesignationId, "CHEF", "Head chef", null, "KIT", "G3", "monthly-basic", view.Version), Ct)).Value;
        Assert.Equal(("Head chef", 1), (renamed.Name, renamed.Used));   // linked by code: still the same one
        await Hotel.FailsAsync(() => h.Lists.DeleteDesignationAsync(h.As("owner", "DeleteDesignation", "d7"), new DeleteDesignationRequest(renamed.DesignationId, renamed.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        var retired = (await h.Lists.RetireDesignationAsync(h.As("owner", "RetireDesignation", "d8"), new RetireDesignationRequest(renamed.DesignationId, renamed.Version), Ct)).Value;
        Assert.Equal(1, retired.Used);
        Assert.NotEqual(0L, staff);
    }

    [Fact]
    public async Task Leave_types_keep_one_unpaid_type_mirror_CL_SL_EL_quotas_to_the_policy_and_are_retired_when_used()
    {
        var h = await db.NewHotelAsync();
        var lop = await LeaveOf(h, "LOP");   // first read seeds
        await Hotel.FailsAsync(() => h.Lists.RetireLeaveTypeAsync(h.As("owner", "RetireLeaveType", "l1"), new RetireLeaveTypeRequest(lop.LeaveTypeId, lop.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        await Hotel.FailsAsync(() => h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType", "l2"), new SaveLeaveTypeRequest(lop.LeaveTypeId, "LOP", "Leave without pay", true, null, 0, false, null, lop.Version), Ct), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        await Hotel.FailsAsync(() => h.Lists.DeleteLeaveTypeAsync(h.As("owner", "DeleteLeaveType", "l3"), new DeleteLeaveTypeRequest(lop.LeaveTypeId, lop.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        await Hotel.FailsAsync(() => h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType", "l4"), new SaveLeaveTypeRequest(null, "BL", "Bereavement", true, null, 2), Ct), HccErrorCode.VALIDATION, MasterReasons.Invalid);   // carry needs a quota
        var ul = (await h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType", "l5"), new SaveLeaveTypeRequest(null, "UL", "Unpaid study leave", false), Ct)).Value;
        Assert.Equal((false, null), (ul.Paid, ul.YearlyQuota));
        Assert.True((await h.Lists.RetireLeaveTypeAsync(h.As("owner", "RetireLeaveType", "l6"), new RetireLeaveTypeRequest(lop.LeaveTypeId, lop.Version), Ct)).Value is { Active: false });   // UL remains unpaid

        var cl = await LeaveOf(h, "CL");
        var ten = (await h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType", "l7"), new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual leave", true, 10, 4, false, null, cl.Version), Ct)).Value;
        Assert.Equal((10, 4), (ten.YearlyQuota, ten.CarryForward));
        Assert.Equal(10, await db.LongAsync("SELECT cl_days FROM hr.leave_policy WHERE property_id = @p;", ("@p", h.PropertyId)));   // mirrored (the policy row did not exist: created with the default SL / EL)
        Assert.Equal((7, 15), (await db.LongAsync("SELECT sl_days FROM hr.leave_policy WHERE property_id = @p;", ("@p", h.PropertyId)), await db.LongAsync("SELECT el_days FROM hr.leave_policy WHERE property_id = @p;", ("@p", h.PropertyId))));
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM mst.master_history WHERE property_id = @p AND master = 'leaveTypes' AND row_id = @r AND field IN ('yearlyQuota', 'carryForward');", ("@p", h.PropertyId), ("@r", cl.LeaveTypeId)));

        // SetLeavePolicy writes through to the list, with history per changed type.
        var owner = h.As("owner", "SetLeavePolicy", "l8");
        await h.Db.Payroll.SetLeavePolicyAsync(owner, new Contracts.Payroll.SetLeavePolicyRequest(10, 7, 21, 2), Ct);
        Assert.Equal(21, (await LeaveOf(h, "EL")).YearlyQuota);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM mst.master_history WHERE property_id = @p AND master = 'leaveTypes' AND code = 'EL' AND field = 'yearlyQuota' AND old_value = '15' AND new_value = '21';", ("@p", h.PropertyId)));
        Assert.Equal(10, (await LeaveOf(h, "CL")).YearlyQuota);   // unchanged types are not touched
        var board = await h.Db.PayrollQueries.GetBoardAsync(h.PropertyId, h.U("owner"), null, null, Ct);
        Assert.Equal((10, 7, 21), (board.LeavePolicy.Cl, board.LeavePolicy.Sl, board.LeavePolicy.El));
    }

    [Fact]
    public async Task Holidays_are_Owner_only_one_per_date_and_a_locked_pay_run_makes_the_date_used()
    {
        var h = await db.NewHotelAsync();
        var r = (await h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "h1"), new SaveHolidayRequest(null, "2026-08-15", "Independence Day", "paid"), Ct)).Value;
        Assert.Equal(("2026-08-15", "FY 2026–27", 0, 10), (r.Code, r.Fy, r.Used, r.SortOrder));
        await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "h2"), new SaveHolidayRequest(null, "2026-08-15", "Another", "restricted"), Ct), HccErrorCode.VALIDATION, MasterReasons.CodeTaken);
        await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "h3"), new SaveHolidayRequest(null, "2026-13-01", "Nope", "paid"), Ct), HccErrorCode.VALIDATION, MasterReasons.Invalid);
        await db.ScalarAsync("""
            INSERT hr.pay_run(property_id, pay_month, status, rates_json, warnings_json, lock_seq, prepared_by, prepared_utc, locked_by, locked_utc)
            VALUES (@p, '2026-08-01', 'Locked', '{}', '[]', 1, @a, '2026-09-02', @b, '2026-09-02');
            """, ("@p", h.PropertyId), ("@a", h.U("accountant")), ("@b", h.U("owner")));
        var used = (await h.Queries.HolidaysAsync(h.PropertyId, h.U("reception"), ct: Ct)).Items.Single();
        Assert.Equal(1, used.Used);
        await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "h4"), new SaveHolidayRequest(r.HolidayId, "2026-08-16", "Independence Day", "paid", null, r.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        await Hotel.FailsAsync(() => h.Lists.DeleteHolidayAsync(h.As("owner", "DeleteHoliday", "h5"), new DeleteHolidayRequest(r.HolidayId, r.Version), Ct), HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse);
        var retired = (await h.Lists.RetireHolidayAsync(h.As("owner", "RetireHoliday", "h6"), new RetireHolidayRequest(r.HolidayId, r.Version), Ct)).Value;
        Assert.False(retired.Active);
        var kind = (await h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday", "h7"), new SaveHolidayRequest(null, "2026-09-14", "Ganesh Chaturthi", "restricted"), Ct)).Value;
        Assert.Equal(("restricted", 20), (kind.Kind, kind.SortOrder));
        var order = (await h.Lists.ReorderHolidaysAsync(h.As("owner", "ReorderHolidays", "h9"), new ReorderHolidaysRequest([kind.HolidayId, r.HolidayId]), Ct)).Value;
        Assert.Equal(["2026-09-14", "2026-08-15"], order.Items.Select(x => x.Code));
        var fresh = order.Items[0];
        Assert.True((await h.Lists.DeleteHolidayAsync(h.As("owner", "DeleteHoliday", "h8"), new DeleteHolidayRequest(fresh.HolidayId, fresh.Version), Ct)).Value.Deleted);
    }
}
