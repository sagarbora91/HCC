using Hcc.Domain.Import;

namespace Hcc.Import.Tests.Kernel;

/// <summary>CSV reading, rupee parsing/rounding, planner rules and the shipped templates (no database).</summary>
public class ImportKernelTests
{
    private static ImportSnapshot Empty(bool closed = false) => new(new DateOnly(2026, 10, 26), closed, [new SnapRoomType(1, "DLX", "Deluxe", 3, 420000, 1)], [], [], [],
        [new SnapRole("OWN", "Owner", "PASSWORD"), new SnapRole("REC", "Reception", "PIN"), new SnapRole("ACC", "Accountant", "PASSWORD")], [], [], null, [], [], new HashSet<string>());

    private static CsvTable Csv(string text) => CsvParser.TryParse(text, out var e) ?? throw new InvalidOperationException(e);

    [Theory]
    [InlineData("4200", 420000, false)]
    [InlineData("4,200.50", 420050, false)]
    [InlineData("₹ 1,25,000", 12500000, false)]
    [InlineData("Rs. 99.5", 9950, false)]
    [InlineData("99.555", 9956, true)]   // half away from zero: 9955.5 → 9956
    [InlineData("99.545", 9955, true)]   // 9954.5 → 9955 (not banker's 9954)
    [InlineData("0.005", 1, true)]
    [InlineData("0.004", 0, true)]
    [InlineData("100000000", 10000000000, false)]
    public void Rupees_parse_to_paise_half_away_from_zero(string text, long paise, bool rounded)
    {
        Assert.True(ImportValues.TryRupees(text, out var p, out var r, out var err), err);
        Assert.Equal(paise, p);
        Assert.Equal(rounded, r);
    }

    [Theory]
    [InlineData("-100")]
    [InlineData("(100)")]
    [InlineData("42OO")]
    [InlineData("1.2.3")]
    [InlineData("")]
    [InlineData("100000000.01")]
    public void Bad_or_negative_amounts_are_refused(string text) => Assert.False(ImportValues.TryRupees(text, out _, out _, out _));

