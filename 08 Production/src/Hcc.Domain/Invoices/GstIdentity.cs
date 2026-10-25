using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Invoices;

/// <summary>GST state / union-territory codes (first two digits of a GSTIN; place of supply on invoices).</summary>
public static class GstStates
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.Ordinal)
    {
        ["01"] = "Jammu and Kashmir", ["02"] = "Himachal Pradesh", ["03"] = "Punjab", ["04"] = "Chandigarh", ["05"] = "Uttarakhand",
        ["06"] = "Haryana", ["07"] = "Delhi", ["08"] = "Rajasthan", ["09"] = "Uttar Pradesh", ["10"] = "Bihar", ["11"] = "Sikkim",
        ["12"] = "Arunachal Pradesh", ["13"] = "Nagaland", ["14"] = "Manipur", ["15"] = "Mizoram", ["16"] = "Tripura", ["17"] = "Meghalaya",
        ["18"] = "Assam", ["19"] = "West Bengal", ["20"] = "Jharkhand", ["21"] = "Odisha", ["22"] = "Chhattisgarh", ["23"] = "Madhya Pradesh",
        ["24"] = "Gujarat", ["26"] = "Dadra and Nagar Haveli and Daman and Diu", ["27"] = "Maharashtra", ["29"] = "Karnataka", ["30"] = "Goa",
        ["31"] = "Lakshadweep", ["32"] = "Kerala", ["33"] = "Tamil Nadu", ["34"] = "Puducherry", ["35"] = "Andaman and Nicobar Islands",
        ["36"] = "Telangana", ["37"] = "Andhra Pradesh", ["38"] = "Ladakh", ["97"] = "Other Territory",
    };

    public static bool IsCode(string? code) => code is not null && ByCode.ContainsKey(code);

    public static string Name(string code) => ByCode.TryGetValue(code, out var n) ? n : code;

    /// <summary>Code of a state by name (property setup stores the state name), or null.</summary>
    public static string? CodeOf(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        return ByCode.FirstOrDefault(kv => string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)).Key;
    }

    /// <summary>A 2-digit place-of-supply code from the request (null = not given).</summary>
    public static string? ParsePlaceOfSupply(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim();
        if (!IsCode(c)) throw HccException.Validation("Place of supply must be a 2-digit GST state code (for example 27 for Maharashtra).");
        return c;
    }
}

/// <summary>
/// GSTIN check: 15 characters = state code (2) + PAN (5 letters, 4 digits, 1 letter) + entity number (1–9, A–Z) + 'Z' + check character.
/// The check character is the GSTN mod-36 checksum (weights 1, 2 alternating; product folded as quotient + remainder of 36). Only the
/// format and checksum are verified — the GST portal is not queried (CA/owner: a live GSTIN search is a later integration).
/// </summary>
public static partial class Gstin
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [GeneratedRegex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex Shape();

    public static char CheckCharacter(string first14)
    {
        ArgumentNullException.ThrowIfNull(first14);
        if (first14.Length != 14) throw new ArgumentException("Fourteen characters expected.", nameof(first14));
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var v = Alphabet.IndexOf(first14[i], StringComparison.Ordinal);
            if (v < 0) throw new ArgumentException("Invalid GSTIN character.", nameof(first14));
            var p = v * (i % 2 == 0 ? 1 : 2);
            sum += p / 36 + p % 36;
        }

        return Alphabet[(36 - sum % 36) % 36];
    }

    public static bool IsValid(string? gstin)
    {
        if (gstin is null || !Shape().IsMatch(gstin)) return false;
        return GstStates.IsCode(gstin[..2]) && CheckCharacter(gstin[..14]) == gstin[14];
    }

    /// <summary>Upper-cased, trimmed and validated; VALIDATION with a clear message otherwise.</summary>
    public static string Validate(string? gstin, string whose)
    {
        var g = (gstin ?? string.Empty).Trim().ToUpperInvariant();
        if (g.Length == 0) throw HccException.Validation($"Enter the {whose} GSTIN.");
        if (!Shape().IsMatch(g)) throw HccException.Validation($"The {whose} GSTIN must be 15 characters: state code, PAN, entity number, Z and a check character.");
        if (!GstStates.IsCode(g[..2])) throw HccException.Validation($"The {whose} GSTIN starts with an unknown state code ({g[..2]}).");
        if (CheckCharacter(g[..14]) != g[14]) throw HccException.Validation($"The {whose} GSTIN check character is wrong; check the number for a typing error.");
        return g;
    }

    public static string StateCode(string gstin) => gstin[..2];
}

