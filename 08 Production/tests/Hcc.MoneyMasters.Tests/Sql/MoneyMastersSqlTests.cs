using Hcc.Contracts;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.MoneyMasters.Tests.Harness;

namespace Hcc.MoneyMasters.Tests.Sql;

/// <summary>Money-master commands and queries on a disposable HccTest_* database: migration 2830 + lazy seed, Owner-only changes (PERM-MST-002,
/// DEC-058), idempotency, STALE_VERSION, in-use rules (records resolve by name, code or an earlier name), audit, outbox, history, masking.</summary>
[Trait("Category", "Sql")]
public class MoneyMastersSqlTests : IAsyncLifetime
{
    private MoneyWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MoneyWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private Task<PaymentMethodList> MethodsAsync(string user = "owner") => _w.Queries.PaymentMethodsAsync(_w.PropertyId, _w.Users[user], ct: Ct);

    private async Task<PaymentMethodView> MethodAsync(string code) => (await MethodsAsync()).Items.Single(m => m.Code == code);

    private async Task<BankAccountView> AccountAsync(string code) => (await _w.Queries.BankAccountsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(a => a.Code == code);

    private static SavePaymentMethodRequest Edit(PaymentMethodView m, string? name = null, string? code = null, string? kind = null) =>
        new(m.PaymentMethodId, code ?? m.Code, name ?? m.Name, kind ?? m.Kind, m.SettlesTo, m.NeedsReference, m.UsableAt, m.SurchargeBps ?? 0, m.Ledger, m.SortOrder, m.Version);

    [Fact]
    public async Task First_read_seeds_the_old_lists_and_the_free_text_vendors_once_with_usage_counts()
    {
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM mst.payment_method WHERE property_id = {_w.PropertyId};", Ct)); // property created after the migration
        var methods = await MethodsAsync("housekeeper");
        Assert.Equal(["CASH", "UPI", "CARD", "BANK", "VCREDIT", "COMPANY"], methods.Items.Select(m => m.Code));
        Assert.Equal(["Cash", "UPI", "Card", "Bank transfer", "Vendor credit", "Company credit"], methods.Items.Select(m => m.Name));
        Assert.Equal([("TILL", true), ("BANK-1", true), ("BANK-1", true), ("BANK-1", true), (null, false), (null, false)], methods.Items.Select(m => (m.SettlesTo, m.NeedsReference || m.Kind == "cash")));
        Assert.Equal(["frontdesk", "pos", "expenses"], methods.Items[0].UsableAt);
        Assert.Equal([2, 1, 0, 0, 1, 0], methods.Items.Select(m => m.UsageCount)); // expenses: Cash ×2, UPI, Vendor credit
        var cats = await _w.Queries.ExpenseCategoriesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct);
        Assert.Equal(["Supplies", "Laundry", "Maintenance", "Utilities", "Petty cash"], cats.Items.Select(c => c.Name));
        Assert.Equal([3, 1, 0, 0, 0], cats.Items.Select(c => c.UsageCount));
        Assert.Equal(["5000", "5010", "5020", "5030", "5040"], cats.Items.Select(c => c.Ledger));
        var vendors = await _w.Queries.ExpenseVendorsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct);
        Assert.Equal([("V-0001", "Fresh Linen Services", "laundry", 1), ("V-0002", "Morning Supplies", "supplier", 3)], vendors.Items.Select(v => (v.Code, v.Name, v.Type, v.UsageCount)));
        var accounts = await _w.Queries.BankAccountsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct);
        Assert.Equal([("TILL", "till", 1), ("BANK-1", "bank", 3)], accounts.Items.Select(a => (a.Code, a.Kind, a.UsageCount)));
        // Idempotent: further reads and the procedure itself add nothing.
        await _w.ExecAsync($"EXEC mst.usp_seed_money_masters {_w.PropertyId};", Ct);
        await MethodsAsync();
        Assert.Equal(6, await _w.CountAsync($"SELECT COUNT(*) FROM mst.payment_method WHERE property_id = {_w.PropertyId};", Ct));
        Assert.Equal(2, await _w.CountAsync($"SELECT COUNT(*) FROM ops.expense_vendor WHERE property_id = {_w.PropertyId};", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM mnt.vendor;", Ct)); // DEC-MA-6: maintenance vendors untouched
    }

    [Fact]
    public async Task Money_and_PAN_fields_are_masked_without_PERM_MST_002_and_history_is_Owner_only()
    {
        var owner = await _w.Commands.SaveExpenseVendorAsync(_w.Ctx("owner", "v1"), new SaveExpenseVendorRequest(null, "V-ACME", "Acme Linen", "laundry", "27ABCDE1234F1Z5", "ABCDE1234F", true), Ct);
        Assert.Equal("ABCDE1234F", owner.Value.Pan);
        var rec = (await _w.Queries.ExpenseVendorsAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items.Single(v => v.Code == "V-ACME");
        Assert.Equal((null, "27ABCDE1234F1Z5", true), (rec.Pan, rec.Gstin, rec.Tds));
        await _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "b1"), new SaveBankAccountRequest(null, "B2", "Savings", "bank", "Demo", "9876", "ABCD0123456", 250000), Ct);
        Assert.Null((await _w.Queries.BankAccountsAsync(_w.PropertyId, _w.Users["cashier"], ct: Ct)).Items.Single(a => a.Code == "B2").OpeningBalancePaise);
        Assert.Equal(250000, (await AccountAsync("B2")).OpeningBalancePaise);
        var e = await Assert.ThrowsAsync<HccException>(() => _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["manager"], MoneyMasterIds.ExpenseVendors, ct: Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, e.Code);
        var h = await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], MoneyMasterIds.ExpenseVendors, owner.Value.ExpenseVendorId, ct: Ct);
        Assert.Contains(h.Items, x => x.Field == "pan" && x.NewValue == "ABCDE1234F");
    }

    [Fact]
    public async Task Only_the_Owner_changes_money_masters_with_audit_history_outbox_and_idempotent_replay()
    {
        var req = new SavePaymentMethodRequest(null, "paytm", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk", "pos"], 150, "1010");
        foreach (var u in new[] { "manager", "techadmin", "accountant", "reception", "cashier" })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx(u, "x-" + u), req, Ct))).Code);
        var added = await _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "pm1"), req, Ct);
        Assert.Equal(("PAYTM", "wallet", "BANK-1", 150, 70), (added.Value.Code, added.Value.Kind, added.Value.SettlesTo, added.Value.SurchargeBps, added.Value.SortOrder));
        var again = await _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "pm1"), req, Ct);
        Assert.Equal(added.Value.PaymentMethodId, again.Value.PaymentMethodId);
        Assert.Equal(7, await _w.CountAsync($"SELECT COUNT(*) FROM mst.payment_method WHERE property_id = {_w.PropertyId};", Ct));
        Assert.Equal("Master Payment methods: added PAYTM · Paytm wallet", (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'MST_PAYMENT_METHOD' ORDER BY audit_id DESC;", Ct))!);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MasterChanged';", Ct));
        Assert.Equal(9, await _w.CountAsync($"SELECT COUNT(*) FROM mst.master_history WHERE master = 'paymentMethods' AND row_id = {added.Value.PaymentMethodId};", Ct));
        var cat = await _w.Commands.SaveExpenseCategoryAsync(_w.Ctx("owner", "c1"), new SaveExpenseCategoryRequest(null, "GAS", "Kitchen gas", "5050", true, 100000), Ct);
        Assert.Equal(("GAS", true, 100000L), (cat.Value.Code, cat.Value.NeedsBill, cat.Value.ApprovalLimitPaise!.Value));
    }

    [Fact]
    public async Task Edits_check_version_code_and_kind_lock_and_a_rename_keeps_old_records_resolving()
    {
        var cash = await MethodAsync("CASH");
        var stale = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s1"), Edit(cash, "Cash 2") with { ExpectedVersion = "AAAAAAAAAAA=" }, Ct));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s2"), Edit(cash, code: "CASH2"), Ct))));
        var kind = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s3"), Edit(cash, kind: "upi") with { SettlesTo = "BANK-1" }, Ct));
        Assert.Equal("MASTER_INVALID", Reason(kind));
        Assert.Contains("2 records", kind.Message, StringComparison.Ordinal);
        var renamed = (await _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s4"), Edit(cash, "Cash (drawer)"), Ct)).Value;
        Assert.Equal(("Cash (drawer)", 2), (renamed.Name, renamed.UsageCount)); // ops.expense still says 'Cash'
        Assert.Equal(2, (await MethodAsync("CASH")).UsageCount);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MST_PAYMENT_METHOD' AND detail = N'Master Payment methods: name Cash → Cash (drawer)';", Ct));
        var nothing = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s5"), Edit(renamed), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, nothing.Code);
        var dup = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SavePaymentMethodAsync(_w.Ctx("owner", "s6"), new SavePaymentMethodRequest(null, "upi", "UPI 2", "upi", "BANK-1", true, ["pos"]), Ct));
        Assert.Equal("MASTER_CODE_TAKEN", Reason(dup));
    }

    [Fact]
    public async Task Retire_reactivate_delete_and_reorder_follow_the_in_use_rules()
    {
        // Retiring every other front-desk method leaves Cash as the last one: refused.
        foreach (var code in new[] { "UPI", "CARD", "BANK" })
        {
            var m = await MethodAsync(code);
            await _w.Commands.RetirePaymentMethodAsync(_w.Ctx("owner", "r-" + code), new PaymentMethodIdRequest(m.PaymentMethodId, m.Version), Ct);
        }

        var cash = await MethodAsync("CASH");
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.RetirePaymentMethodAsync(_w.Ctx("owner", "r-cash"), new PaymentMethodIdRequest(cash.PaymentMethodId, cash.Version), Ct))));
        var upi = await MethodAsync("UPI");
        Assert.Equal((false, "2026-10-24"), (upi.Active, upi.RetiredOn));
        var back = (await _w.Commands.ReactivatePaymentMethodAsync(_w.Ctx("owner", "back"), new PaymentMethodIdRequest(upi.PaymentMethodId, upi.Version), Ct)).Value;
        Assert.True(back.Active);
        // The till: CASH settles to it → cannot retire; a used method cannot be deleted; an unused one can.
        var till = await AccountAsync("TILL");
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.RetireBankAccountAsync(_w.Ctx("owner", "r-till"), new BankAccountIdRequest(till.BankAccountId, till.Version), Ct))));
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.DeletePaymentMethodAsync(_w.Ctx("owner", "d-cash"), new PaymentMethodIdRequest(cash.PaymentMethodId, cash.Version), Ct))));
        var company = await MethodAsync("COMPANY");
        Assert.True((await _w.Commands.DeletePaymentMethodAsync(_w.Ctx("owner", "d-co"), new PaymentMethodIdRequest(company.PaymentMethodId, company.Version), Ct)).Value.Deleted);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE master = 'paymentMethods' AND field = 'deleted';", Ct));
        // Reorder the categories: every row once.
        var cats = (await _w.Queries.ExpenseCategoriesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items;
        var order = cats.Select(c => c.ExpenseCategoryId).Reverse().ToList();
        var list = (await _w.Commands.ReorderExpenseCategoriesAsync(_w.Ctx("owner", "o1"), new ReorderExpenseCategoriesRequest(order), Ct)).Value;
        Assert.Equal(["PETTY", "UTIL", "MAINT", "LAUNDRY", "SUPPLIES"], list.Items.Select(c => c.Code));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => _w.Commands.ReorderExpenseCategoriesAsync(_w.Ctx("owner", "o2"), new ReorderExpenseCategoriesRequest(order.Take(2).ToList()), Ct))).Code);
    }

    [Fact]
    public async Task Bank_account_defaults_are_unique_and_the_opening_balance_is_setup_only()
    {
        var dup = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "a1"), new SaveBankAccountRequest(null, "B2", "Savings", "bank", "Demo", null, null, 0, ["upi"]), Ct));
        Assert.Contains("already the default", dup.Message, StringComparison.Ordinal);
        var bank = await AccountAsync("BANK-1");
        var edit = new SaveBankAccountRequest(bank.BankAccountId, bank.Code, bank.Name, bank.Kind, bank.Bank, "4321", "DEMO0001234", 100000, bank.DefaultFor, bank.SortOrder, bank.Version);
        var setup = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "a2"), edit, Ct));
        Assert.Contains("setup-only", setup.Message, StringComparison.Ordinal); // the UPI expense settles through BANK-1
        var ok = (await _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "a3"), edit with { OpeningBalancePaise = 0 }, Ct)).Value;
        Assert.Equal(("4321", "DEMO0001234"), (ok.AccountLast4, ok.Ifsc));
        var fresh = (await _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "a4"), new SaveBankAccountRequest(null, "B3", "Escrow", "bank", "Demo", null, null, 5000), Ct)).Value;
        var fresh2 = (await _w.Commands.SaveBankAccountAsync(_w.Ctx("owner", "a5"), new SaveBankAccountRequest(fresh.BankAccountId, "B3", "Escrow", "bank", "Demo", null, null, 9000, null, null, fresh.Version), Ct)).Value;
        Assert.Equal(9000, fresh2.OpeningBalancePaise);
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"UPDATE mst.payment_method SET settles_to_id = {bank.BankAccountId} WHERE property_id = {_w.PropertyId} AND code = 'VCREDIT';", Ct)); // CK credit
    }

    [Fact]
    public async Task Expense_vendors_retire_and_reactivate_keep_their_code_and_usage()
    {
        var v = (await _w.Queries.ExpenseVendorsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(x => x.Name == "Morning Supplies");
        var r = (await _w.Commands.RetireExpenseVendorAsync(_w.Ctx("owner", "v1"), new ExpenseVendorIdRequest(v.ExpenseVendorId, v.Version), Ct)).Value;
        Assert.Equal((false, 3), (r.Active, r.UsageCount));
        Assert.Contains("still used: 3", (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'MST_EXPENSE_VENDOR' ORDER BY audit_id DESC;", Ct))!, StringComparison.Ordinal);
        Assert.Equal("MASTER_RETIRED", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.SaveExpenseVendorAsync(_w.Ctx("owner", "v2"),
            new SaveExpenseVendorRequest(r.ExpenseVendorId, r.Code, "Morning Supplies Co", "supplier", null, null, false, null, null, null, r.Version), Ct))));
        Assert.Equal("MASTER_CODE_TAKEN", Reason(await Assert.ThrowsAsync<HccException>(() => _w.Commands.SaveExpenseVendorAsync(_w.Ctx("owner", "v3"),
            new SaveExpenseVendorRequest(null, r.Code, "Another", "other"), Ct))));
        var back = (await _w.Commands.ReactivateExpenseVendorAsync(_w.Ctx("owner", "v4"), new ExpenseVendorIdRequest(r.ExpenseVendorId, r.Version), Ct)).Value;
        Assert.True(back.Active);
    }
}