    [Fact]
    public void Csv_reads_bom_quotes_line_breaks_crlf_and_semicolons()
    {
        var t = Csv("﻿name,notes\r\n\"Rao, Asha\",\"line1\nline2 \"\"quoted\"\"\"\r\n\r\n,\r\nB,x");
        Assert.Equal(["name", "notes"], t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("Rao, Asha", t.Rows[0].Cells[0]);
        Assert.Equal("line1\nline2 \"quoted\"", t.Rows[0].Cells[1]);
        Assert.Equal(2, t.Rows[0].Row);
        Assert.Equal(6, t.Rows[1].Row);   // the multi-line cell and the blank/empty records are skipped in numbering by source line
        var semi = Csv("roomNo;roomType\n101;DLX\n");
        Assert.Equal(';', semi.Delimiter);
        Assert.Equal("DLX", semi.Rows[0].Cells[1]);
        Assert.Null(CsvParser.TryParse("a,b\n\"open", out var err));
        Assert.Contains("never closed", err, StringComparison.Ordinal);
        Assert.Null(CsvParser.TryParse("   ", out _));
    }

    [Fact]
    public void Headers_match_loosely_and_forbidden_columns_are_refused()
    {
        var p = ImportPlanner.Plan(ImportMaster.Rooms, Csv("Room No,ROOM_TYPE,Floor\n101,dlx\n"), Empty());
        Assert.True(p.Valid);
        Assert.Single(p.Warnings);   // Floor ignored
        Assert.Equal(ImportAction.Create, p.Items[0].Action);
        var s = ImportPlanner.Plan(ImportMaster.Staff, Csv("loginName,displayName,roles,PIN\nasha,Asha,REC,123456\n"), Empty());
        Assert.Contains(s.Errors, e => e.Row == 1 && e.Column == "PIN" && e.Message.Contains("not allowed", StringComparison.Ordinal));
        var g = ImportPlanner.Plan(ImportMaster.Guests, Csv("name,phone,Aadhaar Number\nAsha Rao,9820011111,1234\n"), Empty());
        Assert.False(g.Valid);
        Assert.True(ImportMasters.IsForbiddenHeader("password"));
        Assert.True(ImportMasters.IsForbiddenHeader("pan"));
        Assert.False(ImportMasters.IsForbiddenHeader("openingcash"));   // contains "pin" but is not a PIN
        var missing = ImportPlanner.Plan(ImportMaster.Tariffs, Csv("roomType,rate\nDLX,10\n"), Empty());
        Assert.Contains(missing.Errors, e => e.Column == "effectiveFrom" && e.Row == 1);
    }

    [Fact]
    public void Row_level_errors_name_row_column_and_message()
    {
        var p = ImportPlanner.Plan(ImportMaster.Tariffs, Csv("roomType,effectiveFrom,rate\nDLX,2026-11-01,4500\nSTD,2026-11-01,3000\nDLX,2026-10-01,10\nDLX,01-11-2026,4OOO\nDLX,2026-11-01,4500\n"), Empty());
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "roomType" } && e.Message.Contains("STD", StringComparison.Ordinal));
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "effectiveFrom" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "rate" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "effectiveFrom" } && e.Message == "Duplicate of row 2.");
        Assert.Single(p.Items);
    }

    [Fact]
    public void Rounded_amount_is_a_warning_and_stored_in_paise()
    {
        var p = ImportPlanner.Plan(ImportMaster.RoomTypes, Csv("code,name,maxOccupancy,rackRate\nSTE,Suite,4,7500.005\n"), Empty());
        Assert.True(p.Valid);
        Assert.Equal(750001, ((RoomTypeData)p.Items[0].Data!).RackPaise);
        Assert.Contains(p.Warnings, w => w.Column == "rackRate" && w.Message.Contains("₹7,500.01", StringComparison.Ordinal));
    }

    [Fact]
    public void Opening_trial_balance_must_balance_and_needs_setup_state()
    {
        var ok = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,100.50,\n2200,,100.50\n"), Empty(), new DateOnly(2026, 10, 25));
        Assert.True(ok.Valid);
        Assert.Equal(10050, ok.DebitPaise);
        Assert.Equal(10050, ok.CreditPaise);
        var bad = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,100.50,\n2200,,100\n"), Empty(), new DateOnly(2026, 10, 25));
        var e = Assert.Single(bad.Errors);
        Assert.Equal(0, e.Row);
        Assert.Contains("total debits ₹100.50, total credits ₹100, difference ₹0.50", e.Message, StringComparison.Ordinal);
        var both = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,10,10\n9999,,5\nNOPE,1,\n"), Empty(), new DateOnly(2026, 10, 25));
        Assert.Contains(both.Errors, x => x is { Row: 2, Column: "credit" });
        Assert.Contains(both.Errors, x => x is { Row: 3, Column: "ledgerCode" });   // suspense is never a ledger
        Assert.Contains(both.Errors, x => x is { Row: 4, Column: "ledgerCode" });
        var noDate = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,1,\n2200,,1\n"), Empty(), null);
        Assert.Contains(noDate.Errors, x => x.Column == "asOfDate");
        var closed = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,1,\n2200,,1\n"), Empty(closed: true), new DateOnly(2026, 10, 25));
        Assert.Contains(closed.Errors, x => x.Row == 0 && x.Message.Contains("first business day is already closed", StringComparison.Ordinal));
        var till = ImportPlanner.Plan(ImportMaster.TillCash, Csv("tillCode,tillName,openingCash\nFD-1,Desk,100\n"), Empty(closed: true));
        Assert.False(till.Valid);
    }

    [Fact]
    public void Staff_roles_map_codes_or_names_never_owner_and_pick_the_credential_kind()
    {
        var p = ImportPlanner.Plan(ImportMaster.Staff, Csv("loginName,displayName,roles\nAsha.R,Asha,reception\nmeena,Meena,REC;Accountant\nboss,Boss,OWN\nx,Short,REC\n"), Empty());
        Assert.Equal("asha.r", ((StaffData)p.Items[0].Data!).Login);
        Assert.Equal("PIN", ((StaffData)p.Items[0].Data!).CredentialKind);
        Assert.Equal("PASSWORD", ((StaffData)p.Items[1].Data!).CredentialKind);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "roles" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "loginName" });
    }

    [Fact]
    public void Ledgers_refuse_hcc_codes_and_group_mismatches()
    {
        var p = ImportPlanner.Plan(ImportMaster.Ledgers, Csv("code,name,group,hccAccount\n1000,Cash,Asset,\nCAP,Capital,Capital,\nBANK,Bank,Asset,1030\nX,Wrong,Liability,1030\nY,Eq,Equity,1000\n"), Empty());
        Assert.Contains(p.Errors, e => e is { Row: 2, Column: "code" });
        Assert.Equal("Equity", ((LedgerData)p.Items.Single(i => i.Key == "CAP").Data!).Group);
        Assert.Equal("1030", ((LedgerData)p.Items.Single(i => i.Key == "BANK").Data!).HccAccount);
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "hccAccount" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "hccAccount" });
    }

    [Fact]
    public void Shipped_templates_match_the_column_specs_and_every_sample_parses()
    {
        var readme = File.ReadAllText(Path.Combine(Harness.Templates.Folder, "README.md"));
        foreach (var spec in ImportMasters.All)
        {
            var header = Csv(Harness.Templates.Template(spec.Wire) + "\n");
            Assert.Equal(spec.Columns.Select(c => c.Name), header.Headers);
            var sample = Csv(Harness.Templates.Sample(spec.Wire));
            Assert.Equal(spec.Columns.Select(c => c.Name), sample.Headers);
            Assert.NotEmpty(sample.Rows);
            Assert.Contains($"### {spec.Wire}.csv", readme, StringComparison.Ordinal);
            foreach (var c in spec.Columns) Assert.Contains($"| {c.Name} |", readme, StringComparison.Ordinal);
        }
    }
}
