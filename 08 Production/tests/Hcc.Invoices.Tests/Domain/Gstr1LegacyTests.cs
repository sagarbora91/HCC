using Hcc.Domain.Invoices;

namespace Hcc.Invoices.Tests.Domain;

/// <summary>LF B7 (LF-CONTRACT §5): the legacy Lodge series as an LGC row of Table 13 (never in the rate tables or Table 12).</summary>
public class Gstr1LegacyTests
{
    [Fact]
    public void Legacy_row_first_with_first_last_count_cancelled_and_nothing_else_changes()
    {
        var plain = Gstr1Summary.Build([], []);
        var r = Gstr1Summary.Build([], [], [new(1204, new DateOnly(2026, 5, 4), false), new(1199, new DateOnly(2026, 5, 2), true), new(1210, new DateOnly(2026, 5, 9), false)]);
        var lgc = Assert.Single(r.Documents);
        Assert.Equal(new Gstr1DocRow("LGC", "1199", "1210", 3, 1), lgc);
        Assert.Equal(plain.Rates, r.Rates);
        Assert.Equal(plain.Sac, r.Sac);
        Assert.Empty(Gstr1Summary.Build([], [], []).Documents);
        Assert.Equal("Legacy Lodge bills (plain numbers, before cut-over)", Gstr1Summary.LegacyLabel);
    }

    [Fact]
    public void Table12_rows_carry_uqc_oth_and_descriptions()
    {
        Assert.Equal("Room or unit accommodation", Gstr1Summary.SacDescription("996311"));
        Assert.Equal("Other services", Gstr1Summary.SacDescription("123456"));
        Assert.Empty(Gstr1Summary.Table12(Gstr1Summary.Build([], []), []));
    }
}
