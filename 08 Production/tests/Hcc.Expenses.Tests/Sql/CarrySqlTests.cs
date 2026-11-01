using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Errors;
using Hcc.Expenses.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Expenses;
using static Hcc.Expenses.Tests.Harness.Assertions;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>
/// ACC-3 · lane acc3-carry on SQL Server (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7; migrations 4270, 4271): C1 asset put to use / disposal
/// (AssetService; the sale goes through the manual-voucher port), C2 a return against a capital purchase reduces the register cost, C3 the
/// re-allocation of a vendor payment (audit row, ageing follows), C4 the GSTIN / state warning on a vendor payment.
/// </summary>
[Trait("Category", "Sql")]
public class CarrySqlTests(ExpensesDb db) : IClassFixture<ExpensesDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private sealed class Manual : IGstManualVouchers
    {
        public List<GstManualVoucherDraft> Saved { get; } = [];

        public Task<GstManualVoucherRef> SaveAsync(ICommandTransaction tx, Caller caller, GstManualVoucherDraft draft, CancellationToken ct)
        {
            Saved.Add(draft);
            return Task.FromResult(new GstManualVoucherRef((7000 + Saved.Count).ToString(System.Globalization.CultureInfo.InvariantCulture), caller.PrincipalId == Who.Owner ? "approved" : "pending"));
        }

        public Task<int> PendingAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(0);
    }

    private async Task<string> CategoryAsync(string ledger, bool capital = false)
    {
        await db.ExecAsync("EXEC mst.usp_seed_money_masters @p", ("@p", SqlDbType.Int, db.PropertyId));
        var tag = Tag();
        var name = "Head " + tag;
        await db.ExecAsync($"""
            INSERT ops.expense_category(property_id, code, name, sort_order, active, ledger, needs_bill, updated_by, updated_utc, rcm, rcm_bps, capital, itc_blocked)
            VALUES (@p, 'C-{tag}', @n, 900, 1, '{ledger}', 0, 1, SYSUTCDATETIME(), 0, NULL, {(capital ? 1 : 0)}, 0);
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name));
        return name;
    }

    private async Task<(string Code, string Name)> VendorAsync(string? state = "MH", string? gstin = null)
    {
        await db.ExecAsync("EXEC mst.usp_seed_money_masters @p", ("@p", SqlDbType.Int, db.PropertyId));
        var tag = Tag();
        var (code, name) = ("V-" + tag, "Vendor " + tag + " (fictional)");
        await db.ExecAsync($"""
            INSERT ops.expense_vendor(property_id, code, name, sort_order, vendor_type, tds, updated_by, updated_utc, gst_state, gstin)
            VALUES (@p, @c, @n, 900, 'supplier', 0, 1, SYSUTCDATETIME(), {(state is null ? "NULL" : "'" + state + "'")}, {(gstin is null ? "NULL" : "'" + gstin + "'")});
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@c", SqlDbType.VarChar, code), ("@n", SqlDbType.NVarChar, name));
        return (code, name);
    }

    private async Task<ExpenseView> BillAsync(string vendor, string category, long amount, long? gst = null, string? itc = null)
    {
        var e = (await db.Commands.SubmitAsync(db.Ctx(Who.Reception), new SubmitExpenseRequest(vendor, category, amount, "Vendor credit", Description: "Bill " + Tag(),
            GstRateBps: gst is null ? null : 1800, GstPaise: gst, Itc: itc), Ct)).Value.Expense;
        return (await db.Commands.DecideAsync(db.Ctx(Who.Owner), new DecideExpenseRequest(e.ExpenseId, e.Version, true), Ct)).Value.Expense;
    }

    private (AssetService Svc, Manual Manual) Assets()
    {
        var manual = new Manual();
        return (new AssetService(db.Platform.Commands, db.Policy, new SqlFixedAssetStore(db.Platform.Connections), new SqlAccountingStore(db.Platform.Connections), db.Clock, manual), manual);
    }

    private async Task<FixedAssetView> AssetAsync(long expenseId) => (await db.VendorQueries.AssetsAsync(db.PropertyId, Who.Owner, Ct)).Items.Single(x => x.ExpenseId == expenseId);

    [Fact]
    public async Task C1_put_to_use_and_disposal_Owner_or_Accountant_a_sale_posts_a_Receipt_Dr_tender_Cr_block_a_scrap_posts_nothing()
    {
        var furn = await CategoryAsync("1510", capital: true);
        var (_, ka) = await VendorAsync("KA");
        var b = await BillAsync(ka, furn, 236_000, gst: 36_000, itc: "allowed");
        var a = await AssetAsync(b.ExpenseId);
        var (svc, manual) = Assets();
        await Fails(HccErrorCode.FORBIDDEN, () => svc.PutToUseAsync(db.As(Who.Manager), ExpensesDb.Key(), new SetAssetPutToUseRequest(a.AssetId), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.PutToUseAsync(db.As(Who.Owner), ExpensesDb.Key(), new SetAssetPutToUseRequest(a.AssetId, "2026-01-01"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.PutToUseAsync(db.As(Who.Owner), ExpensesDb.Key(), new SetAssetPutToUseRequest(a.AssetId, "2099-01-01"), Ct));
        var put = (await svc.PutToUseAsync(db.As(Who.Accountant), ExpensesDb.Key(), new SetAssetPutToUseRequest(a.AssetId), Ct)).Value;
        Assert.Equal("2026-10-24", put.Asset.PutToUse);
        Assert.Equal(0.5m, AssetRules.DepreciationFactor(new DateOnly(2026, 10, 24), null, AssetRules.FyEnd(new DateOnly(2026, 10, 24))));

        await Fails(HccErrorCode.VALIDATION, () => svc.DisposeAsync(db.As(Who.Owner), ExpensesDb.Key(), new DisposeFixedAssetRequest(a.AssetId, 50_000), Ct)); // tender needed
        await Fails(HccErrorCode.VALIDATION, () => svc.DisposeAsync(db.As(Who.Owner), ExpensesDb.Key(), new DisposeFixedAssetRequest(a.AssetId, -1), Ct));
        var sold = (await svc.DisposeAsync(db.As(Who.Owner), ExpensesDb.Key(), new DisposeFixedAssetRequest(a.AssetId, 50_000, "Bank transfer", Reason: "Sold to staff"), Ct)).Value;
        var v = Assert.Single(manual.Saved);
        Assert.Equal(("Receipt", "journal", a.AssetNo), (v.Type, v.Template, v.Reference));
        Assert.Equal([(50_000L, 0L), (0L, 50_000L)], v.Lines.Select(l => (l.DebitPaise, l.CreditPaise)).ToArray());
        Assert.Equal("1510", v.Lines[1].Account);
        Assert.NotNull(sold.Flag); // the only asset of the block (and the block value of this harness has no projected purchase) → "… — CA"
        Assert.Contains("rulebook F4", sold.Flag, StringComparison.Ordinal);
        var d = await AssetAsync(b.ExpenseId);
        Assert.Equal(("2026-10-24", 50_000L, sold.ManualVoucherId, "Bank transfer"), (d.DisposedDate, d.DisposedAmountPaise, d.DisposedVoucher, d.DisposalMethod));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.DisposeAsync(db.As(Who.Owner), ExpensesDb.Key(), new DisposeFixedAssetRequest(a.AssetId, 0), Ct));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_ASSET_DISPOSE' AND target = @t", ("@t", SqlDbType.VarChar, "fixed-asset:" + a.AssetId)));

        // a scrap: nothing received → no voucher
        var b2 = await BillAsync(ka, furn, 11_800, gst: 1_800, itc: "allowed");
        var a2 = await AssetAsync(b2.ExpenseId);
        var scrap = (await svc.DisposeAsync(db.As(Who.Accountant), ExpensesDb.Key(), new DisposeFixedAssetRequest(a2.AssetId, 0, Reason: "Broken"), Ct)).Value;
        Assert.Null(scrap.ManualVoucherId);
        Assert.Single(manual.Saved);
    }

    [Fact]
    public async Task C2_a_posted_return_against_a_capital_purchase_reduces_the_register_cost_net_of_its_ITC()
    {
        var furn = await CategoryAsync("1510", capital: true);
        var (_, mh) = await VendorAsync();
        var b = await BillAsync(mh, furn, 236_000, gst: 36_000, itc: "allowed");
        Assert.Equal(200_000L, (await AssetAsync(b.ExpenseId)).CostPaise);
        await db.Vendor.ReturnAsync(db.Ctx(Who.Manager), new RecordExpenseReturnRequest(b.ExpenseId, 59_000, 9_000, "Five chairs damaged"), Ct); // pending
        Assert.Equal(200_000L, (await AssetAsync(b.ExpenseId)).CostPaise);
        await db.Vendor.ReturnAsync(db.Ctx(Who.Owner), new RecordExpenseReturnRequest(b.ExpenseId, 59_000, 9_000, "Five chairs damaged"), Ct); // posted at once
        var a = await AssetAsync(b.ExpenseId);
        Assert.Equal((150_000L, 200_000L, 50_000L), (a.CostPaise, a.OriginalCostPaise, a.ReturnedPaise));
        Assert.Equal(200_000L, await db.CountAsync("SELECT cost_paise FROM acc.fixed_asset WHERE expense_id = @e", ("@e", SqlDbType.BigInt, b.ExpenseId))); // stored purchase cost
    }

    [Fact]
    public async Task C3_reallocate_a_vendor_payment_own_allocation_counts_as_open_audit_row_ageing_follows_C4_warning()
    {
        var cat = await CategoryAsync("5000");
        var (code, v) = await VendorAsync("MH", "24ABCDE1234F1Z5");
        var a = await BillAsync(v, cat, 300_000);
        var b = await BillAsync(v, cat, 200_000);
        var paid = (await db.Vendor.PayAsync(db.Ctx(Who.Manager), new RecordVendorPaymentRequest(code, 250_000, "Bank transfer", "NEFT-C3"), Ct)).Value;
        Assert.Contains("starts 24, but its GST state is MH (27)", paid.Warning, StringComparison.Ordinal); // C4: warning, the payment stands
        var p = paid.Payment;
        Assert.Equal([new VendorAllocationDto(a.ExpenseId, 250_000)], p.Allocations.ToArray());
        await Fails(HccErrorCode.FORBIDDEN, () => db.Vendor.ReallocateAsync(db.Ctx(Who.Reception), new ReallocateVendorPaymentRequest(p.PaymentId, []), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.ReallocateAsync(db.Ctx(Who.Manager), new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(b.ExpenseId, 250_000)]), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Vendor.ReallocateAsync(db.Ctx(Who.Manager),
            new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(a.ExpenseId, 150_000), new VendorAllocationDto(b.ExpenseId, 150_000)]), Ct));
        var r = (await db.Vendor.ReallocateAsync(db.Ctx(Who.Accountant),
            new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(b.ExpenseId, 200_000), new VendorAllocationDto(a.ExpenseId, 50_000)]), Ct)).Value;
        Assert.Equal([(b.ExpenseId, 200_000L), (a.ExpenseId, 50_000L)], r.Payment.Allocations.OrderByDescending(x => x.AmountPaise).Select(x => (x.ExpenseId, x.AmountPaise)).ToArray());
        Assert.Equal(0L, r.AdvancePaise);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM ops.vendor_payment_realloc WHERE vendor_payment_id = @p", ("@p", SqlDbType.BigInt, p.PaymentId)));
        var row = (await db.VendorQueries.AgeingAsync(db.PropertyId, Who.Owner, null, code, Ct)).Rows.Single();
        Assert.Equal([(a.ExpenseId, 250_000L)], row.Bills.Select(x => (x.ExpenseId, x.OpenPaise)).ToArray());
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Vendor.ReallocateAsync(db.Ctx(Who.Owner),
            new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(b.ExpenseId, 200_000), new VendorAllocationDto(a.ExpenseId, 50_000)]), Ct)); // unchanged
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecAsync("DELETE ops.vendor_payment_realloc;")); // append-only
        // a shorter list leaves an advance; a void payment cannot be re-allocated
        var adv = (await db.Vendor.ReallocateAsync(db.Ctx(Who.Owner), new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(a.ExpenseId, 100_000)]), Ct)).Value;
        Assert.Equal(150_000L, adv.AdvancePaise);
        await db.Vendor.VoidAsync(db.Ctx(Who.Owner), new VoidVendorPaymentRequest(p.PaymentId, "Bounced"), Ct);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Vendor.ReallocateAsync(db.Ctx(Who.Owner), new ReallocateVendorPaymentRequest(p.PaymentId, [new VendorAllocationDto(a.ExpenseId, 1)]), Ct));
    }
}
