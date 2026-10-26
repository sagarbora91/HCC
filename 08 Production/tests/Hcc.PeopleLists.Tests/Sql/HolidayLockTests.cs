using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Contracts.PeopleLists;
using Hcc.PeopleLists.Tests.Harness;
using HolidayView = Hcc.Contracts.PeopleLists.HolidayView;

namespace Hcc.PeopleLists.Tests.Sql;

/// <summary>
/// MA-11 review (merge report §6.1): the Holiday calendar master commands refuse a paid holiday in a locked pay month, the same rule as Wave B's
/// SavePaidHoliday / RemoveHoliday (DEC-083). Restricted holidays (no pay effect) and open months are not affected.
/// </summary>
[Trait("Category", "Sql")]
public sealed class HolidayLockTests(PeopleDb db) : IClassFixture<PeopleDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Locked = "September 2026 payroll is locked — its holidays cannot change.";

    private static async Task<HolidayView> SaveAsync(Hotel h, string date, string kind, string name = "Holiday") =>
        (await h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday"), new SaveHolidayRequest(null, date, name + " " + date, kind), Ct)).Value;

    [Fact]
    public async Task Paid_holidays_of_a_locked_pay_month_cannot_change_through_either_holiday_command()
    {
        var h = await db.NewHotelAsync();
        var paid = await SaveAsync(h, "2026-09-15", "paid");
        var restricted = await SaveAsync(h, "2026-09-20", "restricted");
        await h.Db.ScalarAsync("""
            INSERT hr.pay_run(property_id, pay_month, status, rates_json, warnings_json, lock_seq, prepared_by, prepared_utc, locked_by, locked_utc)
            VALUES (@p, '2026-09-01', 'Locked', '{}', '[]', 1, @prep, SYSUTCDATETIME(), @lock, SYSUTCDATETIME());
            """, ("@p", h.PropertyId), ("@prep", h.U("manager")), ("@lock", h.U("owner")));

        Assert.Equal(Locked, (await Hotel.FailsAsync(() => SaveAsync(h, "2026-09-25", "paid"), HccErrorCode.INVALID_TRANSITION)).Message);
        Assert.Equal(Locked, (await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday"),
            new SaveHolidayRequest(paid.HolidayId, paid.Code, "Renamed", "paid", null, paid.Version), Ct), HccErrorCode.INVALID_TRANSITION)).Message);
        await Hotel.FailsAsync(() => h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday"),   // moved out of the locked month (in use: the code cannot change)
            new SaveHolidayRequest(paid.HolidayId, "2026-10-15", paid.Name, "paid", null, paid.Version), Ct), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal(Locked, (await Hotel.FailsAsync(() => h.Lists.RetireHolidayAsync(h.As("owner", "RetireHoliday"),
            new RetireHolidayRequest(paid.HolidayId, paid.Version), Ct), HccErrorCode.INVALID_TRANSITION)).Message);
        await Hotel.FailsAsync(() => h.Lists.DeleteHolidayAsync(h.As("owner", "DeleteHoliday"),   // in use by the locked run: never deleted
            new DeleteHolidayRequest(paid.HolidayId, paid.Version), Ct), HccErrorCode.INVALID_TRANSITION);
        // Wave B's command: the same rule, the same words.
        Assert.Equal(Locked, (await Hotel.FailsAsync(() => h.Db.Payroll.SaveHolidayAsync(h.As("owner", PayrollService.OpSaveHoliday),
            new SavePaidHolidayRequest(new DateOnly(2026, 9, 26), "Extra"), Ct), HccErrorCode.INVALID_TRANSITION)).Message);

        // A restricted holiday has no pay effect; an open month is unaffected.
        Assert.False((await h.Lists.RetireHolidayAsync(h.As("owner", "RetireHoliday"), new RetireHolidayRequest(restricted.HolidayId, restricted.Version), Ct)).Value.Active);
        Assert.Equal("paid", (await SaveAsync(h, "2026-10-02", "paid")).Kind);
        Assert.Equal("2026-09-15", (await h.Queries.HolidaysAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.HolidayId == paid.HolidayId).Code);
    }
}
