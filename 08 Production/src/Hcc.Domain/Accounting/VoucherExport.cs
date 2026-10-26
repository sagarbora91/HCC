using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Domain.Text;

namespace Hcc.Domain.Accounting;

/// <summary>
/// Exports (prototype <c>accCsv</c>, <c>accTallyXml</c>): the generic voucher CSV and the SIMULATED Tally "Import Data" XML.
/// Building or saving the XML is NOT "imported into Tally"; only Tally's own import confirms that (ETP Tally preparation rule).
/// Tally convention: debit = ISDEEMEDPOSITIVE Yes with a negative amount, credit = No with a positive amount.
/// A03b-05: every voucher carries a stable <c>REMOTEID</c> (and the same <c>GUID</c>) = <see cref="RemoteId"/> of the hotel's Tally
/// identity and the voucher's source key, so the same voucher always has the same id. Tally treats a voucher whose REMOTEID / GUID
/// already exists as that voucher: with "Overwrite vouchers when a voucher with same GUID exists" (TallyPrime Import › Transactions;
/// Tally.ERP 9 F12 "Overwrite vouchers during import") set to Yes, importing it again replaces it instead of adding a second copy.
/// <c>ACTION</c> stays <c>Create</c>: <c>Alter</c> fails for a voucher Tally does not hold yet, and HCC cannot know what was imported.
/// <c>VCHKEY</c> / <c>MASTERID</c> are Tally's own internal keys and are never invented here.
/// </summary>
public static partial class VoucherExport
{
    public const int TallyMaxDays = 31;

    public const string CsvHeader = "Date,Voucher No,Voucher Type,Ledger,Debit,Credit,Narration,Source ID";

    public const string TallyComment = "<!-- SIMULATED EXPORT · Hotel Control Centre · nothing was sent to Tally · saving this file does not mean it was imported -->";

    /// <summary>A03b-05: the fixed comment line after <see cref="TallyComment"/> (same text in the prototype builder) explaining the voucher ids.</summary>
    public const string TallyIdNote = "<!-- VOUCHER IDS · every voucher carries a REMOTEID and the same GUID (HCC-hotel identity-source record) that never changes."
        + " With \"Overwrite vouchers when a voucher with same GUID exists\" set to Yes in Tally's import (TallyPrime: Import › Transactions), importing"
        + " a voucher again replaces it instead of adding a second copy. ACTION stays Create; Tally assigns its own VCHKEY and MASTERID. -->";

    /// <summary>
    /// The Tally <c>REMOTEID</c> / <c>GUID</c> of a voucher: <c>HCC-&lt;identity&gt;-&lt;source key&gt;</c>. The identity is the hotel's own
    /// (server: <c>acc.tally_identity</c>, one random GUID per property, migration 3950; prototype: <c>DEMO</c>) and the source key is unique
    /// per property and never changes, so the id is the same in every export of that voucher (locked snapshot or live) and differs from
    /// every other voucher's — also after a reinstall with a new database, which gets a new identity.
    /// </summary>
    public static string RemoteId(string identity, string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        return "HCC-" + identity + "-" + sourceKey;
    }

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex XmlControl();

    /// <summary>Formula-injection guard (prototype csvSafe): text starting with = + - @ tab CR gets a leading apostrophe unless numeric (<see cref="CsvFormula"/>).</summary>
    public static string CsvSafe(string t) => CsvFormula.Neutralise(t);

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

    // ── Notes of a Tally export (DEC-077 re-export / provisional days; A03b-05 voucher ids, pending events, exports before the ids) ──

    /// <summary>DEC-077 + A03b-05: a reopened day that had been exported, carried again (same text in the prototype builder).</summary>
    public static string ReexportNote(DateOnly date, string reason)
        => $"RE-EXPORT · {Iso(date)} changed after it was exported (day reopened: {reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds the new ones, so nothing needs deleting first.";

    /// <summary>A03b-05: a day exported while accounting events were pending, carried again by the next export.</summary>
    public static string ReexportAfterProvisionalNote(DateOnly date, string reason)
        => $"RE-EXPORT · {Iso(date)} was exported while it was provisional ({reason}). Its vouchers keep their REMOTEID: importing this file replaces them in Tally and adds any that were missing.";

