using System.Data;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Expenses.Tests.Harness;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>
/// DEC-120 · SCC expense features on SQL Server with HCC's approval rules kept: description, optional vendor (auto-added; a retired one refused),
/// chosen date, GST on the bill, bill photo, SCC's duplicate confirm, edit with a change log, void with SCC's six reasons, petty cash,
/// budgets, recurring templates, the Udhaar register, the SCC CSVs — and the server gaps the SCC review found (category from the master,
/// "needs a bill", the category's approval limit, retired vendors, ₹10 crore).
/// </summary>
[Trait("Category", "Sql")]
public class ExpenseSccCommandTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>The property's money masters as first-time setup leaves them (mst.usp_seed_money_masters), plus a test category.</summary>
    private async Task<string> CategoryAsync(bool needsBill = false, long? limit = null, bool active = true)
    {
        await db.ExecAsync("EXEC mst.usp_seed_money_masters @p", ("@p", SqlDbType.Int, db.PropertyId));
        var tag = Tag().ToUpperInvariant();
        var name = "Test head " + tag;
        await db.ExecAsync($"""
            INSERT ops.expense_category(property_id, code, name, sort_order, active, retired_on, ledger, needs_bill, approval_limit_paise, updated_by, updated_utc)
            VALUES (@p, 'T-{tag}', @n, 900, {(active ? 1 : 0)}, {(active ? "NULL" : "'2026-10-01'")}, '5000', {(needsBill ? 1 : 0)}, {(limit is null ? "NULL" : limit.ToString())}, 1, SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name));
        return name;
    }

    private async Task<ExpenseResult> Submit(int who, SubmitExpenseRequest r) => (await db.Commands.SubmitAsync(db.Ctx(who), r, Ct)).Value;

    private static SubmitExpenseRequest Req(string description, long amount, string method = "UPI", string category = "Supplies", string? vendor = null) =>
        new(vendor, category, amount, method, Description: description);

    [Fact]
    public async Task Description_is_required_vendor_is_optional_a_new_vendor_joins_the_list_and_a_retired_one_is_refused()
    {
        var ex = await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest("X", "Supplies", 100, "UPI"), Ct));
        Assert.Equal("Description is required", ex.Message);
        var noVendor = (await Submit(Who.Reception, Req("Tea for staff " + Tag(), 100))).Expense;
        Assert.Equal((string.Empty, "Pending"), (noVendor.Vendor, noVendor.Status));

        var name = "Brand New Electricals " + Tag();
        await Submit(Who.Reception, Req("Bulbs " + Tag(), 900, vendor: name));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM ops.expense_vendor WHERE property_id = @p AND name = @n AND active = 1",
            ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM mst.master_history h JOIN ops.expense_vendor v ON v.expense_vendor_id = h.row_id WHERE h.master = 'expenseVendors' AND v.name = @n",
            ("@n", SqlDbType.NVarChar, name)));
        var again = (await Submit(Who.Reception, Req("Bulbs again " + Tag(), 901, vendor: name.ToUpperInvariant()))).Expense;
        Assert.Equal(name, again.Vendor);                                                           // stored as the list name, not added twice
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM ops.expense_vendor WHERE property_id = @p AND name = @n", ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name)));

        await db.ExecAsync("UPDATE ops.expense_vendor SET active = 0, retired_on = '2026-10-01' WHERE property_id = @p AND name = @n", ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name));
        var retired = await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("x " + Tag(), 100, vendor: name), Ct));
        Assert.Equal("MASTER_RETIRED", retired.Details!["reason"].GetString());

        var draft = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest("Draft shop", "Supplies", 100, "UPI", true), Ct)).Value.Expense;
        Assert.Equal("Description is required — add it with Edit before submitting.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitDraftAsync(db.Ctx(Who.Reception), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct))).Message);
    }

    [Fact]
    public async Task Server_gaps_category_from_the_master_needs_bill_category_limit_and_ten_crore()
    {
        // Unknown category text is refused (it used to be stored as typed); a retired one answers MASTER_RETIRED.
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("Gas " + Tag(), 100, category: "Kitchen gas"), Ct));
        var retired = await CategoryAsync(active: false);
        Assert.Equal("MASTER_RETIRED", (await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("x " + Tag(), 100, category: retired), Ct)))
            .Details!["reason"].GetString());

        // "Needs a bill": the bill number is required once the expense goes for approval (a draft may wait).
        var bill = await CategoryAsync(needsBill: true);
        Assert.Equal($"{bill} expenses need the bill / receipt number.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("Water board " + Tag(), 100, category: bill), Ct))).Message);
        var draft = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest(null, bill, 100, "UPI", true, Description: "Water " + Tag()), Ct)).Value.Expense;
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitDraftAsync(db.Ctx(Who.Reception), new SubmitExpenseDraftRequest(draft.ExpenseId, draft.Version), Ct));
        var ok = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest(null, bill, 100, "UPI", Receipt: "WB-1", Description: "Water " + Tag()), Ct)).Value.Expense;
        Assert.Equal(("Pending", "WB-1"), (ok.Status, ok.Receipt));

        // The category's approval limit replaces the Manager's ₹5,000 (prototype mnyApprovalLimit): ₹1,000 here.
        var petty = await CategoryAsync(limit: 100_000);
        var big = (await Submit(Who.Reception, Req("Tea " + Tag(), 200_000, category: petty))).Expense;
        Assert.Contains("need Owner approval", (await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(big.ExpenseId, big.Version, true), Ct))).Message);
        Assert.Equal("Rejected", (await db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(big.ExpenseId, big.Version, false, "No"), Ct)).Value.Expense.Status);
        var small = (await Submit(Who.Reception, Req("Snacks " + Tag(), 90_000, category: petty))).Expense;
        Assert.Equal("Posted", (await db.ApproveAsync(Who.Manager, small)).Status);
        var owner = (await Submit(Who.Reception, Req("Tea " + Tag(), 300_000, category: petty))).Expense;
        Assert.Equal("Posted", (await db.ApproveAsync(Who.Owner, owner)).Status);

        // ₹10 crore (the server's Paise maximum; the prototype stopped at ₹10 lakh — now both ₹10 crore).
        Assert.Equal(10_000_000_000, (await Submit(Who.Reception, Req("Renovation " + Tag(), 10_000_000_000, "Vendor credit"))).Expense.AmountPaise);
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("Too big " + Tag(), 10_000_000_001, "Vendor credit"), Ct));
    }

    [Fact]
    public async Task Chosen_date_never_after_the_business_date_nor_a_closed_day()
    {
        Assert.Equal("An expense cannot be dated after the business date (2026-10-24).", (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            Req("x " + Tag(), 100) with { ExpenseDate = "2026-10-25" }, Ct))).Message);
        Assert.Contains("The Owner must reopen it first", (await Fails(HccErrorCode.DAY_CLOSED, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            Req("x " + Tag(), 100) with { ExpenseDate = "2026-10-20" }, Ct))).Message);
        Assert.Equal("2026-10-24", (await Submit(Who.Reception, Req("Today " + Tag(), 100) with { ExpenseDate = "2026-10-24" })).Expense.BusinessDate);
    }

    [Fact]
    public async Task Gst_bill_photo_and_duplicates_follow_SCC()
    {
        var gstAsk = await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("Paint " + Tag(), 10_000) with { GstRateBps = 1800, GstPaise = 20_000 }, Ct));
        Assert.Equal("GST amount (₹200) is more than the entry amount (₹100). Save anyway?", gstAsk.Message);
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var withGst = (await Submit(Who.Reception, Req("Cement " + Tag(), 118_000, "UPI", "Maintenance") with
        {
            GstRateBps = 1800, GstPaise = 18_000, BillPhotoBase64 = Convert.ToBase64String(jpeg), BillPhotoContentType = "image/jpeg",
        })).Expense;
        Assert.Equal((1800, 18_000L, true), (withGst.GstRateBps, withGst.GstPaise, withGst.HasBillPhoto));
        var photo = await db.Queries.BillPhotoAsync(db.PropertyId, Who.Manager, withGst.ExpenseId, Ct);
        Assert.Equal(("image/jpeg", Convert.ToBase64String(jpeg)), (photo.ContentType, photo.ImageBase64));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Queries.BillPhotoAsync(db.PropertyId, Who.Cashier, withGst.ExpenseId, Ct)); // not theirs (own-only reader)
        var tooBig = Convert.ToBase64String(new byte[4 * 1024 * 1024 + 1]);
        Assert.Equal("Photo too large (max 4MB)", (await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception),
            Req("x " + Tag(), 100) with { BillPhotoBase64 = tooBig, BillPhotoContentType = "image/jpeg" }, Ct))).Message);
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), Req("x " + Tag(), 100) with { BillPhotoBase64 = "AAAA", BillPhotoContentType = "text/html" }, Ct));

        // SCC duplicate key: same date + amount + description (any case); "Save anyway?" then saved and DuplicateOf lists the first.
        var desc = "Water cans " + Tag();
        var first = (await Submit(Who.Reception, Req(desc, 30_000))).Expense;
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Cashier), Req(desc.ToUpperInvariant(), 30_000, "Cash"), Ct));
        var second = await Submit(Who.Cashier, Req(desc.ToUpperInvariant(), 30_000, "Cash") with { ConfirmDuplicate = true });
        Assert.Equal([first.ExpenseId], second.DuplicateOf);
    }

    [Fact]
    public async Task Edit_logs_old_and_new_with_a_reason_an_editor_never_approves_and_an_approved_expense_is_locked()
    {
        var e = (await Submit(Who.Reception, Req("Morning supplies " + Tag(), 85_000))).Expense;
        Assert.Equal("Reason required", (await Fails(HccErrorCode.VALIDATION, () => db.Commands.EditAsync(db.Ctx(Who.Manager),
            new EditExpenseRequest(e.ExpenseId, e.Version, 90_000, "Supplies", "UPI", "x", " "), Ct))).Message);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.EditAsync(db.Ctx(Who.Cashier), new EditExpenseRequest(e.ExpenseId, e.Version, 90_000, "Supplies", "UPI", "x", "r"), Ct));
        var jpeg = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 9 });
        var edited = (await db.Commands.EditAsync(db.Ctx(Who.Manager), new EditExpenseRequest(e.ExpenseId, e.Version, 90_000, "Supplies", "UPI", "Morning supplies (corrected)",
            "Bill says ₹900", 500, 4_286, null, jpeg, "image/jpeg"), Ct)).Value.Expense;
        Assert.Equal((90_000L, "Morning supplies (corrected)", 4_286L, true), (edited.AmountPaise, edited.Description, edited.GstPaise, edited.HasBillPhoto));
        var read = await db.Queries.GetAsync(db.PropertyId, Who.Owner, e.ExpenseId, Ct);
        var log = Assert.Single(read.Edits!);
        Assert.Equal((Who.Manager, "Bill says ₹900"), (log.By, log.Reason));
        Assert.Equal((85_000L, 90_000L, "(none)", "(changed)"), (log.Old["amount"].GetInt64(), log.New["amount"].GetInt64(), log.Old["billPhoto"].GetString(), log.New["billPhoto"].GetString()));
        Assert.Contains("You changed this expense", (await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Manager),
            new DecideExpenseRequest(e.ExpenseId, read.Version, true), Ct))).Message);
        var posted = await db.ApproveAsync(Who.Owner, read);
        Assert.Contains("Approved entry is locked", (await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.EditAsync(db.Ctx(Who.Owner),
            new EditExpenseRequest(e.ExpenseId, posted.Version, 1, "Supplies", "UPI", "x", "r"), Ct))).Message);
    }

    [Fact]
    public async Task Void_takes_one_of_SCCs_six_reasons_never_by_the_Cashier_and_a_void_neither_counts_nor_holds_the_close()
    {
        var e = (await Submit(Who.Reception, Req("Pending item " + Tag(), 12_345))).Expense;
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.VoidAsync(db.Ctx(Who.Manager), new VoidExpenseRequest(e.ExpenseId, e.Version, "Because"), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.VoidAsync(db.Ctx(Who.Cashier), new VoidExpenseRequest(e.ExpenseId, e.Version, "Duplicate entry"), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.VoidAsync(db.Ctx(Who.Reception), new VoidExpenseRequest(e.ExpenseId, e.Version, "Duplicate entry"), Ct)); // own pending: a decider voids it
        var v = (await db.Commands.VoidAsync(db.Ctx(Who.Accountant), new VoidExpenseRequest(e.ExpenseId, e.Version, "Wrong payment mode", "Paid by the owner"), Ct)).Value.Expense;
        Assert.Equal(("Void", "Wrong payment mode — Paid by the owner", Who.Accountant), (v.Status, v.VoidReason, v.VoidedBy));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM ops.expense WHERE expense_id = @i AND status = 'Pending'", ("@i", SqlDbType.BigInt, e.ExpenseId)));
        var voids = await db.Queries.ListAsync(db.PropertyId, Who.Owner, "Void", null, null, 1, 100, Ct);
        Assert.Contains(voids.Items, i => i.ExpenseId == e.ExpenseId);                            // kept for audit, listed as Void (never Posted / Pending)

        var draft = (await db.Commands.SubmitAsync(db.Ctx(Who.Cashier), new SubmitExpenseRequest(null, "Supplies", 100, "Cash", true), Ct)).Value.Expense;
        Assert.Equal("Void", (await db.Commands.VoidAsync(db.Ctx(Who.Cashier), new VoidExpenseRequest(draft.ExpenseId, draft.Version, "Other"), Ct)).Value.Expense.Status); // own draft
        var (till, _) = await db.OpenTillAsync(50_000);
        var posted = await db.ApproveAsync(Who.Owner, (await Submit(Who.Reception, Req("Posted item " + Tag(), 1_000, "Cash"))).Expense, till);
        Assert.Contains("reverse it instead", (await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Commands.VoidAsync(db.Ctx(Who.Owner),
            new VoidExpenseRequest(posted.ExpenseId, posted.Version, "Duplicate entry"), Ct))).Message);
    }

    [Fact]
    public async Task Petty_cash_top_ups_are_approved_by_a_second_person_and_disbursements_are_drawer_cash_expenses_for_approval()
    {
        var top = (await db.Registers.RequestPettyFloatAsync(db.Ctx(Who.Cashier), new RequestPettyFloatRequest(1_000_000, "Monthly float"), Ct)).Value;
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.DecidePettyFloatAsync(db.Ctx(Who.Cashier), new DecidePettyFloatRequest(top.Id!.Value, true), Ct));
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Registers.DecidePettyFloatAsync(db.Ctx(Who.Manager), new DecidePettyFloatRequest(top.Id!.Value, true), Ct)); // above ₹5,000
        await db.Registers.DecidePettyFloatAsync(db.Ctx(Who.Owner), new DecidePettyFloatRequest(top.Id!.Value, true), Ct);
        var own = (await db.Registers.RequestPettyFloatAsync(db.Ctx(Who.Manager), new RequestPettyFloatRequest(1_000), Ct)).Value;
        Assert.Contains("your own", (await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.DecidePettyFloatAsync(db.Ctx(Who.Manager), new DecidePettyFloatRequest(own.Id!.Value, true), Ct))).Message);
        var reg = await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct);
        Assert.Contains(reg.Petty, p => p.EntryId == top.Id && p.Status == "Approved" && p.DecidedBy == Who.Owner);

        var (till, shift) = await db.OpenTillAsync(900_000);
        var d = (await db.Registers.PettyDisbursementAsync(db.Ctx(Who.Cashier), new RecordPettyDisbursementRequest(860_000, "Petty cash", "Tea and snacks"), Ct)).Value;
        var e = await db.Queries.GetAsync(db.PropertyId, Who.Owner, d.Id!.Value, Ct);
        Assert.Equal(("Pending", "Cash", "Petty: Tea and snacks", "petty"), (e.Status, e.Method, e.Description, e.Source));
        Assert.Equal(900_000, await db.ExpectedCashAsync(shift.ShiftId));                     // nothing leaves the drawer before approval
        await db.ApproveAsync(Who.Owner, e, till);
        Assert.Equal(40_000, await db.ExpectedCashAsync(shift.ShiftId));
    }

    [Fact]
    public async Task Budgets_recurring_templates_and_the_budget_alert()
    {
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.SetBudgetAsync(db.Ctx(Who.Cashier), new SetExpenseBudgetRequest("Supplies", 100), Ct));
        var cat = await CategoryAsync();
        await db.Registers.SetBudgetAsync(db.Ctx(Who.Manager), new SetExpenseBudgetRequest(cat, 100_000), Ct);
        var alert = await Submit(Who.Reception, Req("Soap " + Tag(), 95_000, category: cat));
        Assert.Equal($"{cat} would be 95% of Oct budget once approved", alert.BudgetAlert);
        var approved = (await db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(alert.Expense.ExpenseId, alert.Expense.Version, true), Ct)).Value;
        Assert.Equal($"{cat} now 95% of Oct budget", approved.BudgetAlert);

        // Copy last month: the heads with a budget in September and none in October.
        await db.ExecAsync("INSERT ops.expense_budget(property_id, budget_month, category, amount_paise, updated_by, updated_utc) VALUES (@p, '2026-09', N'Laundry', 70000, 1, SYSUTCDATETIME()), (@p, '2026-09', @c, 5, 1, SYSUTCDATETIME())",
            ("@p", SqlDbType.Int, db.PropertyId), ("@c", SqlDbType.NVarChar, cat));
        Assert.Equal("2026-09 → 2026-10 · 1 head", (await db.Registers.CopyBudgetsAsync(db.Ctx(Who.Accountant), new CopyExpenseBudgetsRequest(), Ct)).Value.Message);
        Assert.Equal("Every head already has a budget for 2026-10", (await Fails(HccErrorCode.VALIDATION, () => db.Registers.CopyBudgetsAsync(db.Ctx(Who.Accountant), new CopyExpenseBudgetsRequest(), Ct))).Message);
        var reg = await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Cashier, Ct);
        Assert.Contains(reg.Budgets, b => b.Month == "2026-10" && b.Category == "Laundry" && b.AmountPaise == 70_000);

        // Recurring: Manager / Accountant / Owner keep templates; posting raises a DRAFT for approval; a second post needs "Again".
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.SaveRecurringAsync(db.Ctx(Who.Cashier), new SaveRecurringExpenseRequest(null, "Rent", 100, 1, "Utilities", "Bank transfer"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Registers.SaveRecurringAsync(db.Ctx(Who.Manager), new SaveRecurringExpenseRequest(null, "Rent", 100, 32, "Utilities", "Bank transfer"), Ct));
        var name = "Shop rent " + Tag();
        var t = (await db.Registers.SaveRecurringAsync(db.Ctx(Who.Manager), new SaveRecurringExpenseRequest(null, name, 2_500_000, 31, "Utilities", "Bank transfer", "Landlord " + Tag()), Ct)).Value;
        Assert.Contains("already exists. Add another?", (await Fails(HccErrorCode.VALIDATION, () => db.Registers.SaveRecurringAsync(db.Ctx(Who.Manager),
            new SaveRecurringExpenseRequest(null, name.ToUpperInvariant(), 1, 1, "Utilities", "UPI"), Ct))).Message);
        var post = (await db.Registers.PostRecurringAsync(db.Ctx(Who.Cashier), new PostRecurringExpenseRequest((int)t.Id!.Value, 2_400_000), Ct)).Value;
        var draft = await db.Queries.GetAsync(db.PropertyId, Who.Cashier, post.Id!.Value, Ct);
        Assert.Equal(("Draft", $"{name} — 2026-10", "recurring", $"rec:{t.Id}:2026-10", 2_400_000L), (draft.Status, draft.Description, draft.Source, draft.SourceRef, draft.AmountPaise));
        Assert.Equal("Already posted for 2026-10 (1 entry · ₹24,000). Confirming will post ANOTHER entry for this month.",
            (await Fails(HccErrorCode.VALIDATION, () => db.Registers.PostRecurringAsync(db.Ctx(Who.Cashier), new PostRecurringExpenseRequest((int)t.Id!.Value, 2_400_000), Ct))).Message);
        await db.Registers.PostRecurringAsync(db.Ctx(Who.Cashier), new PostRecurringExpenseRequest((int)t.Id!.Value, 100, Again: true), Ct);
        await db.Registers.ToggleRecurringAsync(db.Ctx(Who.Accountant), new RecurringExpenseRequest((int)t.Id!.Value), Ct);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.PostRecurringAsync(db.Ctx(Who.Cashier), new PostRecurringExpenseRequest((int)t.Id!.Value, 100, Again: true), Ct));
        await db.Registers.DeleteRecurringAsync(db.Ctx(Who.Owner), new RecurringExpenseRequest((int)t.Id!.Value), Ct);
        Assert.DoesNotContain((await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct)).Recurring, r => r.TemplateId == t.Id);
        Assert.Equal(2, await db.CountAsync("SELECT COUNT(*) FROM ops.expense WHERE source_ref = @r", ("@r", SqlDbType.VarChar, $"rec:{t.Id}:2026-10"))); // posted entries are kept
    }

    [Fact]
    public async Task Udhaar_never_touches_cash_caps_overpayment_above_fifty_paise_and_only_a_decider_writes_off()
    {
        var (_, shift) = await db.OpenTillAsync(10_000);
        var add = (await db.Registers.AddUdhaarAsync(db.Ctx(Who.Cashier), new AddUdhaarRequest("Ramesh (fictional)", 250_000, "+91 98765 43210", null, "Banquet snacks"), Ct)).Value;
        await db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(add.Id!.Value, 100_000, "Cash"), Ct);
        await db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(add.Id!.Value, 150_050, "UPI"), Ct);   // ₹0.50 over: taken as typed
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Registers.PayUdhaarAsync(db.Ctx(Who.Cashier), new PayUdhaarRequest(add.Id!.Value, 100, "Cash"), Ct));
        var two = (await db.Registers.AddUdhaarAsync(db.Ctx(Who.Reception), new AddUdhaarRequest("Suresh (fictional)", 100_000), Ct)).Value;
        Assert.Contains("capped", (await db.Registers.PayUdhaarAsync(db.Ctx(Who.Reception), new PayUdhaarRequest(two.Id!.Value, 100_051, "Cash"), Ct)).Value.Message);
        await Fails(HccErrorCode.VALIDATION, () => db.Registers.AddUdhaarAsync(db.Ctx(Who.Cashier), new AddUdhaarRequest(" ", 100), Ct));
        var three = (await db.Registers.AddUdhaarAsync(db.Ctx(Who.Cashier), new AddUdhaarRequest("Mohan (fictional)", 50_000), Ct)).Value;
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.VoidUdhaarAsync(db.Ctx(Who.Cashier), new VoidUdhaarRequest(three.Id!.Value), Ct));
        await db.Registers.VoidUdhaarAsync(db.Ctx(Who.Manager), new VoidUdhaarRequest(three.Id!.Value, "Written off"), Ct);
        var reg = await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct);
        var r = Assert.Single(reg.Udhaar, u => u.ReceivableId == add.Id);
        Assert.Equal(("9876543210", "2026-10-24", "2026-11-23", 250_050L), (r.Mobile, r.CreditDate, r.DueDate, r.Settlements.Sum(s => s.AmountPaise)));
        Assert.Equal(100_000, Assert.Single(reg.Udhaar, u => u.ReceivableId == two.Id).Settlements.Sum(s => s.AmountPaise));
        Assert.True(Assert.Single(reg.Udhaar, u => u.ReceivableId == three.Id).Void);
        Assert.Equal(10_000, await db.ExpectedCashAsync(shift.ShiftId));                       // never posts to cash or income
    }

    [Fact]
    public async Task The_ledger_and_month_CSVs_carry_SCCs_columns_and_only_deciders_export_them()
    {
        var e = (await Submit(Who.Reception, Req("Cement, \"grade 53\" " + Tag(), 118_050, "UPI", "Maintenance", "=Hardware") with { GstRateBps = 1800, GstPaise = 18_008 })).Expense;
        await db.ApproveAsync(Who.Owner, e);
        await Fails(HccErrorCode.FORBIDDEN, () => db.RegisterQueries.LedgerCsvAsync(db.PropertyId, Who.Cashier, Ct));
        var l = await db.RegisterQueries.LedgerCsvAsync(db.PropertyId, Who.Manager, Ct);
        Assert.Equal("ledger_2026-10-24.csv", l.FileName);
        var lines = l.Csv.Split('\n');
        Assert.Equal("Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,Void,VoidReason,CreatedBy,Store,GSTAmt,GSTRate%,Ref,Status", lines[0]);
        Assert.Contains(lines, x => x.StartsWith("2026-10-24,HCC Demo Residency,expense,Maintenance,UPI,'=Hardware,\"Cement, \"\"grade 53\"\" ", StringComparison.Ordinal)
                                    && x.EndsWith($",1180.5,manual,,,user {Who.Reception},Unassigned,180.08,18,EXP-{e.ExpenseId},Posted", StringComparison.Ordinal));
        var m = await db.RegisterQueries.MonthCsvAsync(db.PropertyId, Who.Accountant, "2026-10", Ct);
        Assert.Equal("expense_month_2026-10.csv", m.FileName);
        Assert.Equal("Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,GSTAmt,GSTRate%,Ref,Status", m.Csv.Split('\n')[0]);
        Assert.DoesNotContain(m.Csv.Split('\n').Skip(1), x => x.EndsWith(",Pending", StringComparison.Ordinal) || x.EndsWith(",Void", StringComparison.Ordinal));
    }
}

/// <summary>DEC-120 · SCC month lock (tax feed) on its own database (a locked October would refuse the other tests' entries).</summary>
[Trait("Category", "Sql")]
public class ExpenseMonthLockTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_locks_a_month_writes_into_it_need_the_Owner_and_a_reason_and_turn_the_feed_stale_until_regenerated()
    {
        var pending = await db.SubmitAsync(Who.Reception, "Before lock", 10_000, "UPI");
        var posted = await db.ApproveAsync(Who.Owner, await db.SubmitAsync(Who.Reception, "Posted before", 20_000, "UPI"));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Registers.LockMonthAsync(db.Ctx(Who.Manager), new LockExpenseMonthRequest("2026-10"), Ct));
        Assert.Equal("Pick the current or a past month — a future month cannot be locked",
            (await Fails(HccErrorCode.VALIDATION, () => db.Registers.LockMonthAsync(db.Ctx(Who.Owner), new LockExpenseMonthRequest("2026-11"), Ct))).Message);
        await db.Registers.LockMonthAsync(db.Ctx(Who.Owner), new LockExpenseMonthRequest("2026-10"), Ct);
        var feed = Assert.Single((await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct)).Months);
        Assert.Equal(("2026-10", 20_000L, 1, false), (feed.Month, feed.ExpensePaise, feed.Count, feed.Stale));

        Assert.Equal("Month 2026-10 is tax-locked — its tax feed was already generated. Owner override required.",
            (await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest(null, "Supplies", 500, "UPI", Description: "Late bill"), Ct))).Message);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(pending.ExpenseId, pending.Version, true), Ct));
        Assert.Equal("Rejected", (await db.Commands.DecideAsync(db.Ctx(Who.Manager), new DecideExpenseRequest(pending.ExpenseId, pending.Version, false, "No"), Ct)).Value.Expense.Status); // posts nothing
        await Fails(HccErrorCode.VALIDATION, () => db.Commands.SubmitAsync(db.Ctx(Who.Owner), new SubmitExpenseRequest(null, "Supplies", 500, "UPI", Description: "Late bill"), Ct));
        var late = (await db.Commands.SubmitAsync(db.Ctx(Who.Owner), new SubmitExpenseRequest(null, "Supplies", 500, "UPI", Description: "Late bill", OverrideReason: "Bill found late"), Ct)).Value.Expense;
        Assert.True(Assert.Single((await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct)).Months).Stale);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'EXPENSE_MONTH_OVERRIDE' AND target = 'expense-month:2026-10'"));
        Assert.Equal("Month is tax-locked — add a specific note for the override",
            (await Fails(HccErrorCode.VALIDATION, () => db.Commands.VoidAsync(db.Ctx(Who.Owner), new VoidExpenseRequest(late.ExpenseId, late.Version, "Other"), Ct))).Message);

        db.Clock.Now = db.Clock.Now.AddMinutes(5);
        await db.Registers.LockMonthAsync(db.Ctx(Who.Owner), new LockExpenseMonthRequest("2026-10"), Ct);
        Assert.False(Assert.Single((await db.RegisterQueries.RegistersAsync(db.PropertyId, Who.Owner, Ct)).Months).Stale);  // regenerated
        Assert.Equal("Posted", posted.Status);
    }
}
