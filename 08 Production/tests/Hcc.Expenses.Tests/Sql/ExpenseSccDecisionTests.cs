using System.Data;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Expenses;
using Hcc.Expenses.Tests.Harness;
using Microsoft.Data.SqlClient;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>
/// SCC decisions wave · expenses (owner, 27 Oct 2026; Decision Register rows binding) on SQL Server: DEC-129 budgets and recurring templates are
/// Owner-only (PERM-MST-002), posting a due template stays PERM-EXP-001; DEC-130 an Udhaar cash payment is a desk receipt into the open shift's
/// drawer (pay.cash_movement kind payment / source type udhaar; expected cash and the shift count include it) and every desk receipt
/// publishes UdhaarPaymentRecorded for Accounting; DEC-135 the bill's GST carries ITC allowed / disallowed (default disallowed; migration
/// 4190 default for old rows), carried to the posted / reversed events and the CSVs.
/// </summary>
[Trait("Category", "Sql")]
public class ExpenseSccDecisionTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6];

    private async Task<JsonElement> PayloadAsync(string type, long aggregateId)
    {
        var json = (string)(await db.ScalarAsync("SELECT TOP (1) payload FROM plat.outbox WHERE event_type = @t AND aggregate_id = @i ORDER BY outbox_id DESC",
            ("@t", SqlDbType.VarChar, type), ("@i", SqlDbType.BigInt, aggregateId)))!;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── DEC-129 ──

    [Fact]
    public async Task Dec129_budgets_and_recurring_templates_are_Owner_only_and_posting_a_due_template_stays_with_expense_users()
    {
        foreach (var who in new[] { Who.Manager, Who.Accountant, Who.Cashier, Who.Reception })
        {
            Assert.Equal("Only the Owner sets and copies the monthly budgets (money settings, DEC-058).",
                (await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.SetBudgetAsync(db.Ctx(who), new SetExpenseBudgetRequest("Supplies", 100_000), Ct))).Message);
            await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.CopyBudgetsAsync(db.Ctx(who), new CopyExpenseBudgetsRequest(), Ct));
            Assert.StartsWith("Only the Owner keeps the recurring templates",
                (await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.SaveRecurringAsync(db.Ctx(who), new SaveRecurringExpenseRequest(null, "Rent " + Tag(), 100, 1, "Utilities", "Bank transfer"), Ct))).Message,
                StringComparison.Ordinal);
        }

        await db.Registers.SetBudgetAsync(db.Ctx(Who.Owner), new SetExpenseBudgetRequest("Supplies", 120_000), Ct);
        var t = (await db.Registers.SaveRecurringAsync(db.Ctx(Who.Owner), new SaveRecurringExpenseRequest(null, "Generator AMC " + Tag(), 300_000, 1, "Utilities", "Bank transfer"), Ct)).Value;
        var id = (int)t.Id!.Value;
        foreach (var who in new[] { Who.Manager, Who.Accountant })
        {
            await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.ToggleRecurringAsync(db.Ctx(who), new RecurringExpenseRequest(id), Ct));
            await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.DeleteRecurringAsync(db.Ctx(who), new RecurringExpenseRequest(id), Ct));
        }

        // Everyone who could read budgets and templates still does; posting a due template stays with the expense users — a draft for approval.
        var reg = await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Cashier, Ct);
        Assert.Contains(reg.Budgets, b => b.Category == "Supplies" && b.AmountPaise == 120_000);
        Assert.Contains(reg.Recurring, r => r.TemplateId == id);
        var post = (await db.Registers.PostRecurringAsync(db.Ctx(Who.Cashier), new PostRecurringExpenseRequest(id, 300_000), Ct)).Value;
        Assert.Equal("Draft", (await db.Queries.GetAsync(db.PropertyId, Who.Cashier, post.Id!.Value, Ct)).Status);
        await db.Registers.PostRecurringAsync(db.Ctx(Who.Manager), new PostRecurringExpenseRequest(id, 300_000, Again: true), Ct);
        await db.Registers.ToggleRecurringAsync(db.Ctx(Who.Owner), new RecurringExpenseRequest(id), Ct);
        await db.Registers.DeleteRecurringAsync(db.Ctx(Who.Owner), new RecurringExpenseRequest(id), Ct);
    }

    // ── DEC-130 ──

    [Fact]
    public async Task Dec130_udhaar_cash_is_a_desk_receipt_into_the_open_shift_drawer_counted_at_shift_close_and_published_for_Accounting()
    {
        var (till, shift) = await db.OpenTillAsync(50_000);
        var credit = (await db.Registers.AddUdhaarAsync(db.Ctx(Who.Cashier), new AddUdhaarRequest("Ramesh (fictional) " + Tag(), 300_000), Ct)).Value;
        Assert.Equal(50_000, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.StartsWith("Choose the till that receives the cash",
            (await Fails(HccErrorCode.VALIDATION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 100_000, "Cash"), Ct))).Message,
            StringComparison.Ordinal);

        var paid = (await db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 100_000, "Cash", TillId: till), Ct)).Value;
        Assert.EndsWith("paid ₹1,000 · cash into the drawer (desk receipt)", paid.Message, StringComparison.Ordinal);
        Assert.Equal(150_000, await db.ExpectedCashAsync(shift.ShiftId));                      // the cashier's drawer total includes it
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE kind = 'payment' AND source_type = 'udhaar' AND source_id = @s AND amount_paise = 100000 AND shift_id = @sh",
            ("@s", SqlDbType.BigInt, paid.Id!.Value), ("@sh", SqlDbType.BigInt, shift.ShiftId)));
        var e = await PayloadAsync("UdhaarPaymentRecorded", credit.Id!.Value);
        Assert.Equal((paid.Id!.Value, "Cash", 100_000L, "2026-10-24"), (e.GetProperty("settlementId").GetInt64(), e.GetProperty("tender").GetString(),
            e.GetProperty("amountPaise").GetInt64(), e.GetProperty("businessDate").GetString()));
        Assert.Equal(JsonValueKind.Number, e.GetProperty("cashMovementId").ValueKind);

        // Other modes: their own ledger (no drawer), with the transaction reference like any other receipt; Vendor credit is no receipt.
        Assert.Equal("Enter the transaction reference for a non-cash payment.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 10_000, "UPI"), Ct))).Message);
        var upi = (await db.Registers.PayUdhaarAsync(db.Ctx(Who.Accountant), new PayUdhaarRequest(credit.Id!.Value, 10_000, "UPI", "UPI-DEMO-9"), Ct)).Value;
        Assert.Equal(150_000, await db.ExpectedCashAsync(shift.ShiftId));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'udhaar' AND source_id = @s", ("@s", SqlDbType.BigInt, upi.Id!.Value)));
        Assert.Equal("UPI-DEMO-9", (await PayloadAsync("UdhaarPaymentRecorded", credit.Id!.Value)).GetProperty("reference").GetString());
        await Fails(HccErrorCode.VALIDATION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 100, "Vendor credit"), Ct));

        // Cash follows the drawer rules: a desk user (the Accountant holds no PERM-PAY-001) and an open shift on that till.
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Accountant), new PayUdhaarRequest(credit.Id!.Value, 100, "Cash", TillId: till), Ct));
        var empty = (await db.Cashier.CreateTillAsync(db.As(Who.Owner), ExpensesDb.Key(), new CreateTillRequest("T" + Tag(), "Back office"), Ct)).Value;
        Assert.StartsWith("Open cash shift required: Udhaar cash goes into the open shift's drawer.",
            (await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 100, "Cash", TillId: empty.TillId), Ct))).Message,
            StringComparison.Ordinal);

        // The settlement rows: desk receipts with till and reference; the registers view carries them.
        var reg = await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct);
        var s = Assert.Single(reg.Udhaar, u => u.ReceivableId == credit.Id).Settlements;
        Assert.Equal(new (bool, int?, string?)[] { (true, till, null), (true, null, "UPI-DEMO-9") }, s.Select(x => (x.Desk, x.TillId, x.Reference)).ToArray());

        // The shift count (what the day close reads): counted = expected with the Udhaar cash → closes without a difference.
        var open = await db.Billing.GetShiftAsync(db.As(Who.Owner), shift.ShiftId, Ct);
        var counted = (await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), ExpensesDb.Key(), new SubmitShiftCountRequest(shift.ShiftId, 150_000, null, null, null, open.Shift.Version), Ct)).Value;
        Assert.Equal(("Closed", (long?)150_000L, (long?)150_000L), (counted.Status, counted.ExpectedPaise, counted.CountedPaise));
        // A cash payment after the drawer is counted (no open shift) is refused.
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(credit.Id!.Value, 100, "Cash", TillId: till), Ct));
    }

    // ── DEC-135 ──

    [Fact]
    public async Task Dec135_itc_defaults_to_disallowed_is_kept_with_GST_only_and_reaches_the_posted_and_reversed_events_and_the_CSV()
    {
        static SubmitExpenseRequest Bill(string what, long amount, long? gst, string? itc = null, string method = "UPI") =>
            new(null, "Supplies", amount, method, Description: what + " " + Tag(), GstRateBps: gst is null ? null : 1800, GstPaise: gst, Itc: itc);
        var d = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), Bill("Soap", 118_000, 18_000), Ct)).Value.Expense;
        Assert.Equal("disallowed", d.Itc);                                                              // nothing is claimed unless marked
        var a = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), Bill("Bulbs", 59_000, 9_000, "allowed"), Ct)).Value.Expense;
        Assert.Equal("allowed", a.Itc);
        Assert.Null((await db.Commands.SubmitAsync(db.Ctx(Who.Reception), Bill("Tea", 5_000, null, "allowed"), Ct)).Value.Expense.Itc);   // no GST, no flag
        Assert.Equal("Choose ITC allowed or ITC disallowed.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Bill("X", 5_000, 100, "maybe"), Ct))).Message);
        Assert.StartsWith("ITC cannot be allowed on more GST than the bill amount",
            (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Bill("Y", 5_000, 6_000, "allowed") with { ConfirmGst = true }, Ct))).Message,
            StringComparison.Ordinal);

        // Edit while pending: the flag changes (logged) and is kept when the edit does not send it.
        var ed = (await db.Commands.EditAsync(db.Ctx(Who.Reception), new EditExpenseRequest(d.ExpenseId, d.Version, 118_000, "Supplies", "UPI", d.Description, "CA: ITC eligible",
            1800, 18_000, Itc: "allowed"), Ct)).Value.Expense;
        Assert.Equal("allowed", ed.Itc);
        var log = Assert.Single((await db.Queries.GetAsync(db.PropertyId, Who.Owner, d.ExpenseId, Ct)).Edits!);
        Assert.Equal(("disallowed", "allowed"), (log.Old["itc"].GetString(), log.New["itc"].GetString()));
        ed = (await db.Commands.EditAsync(db.Ctx(Who.Reception), new EditExpenseRequest(d.ExpenseId, ed.Version, 118_000, "Supplies", "UPI", d.Description, "Wording",
            1800, 18_000), Ct)).Value.Expense;
        Assert.Equal("allowed", ed.Itc);

        // Approve: the event carries the GST and the flag (Accounting splits it); the reversal record mirrors it with −GST.
        var posted = await db.ApproveAsync(Who.Owner, ed);
        var p = await PayloadAsync("ExpensePosted", posted.ExpenseId);
        Assert.Equal((18_000L, "allowed"), (p.GetProperty("gstPaise").GetInt64(), p.GetProperty("itc").GetString()));
        var pd = await PayloadAsync("ExpensePosted", (await db.ApproveAsync(Who.Owner, a)).ExpenseId);
        Assert.Equal("allowed", pd.GetProperty("itc").GetString());
        var rev = (await db.Commands.ReverseAsync(db.Ctx(Who.Owner), new ReverseExpenseRequest(posted.ExpenseId, posted.Version, "Wrong bill"), Ct)).Value;
        Assert.Equal((-18_000L, "allowed"), (rev.Reversal.GstPaise!.Value, rev.Reversal.Itc));
        var r = await PayloadAsync("ExpenseReversed", rev.Reversal.ExpenseId);
        Assert.Equal((-18_000L, "allowed", -118_000L), (r.GetProperty("gstPaise").GetInt64(), r.GetProperty("itc").GetString(), r.GetProperty("amountPaise").GetInt64()));

        // CSV: HCC's ITC column; the month figures split the GST paid.
        var csv = (await db.RegisterQueries.MonthCsvAsync(db.PropertyId, Who.Owner, "2026-10", Ct)).Csv.Split('\n');
        Assert.EndsWith(",Status,ITC", csv[0], StringComparison.Ordinal);
        Assert.Contains(csv, l => l.EndsWith($",EXP-{a.ExpenseId},Posted,allowed", StringComparison.Ordinal));

        // Migration 4190: old rows default to disallowed, and ITC can never be set on a row without GST.
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM ops.expense WHERE gst_paise IS NULL AND itc_allowed = 1"));
        var sql = await Assert.ThrowsAsync<SqlException>(() => db.ExecAsync("UPDATE ops.expense SET itc_allowed = 1 WHERE gst_paise IS NULL AND property_id = @p",
            ("@p", SqlDbType.Int, db.PropertyId)));
        Assert.Contains("CK_expense_itc", sql.Message, StringComparison.Ordinal);
    }
}

