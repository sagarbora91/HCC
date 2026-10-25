using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Accounting;

/// <summary>
/// Exports (prototype <c>accCsv</c>, <c>accTallyXml</c>): the generic voucher CSV and the SIMULATED Tally "Import Data" XML.
/// Building or saving the XML is NOT "imported into Tally"; only Tally's own import confirms that (ETP Tally preparation rule).
/// Tally convention: debit = ISDEEMEDPOSITIVE Yes with a negative amount, credit = No with a positive amount.
/// </summary>
public static partial class VoucherExport
{
    public const int TallyMaxDays = 31;

    public const string CsvHeader = "Date,Voucher No,Voucher Type,Ledger,Debit,Credit,Narration,Source ID";

    public const string TallyComment = "<!-- SIMULATED EXPORT · Hotel Control Centre · nothing was sent to Tally · saving this file does not mean it was imported -->";

    [GeneratedRegex(@"^[+-]?\d[\d,]*(\.\d+)?$")]
    private static partial Regex Numeric();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex XmlControl();

    /// <summary>Formula-injection guard (prototype csvSafe): text starting with = + - @ tab CR gets a leading apostrophe unless numeric.</summary>
    public static string CsvSafe(string t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.Length > 0 && "=+-@\t\r".Contains(t[0], StringComparison.Ordinal) && !Numeric().IsMatch(t) ? "'" + t : t;
    }

    /// <summary>Quoted only when needed (prototype csvMinimal).</summary>
    public static string CsvCell(string v)
    {
        var t = CsvSafe(v);
        return t.AsSpan().IndexOfAny("\",\r\n") >= 0 ? "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : t;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>One row per voucher entry; rupees with 2 decimals; CRLF (spec §9).</summary>
    public static string Csv(IEnumerable<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var rows = new List<string> { CsvHeader };
        foreach (var v in vouchers)
            foreach (var e in v.Entries)
                rows.Add(string.Join(",", new[]
                {
                    Iso(v.Date), v.Number, v.Type.ToString(), ChartOfAccounts.NameOf(e.Account),
                    e.DebitPaise != 0 ? SourceRules.Rupees(e.DebitPaise) : "", e.CreditPaise != 0 ? SourceRules.Rupees(e.CreditPaise) : "", v.Narration, v.SourceKey,
                }.Select(CsvCell)));
        return string.Join("\r\n", rows);
    }

    public static string XmlEscape(string t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return XmlControl().Replace(t, "").Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>Range rule: from ≤ to, at most 31 days. Returns an error message or null.</summary>
    public static string? TallyRangeError(DateOnly from, DateOnly to)
    {
        if (from > to) return "Choose a valid range: \"from\" on or before \"to\".";
        return to.DayNumber - from.DayNumber + 1 > TallyMaxDays ? $"Choose at most {TallyMaxDays} days." : null;
    }

    /// <summary>Vouchers as a Tally "Import Data" envelope (simulated; nothing is sent anywhere).</summary>
    public static string TallyXml(IEnumerable<Voucher> vouchers, string company)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(company);
        static string Td(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var body = string.Join("\n", vouchers.Select(v =>
        {
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"   <TALLYMESSAGE xmlns:UDF=\"TallyUDF\"><VOUCHER VCHTYPE=\"{v.Type}\" ACTION=\"Create\" OBJVIEW=\"Accounting Voucher View\">\n");
            sb.Append(CultureInfo.InvariantCulture, $"    <DATE>{Td(v.Date)}</DATE><VOUCHERTYPENAME>{v.Type}</VOUCHERTYPENAME><VOUCHERNUMBER>{XmlEscape(v.Number)}</VOUCHERNUMBER><NARRATION>{XmlEscape(v.Narration)}</NARRATION><EFFECTIVEDATE>{Td(v.Date)}</EFFECTIVEDATE>\n");
            sb.Append(string.Join("\n", v.Entries.Select(e =>
                $"    <ALLLEDGERENTRIES.LIST><LEDGERNAME>{XmlEscape(ChartOfAccounts.NameOf(e.Account))}</LEDGERNAME><ISDEEMEDPOSITIVE>{(e.DebitPaise != 0 ? "Yes" : "No")}</ISDEEMEDPOSITIVE><AMOUNT>{(e.DebitPaise != 0 ? "-" + SourceRules.Rupees(e.DebitPaise) : SourceRules.Rupees(e.CreditPaise))}</AMOUNT></ALLLEDGERENTRIES.LIST>")));
            sb.Append("\n   </VOUCHER></TALLYMESSAGE>");
            return sb.ToString();
        }));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + TallyComment + "\n<ENVELOPE>\n <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>\n <BODY><IMPORTDATA>\n"
            + $"  <REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME><STATICVARIABLES><SVCURRENTCOMPANY>{XmlEscape(company)}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>\n"
            + "  <REQUESTDATA>\n" + body + "\n  </REQUESTDATA>\n </IMPORTDATA></BODY>\n</ENVELOPE>\n";
    }
}
