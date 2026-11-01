using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// ACC-2 · lane acc2-tds on SQL (migrations 4243–4245) with the real P1 policy: the firm's TDS switches and dated rate overrides are the Owner's
/// (PERM-CFG-003), the remittance and the reads are PERM-ACC-013 (Owner, Accountant); a remittance never exceeds the TDS held back for its row
/// and month (the bills come from the purchase lane's ops.expense columns — absent in this branch, so a decorator supplies them), and is
/// stored append-only, unlinked until the manual-voucher service is wired (ITdsRemittancePoster).
/// </summary>
[Trait("Category", "Sql")]
public sealed class TdsSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static long R(long rupees) => rupees * 100;

    /// <summary>The SQL store with the bills the purchase lane will store (tds_paise / tds_section).</summary>
    private sealed class WithBills(SqlTdsStore inner, IReadOnlyList<TdsBillRow> bills) : ITdsStore
    {
        public Task SerializeAsync(ICommandTransaction tx, int p, CancellationToken ct) => inner.SerializeAsync(tx, p, ct);
        public Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int p, CancellationToken ct) => inner.BusinessDateAsync(tx, p, ct);
        public Task<TdsSettings> SettingsAsync(ICommandTransaction? tx, int p, CancellationToken ct) => inner.SettingsAsync(tx, p, ct);
        public Task SaveSettingsAsync(ICommandTransaction tx, int p, TdsSettings s, int a, DateTime n, CancellationToken ct) => inner.SaveSettingsAsync(tx, p, s, a, n, ct);
        public Task<IReadOnlyList<TdsRateRecord>> RatesAsync(ICommandTransaction? tx, int p, CancellationToken ct) => inner.RatesAsync(tx, p, ct);
        public Task<long> InsertRateAsync(ICommandTransaction tx, int p, string k, DateOnly f, long v, string r, int a, DateTime n, CancellationToken ct) => inner.InsertRateAsync(tx, p, k, f, v, r, a, n, ct);
        public Task<IReadOnlyList<TdsRemitRecord>> RemitsAsync(ICommandTransaction? tx, int p, CancellationToken ct) => inner.RemitsAsync(tx, p, ct);
        public Task<long> InsertRemitAsync(ICommandTransaction tx, int p, TdsRemitRecord r, int a, DateTime n, CancellationToken ct) => inner.InsertRemitAsync(tx, p, r, a, n, ct);
        public Task LinkVoucherAsync(ICommandTransaction tx, long id, string v, CancellationToken ct) => inner.LinkVoucherAsync(tx, id, v, ct);
        public Task<IReadOnlyList<TdsBillRow>> BillsAsync(ICommandTransaction? tx, int p, CancellationToken ct) => Task.FromResult(bills);
        public Task<TdsVendor?> VendorAsync(ICommandTransaction? tx, int p, string v, CancellationToken ct) => inner.VendorAsync(tx, p, v, ct);
        public Task<IReadOnlyList<TdsPriorBill>> VendorBillsAsync(ICommandTransaction? tx, int p, string v, DateOnly f, DateOnly t, long? x, CancellationToken ct) => inner.VendorBillsAsync(tx, p, v, f, t, x, ct);
        public Task<IReadOnlyList<TdsReceivablePayment>> ReceivablePaymentsAsync(ICommandTransaction? tx, int p, DateOnly f, DateOnly t, CancellationToken ct) => inner.ReceivablePaymentsAsync(tx, p, f, t, ct);
        public Task SetForm168Async(ICommandTransaction tx, int p, long id, string j, CancellationToken ct) => inner.SetForm168Async(tx, p, id, j, ct); // ACC-3 C7
        public Task<IReadOnlySet<long>> CompanyIdsAsync(ICommandTransaction? tx, int p, CancellationToken ct) => inner.CompanyIdsAsync(tx, p, ct);
    }

    private sealed class Poster : ITdsRemittancePoster
    {
        public List<TdsRemittanceVoucher> Posted { get; } = [];

        public Task<string?> PostAsync(ICommandTransaction tx, Hcc.Application.Billing.Caller caller, TdsRemittanceVoucher voucher, CancellationToken ct)
        {
            Posted.Add(voucher);
            return Task.FromResult<string?>("9001");
        }
    }

    [Fact]
    public async Task Settings_rates_remittance_and_reads_with_permissions()
    {
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var sql = new SqlTdsStore(db.Platform.Connections);
        var bills = new List<TdsBillRow>
        {
            new(1, new DateOnly(2026, 10, 5), "Shiv Civil Works (fictional)", "AAAPS1234C", "contractor", R(50_000), R(1_000)),
            new(2, new DateOnly(2026, 10, 1), "Godown Owner (fictional)", "AAAPS1234C", "rent", R(60_000), R(6_000)),
        };
        var poster = new Poster();
        var svc = new TdsService(db.Platform.Commands, new WithBills(sql, bills), policy, db.Clock, poster);

        // ── switches: defaults on / other; Owner only ──
        Assert.Equal(TdsSettings.Default, await sql.SettingsAsync(null, db.PropertyId, Ct));
        foreach (var role in new[] { "ACC", "MGR", "TAD", "CSH" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.SetSettingsAsync(db.As(role), Key(), new SetTdsSettingsRequest(false, null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetSettingsAsync(db.As("OWN"), Key(), new SetTdsSettingsRequest(null, null), Ct));
        var s = (await svc.SetSettingsAsync(db.As("OWN"), Key(), new SetTdsSettingsRequest(null, "individual"), Ct)).Value;
        Assert.Equal((true, "individual"), (s.HotelDeductsTds, s.DeductorType));
        Assert.Equal(new TdsSettings(true, TdsColumn.Individual), await sql.SettingsAsync(null, db.PropertyId, Ct));

        // ── rate override: dated, never backdated, reason required ──
        await Fails(HccErrorCode.FORBIDDEN, () => svc.SetRateAsync(db.As("ACC"), Key(), new SetTdsRateRequest("tds.contractor.bpsOther", 150, "2026-10-25", "CA advice"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetRateAsync(db.As("OWN"), Key(), new SetTdsRateRequest("tds.contractor.bpsOther", 150, "2026-10-23", "CA advice"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetRateAsync(db.As("OWN"), Key(), new SetTdsRateRequest("tds.contractor.bpsOther", 150, null, "x"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetRateAsync(db.As("OWN"), Key(), new SetTdsRateRequest("tds.contractor.bpsOther", 200, null, "CA advice"), Ct)); // already in force
        var rate = (await svc.SetRateAsync(db.As("OWN"), Key(), new SetTdsRateRequest("tds.contractor.bpsOther", 150, "2026-10-25", "CA advice"), Ct)).Value;
        Assert.Equal(("Scheduled", 200L, 150L), (rate.Status, rate.Before, rate.After));
        var versions = (await sql.RatesAsync(null, db.PropertyId, Ct)).Select(r => new TdsRateVersion(r.Key, r.EffectiveFrom, r.Value)).ToList();
        Assert.Equal((200L, 150L), (TdsRates.ValueOn("tds.contractor.bpsOther", AccountingDb.Day, versions), TdsRates.ValueOn("tds.contractor.bpsOther", AccountingDb.Day.AddDays(1), versions)));

        // ── remittance: PERM-ACC-013; never more than held back; Accountant allowed ──
        var pay = new RecordTdsRemittanceRequest("2026-10", "contractor", R(1_000), "Bank transfer", "CIN-0001");
        foreach (var role in new[] { "MGR", "CSH", "REC", "TAD" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.RecordRemittanceAsync(db.As(role), Key(), pay, Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.RecordRemittanceAsync(db.As("ACC"), Key(), pay with { AmountPaise = R(1_001) }, Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.RecordRemittanceAsync(db.As("ACC"), Key(), pay with { Tender = "Cash" }, Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.RecordRemittanceAsync(db.As("ACC"), Key(), pay with { Month = "2026-11" }, Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.RecordRemittanceAsync(db.As("ACC"), Key(), pay with { Month = "2026-08" }, Ct));
        var done = (await svc.RecordRemittanceAsync(db.As("ACC"), Key(), pay, Ct)).Value;
        Assert.Equal(("2026-10", "contractor", R(1_000), "9001", 0L), (done.Month, done.Section, done.AmountPaise, done.ManualVoucherId, done.OutstandingPaise));
        var v = Assert.Single(poster.Posted);
        Assert.Equal(("tds-remittance", "2150", "CIN-0001", "TDS remittance · contractor (s.393(1), old 194C) · 2026-10 · challan CIN-0001"), (v.Template, v.PayableLedger, v.Reference, v.Narration));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.RecordRemittanceAsync(db.As("OWN"), Key(), pay with { Challan = "CIN-0002" }, Ct)); // nothing left
        Assert.Equal(1L, await db.LongAsync("SELECT COUNT(*) FROM acc.tds_remit WHERE manual_voucher_id = 9001;"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("DELETE acc.tds_remit;")); // append-only trigger
        Assert.Equal(1L, await db.LongAsync("SELECT COUNT(*) FROM acc.tds_remit;"));

        // ── reads ──
        await Fails(HccErrorCode.FORBIDDEN, () => svc.GetAsync(db.As("MGR"), Ct));
        var state = await svc.GetAsync(db.As("ACC"), Ct);
        Assert.Equal((true, "individual", 6, 1, 1), (state.HotelDeductsTds, state.DeductorType, state.Table.Count, state.Rates.Count, state.Remittances.Count));
        Assert.Equal((100, 200), (state.Table.Single(t => t.Code == "contractor").IndividualBps, state.Table.Single(t => t.Code == "contractor").OtherBps)); // the override starts tomorrow
        var report = await svc.DeducteesAsync(db.As("OWN"), "2026-10", Ct);
        Assert.Equal((R(110_000), R(7_000), R(1_000), R(6_000)), (report.TaxablePaise, report.TdsPaise, report.RemittedPaise, report.PendingPaise));
        Assert.Empty((await svc.ReceivableAsync(db.As("ACC"), "2026-27", Ct)).Rows);
        // The bill reads of the real store run before the purchase lane's columns exist: nothing carries TDS yet.
        Assert.Empty(await sql.BillsAsync(null, db.PropertyId, Ct));
        Assert.Empty(await sql.VendorBillsAsync(null, db.PropertyId, "Nobody (fictional)", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), null, Ct));
        Assert.Null(await sql.VendorAsync(null, db.PropertyId, "Nobody (fictional)", Ct));
    }
}
