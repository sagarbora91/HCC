using System.Text.RegularExpressions;

namespace Hcc.Domain.Audit;

/// <summary>F4-T3 qw-audit: pure rules of the audit viewer (prototype <c>domain/streams/audit-viewer.ts</c> and <c>mstMaskMoney</c>).</summary>
public static partial class AuditViewRules
{
    [GeneratedRegex(@"₹\s?[\d,]+(\.\d+)?")]
    private static partial Regex Money();

    /// <summary>Rupee amounts in an audit detail become "₹ ••" for readers without money rights (DEC-061).</summary>
    public static string MaskMoney(string detail) => Money().Replace(detail, "₹ ••");

    /// <summary>Entity type of an audit target ("user:12" → "user"; no colon → the whole target).</summary>
    public static string EntityOf(string target)
    {
        var i = target.IndexOf(':', StringComparison.Ordinal);
        return i < 0 ? target : target[..i];
    }

    /// <summary>Entity id of an audit target ("user:12" → "12"; none → empty).</summary>
    public static string EntityIdOf(string target)
    {
        var i = target.IndexOf(':', StringComparison.Ordinal);
        return i < 0 ? string.Empty : target[(i + 1)..];
    }
}
