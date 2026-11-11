// FX-RPT-C · rpt.admin.report-dictionary: the twelve static rows of the prototype dictionary, no money, no date; PERM-RPT-001 / 002 holders (Owner, Manager, Accountant) see it,
// the Cashier (own-shift grant only), Reception and Housekeeping are refused. Disposable HccTest_* database for the real access policy.
using Hcc.Reporting.Tests.CuB;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxC;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.FxC;

[Trait("Category", "Sql")]
public sealed class ReportDictionaryReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>
{
    [Fact]
    public async Task Twelve_rows_word_for_word_with_the_zero_handling_column()
    {
        var r = await Run(db, Runner(), "OWN", ReportDictionaryReport.Id);
        Assert.Equal(12, r.Rows.Count);
        Assert.Equal("Sellable rooms", S(r.Rows[0], "metric"));
        Assert.Equal("sellable = 0 -> \"—\"", S(r.Rows[2], "zero"));
        Assert.Equal("GST (CGST + SGST)", S(r.Rows[11], "metric"));
        Assert.Equal(ReportDictionaryReport.Entries.Select(e => e.Metric), r.Rows.Select(x => S(x, "metric")));
        Assert.DoesNotContain(r.Columns, c => c.Money);
        Assert.All(r.Rows, x => Assert.Equal(Hcc.Application.Reporting.Rpt.ReportRowKind.Row, x.Kind));
    }

    private Hcc.Reporting.ReportRunner Runner() => CuBRig.Runner(db, new ReportDictionaryReport());

    [Fact]
    public async Task Finance_and_operations_report_holders_may_run_it_everyone_else_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, ReportDictionaryReport.Id)));
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, ReportDictionaryReport.Id)));
    }
}
