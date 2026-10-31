using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Legacy;

/// <summary>The figures of one FY that the importer reconciles (contract §5 / §6), computed from the inserted <c>lgc</c> rows.</summary>
public sealed record LegacyFigures(long BillsCount, long NetPaise, long RoomGstPaise, long FbPaise, long ReceiptsCount, long ReceiptsPaise, long RefundsCount, long RefundsPaise,
    long? DuplicateStays = null);

/// <summary>Expected figures of one FY from totals.json; a figure the file does not carry is null (and reported as a difference).</summary>
public sealed record LegacyExpected(string Fy, IReadOnlyDictionary<string, long?> Figures);

public sealed record LegacyTotals(string ExtractorVersion, IReadOnlyDictionary<string, LegacyExpected> PerFy);

public sealed record LegacyFigureDifference(string Figure, long Expected, long Actual);

/// <summary>
/// Pure rules of the legacy importer (LG-HISTORY-CONTRACT §5): the parts, the run-folder path checks, the refusal of ID-number columns, the
/// totals.json reader, the per-FY reconciliation with the ±1 paise per row tolerance, and the idempotency key of a run.
/// </summary>
public static partial class LegacyRules
{
    public const string Guests = "guests", History = "history", Cutover = "cutover";

    public static readonly IReadOnlyList<string> AllParts = [Guests, History, Cutover];

    /// <summary>The reconciled figure names in result order (camelCase as in totals.json and the result JSON).</summary>
    public static readonly IReadOnlyList<string> FigureNames = ["billsCount", "netPaise", "roomGstPaise", "fbPaise", "receiptsCount", "receiptsPaise", "refundsCount", "refundsPaise"];

    public const string DuplicateStaysFigure = "staysDuplicate";

    /// <summary>The <c>doc.id_capture.doc_kind</c> of an image copied from the legacy program (contract §5 rule 1; migration 4605 allows it).</summary>
    public const string CaptureKind = "legacy";

    [GeneratedRegex(@"^\d{4}-\d{2}$")]
    private static partial Regex FyPattern();

    public static bool IsFy(string? fy) => fy is not null && FyPattern().IsMatch(fy);

    /// <summary>Distinct known parts in contract order; null when the list is empty or names an unknown part.</summary>
    public static IReadOnlyList<string>? NormaliseParts(IEnumerable<string?>? parts)
    {
        if (parts is null) return null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parts)
        {
            var k = (p ?? string.Empty).Trim().ToLowerInvariant();
            if (!AllParts.Contains(k)) return null;
            set.Add(k);
        }

