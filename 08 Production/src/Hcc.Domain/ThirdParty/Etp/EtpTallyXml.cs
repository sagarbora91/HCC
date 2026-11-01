using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Hcc.Domain.ThirdParty.Etp;

/// <summary>
/// Adapted from ETP <c>TallyXmlExportService</c> (E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\src\Etp.Reporting.Infrastructure.SqlServer\AccountingAndSharingServices.cs,
/// the owner's own code, DEC-054; provenance entry ETP-TALLY in 08 Production/third-party/source-reuse.json). Only three ideas are carried over,
/// nothing else of that class: (1) the <see cref="XmlWriter"/> envelope with UTF-8 without a byte-order mark and indentation, (2) the Tally amount
/// rule — a debit is a negative amount with ISDEEMEDPOSITIVE Yes, a credit a positive amount with No, two decimals, invariant culture — here over
/// integer paise instead of decimals, and (3) the SHA-256 of the written bytes as the file's fingerprint. Change notice: synchronous and in memory
/// (the caller returns the file as a string / bytes and logs the hash), CRLF line ends, one-space indentation, no file or batch types.
/// </summary>
public static class EtpTallyXml
{
    /// <summary>Writer settings of an HCC Tally file: UTF-8 without BOM, one-space indentation, CRLF (Windows line ends).</summary>
    public static XmlWriterSettings Settings() => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        IndentChars = " ",
        NewLineChars = "\r\n",
        NewLineHandling = NewLineHandling.Replace,
        CheckCharacters = true,
    };

    /// <summary>Tally amount of an entry: debit (<paramref name="net"/> &gt; 0) negative, credit positive, rupees with two decimals.</summary>
    public static string Amount(long net)
    {
        var a = Math.Abs(net);
        var text = (a / 100).ToString(CultureInfo.InvariantCulture) + "." + (a % 100).ToString("00", CultureInfo.InvariantCulture);
        return net > 0 ? "-" + text : text;
    }

    /// <summary>Lower-case hexadecimal SHA-256 of the bytes (the file's fingerprint in the export log).</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
