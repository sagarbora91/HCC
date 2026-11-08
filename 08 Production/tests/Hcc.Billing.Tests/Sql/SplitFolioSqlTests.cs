using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>[F8-SF-SERVER] Split folio on SQL Server (migrations 4350 / 4351): open a company folio, routed postings land on the routed folio_id,
/// statements per folio, a single-folio stay is unchanged, the primary-folio lookups stay unique.</summary>
[Trait("Category", "Sql")]
public class SplitFolioSqlTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubGuard(string name, long limit, long used) : ICompanyCreditGuard
    {
        public Task<CompanyRow> LockAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct = default) =>
            Task.FromResult(new CompanyRow(companyId, propertyId, name, null, null, null, null, null, new Dictionary<string, long>(), limit, "v", true));

        public Task<CompanyExposure> ExposureAsync(ICommandTransaction? tx, int propertyId, long companyId, CancellationToken ct = default) =>
            Task.FromResult(new CompanyExposure(new Paise(used), Paise.Zero, Paise.Zero, []));
    }

    private (SqlSplitFolioStore Store, SplitFolioService Service) Split(long limit = 5_000_000, long used = 0)
    {
        var billingStore = new SqlBillingStore(new SqlConnectionFactory(db.Options));
        var store = new SqlSplitFolioStore(new SqlConnectionFactory(db.Options), billingStore, new StubGuard("Deccan Agro Ltd", limit, used));
        return (store, new SplitFolioService(db.Runner, db.Policy, store, db.Clock));
    }

    private CommandContext Ctx(string op) => new(db.PropertyId, Who.Manager, op, BillingDb.Key());

    private async Task<long> CompanyAsync() => await db.LongAsync("""
        INSERT grp.company_account(property_id, name, gstin, credit_limit_paise, created_by, created_utc)
        OUTPUT INSERTED.company_id VALUES (@p, @n, '27AAACD1111A1Z1', 5000000, 1, SYSUTCDATETIME());
        """, ("@p", System.Data.SqlDbType.Int, db.PropertyId), ("@n", System.Data.SqlDbType.NVarChar, "Deccan Agro " + Guid.NewGuid().ToString("N")[..6]));

    private async Task<long> StoredFolioAsync(long chargeId) => await db.LongAsync("SELECT folio_id FROM bill.charge WHERE charge_id = @c", ("@c", System.Data.SqlDbType.BigInt, chargeId));

    private async Task<(long Rid, long FolioA)> StayAsync(long rate = 300_000)
    {
        var (rid, fid) = await db.NewFolioAsync("InHouse", BillingDb.Day, BillingDb.Day.AddDays(3));
        db.Stays[rid] = (fid, rate);
        return (rid, fid);
    }

    [Fact]
    public async Task F8_SF_SERVER_company_folio_round_trip_routes_room_to_B_extras_stay_on_A_and_statements_add_up()
    {
        var (store, svc) = Split();
        var (rid, fa) = await StayAsync();
        var co = await CompanyAsync();
        var opened = (await svc.OpenAsync(Ctx(SplitFolioService.OpOpen), new OpenFolioRequest(rid, "company", co, null, null, null, ["room"], "Company pays the room"), Ct)).Value;
        Assert.Equal(("B", "company"), (opened.Letter, opened.PayerKind));

        var night = (await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct)).Value;
        Assert.Equal(opened.FolioId, await StoredFolioAsync(night.ChargeId)); // room goes to the company folio
        var laundry = (await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(fa, "Laundry", 50_000, "laundry"), Ct)).Value;
        Assert.Equal(fa, await StoredFolioAsync(laundry.ChargeId)); // extras are not routed: they stay on A

        var rows = (await store.SummaryAsync(db.PropertyId, rid, Ct))!;
        Assert.Equal(["A", "B"], rows.Select(r => r.Letter));
        Assert.Equal(["extras"], rows[0].Routes);
        Assert.Equal(["room"], rows[1].Routes);
        Assert.Equal(laundry.AmountPaise + laundry.TaxPaise, rows[0].TotalPaise);
        Assert.Equal(night.AmountPaise + night.TaxPaise, rows[1].TotalPaise);
        Assert.StartsWith("Deccan Agro", rows[1].PayerName);
        Assert.Equal("27AAACD1111A1Z1", rows[1].PayerGstin);

        // the primary-folio lookups still return exactly A for the split reservation
        var billingStore = new SqlBillingStore(new SqlConnectionFactory(db.Options));
        var found = await billingStore.FindReservationFolioAsync(db.PropertyId, rid, Ct);
        Assert.Equal(fa, found!.FolioId);
        var desk = await db.Queries.GetDeskFoliosAsync(db.As(Who.Cashier), Ct);
        Assert.Single(desk.Items, i => i.ReservationId == rid);
    }

    [Fact]
    public async Task F8_SF_SERVER_single_folio_stay_is_unchanged_and_reads_as_one_folio()
    {
        var (rid, fa) = await StayAsync();
        var night = (await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct)).Value;
        Assert.Equal(fa, await StoredFolioAsync(night.ChargeId));
        var (store, _) = Split();
        var rows = (await store.SummaryAsync(db.PropertyId, rid, Ct))!;
        var only = Assert.Single(rows);
        Assert.Equal(("A", "guest", false), (only.Letter, only.PayerKind, only.Removed));
        Assert.Equal(["room", "extras"], only.Routes);
        Assert.Equal(night.AmountPaise + night.TaxPaise, only.TotalPaise);
    }

    [Fact]
    public async Task F8_SF_SERVER_a_used_folio_cannot_be_removed_and_an_empty_one_returns_its_routes_to_A()
    {
        var (_, svc) = Split();
        var (rid, fa) = await StayAsync();
        var co = await CompanyAsync();
        var b = (await svc.OpenAsync(Ctx(SplitFolioService.OpOpen), new OpenFolioRequest(rid, "company", co, null, null, null, ["room", "extras"], null), Ct)).Value;
        var charge = (await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(fa, "Laundry", 10_000, "laundry"), Ct)).Value;
        Assert.Equal(b.FolioId, await StoredFolioAsync(charge.ChargeId)); // extras routed to B
        await Assert.ThrowsAsync<Hcc.Domain.Errors.HccException>(() => svc.RemoveAsync(Ctx(SplitFolioService.OpRemove), new RemoveFolioRequest(rid, 2), Ct));

        var (rid2, fa2) = await StayAsync();
        var c2 = await CompanyAsync();
        await svc.OpenAsync(Ctx(SplitFolioService.OpOpen), new OpenFolioRequest(rid2, "company", c2, null, null, null, ["extras"], null), Ct);
        await svc.RemoveAsync(Ctx(SplitFolioService.OpRemove), new RemoveFolioRequest(rid2, 2), Ct);
        var back = (await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(fa2, "Laundry", 10_000, "laundry"), Ct)).Value;
        Assert.Equal(fa2, await StoredFolioAsync(back.ChargeId));
    }

    [Fact]
    public async Task F8_SF_SERVER_open_folio_on_a_reservation_without_folio_creates_the_primary_first()
    {
        var (_, svc) = Split();
        var rid = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', '2026-10-25', '2026-10-27', 'Confirmed');
            """, ("@p", System.Data.SqlDbType.Int, db.PropertyId), ("@no", System.Data.SqlDbType.VarChar, "SFS" + Guid.NewGuid().ToString("N")[..8]));
        var r = (await svc.OpenAsync(Ctx(SplitFolioService.OpOpen), new OpenFolioRequest(rid, "guest", null, null, "Fictional payer", null, ["extras"], null), Ct)).Value;
        Assert.Equal(2, r.Seq);
        Assert.Equal(1L, Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM bill.folio WHERE reservation_id = @r AND folio_seq = 1", ("@r", System.Data.SqlDbType.BigInt, rid))));
    }
}
