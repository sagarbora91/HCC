// CU f8e-open FIX 3: the statement opening balance through CompanyCreditReport.SelectAsync on a real database: real company store, real credit store,
// real opening-entry store. A draft opening entry gives 0; the approved one gives 40000; a credit opening gives a negative opening; a missing
// opening store fails loudly. Disposable HccTest_* database. Fictional data.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Groups;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Groups;
using Hcc.Reporting.Reports.P2B;

namespace Hcc.Reporting.Tests.Sql;

[Trait("Category", "Sql")]
public sealed class CuF8eOpenCompanyCreditSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CompanyCreditReport Report(bool withOpenings = true)
    {
        var cf = db.Platform.Connections;
        var companies = new SqlCompanyStore(cf);
        var store = new SqlCompanyCreditStore(cf);
        var credit = new CompanyCredit(null!, companies, store, null!, null!, TimeProvider.System);   // PictureAsync needs only the stores while no group master is billed to the company
        return new CompanyCreditReport(companies, credit, store, withOpenings ? new SqlOpeningStore(cf) : null);
    }

    private Task<ReportResult> Run(CompanyCreditReport r, long companyId) =>
        r.SelectAsync(new Caller(db.PropertyId, db.Users["OWN"]), new BoundParams(new Dictionary<string, string> { ["company"] = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture) }), Ct);

    private static ReportRow[] Statement(ReportResult r) => [.. r.Sections![1].Rows];

    [Fact]
    public async Task Statement_opening_comes_from_the_approved_opening_entry_and_never_from_a_draft()
    {
        var p = db.PropertyId;
        var own = db.Users["OWN"];
        await db.ScalarAsync($"""
            INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) VALUES ({p}, N'Opening Traders (fictional)', 5000000, {own}, SYSUTCDATETIME());
            DECLARE @co BIGINT = SCOPE_IDENTITY();
            INSERT grp.company_payment(company_id, business_date, tender, reference, amount_paise, principal_id, recorded_utc, tds_paise) VALUES (@co, '2026-10-20', 'Bank transfer', N'UTR-CU1', 30000, {own}, SYSUTCDATETIME(), 2000);
            -- a DRAFT opening entry first
            INSERT acc.opening_entry(property_id, as_of, status, entered_by, entered_utc) VALUES ({p}, '2026-04-01', 'draft', {own}, SYSUTCDATETIME());
            INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES ({p}, 1, '1110/CMP-' + CAST(@co AS VARCHAR(20)), 40000, 0), ({p}, 2, '3100', 0, 40000);
            SELECT @co;
            """);
        var cid = Convert.ToInt64(await db.ScalarAsync($"SELECT company_id FROM grp.company_account WHERE property_id = {p} AND name = N'Opening Traders (fictional)'"), System.Globalization.CultureInfo.InvariantCulture);
        var report = Report();

        var draft = Statement(await Run(report, cid));
        Assert.Equal(0L, draft[0].Cells["balance"]);                                  // a draft never counts
        Assert.Equal(-32000L, draft[^1].Cells["balance"]);

        await db.ScalarAsync($"UPDATE acc.opening_entry SET status = 'approved', approved_by = {own}, approved_utc = SYSUTCDATETIME() WHERE property_id = {p};");
        var approved = Statement(await Run(report, cid));
        Assert.Equal("Opening outstanding", approved[0].Cells["narration"]);
        Assert.Equal(40000L, approved[0].Cells["balance"]);
        Assert.Equal(8000L, approved[1].Cells["balance"]);                            // 40000 - (30000 + 2000 TDS)
        Assert.Equal(32000L, approved[^1].Cells["payment"]);
        Assert.Equal(8000L, approved[^1].Cells["balance"]);

        // an advance: the same company's line becomes a credit of 5000 -> the statement opens at -5000 (party ledger and ageing agree)
        await db.ScalarAsync($"UPDATE acc.opening_line SET debit_paise = 0, credit_paise = 5000 WHERE property_id = {p} AND line_no = 1; UPDATE acc.opening_line SET credit_paise = 0, debit_paise = 5000 WHERE property_id = {p} AND line_no = 2;");
        var advance = Statement(await Run(report, cid));
        Assert.Equal(-5000L, advance[0].Cells["balance"]);
        Assert.Equal(-37000L, advance[^1].Cells["balance"]);

        // a company with no opening line opens at 0
        await db.ScalarAsync($"INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) VALUES ({p}, N'No Opening Traders (fictional)', 100, {own}, SYSUTCDATETIME());");
        var other = Convert.ToInt64(await db.ScalarAsync($"SELECT company_id FROM grp.company_account WHERE property_id = {p} AND name = N'No Opening Traders (fictional)'"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0L, Statement(await Run(report, other))[0].Cells["balance"]);
    }

    [Fact]
    public async Task A_host_without_the_opening_store_fails_loudly_instead_of_showing_zero()
    {
        var p = db.PropertyId;
        await db.ScalarAsync($"INSERT grp.company_account(property_id, name, credit_limit_paise, created_by, created_utc) VALUES ({p}, N'Loud Traders (fictional)', 100, {db.Users["OWN"]}, SYSUTCDATETIME());");
        var cid = Convert.ToInt64(await db.ScalarAsync($"SELECT company_id FROM grp.company_account WHERE property_id = {p} AND name = N'Loud Traders (fictional)'"), System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Report(withOpenings: false), cid));
    }
}
