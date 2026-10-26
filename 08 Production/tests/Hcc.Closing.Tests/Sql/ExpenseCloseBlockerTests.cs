using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Application.Invoices;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Expenses;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Stays;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// A03b-09: the server's close preflight has the prototype's gate list. An expense submitted for approval blocks RunBusinessDayClose
/// ("Expenses resolved"), as does a credit-note request awaiting the Owner (an approval in the prototype); deciding them clears the gate.
/// A draft expense is the submitter's own and does not block (the prototype counts only Pending ones).
/// </summary>
[Trait("Category", "Sql")]
public sealed class ExpenseCloseBlockerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D24 = new(2026, 10, 24);

    private static async Task<ExpenseView> SubmitAsync(Hotel h, string vendor, long amount, bool draft = false)
        => (await h.Get<ExpenseCommands>().SubmitAsync(h.Ctx(h.Reception, ExpenseCommands.OpSubmit), new SubmitExpenseRequest(vendor, "Supplies", amount, "UPI", draft), Ct)).Value.Expense;

    // The Manager approves within the limit; the Owner rejects (under the real policy a Manager's limited grant also asks an amount for a
    // rejection: "Amount needed for a limited permission" — reported separately, not changed here).
    private static Task<CommandResult<ExpenseResult>> DecideAsync(Hotel h, ExpenseView e, bool approve)
        => h.Get<ExpenseCommands>().DecideAsync(h.Ctx(approve ? h.Manager : h.Owner, ExpenseCommands.OpDecide), new DecideExpenseRequest(e.ExpenseId, e.Version, approve, approve ? null : "Duplicate bill"), Ct);

    private static async Task<IReadOnlyList<string>> BlockingAsync(Hotel h)
        => (await h.Closing.GetPreflightAsync(h.As(h.Owner), D24, Ct)).Checks.Where(c => c.Blocking).Select(c => c.Id).ToList();

    [Fact]
    public async Task A_pending_expense_blocks_the_close_with_the_prototype_wording_and_approving_it_lets_the_day_close()
    {
        var h = await Hotel.CreateAsync(Ct);
        await using var _ = h;
        await SubmitAsync(h, "Draft Vendor", 12_000, draft: true);             // a draft does not block
        Assert.DoesNotContain("expenses", await BlockingAsync(h));
        var e = await SubmitAsync(h, "Aqua Water Cans", 30_000);

        var pre = await h.Closing.GetPreflightAsync(h.As(h.Owner), D24, Ct);
        var check = pre.Checks.Single(c => c.Id == "expenses");
        Assert.Equal(("Expenses resolved", "Approve or reject pending expenses.", true), (check.Title, check.Hint, check.Blocking));
        Assert.Equal(["Aqua Water Cans · ₹300 · EXP-" + e.ExpenseId], check.Items.Select(i => i.Label));
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(ct: Ct));
        Assert.Equal("Day close 2026-10-24 is blocked: expenses resolved — Approve or reject pending expenses.", ex.Message);
        Assert.Equal(["expenses"], ex.Details!["blockers"].EnumerateArray().Select(b => b.GetProperty("id").GetString()));
        Assert.Equal(("Blocked", "preflight"), ((await h.Closing.GetRunAsync(h.As(h.Owner), D24, Ct)).Status, (await h.Closing.GetRunAsync(h.As(h.Owner), D24, Ct)).CurrentStep));

        Assert.Equal("Posted", (await DecideAsync(h, e, approve: true)).Value.Expense.Status);
        Assert.DoesNotContain("expenses", await BlockingAsync(h));
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
    }

    [Fact]
    public async Task A_rejected_expense_no_longer_blocks_the_close()
    {
        var h = await Hotel.CreateAsync(Ct);
        await using var _ = h;
        var e = await SubmitAsync(h, "Sharma Electricals", 45_000);
        Assert.Contains("expenses", await BlockingAsync(h));
        Assert.Equal("Rejected", (await DecideAsync(h, e, approve: false)).Value.Expense.Status);
        Assert.DoesNotContain("expenses", await BlockingAsync(h));
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
    }

    [Fact]
    public async Task A_credit_note_request_awaiting_the_Owner_blocks_the_close_until_it_is_decided()
    {
        var h = await Hotel.CreateAsync(Ct);
        await using var _ = h;
        await h.ScalarAsync($"UPDATE inv.property SET gstin = '27AAAAA0000A1Z2', address = N'12 Station Road, Latur (fictional)', business_date = '2026-10-23' WHERE property_id = {h.PropertyId};", Ct);
        var (_, stay, folio) = await h.SeedInHouseAsync("103", "2026-10-23", "2026-10-24", ct: Ct);
        await h.Billing.PostRoomNightAsync(h.As(h.Owner), Hotel.Key(), new PostRoomNightRequest(stay, new DateOnly(2026, 10, 23)), Ct);
        await h.ScalarAsync($"UPDATE inv.property SET business_date = '2026-10-24' WHERE property_id = {h.PropertyId};", Ct);
        await h.PayAsync(folio, (await h.BillingQueries.GetFolioAsync(h.As(h.Owner), folio, Ct)).Totals.BalancePaise, "UPI", ct: Ct);
        await h.Stays.CheckoutStayAsync(h.Ctx(h.Reception, "CheckoutStay"), new CheckoutStayRequest(stay, await h.VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stay}", Ct)), Ct);
        var invoices = h.Get<InvoiceService>();
        var invoice = (await invoices.IssueInvoiceAsync(h.Ctx(h.Reception, InvoiceService.OpIssue), new IssueInvoiceRequest("folio", folio), Ct)).Value;
        var req = (await invoices.RequestCreditNoteAsync(h.Ctx(h.Manager, InvoiceService.OpRequestCreditNote),
            new RequestCreditNoteRequest(invoice.InvoiceId, "amount", "Rate agreed lower at checkout", 10_000), Ct)).Value;
        Assert.Equal("PendingApproval", req.Outcome);

        var approvals = (await h.Closing.GetPreflightAsync(h.As(h.Owner), D24, Ct)).Checks.Single(c => c.Id == "approvals");
        Assert.True(approvals.Blocking);
        Assert.Equal([$"Credit-note · ₹100 · invoice {invoice.InvoiceNo} · request {req.RequestId}"], approvals.Items.Select(i => i.Label));
        var ex = await Expect.Fails(HccErrorCode.INVALID_TRANSITION, () => h.CloseAsync(ct: Ct));
        Assert.Equal("Day close 2026-10-24 is blocked: approvals resolved — Approve or reject pending requests.", ex.Message);

        await invoices.DecideCreditNoteAsync(h.Ctx(h.Owner, InvoiceService.OpDecideCreditNote), new DecideCreditNoteRequest(req.RequestId, false, "Rate stays as invoiced", req.RequestVersion), Ct);
        Assert.DoesNotContain("approvals", await BlockingAsync(h));
        Assert.Equal("Completed", (await h.CloseAsync(ct: Ct)).Value.Status);
    }
}