/// <summary>
/// SAC (services accounting code) per supply category, printed on every invoice line. ILLUSTRATIVE — CA TO CONFIRM (rule 46 needs the
/// SAC at 4 digits up to ₹5 crore turnover, 6 above; we print 6).
/// </summary>
public static class SacCodes
{
    public static string For(GstCategory category) => category switch
    {
        GstCategory.Room => "996311",     // room or unit accommodation services
        GstCategory.Food => "996331",     // services by restaurants / room service
        GstCategory.Laundry => "999712",  // laundry and dry-cleaning services
        GstCategory.Banquet => "996334",  // banquet / event catering and hall services
        _ => "999799",                    // other services n.e.c.
    };
}

/// <summary>Invoice document types and their numbering.</summary>
public enum InvoiceDocType
{
    TaxInvoice,
    BillOfSupply,
    CreditNote,
}

/// <summary>
/// Number format <c>{prefix}/{fy}/{seq}</c>, e.g. <c>INV/2627/0000001</c> (FY 2026-27): always exactly 16 characters (CGST Rules r.46(b):
/// at most 16, letters, digits, '-' and '/', unique per financial year). The sequence is zero-padded to fill the 16 characters; a
/// prefix longer than 6 would leave fewer than 4 digits and is refused.
/// </summary>
public static partial class InvoiceNumbering
{
    public const int MaxLength = 16;

    public const int MaxPrefix = 6;

    [GeneratedRegex(@"^[A-Z0-9-]{1,6}$")]
    private static partial Regex PrefixShape();

    public static string Code(InvoiceDocType t) => t switch
    {
        InvoiceDocType.TaxInvoice => "INV",
        InvoiceDocType.BillOfSupply => "BOS",
        InvoiceDocType.CreditNote => "CRN",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    public static InvoiceDocType Parse(string code) => code switch
    {
        "INV" => InvoiceDocType.TaxInvoice,
        "BOS" => InvoiceDocType.BillOfSupply,
        "CRN" => InvoiceDocType.CreditNote,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    public static string Label(InvoiceDocType t) => t switch
    {
        InvoiceDocType.TaxInvoice => "Tax invoice",
        InvoiceDocType.BillOfSupply => "Bill of supply",
        _ => "Credit note",
    };

    /// <summary>Prefix of a new series: the tax-invoice prefix comes from Master configuration (invoicePrefix); the others are fixed.</summary>
    public static string DefaultPrefix(InvoiceDocType t) => t switch
    {
        InvoiceDocType.TaxInvoice => "INV",
        InvoiceDocType.BillOfSupply => "BOS",
        _ => "CN",
    };

    /// <summary>"2627" for FY 2026-27 (1 April 2026 – 31 March 2027).</summary>
    public static string FyCode(DateOnly date)
    {
        var fy = FinancialYear.Of(date);
        return string.Create(CultureInfo.InvariantCulture, $"{fy.From.Year % 100:00}{(fy.From.Year + 1) % 100:00}");
    }

    public static string ValidatePrefix(string? prefix)
    {
        var p = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (!PrefixShape().IsMatch(p))
            throw HccException.Validation($"The invoice prefix must be 1–{MaxPrefix} capital letters, digits or hyphens so the number fits the GST limit of {MaxLength} characters. Shorten it in Master configuration (invoicePrefix).");
        return p;
    }

    public static string Format(string prefix, string fyCode, int seq)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(fyCode);
        if (seq <= 0) throw new ArgumentOutOfRangeException(nameof(seq));
        var width = MaxLength - prefix.Length - fyCode.Length - 2;
        var digits = seq.ToString(CultureInfo.InvariantCulture);
        if (digits.Length > width) throw HccException.Validation($"The {prefix} series for FY {fyCode} is full ({width} digits). Use a shorter prefix next year.");
        return prefix + "/" + fyCode + "/" + digits.PadLeft(width, '0');
    }
}
