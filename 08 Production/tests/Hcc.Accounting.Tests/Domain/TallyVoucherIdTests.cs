using System.Text.RegularExpressions;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// A03b-05: every Tally voucher carries a REMOTEID and the same GUID made of the hotel's identity and the voucher's source key, so the
/// same voucher has the same id in every export (and Tally replaces it on a re-import instead of creating it twice); the export notes
/// say what the accountant has to do now that a re-import no longer needs the day deleted first.
/// </summary>
public sealed partial class TallyVoucherIdTests
{
    private static readonly DateOnly D = new(2026, 10, 24);

    [GeneratedRegex("<VOUCHER REMOTEID=\"([^\"]+)\" VCHTYPE=\"[A-Za-z]+\" ACTION=\"Create\" OBJVIEW=\"Accounting Voucher View\">\n    <DATE>\\d{8}</DATE><GUID>([^<]+)</GUID>")]
    private static partial Regex VoucherIds();

    private static IReadOnlyList<Voucher> Day(params AccountingSource[] sources) => VoucherBuilder.Build(D, sources, ChartOfAccounts.Effective(new Dictionary<string, string>()));

    private static AccountingSource Expense(int id, long amount) =>
        SourceRules.Expense("expense:" + id, false, "Utilities", "Cash", D, amount, "Demo Power (fictional) · Utilities · Cash");

    private static List<(string RemoteId, string Guid)> Ids(string xml) => VoucherIds().Matches(xml).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    [Fact]
    public void Every_voucher_gets_a_REMOTEID_equal_to_its_GUID_from_identity_and_source_key_the_same_in_every_export_and_unique()
    {
        var first = VoucherExport.TallyXml(Day(Expense(1, 10_000), Expense(2, 20_000)), "HCC Demo Residency", "0123456789abcdef0123456789abcdef");
        var ids = Ids(first);
        Assert.Equal([("HCC-0123456789abcdef0123456789abcdef-expense:1", "HCC-0123456789abcdef0123456789abcdef-expense:1"),
            ("HCC-0123456789abcdef0123456789abcdef-expense:2", "HCC-0123456789abcdef0123456789abcdef-expense:2")], ids);

        // A later source on the same day: the earlier vouchers keep their ids (and their numbers), the new one gets its own.
        var again = Ids(VoucherExport.TallyXml(Day(Expense(1, 10_000), Expense(2, 20_000), Expense(3, 5_000)), "HCC Demo Residency", "0123456789abcdef0123456789abcdef"));
        Assert.Equal(ids, again.Take(2));
        Assert.Equal(3, again.Select(x => x.RemoteId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("HCC-0123456789abcdef0123456789abcdef-expense:3", again[2].RemoteId);

        // Another hotel identity (another property or a reinstalled database) never reuses an id.
        Assert.DoesNotContain(Ids(VoucherExport.TallyXml(Day(Expense(1, 10_000)), "HCC Demo Residency", "fedcba9876543210fedcba9876543210"))[0].RemoteId, ids.Select(x => x.RemoteId));
        Assert.Equal("HCC-DEMO-a&b", VoucherExport.RemoteId("DEMO", "a&b"));
        Assert.Contains("REMOTEID=\"HCC-DEMO-a&amp;b\"", VoucherExport.TallyXml(Day(SourceRules.Expense("a&b", false, "Utilities", "Cash", D, 100, "x")), "H", "DEMO"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_vouchers_with_one_source_key_cannot_share_a_file_and_the_id_note_follows_the_simulated_comment()
    {
        var twice = Day(Expense(7, 100)).Concat(Day(Expense(7, 100))).ToList();
        Assert.Throws<InvalidOperationException>(() => VoucherExport.TallyXml(twice, "H", "DEMO"));
        Assert.Throws<ArgumentException>(() => VoucherExport.TallyXml(Day(Expense(1, 100)), "H", " "));
        var lines = VoucherExport.TallyXml([], "H", "DEMO").Split('\n');
        Assert.Equal(VoucherExport.TallyComment, lines[1]);
        Assert.Equal(VoucherExport.TallyIdNote, lines[2]);
        Assert.Contains("Overwrite vouchers when a voucher with same GUID exists", VoucherExport.TallyIdNote, StringComparison.Ordinal);
        Assert.DoesNotContain("--", VoucherExport.TallyIdNote[4..^3], StringComparison.Ordinal);
    }

    [Fact]
    public void Re_export_notes_no_longer_ask_to_delete_first_except_for_a_day_exported_before_the_ids()
    {
        Assert.DoesNotContain("Delete", VoucherExport.ReexportNote(D, "Wrong folio"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("RE-EXPORT · 2026-10-24 changed after it was exported (day reopened: Wrong folio). Its vouchers keep their REMOTEID",
            VoucherExport.ReexportNote(D, "Wrong folio"), StringComparison.Ordinal);
        Assert.StartsWith("RE-EXPORT · 2026-10-24 was exported while it was provisional (3 accounting event(s) were still pending)",
            VoucherExport.ReexportAfterProvisionalNote(D, Hcc.Application.Accounting.AccountingQueries.PendingFlagReason(3)), StringComparison.Ordinal);
        Assert.StartsWith("PROVISIONAL · 2026-10-24 is not locked and 2 accounting event(s) of this hotel are not yet in the vouchers",
            VoucherExport.PendingNote(D, 2), StringComparison.Ordinal);
        Assert.StartsWith("PROVISIONAL · 2026-10-24 is reopened and may still change", VoucherExport.ReopenedNote(D), StringComparison.Ordinal);
        Assert.Contains("delete that day's HCC vouchers from it in Tally before importing this file", VoucherExport.LegacyNote(D), StringComparison.Ordinal);
        Assert.True(Hcc.Application.Accounting.AccountingQueries.PendingFlagReason(int.MaxValue).Length <= 200); // fits acc.tally_reexport.reason
    }
}