    /// <summary>DEC-077: the day the Owner has reopened (same text in the prototype builder).</summary>
    public static string ReopenedNote(DateOnly date)
        => $"PROVISIONAL · {Iso(date)} is reopened and may still change; it is carried again by the first export after it is closed again, and importing that file replaces these vouchers by their REMOTEID.";

    /// <summary>A03b-05: an unlocked day exported while <paramref name="pending"/> accounting events were not yet in the vouchers.</summary>
    public static string PendingNote(DateOnly date, int pending)
        => $"PROVISIONAL · {Iso(date)} is not locked and {pending.ToString(CultureInfo.InvariantCulture)} accounting event(s) of this hotel are not yet in the vouchers (still being processed, or parked after an error), so this day may be incomplete. It is carried again by the first export after they are processed, and importing that file replaces these vouchers by their REMOTEID.";

    /// <summary>A03b-05: a day that an export made before vouchers carried a REMOTEID took to Tally.</summary>
    public static string LegacyNote(DateOnly date)
        => $"LEGACY · {Iso(date)} was exported before vouchers carried a REMOTEID. If that earlier file was imported, delete that day's HCC vouchers from it in Tally before importing this file, or Tally will hold them twice.";

    /// <summary>An XML comment line with free text (escaped; "--" cannot end a comment early).</summary>
    public static string TallyNote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "<!-- " + XmlEscape(text).Replace("--", "- -", StringComparison.Ordinal) + " -->";
    }

    /// <summary>
    /// Vouchers as a Tally "Import Data" envelope (simulated; nothing is sent anywhere). <paramref name="identity"/> is the hotel's Tally
    /// identity for <see cref="RemoteId"/> (A03b-05); <paramref name="notes"/> (DEC-077 re-export / provisional days) follow the SIMULATED
    /// comment and <see cref="TallyIdNote"/>. Two vouchers with the same source key cannot be in one file (each REMOTEID is unique).
    /// </summary>
    public static string TallyXml(IEnumerable<Voucher> vouchers, string company, string identity, IReadOnlyList<string>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(company);
        static string Td(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var list = vouchers.ToList();
        var ids = list.Select(v => RemoteId(identity, v.SourceKey)).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("Two vouchers in one Tally export share a source key; every REMOTEID must be unique.");
        var body = string.Join("\n", list.Select((v, i) =>
        {
            var sb = new StringBuilder();
            var id = XmlEscape(ids[i]);
            sb.Append(CultureInfo.InvariantCulture, $"   <TALLYMESSAGE xmlns:UDF=\"TallyUDF\"><VOUCHER REMOTEID=\"{id}\" VCHTYPE=\"{v.Type}\" ACTION=\"Create\" OBJVIEW=\"Accounting Voucher View\">\n");
            sb.Append(CultureInfo.InvariantCulture, $"    <DATE>{Td(v.Date)}</DATE><GUID>{id}</GUID><VOUCHERTYPENAME>{v.Type}</VOUCHERTYPENAME><VOUCHERNUMBER>{XmlEscape(v.Number)}</VOUCHERNUMBER><NARRATION>{XmlEscape(v.Narration)}</NARRATION><EFFECTIVEDATE>{Td(v.Date)}</EFFECTIVEDATE>\n");
            sb.Append(string.Join("\n", v.Entries.Select(e =>
                $"    <ALLLEDGERENTRIES.LIST><LEDGERNAME>{XmlEscape(ChartOfAccounts.NameOf(e.Account))}</LEDGERNAME><ISDEEMEDPOSITIVE>{(e.DebitPaise != 0 ? "Yes" : "No")}</ISDEEMEDPOSITIVE><AMOUNT>{(e.DebitPaise != 0 ? "-" + SourceRules.Rupees(e.DebitPaise) : SourceRules.Rupees(e.CreditPaise))}</AMOUNT></ALLLEDGERENTRIES.LIST>")));
            sb.Append("\n   </VOUCHER></TALLYMESSAGE>");
            return sb.ToString();
        }));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + TallyComment + "\n" + TallyIdNote + "\n" + string.Concat((notes ?? []).Select(n => TallyNote(n) + "\n")) + "<ENVELOPE>\n <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>\n <BODY><IMPORTDATA>\n"
            + $"  <REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME><STATICVARIABLES><SVCURRENTCOMPANY>{XmlEscape(company)}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>\n"
            + "  <REQUESTDATA>\n" + body + "\n  </REQUESTDATA>\n </IMPORTDATA></BODY>\n</ENVELOPE>\n";
    }
}