        return set.Count == 0 ? null : AllParts.Where(set.Contains).ToList();
    }

    /// <summary>Null when the folder text is acceptable as a server-side path (absolute, local drive, no UNC / device path); else the refusal text.</summary>
    public static string? FolderProblem(string? folder)
    {
        var f = (folder ?? string.Empty).Trim();
        if (f.Length == 0) return "Give the run folder (an absolute path on this server).";
        if (f.Length > 400) return "The folder path is at most 400 characters.";
        if (f.StartsWith(@"\\", StringComparison.Ordinal) || f.StartsWith("//", StringComparison.Ordinal))
            return "Network (UNC) paths are refused: copy the run folder to a local drive of this server.";
        if (!Path.IsPathFullyQualified(f)) return @"The run folder must be an absolute path on this server (for example E:\...\LG-EXTRACT\<run>).";
        return null;
    }

    /// <summary>
    /// True when a CSV column name looks like it holds an ID number (contract §5 rule 1: idNumber, aadhaar, pan, passport, IDNumber …).
    /// Compared on the lower-cased name with every non-alphanumeric character removed.
    /// </summary>
    public static bool IsIdColumn(string? column)
    {
        var sb = new StringBuilder();
        foreach (var c in (column ?? string.Empty).ToLowerInvariant()) if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
        var n = sb.ToString();
        if (n.Length == 0) return false;
        if (n is "pan" or "panno" or "pannumber" or "idno" or "idnum" or "uid" or "uidno" or "voterid" or "epic" or "dlno") return true;
        return n.Contains("idnumber", StringComparison.Ordinal) || n.Contains("aadhaar", StringComparison.Ordinal) || n.Contains("aadhar", StringComparison.Ordinal)
            || n.Contains("passport", StringComparison.Ordinal);
    }

    /// <summary>The run's idempotency key: folder + parts + dry run + SHA-256 of totals.json (lane brief), as "lgc-" + 40 hex characters.</summary>
    public static string IdempotencyKey(string folder, IReadOnlyList<string> parts, bool dryRun, byte[] totalsSha256)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(parts);
        var text = string.Join('\n', folder.Trim().ToUpperInvariant(), string.Join(',', parts), dryRun ? "dry" : "commit", Convert.ToHexString(totalsSha256 ?? []));
        return "lgc-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..40].ToLowerInvariant();
    }

    /// <summary>
    /// Reads totals.json. Accepted layouts: a <c>perFy</c> (or <c>fy</c> / <c>years</c>) object keyed by FY, or an array of objects with an
    /// <c>fy</c> member, or FY keys at the root. Inside an FY the importer's figures are read from <c>after</c> / <c>afterDedup</c> / <c>imported</c>
    /// when present (what the CSVs carry after the §7 de-dup), else from the FY object itself. Names are case-insensitive.
    /// </summary>
    public static LegacyTotals? ParseTotals(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "totals.json is empty.";
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "totals.json must be a JSON object.";
                return null;
            }

            var version = Member(root, "extractorVersion") is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? string.Empty
                : Member(root, "version") is { } v2 ? v2.ToString() : string.Empty;
            var perFy = new Dictionary<string, LegacyExpected>(StringComparer.Ordinal);
            var container = Member(root, "perFy") ?? Member(root, "fy") ?? Member(root, "years");
            if (container is { ValueKind: JsonValueKind.Object } map)
            {
                foreach (var p in map.EnumerateObject())
                    if (IsFy(p.Name) && p.Value.ValueKind == JsonValueKind.Object) perFy[p.Name] = Expected(p.Name, p.Value);
            }
            else if (container is { ValueKind: JsonValueKind.Array } list)
            {
                foreach (var e in list.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && Member(e, "fy") is { ValueKind: JsonValueKind.String } f && IsFy(f.GetString()))
                        perFy[f.GetString()!] = Expected(f.GetString()!, e);
            }
            else
            {
                foreach (var p in root.EnumerateObject())
                    if (IsFy(p.Name) && p.Value.ValueKind == JsonValueKind.Object) perFy[p.Name] = Expected(p.Name, p.Value);
            }

            if (perFy.Count == 0)
            {
                error = "totals.json carries no financial year (expected a perFy object keyed 'YYYY-YY').";
                return null;
            }

            return new LegacyTotals(version.Length > 20 ? version[..20] : version, perFy);
        }
        catch (JsonException)
        {
            error = "totals.json is not valid JSON.";
            return null;
        }
    }

    private static JsonElement? Member(JsonElement o, string name)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in o.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static LegacyExpected Expected(string fy, JsonElement o)
    {
        var inner = Member(o, "after") ?? Member(o, "afterDedup") ?? Member(o, "imported");
        var figures = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var name in FigureNames.Append(DuplicateStaysFigure))
        {
            var m = (inner is { ValueKind: JsonValueKind.Object } i ? Member(i, name) : null) ?? Member(o, name);
            figures[name] = m is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var x) ? x
                : m is { ValueKind: JsonValueKind.String } s && long.TryParse(s.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : null;
        }

        return new LegacyExpected(fy, figures);
    }

    public static long Figure(LegacyFigures f, string name)
    {
        ArgumentNullException.ThrowIfNull(f);
        return name switch
        {
            "billsCount" => f.BillsCount,
            "netPaise" => f.NetPaise,
            "roomGstPaise" => f.RoomGstPaise,
            "fbPaise" => f.FbPaise,
            "receiptsCount" => f.ReceiptsCount,
            "receiptsPaise" => f.ReceiptsPaise,
            "refundsCount" => f.RefundsCount,
            "refundsPaise" => f.RefundsPaise,
            DuplicateStaysFigure => f.DuplicateStays ?? 0,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    /// <summary>
    /// Compares the inserted rows' figures with totals.json (contract §6): counts exactly; money within ±1 paise per row of that table
    /// (bills for net / room GST / F&amp;B, deposit rows for receipts, refund rows for refunds). A figure totals.json lacks is a difference
    /// with expected -1; an FY absent from totals.json therefore differs on every figure. <c>duplicateStays</c> is compared only when present.
    /// </summary>
    public static IReadOnlyList<LegacyFigureDifference> Differences(LegacyExpected? expected, LegacyFigures actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var diffs = new List<LegacyFigureDifference>();
        foreach (var name in FigureNames)
        {
            var a = Figure(actual, name);
            if (expected?.Figures.GetValueOrDefault(name) is not { } e)
            {
                diffs.Add(new LegacyFigureDifference(name, -1, a));
                continue;
            }

            var tolerance = name switch
            {
                "netPaise" or "roomGstPaise" or "fbPaise" => actual.BillsCount,
                "receiptsPaise" => actual.ReceiptsCount,
                "refundsPaise" => actual.RefundsCount,
                _ => 0,
            };
            if (Math.Abs(e - a) > tolerance) diffs.Add(new LegacyFigureDifference(name, e, a));
        }

        if (expected?.Figures.GetValueOrDefault(DuplicateStaysFigure) is { } dup && actual.DuplicateStays is { } ad && dup != ad)
            diffs.Add(new LegacyFigureDifference(DuplicateStaysFigure, dup, ad));
        return diffs;
    }

    /// <summary>ID type text stored on the guest profile for a legacy type code (contract §4 secret file; GuestIdRules.KindOf maps it back).</summary>
    public static string IdTypeLabel(string? legacyType) => (legacyType ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "AADHAAR" => "Aadhaar",
        "PASSPORT" => "Passport",
        "DL" => "Driving licence",
        "VOTER" => "Voter ID",
        "PAN" => "PAN",
        _ => "Other",
    };

    /// <summary>Documents-module kind of a legacy type code (doc.id_capture.ocr_doc_kind of a copied image).</summary>
    public static string DocKindOf(string? legacyType) => (legacyType ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "AADHAAR" => "aadhaar",
        "PASSPORT" => "passport",
        "DL" => "dl",
        "VOTER" => "voter",
        "PAN" => "pan",
        _ => "other",
    };
}
