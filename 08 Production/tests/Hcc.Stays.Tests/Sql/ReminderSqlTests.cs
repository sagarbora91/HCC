using Hcc.Application.Access;
using Hcc.Application.Reminders;
using Hcc.Contracts;
using Hcc.Contracts.Reminders;
using Hcc.Infrastructure.SqlServer.Reminders;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>LF B4 desk reminders through SQL (migration 4704; roles from plat.user_role, here a small stand-in assignment).</summary>
[Trait("Category", "Sql")]
public class ReminderSqlTests : IAsyncLifetime
{
    private P3World _w = null!;
    private ReminderCommands _cmd = null!;
    private ReminderQueries _qry = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int HkId;
    private int _rec, _mgr, _acc;

    private sealed class AllowAll : IAccessPolicy
    {
        public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
            principalId == HkId ? throw new Hcc.Domain.Errors.HccException(HccErrorCode.FORBIDDEN, "Access denied") : Task.CompletedTask;

        public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(principalId != HkId);
    }

    public async ValueTask InitializeAsync()
    {
        _w = await P3World.CreateAsync(Ct);
        var store = new SqlReminderStore(_w.Db.Platform.Connections);
        var policy = new AllowAll();
        _cmd = new ReminderCommands(_w.Db.Platform.Commands, policy, store, _w.Clock);
        _qry = new ReminderQueries(policy, store);
        async Task<int> UserAsync(string login, string? role)
        {
            var id = Convert.ToInt32(await _w.Db.ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({_w.PropertyId}, '{login}', N'{login}', '2026-01-01');", Ct), System.Globalization.CultureInfo.InvariantCulture);
            if (role is not null) await _w.Db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", Ct);
            return id;
        }

        _rec = await UserAsync("rmd-rec", "REC");
        _mgr = await UserAsync("rmd-mgr", "MGR");
        _acc = await UserAsync("rmd-acc", "ACC");
        HkId = await UserAsync("rmd-hk", "HKP");
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<Application.Commands.CommandResult<ReminderView>> SaveAsync(int user, string key, SaveReminderRequest r) => _cmd.SaveAsync(_w.Ctx(user, key), r, ct: Ct);

    [Fact]
    public async Task Save_list_done_roll_and_dismiss()
    {
        var a = await SaveAsync(_rec, "r1", new SaveReminderRequest(null, "Fire extinguisher refill", "2026-10-28"));
        Assert.Equal(("open", "all", 3, "2026-10-25"), (a.Value.Status, a.Value.Role, a.Value.LeadDays, a.Value.ShowFrom));
        Assert.Empty((await _qry.ListAsync(_w.PropertyId, _rec, "today", new DateOnly(2026, 10, 24), Ct)).Items);
        Assert.Single((await _qry.ListAsync(_w.PropertyId, _rec, "today", new DateOnly(2026, 10, 25), Ct)).Items);
        Assert.Single((await _qry.ListAsync(_w.PropertyId, _rec, "upcoming", new DateOnly(2026, 10, 24), Ct)).Items);
        var m = await SaveAsync(_rec, "r2", new SaveReminderRequest(null, "Monthly pest control", "2026-01-31", Repeat: "monthly"));
        var done = await _cmd.DoneAsync(_w.Ctx(_acc, "d1"), new ReminderIdRequest(m.Value.ReminderId), Ct); // addressed to everyone
        Assert.Equal(("open", "2026-02-28"), (done.Value.Status, done.Value.NextDate));
        var once = await _cmd.DoneAsync(_w.Ctx(_rec, "d2"), new ReminderIdRequest(a.Value.ReminderId), Ct);
        Assert.Equal("done", once.Value.Status);
        await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _cmd.DoneAsync(_w.Ctx(_rec, "d3"), new ReminderIdRequest(a.Value.ReminderId), Ct));
        var x = await SaveAsync(_rec, "r3", new SaveReminderRequest(null, "Not needed", "2026-11-05"));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _cmd.DismissAsync(_w.Ctx(_rec, "x1"), new DismissReminderRequest(x.Value.ReminderId, "x"), Ct));
        var d = await _cmd.DismissAsync(_w.Ctx(_rec, "x2"), new DismissReminderRequest(x.Value.ReminderId, "Vendor already did it"), Ct);
        Assert.Equal("dismissed", d.Value.Status);
        Assert.Contains("Dismissed: Vendor already did it", d.Value.Note);
        Assert.Equal(2, (await _qry.ListAsync(_w.PropertyId, _rec, "done", new DateOnly(2026, 10, 24), Ct)).Items.Count);
    }

    [Fact]
    public async Task Roles_limit_who_can_address_edit_and_act_and_the_permission_gates_everything()
    {
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => SaveAsync(_rec, "g1", new SaveReminderRequest(null, "For the manager", "2026-10-28", Role: "MGR")));
        var forRec = await SaveAsync(_mgr, "g2", new SaveReminderRequest(null, "Reception only", "2026-10-28", Role: "REC", LeadDays: 0));
        Assert.Single((await _qry.ListAsync(_w.PropertyId, _rec, "today", new DateOnly(2026, 10, 28), Ct)).Items);
        Assert.Empty((await _qry.ListAsync(_w.PropertyId, _acc, "today", new DateOnly(2026, 10, 28), Ct)).Items);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _cmd.DoneAsync(_w.Ctx(_acc, "g3"), new ReminderIdRequest(forRec.Value.ReminderId), Ct));
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => SaveAsync(HkId, "g4", new SaveReminderRequest(null, "Nope", "2026-10-28")));
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => SaveAsync(_rec, "g5", new SaveReminderRequest(forRec.Value.ReminderId, "Changed", "2026-10-29")));
        var edited = await SaveAsync(_mgr, "g6", new SaveReminderRequest(forRec.Value.ReminderId, "Changed", "2026-10-29", Role: "REC"));
        Assert.Equal("Changed", edited.Value.Subject);
    }
}
