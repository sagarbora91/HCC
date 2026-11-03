// F7-R7 · the document loaders on a disposable HccTest_* database: the folio header and the quotation (role-checked), the registration card (ID number masked, never the full
// number), and screen = Excel = PDF = print on every document built from them.
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Tax;
using Hcc.Domain.Tax;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Documents;
using Hcc.Reporting.Reports.Guests;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.Documents;

[Trait("Category", "Sql")]
public sealed class DocumentSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;
    private readonly FakePolicy _policy = Policy();
    private long _folio;

    private static FakePolicy Policy()
    {
        var p = new FakePolicy();
        p.Grants["PERM-STY-001"] = ["OWN", "MGR", "REC"];
        return p;
    }

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (!_seeded)
            {
                await Seed();
                _seeded = true;
            }

            _folio = Convert.ToInt64(await db.ScalarAsync("SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2001'"));
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task Seed()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        await db.ScalarAsync($"""
            DECLARE @p INT = {p};
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic');
            DECLARE @cls INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS');
            INSERT inv.room(property_id, room_no, room_type_id, hk_status) VALUES (@p, '101', @cls, 'Ready');
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101');
            INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc)
              VALUES (@p, N'Ananya Rao', '+919820001041', N'Indian', N'Aadhaar', '4101', N'10 Demo Street, Bengaluru, Karnataka (fictional)', 'manual', '2026-10-01');
            DECLARE @g1 BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Ananya Rao');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source, nightly_rate_paise)
              VALUES (@p, 'HCC-2001', N'Ananya Rao', '2026-10-23', '2026-10-25', 'InHouse', @r101, @cls, 2, 0, 'Direct', 200000),
                     (@p, 'HCC-2002', N'No Rate', '2026-10-26', '2026-10-27', 'Confirmed', @r101, @cls, 1, 0, 'Direct', NULL);
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no = 'HCC-2001';
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) SELECT reservation_id, @g1, '2026-10-20', {own} FROM inv.reservation WHERE public_no = 'HCC-2001';
            DECLARE @f BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2001');
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', 100000, '2026-10-23');
            SELECT 1;
            """);
    }

    /// <summary>The dated master with the statutory seed rows (no property overrides) behind the resolver interface.</summary>
    private sealed class SeedResolver : IGstRateResolver
    {
        private static readonly GstMaster Master = GstMaster.Seed();
        public Task<GstRate> RoomNightAsync(Hcc.Application.Commands.ICommandTransaction? tx, int propertyId, DateOnly night, Hcc.Domain.Money.Paise p, CancellationToken ct = default) => Task.FromResult(Master.RoomNight(night, p));
        public Task<IReadOnlyList<(DateOnly Night, GstRate Rate)>> RoomNightsAsync(Hcc.Application.Commands.ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, Hcc.Domain.Money.Paise n, CancellationToken ct = default)
        {
            var l = new List<(DateOnly, GstRate)>();
            for (var d = from; d < to; d = d.AddDays(1)) l.Add((d, Master.RoomNight(d, n)));
            return Task.FromResult<IReadOnlyList<(DateOnly, GstRate)>>(l);
        }

        public Task<GstRate> ChargeAsync(Hcc.Application.Commands.ICommandTransaction? tx, int propertyId, DateOnly date, GstCategory category, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly IGstRateResolver Gst = new SeedResolver();

    [Fact]
    public void Quotation_tax_follows_the_dated_master_old_rate_before_22_Sep_2025()
    {
        var master = GstMaster.Seed();
        var old = Enumerable.Range(0, 2).Select(i => (new DateOnly(2025, 9, 21).AddDays(i), master.RoomNight(new DateOnly(2025, 9, 21).AddDays(i), new Hcc.Domain.Money.Paise(200000)))).ToList();
        Assert.Equal(new[] { 1200, 500 }, old.Select(n => n.Item2.Bps));
        Assert.Equal(24000 + 10000, DocumentSql.StayTax(old, 200000));
        Assert.Equal(2 * 10000, DocumentSql.StayTax([(new DateOnly(2026, 10, 23), master.RoomNight(new DateOnly(2026, 10, 23), new Hcc.Domain.Money.Paise(200000)))], 200000) * 2);
    }

    private Caller C(int principal) => new(db.PropertyId, principal);

    [Fact]
    public async Task Folio_header_and_quotation_come_from_the_reservation()
    {
        var sql = new DocumentSql(db.Platform, _policy, Gst);
        var h = await sql.FolioHeaderAsync(C(db.Users["OWN"]), _folio, Ct);
        Assert.Equal(("Ananya Rao", "101", "HCC-2001"), (h.Guest, h.Room, h.Booking));
        Assert.Equal(new DateOnly(2026, 10, 23), h.Arrival);
        var q = await sql.QuotationAsync(C(db.Users["REC"]), "HCC-2001", Ct);
        Assert.Equal(200000, q.RatePaise);
        Assert.Equal(2 * 10000, q.TaxPaise);
        Assert.Equal(100000, q.AdvancePaise);
        Assert.Equal(2, q.Guests);
        var m = DocumentBuilders.Quotation(q, "Hotel Parth Executive");
        Assert.Equal(2L * 200000 + 20000, m.Tables[0].Rows[0].Cells["total"]);
        RendererTests.ThreeWay(DocumentResults.ToReport(m, DateTimeOffset.UnixEpoch, "T"));
    }

    [Fact]
    public async Task Quotation_is_role_checked_and_needs_an_agreed_rate()
    {
        var sql = new DocumentSql(db.Platform, _policy, Gst);
        var denied = await Assert.ThrowsAsync<HccException>(() => sql.QuotationAsync(C(db.Users["HKP"]), "HCC-2001", Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, denied.Code);
        await Assert.ThrowsAsync<HccException>(() => sql.QuotationAsync(C(db.Users["OWN"]), "HCC-2002", Ct));
        var missing = await Assert.ThrowsAsync<HccException>(() => sql.QuotationAsync(C(db.Users["OWN"]), "HCC-9999", Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, missing.Code);
        var nofolio = await Assert.ThrowsAsync<HccException>(() => sql.FolioHeaderAsync(C(db.Users["OWN"]), 999999, Ct));
        Assert.Equal(HccErrorCode.NOT_FOUND, nofolio.Code);
    }

    [Fact]
    public async Task Registration_card_masks_the_id_number_in_every_output()
    {
        var l = await LodgeLayouts.RegistrationCardAsync(db.Platform, C(db.Users["REC"]), "HCC-2001", Ct);
        var m = DocumentBuilders.FromLodge(l, "doc.registration-card", "Guest registration card", "HCC-2001", "Hotel Parth Executive", false);
        Assert.Equal("XXXX-XXXX-4101", l.Pairs.First(x => x.Label == "ID number").Value);
        Assert.DoesNotContain("100079324101", DocumentHtml.FullPage(m), StringComparison.Ordinal);
        Assert.False(m.Money);
        RendererTests.ThreeWay(DocumentResults.ToReport(m, DateTimeOffset.UnixEpoch, "T"));
        var (pages, lines) = PdfText.Extract(new PdfRenderer().Render(DocumentResults.ToReport(m, DateTimeOffset.UnixEpoch, "T"), new RenderContext("HPE", "Hotel", ReportFormat.Pdf)).Bytes);
        Assert.True(pages >= 1);
        Assert.Contains(lines, x => x.Contains("XXXX-XXXX-4101", StringComparison.Ordinal));
    }
}
