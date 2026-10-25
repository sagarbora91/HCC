using System.Data;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Expenses.Tests.Harness;
using Microsoft.Data.SqlClient;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>Expense commands on SQL Server (WF-EXP-001; PERM-EXP-001/002/003, PERM-SOD-001, DEC-052 shared drawer, DEC-063 G4/G5).</summary>
[Trait("Category", "Sql")]
public class ExpenseCommandTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Draft_submit_approve_reverse_counts_the_drawer_exactly_once()
    {
        var (till, shift) = await db.OpenTillAsync(500_000);
        var baseline = await db.SubmitAsync(Who.Manager, "Fresh Linen Services", 180_000, "Cash", "Laundry");
        await db.ApproveAsync(Who.Owner, baseline, till);
        var d0 = await db.ExpectedCashAsync(shift.ShiftId);
        Assert.Equal(320_000, d0);

        var draft = (await db.Commands.SubmitAsync(db.Ctx(Who.Cashier), new SubmitExpenseRequest("Aqua Water Cans", "Supplies", 30_000, "Cash", true, "20 cans", "aqua-0001.jpg"), Ct)).Value.Expense;
        Assert.Equal(("Draft", "aqua-0001.jpg", "20 cans"), (draft.Status, draft.Receipt, draft.Notes));
        Assert.Equal(d0, await db.ExpectedCashAsync(shift.ShiftId));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.SubmitDraftAsync(db.Ctx(Who.Reception), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct)); // not own draft
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.SubmitDraftAsync(db.Ctx(Who.Manager), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct)); // even a manager: own drafts only
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(draft.ExpenseId, draft.Version, true, null, till), Ct)); // a draft is not approvable

        var pending = (await db.Commands.SubmitDraftAsync(db.Ctx(Who.Cashier), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct)).Value.Expense;
        Assert.Equal("Pending", pending.Status);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Cashier), new DecideExpenseRequest(pending.ExpenseId, pending.Version, true, null, till), Ct)); // no approve right
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(pending.ExpenseId, pending.Version, true, null, null), Ct)); // cash needs a till

        var posted = (await db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(pending.ExpenseId, pending.Version, true, null, till), Ct)).Value;
        Assert.Equal(("Posted", Who.Manager), (posted.Expense.Status, posted.Expense.DecidedBy));
        Assert.NotNull(posted.CashMovementId);
        Assert.Equal(d0 - 30_000, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'expense' AND source_id = @i AND kind = 'expense' AND amount_paise = -30000", ("@i", SqlDbType.BigInt, posted.Expense.ExpenseId)));

        var v = posted.Expense.Version;
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(posted.Expense.ExpenseId, v, " ", till), Ct)); // reason required
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.ReverseAsync(db.Ctx(Who.Cashier), new ReverseExpenseRequest(posted.Expense.ExpenseId, v, "x", till), Ct)); // not the cashier (G5)
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(posted.Expense.ExpenseId, v, "Supplier refunded cans", null), Ct)); // cash back needs a till

        var rev = (await db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(posted.Expense.ExpenseId, v, "Supplier refunded cans", till), Ct)).Value;
        Assert.Equal("Reversed", rev.Original.Status);
        Assert.Equal(("Posted", -30_000L, posted.Expense.ExpenseId, "Supplier refunded cans"), (rev.Reversal.Status, rev.Reversal.AmountPaise, rev.Reversal.ReversalOf, rev.Reversal.Reason));
        Assert.Equal(rev.Reversal.ExpenseId, rev.Original.ReversedBy);
        Assert.Equal("2026-10-24", rev.Reversal.BusinessDate);
        Assert.Equal(d0, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'expense-reversal' AND source_id = @i AND kind = 'expense-reversal' AND amount_paise = 30000", ("@i", SqlDbType.BigInt, rev.Reversal.ExpenseId)));

        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(rev.Original.ExpenseId, rev.Original.Version, "again", till), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(rev.Reversal.ExpenseId, rev.Reversal.Version, "reverse the reversal", till), Ct));

        // Report figures: the reversed pair nets to zero, EXP-101 stays (prototype reportPack 'cash' = 180000). Other tests share the database,
        // so check this day's cash movements instead of the whole-day summary.
        Assert.Equal(180_000, await db.LongAsync("SELECT -SUM(amount_paise) FROM pay.cash_movement WHERE shift_id = @s AND kind IN ('expense','expense-reversal')", ("@s", SqlDbType.BigInt, shift.ShiftId)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.SummaryAsync(db.PropertyId, Who.Reception, "2026-10-24", Ct));
    }

    [Fact]
    public async Task Vendor_credit_has_no_drawer_effect_and_the_manager_limit_sends_big_items_to_the_owner()
    {
        var (_, shift) = await db.OpenTillAsync(100_000);
        var big = await db.SubmitAsync(Who.Reception, "Paint Depot", 600_000, "Vendor credit", "Maintenance");
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(big.ExpenseId, big.Version, true), Ct)); // above 5,000 rupees
        var posted = await db.ApproveAsync(Who.Owner, big);
        Assert.Equal("Posted", posted.Status);
        Assert.Equal(100_000, await db.ExpectedCashAsync(shift.ShiftId));

        var small = await db.SubmitAsync(Who.Reception, "Morning Supplies", 85_000, "UPI");
        Assert.Equal("Posted", (await db.ApproveAsync(Who.Manager, small)).Status);          // within the manager limit, UPI needs no till
        var other = await db.SubmitAsync(Who.Reception, "Late Vendor", 700_000, "UPI");
        var rejected = (await db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(other.ExpenseId, other.Version, false, "Not on the budget"), Ct)).Value.Expense;
        Assert.Equal(("Rejected", "Not on the budget"), (rejected.Status, rejected.Reason));   // rejecting is not limited
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(rejected.ExpenseId, rejected.Version, true), Ct));

        // Duplicate warning (never a block): same vendor, amount and date, rejected items excluded.
        var dup = (await db.Commands.SubmitAsync(db.Ctx(Who.Cashier), new SubmitExpenseRequest("morning supplies", "Supplies", 85_000, "UPI"), Ct)).Value;
        Assert.Equal([small.ExpenseId], dup.DuplicateOf);
        var none = (await db.Commands.SubmitAsync(db.Ctx(Who.Cashier), new SubmitExpenseRequest("Late Vendor", "Supplies", 700_000, "UPI"), Ct)).Value;
        Assert.Empty(none.DuplicateOf);
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest("X", "Supplies", 100, "Bitcoin"), Ct));
    }

    [Fact]
    public async Task Nobody_decides_their_own_expense_and_the_database_agrees()
    {
        var own = await db.SubmitAsync(Who.Owner, "Owner Errand", 10_000, "UPI");
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(own.ExpenseId, own.Version, true), Ct));
        var mgr = await db.SubmitAsync(Who.Manager, "Manager Errand", 10_000, "UPI");
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(mgr.ExpenseId, mgr.Version, false), Ct));
        Assert.Equal("Posted", (await db.ApproveAsync(Who.Accountant, mgr)).Status);      // the Accountant decides without a limit

        await Assert.ThrowsAsync<SqlException>(() => db.ExecAsync("UPDATE ops.expense SET status = 'Posted', decided_by = submitted_by, decided_utc = SYSUTCDATETIME() WHERE expense_id = @i", ("@i", SqlDbType.BigInt, own.ExpenseId)));
    }

    [Fact]
    public async Task A_cash_expense_without_an_open_shift_is_refused_and_leaves_nothing_behind()
    {
        var till = (await db.Cashier.CreateTillAsync(db.As(Who.Owner), ExpensesDb.Key(), new Hcc.Contracts.Billing.CreateTillRequest("NOSHIFT", "Closed till"), Ct)).Value;
        var e = await db.SubmitAsync(Who.Reception, "Aqua Water Cans", 30_000, "Cash");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(e.ExpenseId, e.Version, true, null, till.TillId), Ct));
        var after = await db.Queries.GetAsync(db.PropertyId, Who.Manager, e.ExpenseId, Ct);
        Assert.Equal("Pending", after.Status);
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'expense' AND source_id = @i", ("@i", SqlDbType.BigInt, e.ExpenseId)));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ExpenseApproved' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, e.ExpenseId)));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(9_999_999, e.Version, true), Ct));
    }

    [Fact]
    public async Task Approved_and_reversed_expenses_publish_one_outbox_event_each_and_replays_do_not_repeat()
    {
        var (till, shift) = await db.OpenTillAsync(200_000);
        var e = await db.SubmitAsync(Who.Reception, "Event Vendor", 45_000, "Cash", "Utilities");
        var key = ExpensesDb.Key();
        var req = new DecideExpenseRequest(e.ExpenseId, e.Version, true, null, till);
        var first = await db.Commands.DecideAsync(db.Ctx(Who.Manager, key), req, Ct);
        var again = await db.Commands.DecideAsync(db.Ctx(Who.Manager, key), req, Ct);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.CashMovementId, again.Value.CashMovementId);
        Assert.Equal(155_000, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ExpenseApproved' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, e.ExpenseId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'EXPENSE_POSTED' AND target = @t", ("@t", SqlDbType.VarChar, "expense:" + e.ExpenseId)));

        using var approved = JsonDocument.Parse((string)(await db.ScalarAsync("SELECT payload FROM plat.outbox WHERE event_type = 'ExpenseApproved' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, e.ExpenseId)))!);
        var p = approved.RootElement;
        Assert.Equal((45_000L, "Cash", "Utilities", "2026-10-24", Who.Reception, Who.Manager), (p.GetProperty("amountPaise").GetInt64(), p.GetProperty("method").GetString(),
            p.GetProperty("category").GetString(), p.GetProperty("businessDate").GetString(), p.GetProperty("submittedBy").GetInt32(), p.GetProperty("approvedBy").GetInt32()));

        var rev = (await db.Commands.ReverseAsync(db.Ctx(Who.Accountant), new ReverseExpenseRequest(e.ExpenseId, first.Value.Expense.Version, "Billed twice", till), Ct)).Value;
        using var reversed = JsonDocument.Parse((string)(await db.ScalarAsync("SELECT payload FROM plat.outbox WHERE event_type = 'ExpenseReversed' AND aggregate_id = @i", ("@i", SqlDbType.BigInt, rev.Reversal.ExpenseId)))!);
        var r = reversed.RootElement;
        Assert.Equal((e.ExpenseId, 45_000L, -45_000L, "Billed twice"), (r.GetProperty("originalExpenseId").GetInt64(), r.GetProperty("amountPaise").GetInt64(),
            r.GetProperty("reversalAmountPaise").GetInt64(), r.GetProperty("reason").GetString()));
        Assert.Equal(200_000, await db.ExpectedCashAsync(shift.ShiftId));
    }

    [Fact]
    public async Task Stale_versions_are_refused()
    {
        var draft = await db.SubmitAsync(Who.Reception, "Stale Vendor", 5_000, "UPI", draft: true);
        var sent = (await db.Commands.SubmitDraftAsync(db.Ctx(Who.Reception), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct)).Value.Expense;
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.SubmitDraftAsync(db.Ctx(Who.Reception), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(sent.ExpenseId, draft.Version, true), Ct)); // old token
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(sent.ExpenseId, "not-a-token", true), Ct));
        var posted = await db.ApproveAsync(Who.Manager, sent);
        await Fails(HccErrorCode.STALE_VERSION, () => db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(sent.ExpenseId, sent.Version, "old", null), Ct));
        Assert.Equal("Posted", posted.Status);
    }

    [Fact]
    public async Task Two_simultaneous_reversals_reverse_once()
    {
        var (till, shift) = await db.OpenTillAsync(100_000);
        var e = await db.ApproveAsync(Who.Manager, await db.SubmitAsync(Who.Reception, "Race Vendor", 20_000, "Cash"), till);
        Assert.Equal(80_000, await db.ExpectedCashAsync(shift.ShiftId));
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await db.Commands.ReverseAsync(db.Ctx(Who.Manager), new ReverseExpenseRequest(e.ExpenseId, e.Version, "race", till), Ct);
                return (HccErrorCode?)null;
            }
            catch (Hcc.Domain.Errors.HccException ex)
            {
                return ex.Code;
            }
        }).ToList();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Single(results, r => r is HccErrorCode.INVALID_TRANSITION or HccErrorCode.STALE_VERSION);
        Assert.Equal(100_000, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM ops.expense WHERE reversal_of = @i", ("@i", SqlDbType.BigInt, e.ExpenseId)));
    }

    [Fact]
    public async Task Submitters_see_only_their_own_expenses_approvers_see_all_and_other_properties_are_invisible()
    {
        var mine = await db.SubmitAsync(Who.Reception, "Mine Vendor", 1_100, "UPI");
        var theirs = await db.SubmitAsync(Who.Cashier, "Theirs Vendor", 2_200, "UPI");

        var own = await db.Queries.ListAsync(db.PropertyId, Who.Reception, "Pending", null, null, 1, 100, Ct);
        Assert.True(own.OwnOnly);
        Assert.Contains(own.Items, i => i.ExpenseId == mine.ExpenseId);
        Assert.DoesNotContain(own.Items, i => i.ExpenseId == theirs.ExpenseId);
        await Fails(HccErrorCode.NOT_FOUND, () => db.Queries.GetAsync(db.PropertyId, Who.Reception, theirs.ExpenseId, Ct));
        Assert.Equal(theirs.ExpenseId, (await db.Queries.GetAsync(db.PropertyId, Who.Accountant, theirs.ExpenseId, Ct)).ExpenseId);

        var all = await db.Queries.ListAsync(db.PropertyId, Who.Manager, "Pending", "2026-10-24", "2026-10-24", 1, 100, Ct);
        Assert.False(all.OwnOnly);
        Assert.Contains(all.Items, i => i.ExpenseId == mine.ExpenseId);
        Assert.Contains(all.Items, i => i.ExpenseId == theirs.ExpenseId);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.ListAsync(db.PropertyId, Who.Housekeeper, null, null, null, 1, 50, Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Queries.ListAsync(db.PropertyId, Who.Manager, "Bogus", null, null, 1, 50, Ct));

        await Fails(HccErrorCode.NOT_FOUND, () => db.Commands.DecideAsync(db.Ctx(Who.Owner, property: db.OtherPropertyId), new DecideExpenseRequest(mine.ExpenseId, mine.Version, false), Ct));
        Assert.Equal(["Cash", "UPI", "Bank transfer", "Vendor credit"], (await db.Queries.OptionsAsync(db.PropertyId, Who.Reception, Ct)).Methods);
    }
}
