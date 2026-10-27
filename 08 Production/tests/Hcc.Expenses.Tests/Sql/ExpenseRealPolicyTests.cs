using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Expenses.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Expenses;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>
/// DecideExpense under the REAL access policy (<see cref="SqlAccessPolicy"/> with the 0101 seed: Manager PERM-EXP-002 L 500000 paise), not the
/// stand-in. Wave D found that a Manager could not reject: the limited grant got no amount for a rejection and answered APPROVAL_REQUIRED
/// "Amount needed for a limited permission". The prototype (closing.ts EXPENSE_APPROVE) applies the limit only when approving, so a Manager
/// rejects within and above the limit; approving above it still goes to the Owner.
/// </summary>
[Trait("Category", "Sql")]
public class ExpenseRealPolicyTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Staff(int Owner, int Manager, int Reception, int Cashier, ExpenseCommands Commands);

    private async Task<Staff> StaffAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        async Task<int> UserAsync(string login, string role) => Convert.ToInt32(await db.ScalarAsync($"""
            INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES ({db.PropertyId}, '{login}.{tag}', N'{login} (fictional)', SYSUTCDATETIME());
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, '{role}', '2020-01-01', SYSUTCDATETIME());
            SELECT @u;
            """), CultureInfo.InvariantCulture);
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlExpenseScopeResolver(db.Platform.Connections)]);
        var commands = new ExpenseCommands(db.Platform.Commands, policy, new SqlExpenseStore(db.Platform.Connections), db.Cashier, db.Clock);
        return new Staff(await UserAsync("own", "OWN"), await UserAsync("mgr", "MGR"), await UserAsync("rec", "REC"), await UserAsync("csh", "CSH"), commands);
    }

    private async Task<ExpenseView> SubmitAsync(Staff s, string vendor, long amount)
        => (await s.Commands.SubmitAsync(db.Ctx(s.Reception), new SubmitExpenseRequest(vendor, "Supplies", amount, "UPI", Description: vendor + " " + Guid.NewGuid().ToString("N")[..6]), Ct)).Value.Expense;

    private Task<CommandResult<ExpenseResult>> DecideAsync(Staff s, int who, ExpenseView e, bool approve, string? reason = null)
        => s.Commands.DecideAsync(db.Ctx(who), new DecideExpenseRequest(e.ExpenseId, e.Version, approve, reason), Ct);

    [Fact]
    public async Task A_Manager_rejects_a_pending_expense_within_the_limit_under_the_real_policy()
    {
        var s = await StaffAsync();
        var e = await SubmitAsync(s, "Aqua Water Cans", 300_000);                    // ₹3,000, within the Manager's ₹5,000
        var rejected = (await DecideAsync(s, s.Manager, e, false, "Duplicate bill")).Value.Expense;
        Assert.Equal(("Rejected", s.Manager, "Duplicate bill"), (rejected.Status, rejected.DecidedBy, rejected.Reason));

        var ok = await SubmitAsync(s, "Morning Supplies", 500_000);                  // exactly the limit: the Manager approves
        Assert.Equal("Posted", (await DecideAsync(s, s.Manager, ok, true)).Value.Expense.Status);
    }

    [Fact]
    public async Task Above_the_limit_the_Manager_may_reject_but_approving_goes_to_the_Owner_as_in_the_prototype()
    {
        var s = await StaffAsync();
        var big = await SubmitAsync(s, "Paint Depot", 700_000);                      // ₹7,000, above the Manager's ₹5,000
        var ex = await Fails(HccErrorCode.APPROVAL_REQUIRED, () => DecideAsync(s, s.Manager, big, true));
        Assert.Equal("Above your limit", ex.Message);
        Assert.Equal(500_000, ex.Details!["limit"].GetInt64());
        var rejected = (await DecideAsync(s, s.Manager, big, false, "Not on the budget")).Value.Expense;   // prototype: the limit is for approval only
        Assert.Equal(("Rejected", s.Manager), (rejected.Status, rejected.DecidedBy));

        var other = await SubmitAsync(s, "Sharma Electricals", 900_000);
        Assert.Equal("Posted", (await DecideAsync(s, s.Owner, other, true)).Value.Expense.Status);          // the Owner has no limit
    }

    [Fact]
    public async Task Roles_without_the_approve_permission_still_cannot_reject()
    {
        var s = await StaffAsync();
        var e = await SubmitAsync(s, "Laundry Bags", 20_000);
        await Fails(HccErrorCode.FORBIDDEN, () => DecideAsync(s, s.Cashier, e, false, "x"));
        await Fails(HccErrorCode.FORBIDDEN, () => DecideAsync(s, s.Reception, e, false, "x"));   // and never their own (SOD) either
        await Fails(HccErrorCode.NOT_FOUND, () => s.Commands.DecideAsync(db.Ctx(s.Manager), new DecideExpenseRequest(long.MaxValue, e.Version, true), Ct));
        Assert.Equal("Pending", (await db.Queries.GetAsync(db.PropertyId, Who.Owner, e.ExpenseId, Ct)).Status);
    }
}