/// <summary>DEC-131 · Owner unlock of a locked expense month, on its own database (a locked October would refuse the other tests' entries).</summary>
[Trait("Category", "Sql")]
public class ExpenseMonthUnlockTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ExpenseMonthLockView> Feed() => db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct).ContinueWith(t => Assert.Single(t.Result.Months), TaskScheduler.Default);

    [Fact]
    public async Task Owner_unlocks_a_locked_month_with_a_reason_writes_need_no_override_the_feed_is_stale_and_locking_again_keeps_the_history()
    {
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.UnlockMonthAsync(db.Ctx(Who.Owner), new UnlockExpenseMonthRequest("2026-10", "CA found a missed bill"), Ct));
        await db.ApproveAsync(Who.Owner, await db.SubmitAsync(Who.Reception, "Before lock", 20_000, "UPI"));
        await db.Registers.LockMonthAsync(db.Ctx(Who.Owner), new LockExpenseMonthRequest("2026-10"), Ct);
        var late = new SubmitExpenseRequest(null, "Supplies", 500, "UPI", Description: "Late bill");
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), late, Ct));

        foreach (var who in new[] { Who.Manager, Who.Accountant, Who.Cashier })
            Assert.Equal("Only the Owner can unlock a tax-locked month.",
                (await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.UnlockMonthAsync(db.Ctx(who), new UnlockExpenseMonthRequest("2026-10", "CA found a missed bill"), Ct))).Message);
        Assert.Equal("A reason is required to unlock a month.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Registers.UnlockMonthAsync(db.Ctx(Who.Owner), new UnlockExpenseMonthRequest("2026-10", " x "), Ct))).Message);

        db.Clock.Now = db.Clock.Now.AddMinutes(5);
        await db.Registers.UnlockMonthAsync(db.Ctx(Who.Owner), new UnlockExpenseMonthRequest("2026-10", "CA found a missed bill"), Ct);
        var f = await Feed();
        Assert.Equal((true, true, Who.Owner, "CA found a missed bill", 1), (f.Unlocked, f.Stale, f.UnlockedBy, f.UnlockReason, f.Unlocks!.Count));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'EXPENSE_MONTH_UNLOCK' AND target = 'expense-month:2026-10'"));
        Assert.StartsWith("Month 2026-10 is already unlocked",
            (await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.UnlockMonthAsync(db.Ctx(Who.Owner), new UnlockExpenseMonthRequest("2026-10", "again please"), Ct))).Message,
            StringComparison.Ordinal);

        // Writes into the month need no override now (and record none).
        var e = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), late, Ct)).Value.Expense;
        Assert.Equal("Pending", e.Status);
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'EXPENSE_MONTH_OVERRIDE'"));
        await db.ApproveAsync(Who.Owner, e);

        // Lock again: the feed is regenerated (fresh, not stale), the unlock history stays, writes need the override again.
        db.Clock.Now = db.Clock.Now.AddMinutes(5);
        await db.Registers.LockMonthAsync(db.Ctx(Who.Owner), new LockExpenseMonthRequest("2026-10"), Ct);
        f = await Feed();
        Assert.Equal((false, false, 20_500L, 1), (f.Unlocked, f.Stale, f.ExpensePaise, f.Unlocks!.Count));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), late with { Description = "Second late bill" }, Ct));
        await db.Registers.UnlockMonthAsync(db.Ctx(Who.Owner), new UnlockExpenseMonthRequest("2026-10", "Second correction"), Ct);
        Assert.Equal(2, (await Feed()).Unlocks!.Count);
        Assert.Equal(2, await db.CountAsync("SELECT COUNT(*) FROM ops.expense_month_unlock WHERE property_id = @p", ("@p", SqlDbType.Int, db.PropertyId)));
    }
}
