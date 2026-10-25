using System.Globalization;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Money;

/// <summary>
/// Money as integer paise (DEC-023). Signed: charges/adjustments may be negative; receipts are checked with
/// <see cref="EnsureReceiptAmount"/>. Arithmetic is checked (overflow throws), never floating point.
/// </summary>
public readonly record struct Paise(long Value) : IComparable<Paise>
{
    public static readonly Paise Zero = new(0);

    /// <summary>₹10 crore per field (contracts §2; SQL CHECK on receipts).</summary>
    public static readonly Paise MaxPerField = new(ApiConventions.MaxPaisePerField);

    public static Paise FromRupees(long rupees) => new(checked(rupees * 100));

    public static Paise operator +(Paise a, Paise b) => new(checked(a.Value + b.Value));
    public static Paise operator -(Paise a, Paise b) => new(checked(a.Value - b.Value));
    public static Paise operator -(Paise a) => new(checked(-a.Value));
    public static bool operator <(Paise a, Paise b) => a.Value < b.Value;
    public static bool operator >(Paise a, Paise b) => a.Value > b.Value;
    public static bool operator <=(Paise a, Paise b) => a.Value <= b.Value;
    public static bool operator >=(Paise a, Paise b) => a.Value >= b.Value;

    public static Paise Add(Paise a, Paise b) => a + b;
    public static Paise Subtract(Paise a, Paise b) => a - b;
    public static Paise Negate(Paise a) => -a;

    public int CompareTo(Paise other) => Value.CompareTo(other.Value);

    /// <summary>Receipt amounts: &gt; 0 and ≤ ₹10 crore (SQL <c>CHECK (amount_paise &gt; 0 AND amount_paise &lt;= 10^10)</c>).</summary>
    public Paise EnsureReceiptAmount()
    {
        if (Value <= 0 || Value > ApiConventions.MaxPaisePerField)
            throw HccException.Validation("Amount must be more than ₹0 and at most ₹10 crore.");
        return this;
    }

    /// <summary>Any money field on the wire: |value| ≤ ₹10 crore (therefore always a JSON-safe integer).</summary>
    public Paise EnsureFieldRange()
    {
        if (Value < -ApiConventions.MaxPaisePerField || Value > ApiConventions.MaxPaisePerField)
            throw HccException.Validation("Amount is outside the allowed range (±₹10 crore).");
        return this;
    }

    /// <summary>Adapter boundary check for numbers arriving from JSON or drivers (contracts §4 rule 3).</summary>
    public static Paise FromWire(long value)
    {
        if (value > ApiConventions.JsonSafeIntegerMax || value < -ApiConventions.JsonSafeIntegerMax)
            throw HccException.Validation("Amount is not a safe integer.");
        return new Paise(value).EnsureFieldRange();
    }

    /// <summary>Indian grouping like the prototype's <c>money()</c>: ₹10,750 · ₹123.45 · -₹123.45 · ₹1,00,000.</summary>
    public override string ToString()
    {
        var abs = Value < 0 ? -(decimal)Value : Value;
        var rupees = decimal.Truncate(abs / 100m);
        var paise = (long)(abs % 100m);
        var text = "₹" + GroupIndian(rupees.ToString("0", CultureInfo.InvariantCulture))
            + (paise != 0 ? "." + paise.ToString("00", CultureInfo.InvariantCulture) : "");
        return Value < 0 ? "-" + text : text;
    }

    private static string GroupIndian(string digits)
    {
        if (digits.Length <= 3) return digits;
        var last3 = digits[^3..];
        var rest = digits[..^3];
        var parts = new List<string>();
        while (rest.Length > 2)
        {
            parts.Insert(0, rest[^2..]);
            rest = rest[..^2];
        }

        if (rest.Length > 0) parts.Insert(0, rest);
        return string.Join(",", parts) + "," + last3;
    }
}
