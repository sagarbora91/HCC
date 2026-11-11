using Hcc.Application.Accounting;
using Hcc.Application.Groups;
using Hcc.Domain.Money;
using Hcc.Reporting.Reports.P2B;

namespace Hcc.Reporting.Tests.P2B;

/// <summary>CU f8e-open P2-B: the company-credit statement opens at the company's opening outstanding (prototype <c>openingOutstanding</c>), read from the approved
/// opening entry's party line 1110/CMP-id. Same figures as <c>tests/stream-cu-f8e-open.mjs</c> (40000 opening, 30000 + 2000 TDS paid, 8000 closing).</summary>
public sealed class CuF8eOpenCompanyOpeningTests
{
    private static readonly DateOnly D1 = new(2026, 11, 1);

    private static OpeningEntry Entry(string status, params OpeningLine[] lines) =>
        new(1, new DateOnly(2026, 4, 1), status, lines, 1, DateTime.UtcNow, status == "approved" ? 1 : null, status == "approved" ? DateTime.UtcNow : null, null, "v1");

    [Fact]
    public void Opening_comes_only_from_the_approved_entry_line_of_that_company()
    {
        var approved = Entry("approved", new OpeningLine("1110/CMP-7", 40000, 0), new OpeningLine("1110/CMP-8", 99999, 0), new OpeningLine("1000/T1", 5, 0), new OpeningLine("3100", 0, 40004));
        Assert.Equal(40000L, CompanyCreditReport.OpeningOf(approved, 7));
        Assert.Equal(99999L, CompanyCreditReport.OpeningOf(approved, 8));
        Assert.Equal(0L, CompanyCreditReport.OpeningOf(approved, 9));
        Assert.Equal(0L, CompanyCreditReport.OpeningOf(Entry("draft", new OpeningLine("1110/CMP-7", 40000, 0)), 7));   // a draft never counts
        Assert.Equal(0L, CompanyCreditReport.OpeningOf(null, 7));
        Assert.Equal(15000L, CompanyCreditReport.OpeningOf(Entry("approved", new OpeningLine("1110/CMP-7", 40000, 0), new OpeningLine("1110/CMP-7", 0, 25000)), 7));   // net of credit lines
        Assert.Equal(-5000L, CompanyCreditReport.OpeningOf(Entry("approved", new OpeningLine("1110/CMP-7", 0, 5000)), 7));   // an advance stays negative, as in the party ledger and ageing
    }

    [Fact]
    public void Statement_runs_from_the_opening_to_the_paise_and_defaults_to_zero()
    {
        var facts = new CompanyCreditFacts(D1, [], [new(7, D1, "NEFT", "UTR1", null, new Paise(30000), 1, DateTime.UtcNow, new Paise(2000))], [], []);
        var rows = CompanyCreditReport.Statement(facts, 40000);
        Assert.Equal(40000L, rows[0].Cells["balance"]);
        Assert.Equal(8000L, rows[1].Cells["balance"]);
        Assert.Equal((0L, 32000L, 8000L), (rows[^1].Cells["credit"], rows[^1].Cells["payment"], rows[^1].Cells["balance"]));
        Assert.Equal(-32000L, CompanyCreditReport.Statement(facts)[^1].Cells["balance"]);   // old overload unchanged: no opening
    }

    [Fact]
    public void Statement_from_a_credit_opening_keeps_the_advance_and_matches_the_prototype()
    {
        // Cr 5,000 advance, then a 10,000 company credit: owes 5,000 (party ledger 1110/CMP-7 and ageing agree). Prototype: openingOutstanding -5000.
        var facts = new CompanyCreditFacts(D1, [new(1, 10, "R-1", D1, new Paise(10000), "INV-1")], [], [], []);
        var rows = CompanyCreditReport.Statement(facts, -5000);
        Assert.Equal(-5000L, rows[0].Cells["balance"]);
        Assert.Equal(5000L, rows[1].Cells["balance"]);
        Assert.Equal(5000L, rows[^1].Cells["balance"]);
    }
}
