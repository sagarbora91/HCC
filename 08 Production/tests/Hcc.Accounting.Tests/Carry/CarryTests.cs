using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Accounting;
using Hcc.Domain.Expenses;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Carry;

/// <summary>
/// ACC-3 · lane acc3-carry — the server side of the ACC-2 carry-overs that live in Accounting (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7):
/// C1 F2 / F4 rules, C4 GSTIN vs state, C5 Udhaar B9 per customer per day, C6 TDS per sub-section, C7 Form 168 on the remittance, C8 B11 loans
/// only through a bank, C9 the stale set-off, C10 the dated GST-on-advances switch, C11 the TDS rate versions on GET /accounting/tds.
/// (C2 / C3 / C1 SQL flows: Hcc.Expenses.Tests/Sql/CarrySqlTests.cs.)
/// </summary>
public sealed class CarryRuleTests
{
    [Fact]
    public void C1_F2_factor_counts_from_the_put_to_use_date_and_F4_flags_an_empty_or_negative_block()
    {
        var fy = AssetRules.FyEnd(new DateOnly(2026, 10, 24));
        Assert.Equal(new DateOnly(2027, 3, 31), fy);
        Assert.Equal(0.5m, AssetRules.DepreciationFactor(new DateOnly(2026, 10, 24), null, fy));
        Assert.Equal(1m, AssetRules.DepreciationFactor(new DateOnly(2026, 10, 3), null, fy)); // 180 days
        Assert.Equal(0.5m, AssetRules.DepreciationFactor(new DateOnly(2026, 10, 4), null, fy));
        Assert.Equal(1m, AssetRules.DepreciationFactor(new DateOnly(2025, 6, 1), null, fy));
        Assert.Equal(0m, AssetRules.DepreciationFactor(null, null, fy));
        Assert.Equal(0m, AssetRules.DepreciationFactor(new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1), fy));
        Assert.Null(AssetRules.DisposalFlag("1510", 500_000, 50_000, 2));
        Assert.Contains("no asset left", AssetRules.DisposalFlag("1510", 500_000, 50_000, 0), StringComparison.Ordinal);
        Assert.Contains("goes negative", AssetRules.DisposalFlag("1510", 40_000, 50_000, 3), StringComparison.Ordinal);
    }

    [Fact]
    public void C4_GSTIN_state_mismatch_is_a_warning_with_the_prototype_text()
    {
        Assert.Null(VendorGstin.Warning("K", "29ABCDE1234F1Z5", "KA"));
        Assert.Null(VendorGstin.Warning("M", "27ABCDE1234F1Z5", null)); // empty state = MH
        Assert.Null(VendorGstin.Warning("X", null, "MH"));
        Assert.Equal("GSTIN 24ABCDE1234F1Z5 of D starts 24, but its GST state is MH (27) — check the vendor master (DEC-136: CGST + SGST are billed either way).",
            VendorGstin.Warning("D", "24abcde1234f1z5", "mh"));
    }

    [Fact]
    public void C6_sub_sections_keep_their_old_section_and_label()
    {
        Assert.Equal(["194-I(b)", "194-I(a)", "194J(b)", "194J(a)", "194C"], new[] { "rent", "rent:plant", "professional", "professional:technical", "contractor" }.Select(TdsService.OldOf));
        Assert.Equal("TDS remittance · rent:plant (s.393(1) Sl. 2(ii), old 194-I(a)) · 2026-10 · challan C-1", TdsService.Narration("rent:plant", "2026-10", "C-1"));
        var bills = new List<TdsBillRow>
        {
            new(1, new DateOnly(2026, 10, 5), "Genset (fictional)", "AAAPS1234C", "rent:plant", 100_000, 2_000),
            new(2, new DateOnly(2026, 10, 6), "Landlord (fictional)", "AAAPS1234D", "rent", 100_000, 10_000),
        };
        var remits = new List<TdsRemitRecord> { new(1, "2026-10", "rent:plant", 2_000, "Bank transfer", "C-1", new DateOnly(2026, 10, 7), null) };
        Assert.Equal((0L, 10_000L), (TdsService.Outstanding(bills, remits, "rent:plant", "2026-10"), TdsService.Outstanding(bills, remits, "rent", "2026-10")));
        var lines = TdsReports.Deductees(bills, remits.Select(r => new TdsRemitRow(r.Section, r.Month, r.AmountPaise)), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal([("rent:plant", 2_000L), ("rent", 0L)], lines.OrderBy(l => l.Vendor, StringComparer.Ordinal).Select(l => (l.Section, l.RemittedPaise)).ToArray());
    }

    [Fact]
    public void C7_Form168_figures_round_trip_as_json()
    {
        var json = TdsService.Form168Json([new Form168FigureDto(7, 123_400), new Form168FigureDto(9, 0)]);
        Assert.Equal([new Form168FigureDto(7, 123_400), new Form168FigureDto(9, 0)], TdsService.Form168Of(json)!.ToArray());
        Assert.Null(TdsService.Form168Of(null));
    }

    [Fact]
    public void C8_B11_a_cash_line_with_a_loan_ledger_is_refused_bank_passes()
    {
        Assert.Equal(CashGuard.LoanCashMessage, CashGuard.LoanCashRefusal([("2400/LN-HDFC", false, 2_000_000L), ("1000/DESK", true, 2_000_000L)]));
        Assert.Null(CashGuard.LoanCashRefusal([("2400/LN-HDFC", false, 1_999_900L), ("1000/DESK", true, 1_999_900L)])); // below ₹20,000: rulebook B11 (ACC3-R5)
        Assert.Null(CashGuard.LoanCashRefusal([("2400/LN-HDFC", false, 5_000_000L), ("1030/HDFC", false, 5_000_000L)]));
        Assert.Null(CashGuard.LoanCashRefusal([("5000", false, 5_000_000L), ("1000/DESK", true, 5_000_000L)]));
        Assert.Contains("rulebook B11", CashGuard.LoanCashMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void C9_a_set_off_posted_on_figures_that_changed_is_stale()
    {
        var a = GstSetOff.Compute(new GstHeads(0, 10_000, 10_000), new GstHeads(0, 0, 0), 0);
        var b = GstSetOff.Compute(new GstHeads(0, 12_000, 12_000), new GstHeads(0, 0, 0), 0);
        var posted = new StoredGstSetoff(1, "2026-09", new DateOnly(2026, 10, 2), "gst-setoff:2026-09@2026-10-02", a, 1, DateTime.UtcNow);
        Assert.Null(GstMonthService.StaleSetoff("2026-09", posted, a));
        Assert.Null(GstMonthService.StaleSetoff("2026-09", null, b));
        Assert.Contains("changed after the set-off of 2026-10-02", GstMonthService.StaleSetoff("2026-09", posted, b), StringComparison.Ordinal);
    }
}

/// <summary>ACC-3 acc3-carry on SQL: C5 (B9 per customer), C7 (Form 168 stored), C10 (dated switch), C11 (rate versions on the TDS read).</summary>
[Trait("Category", "Sql")]
public sealed class CarrySqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static readonly DateOnly D = AccountingDb.Day;

    private SqlAccessPolicy Policy() => new(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);

    private async Task<long> CreditAsync(string name, string? mobile, int till, long settled)
    {
        var id = await db.LongAsync("""
            INSERT ops.udhaar_receivable(property_id, customer_name, mobile, amount_paise, credit_date, due_date, created_by, created_utc)
            OUTPUT INSERTED.receivable_id VALUES (@p, @n, @m, 30000000, @d, @d, 1, SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, db.PropertyId), ("@n", SqlDbType.NVarChar, name), ("@m", SqlDbType.Char, (object?)mobile ?? DBNull.Value),
            ("@d", SqlDbType.Date, D.ToDateTime(TimeOnly.MinValue)));
        await db.ScalarAsync("""
            INSERT ops.udhaar_settlement(receivable_id, amount_paise, settle_date, mode, recorded_by, recorded_utc, desk, till_id)
            VALUES (@r, @a, @d, N'Cash', 1, SYSUTCDATETIME(), 1, @t);
            """, ("@r", SqlDbType.BigInt, id), ("@a", SqlDbType.BigInt, settled), ("@d", SqlDbType.Date, D.ToDateTime(TimeOnly.MinValue)), ("@t", SqlDbType.Int, till));
        return id;
    }

    [Fact]
    public async Task C5_B9_Udhaar_cash_is_totalled_per_customer_per_day_across_credits()
    {
        var till = (int)await db.LongAsync("""
            INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES (@p, 'C5-' + LEFT(CONVERT(VARCHAR(36), NEWID()), 8), N'C5 till', 1, SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, db.PropertyId));
        var a = await CreditAsync("Ravi Kumar (fictional)", "9800000001", till, 15_000_000);
        await CreditAsync("Ravi Kumar (fictional)", "9800000001", till, 15_000_000);
        var other = await CreditAsync("Asha (fictional)", "9800000002", till, 100);
        var noMobile = await CreditAsync("Gopal (fictional)", null, till, 5_000_000);
        await CreditAsync(" gopal (FICTIONAL) ", null, till, 5_000_000);
        var store = new SqlFlowsStore(db.Platform.Connections);
        var r = await db.Platform.Commands.RunAsync(new CommandContext(db.PropertyId, db.Users["OWN"], "TestC5", Key()), new { t = 1 }, async (tx, c) =>
        {
            var ravi = await store.ReceiptTotalsAsync(tx, db.PropertyId, new CashParty(CashParty.Udhaar, a), D, c);
            var asha = await store.ReceiptTotalsAsync(tx, db.PropertyId, new CashParty(CashParty.Udhaar, other), D, c);
            var gopal = await store.ReceiptTotalsAsync(tx, db.PropertyId, new CashParty(CashParty.Udhaar, noMobile), D, c);
            await tx.AuditAsync("TEST_C5", "test", "C5 totals read", c);
            return new[] { ravi.Day, ravi.Scope, asha.Day, gopal.Day, gopal.Scope };
        }, Ct);
        Assert.Equal([30_000_000L, 15_000_000L, 100L, 10_000_000L, 5_000_000L], r.Value);
        // the guard refuses the next rupee from Ravi today (₹3,00,000 already ≥ ₹2,00,000), as the prototype's 1120/UD-<mobile> party
        var guard = new CashGuardService(store);
        await Fails(HccErrorCode.VALIDATION, () => db.Platform.Commands.RunAsync(new CommandContext(db.PropertyId, db.Users["OWN"], "TestC5b", Key()), new { t = 2 },
            async (tx, c) => await guard.EnsureReceiptAsync(tx, db.PropertyId, new CashParty(CashParty.Udhaar, a), 100, D, "Ravi", c), Ct));
    }

    [Fact]
    public async Task C7_Form168_figures_are_stored_on_the_remittance_and_read_by_the_receivable_report_C11_rates_listed()
    {
        var company = await db.LongAsync("""
            INSERT grp.company_account(property_id, name, created_by, created_utc) OUTPUT INSERTED.company_id VALUES (@p, N'Deccan Agro (fictional) C7', 1, SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, db.PropertyId));
        var sql = new SqlTdsStore(db.Platform.Connections);
        var svc = new TdsService(db.Platform.Commands, sql, Policy(), db.Clock);
        var remitId = await db.LongAsync("""
            INSERT acc.tds_remit(property_id, month, section, amount_paise, tender, challan, business_date, recorded_by, recorded_utc)
            OUTPUT INSERTED.remit_id VALUES (@p, '2026-10', 'rent:plant', 2000, 'Bank transfer', N'C7-001', '2026-10-24', 1, SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, db.PropertyId)); // C6: the widened CHECK takes a sub-section
        await Fails(HccErrorCode.FORBIDDEN, () => svc.SetForm168Async(db.As("MGR"), Key(), new SetTdsForm168Request(remitId, [new Form168FigureDto(company, 1)]), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetForm168Async(db.As("ACC"), Key(), new SetTdsForm168Request(remitId, [new Form168FigureDto(999_999, 1)]), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => svc.SetForm168Async(db.As("ACC"), Key(), new SetTdsForm168Request(-1, []), Ct));
        var set = (await svc.SetForm168Async(db.As("ACC"), Key(), new SetTdsForm168Request(remitId, [new Form168FigureDto(company, 123_400)]), Ct)).Value;
        Assert.Equal(remitId, set.RemitId);
        var state = await svc.GetAsync(db.As("OWN"), Ct);
        var row = state.Remittances.Single(r => r.RemitId == remitId);
        Assert.Equal(("rent:plant", "194-I(a)", 123_400L), (row.Section, row.Old, row.Form168!.Single().AmountPaise));
        var recv = await svc.ReceivableAsync(db.As("OWN"), "2026-27", Ct);
        Assert.Equal(123_400L, recv.Form168!.Single(f => f.CompanyId == company).AmountPaise);
        // C11: the dated rate versions travel on the same read (the production client maps them into configX.changes)
        await svc.SetRateAsync(db.As("OWN"), Key(), new SetTdsRateRequest("tds.rent.bpsOther", 1200, "2026-10-25", "CA advice"), Ct);
        var rates = (await svc.GetAsync(db.As("OWN"), Ct)).Rates;
        Assert.Contains(rates, r => r.Key == "tds.rent.bpsOther" && r.EffectiveFrom == "2026-10-25" && r.Value == 1200 && r.RateId > 0);
    }

    [Fact]
    public async Task C10_the_switch_is_dated_deposits_before_the_date_carry_no_advance_GST()
    {
        if (await db.LongAsync("SELECT COUNT(*) FROM cmp.profile WHERE property_id = @p;", ("@p", SqlDbType.Int, db.PropertyId)) == 0)
            await db.ScalarAsync("""
                INSERT cmp.profile(property_id, firm_name, entity, gst_type, has_employees, qrmp)
                VALUES (@p, N'HCC Demo Residency (fictional)', 'proprietorship', 'regular', 1, 0);
                """, ("@p", SqlDbType.Int, db.PropertyId));
        var svc = new GstMonthService(db.Platform.Commands, db.Store, new SqlGstMonthStore(db.Platform.Connections), Policy(), db.Clock);
        await Fails(HccErrorCode.VALIDATION, () => svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(true, "24-10-2026"), Ct));
        var on = (await svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(true, "2026-10-25"), Ct)).Value;
        Assert.Equal((true, "2026-10-25"), (on.On, on.From));
        Assert.Equal("2026-10-25", (await svc.LocksAsync(db.As("OWN"), Ct)).GstOnAdvancesFrom);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(true, "2026-10-25"), Ct));
        var f = await db.NewFolioAsync();
        await db.ScalarAsync("UPDATE inv.reservation SET nightly_rate_paise = 200000 WHERE reservation_id = (SELECT reservation_id FROM bill.folio WHERE folio_id = @f);", ("@f", SqlDbType.BigInt, f));
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, f, new { folioId = f, receiptId = 9701L, kind = "deposit", tender = "Cash", amountPaise = 1_050_000L, businessDate = D });
        await db.ProjectAsync();
        var live = (await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, D, Ct)).Live;
        Assert.DoesNotContain(live, v => v.SourceKey == AccountingVoucherProjector.AdvanceGstKey(f, 9701)); // dated 24 Oct, before the switch date
        // moved to the business date: a deposit of that date carries the advance GST
        Assert.Equal("2026-10-24", (await svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(true), Ct)).Value.From);
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, f, new { folioId = f, receiptId = 9702L, kind = "deposit", tender = "Cash", amountPaise = 1_050_000L, businessDate = D });
        await db.ProjectAsync();
        live = (await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, D, Ct)).Live;
        Assert.Contains(live, v => v.SourceKey == AccountingVoucherProjector.AdvanceGstKey(f, 9702));
        var off = (await svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(false), Ct)).Value;
        Assert.Equal((false, (string?)null), (off.On, off.From));
        Assert.Null((await svc.LocksAsync(db.As("OWN"), Ct)).GstOnAdvancesFrom);
        Assert.Equal(1L, await db.LongAsync("SELECT COUNT(*) FROM cmp.profile WHERE property_id = @p AND gst_on_advances_from IS NULL;", ("@p", SqlDbType.Int, db.PropertyId)));
    }
}
