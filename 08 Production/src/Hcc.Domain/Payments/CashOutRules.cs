using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Payments;

/// <summary>Kinds of cash leaving a drawer (SQL <c>pay.cash_transfer.kind</c>, migration 3100).</summary>
public enum CashOutKind
{
    /// <summary>Bank deposit: recorded by a Cashier or Manager from their own open shift, confirmed by a different Manager or Owner.</summary>
    Banking,

    /// <summary>Till-to-till transfer: the giver records it from their own open shift, the receiver accepts it on their own open shift.</summary>
    Till,

    /// <summary>Owner drawing: Owner only, from an open shift, with a reason.</summary>
    Drawing,
}

/// <summary>
/// Rules for cash leaving the drawer (DEC-073; audit A03b-01). Expected cash drops when the money is RECORDED, because it has
/// physically left the drawer: a bank deposit is a <see cref="CashMovementKind.Banking"/> movement at once (the confirmation is the
/// control, not the cash event), a transfer is a <see cref="CashMovementKind.TransferOut"/> on the giver's shift, and a drawing is an
/// <see cref="CashMovementKind.OwnerDrawing"/>. A rejected deposit or a returned transfer puts the cash back with a movement of the same
/// kind and the opposite sign, dated the business date of the decision (audited). Nothing may take a drawer below zero.
/// </summary>
public static class CashOutRules
{
    /// <summary>Slip photos must be under 100 KB (100 × 1,024 = 102,400 bytes): at most 102,399 bytes (DEC-073).</summary>
    public const int SlipMaxBytes = 102_399;

    /// <summary>Slip photos are kept 600 days after they were recorded, then deleted by the retention purge (DEC-073).</summary>
    public const int SlipRetentionDays = 600;

    /// <summary>What a purged slip reads as (the banking record itself is kept).</summary>
    public const string SlipPurgedNote = "Slip photo purged after 600 days.";

    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Posted = "Posted";

    public static string Code(CashOutKind k) => k switch
    {
        CashOutKind.Banking => "banking",
        CashOutKind.Till => "till",
        CashOutKind.Drawing => "drawing",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "Unknown cash-out kind."),
    };

    public static CashOutKind ParseKind(string code) => code switch
    {
        "banking" => CashOutKind.Banking,
        "till" => CashOutKind.Till,
        "drawing" => CashOutKind.Drawing,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown cash-out kind."),
    };

    /// <summary>Amount of cash leaving the drawer: more than ₹0, at most ₹10 crore.</summary>
    public static Paise Amount(long wire) => Paise.FromWire(wire).EnsureReceiptAmount();

    /// <summary>Nothing may take the drawer below zero: the amount must not exceed the expected cash of the shift.</summary>
    public static void EnsureCovered(Paise expected, Paise amount, string what)
    {
        if (amount > expected)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{what} of {amount} would take the drawer below zero: the expected cash is {expected}.");
    }

    /// <summary>Bank name / account label (required, ≤ 100 characters).</summary>
    public static string Bank(string? value) => Text(value, 100, "Enter the bank and account the cash was deposited into.");

    /// <summary>Deposit-slip number: required, ≤ 40 characters of letters, digits, space, '-' and '/'.</summary>
    public static string SlipNumber(string? value)
    {
        var v = Text(value, 40, "Enter the deposit-slip number.");
        if (!v.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '/'))
            throw HccException.Validation("A deposit-slip number uses letters, digits, spaces, '-' and '/' only.");
        return v;
    }

    /// <summary>A reason (required where the rule says so, ≤ 200 characters).</summary>
    public static string Reason(string? value, string message) => Text(value, 200, message);

    /// <summary>
    /// The slip photo as stored: JPEG or PNG only (checked on the bytes, not just the declared type), 1 … 102,399 bytes. The app compresses it
    /// on the device; the server never re-encodes it.
    /// </summary>
    public static (string ContentType, byte[] Bytes) SlipImage(string? base64, string? contentType) =>
        Photo(base64, contentType, SlipMaxBytes, "Take a photo of the deposit slip.", "The slip photo must be a JPEG or PNG image.",
            "The slip photo must be under 100 KB. Take it again (the app compresses it).", "The slip photo is not valid base64.");

    /// <summary>
    /// A photo sent by the app (the deposit slip; DEC-120 the expense bill photo): JPEG or PNG only, checked on the bytes, 1 … <paramref name="maxBytes"/>
    /// bytes; the server never re-encodes it. The messages name the photo.
    /// </summary>
    public static (string ContentType, byte[] Bytes) Photo(string? base64, string? contentType, int maxBytes, string missing, string wrongType, string tooBig, string notBase64)
    {
        if (string.IsNullOrWhiteSpace(base64)) throw HccException.Validation(missing);
        var type = contentType?.Trim().ToLowerInvariant();
        if (type is not ("image/jpeg" or "image/png")) throw HccException.Validation(wrongType);
        // Base64 of maxBytes is 4 × ceil(maxBytes ÷ 3) characters; anything longer is refused before decoding.
        if (base64.Length > (maxBytes + 2) / 3 * 4 + 4) throw HccException.Validation(tooBig);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw HccException.Validation(notBase64);
        }

        if (bytes.Length == 0) throw HccException.Validation(missing);
        if (bytes.Length > maxBytes) throw HccException.Validation(tooBig);
        var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isPng = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A
                    && bytes[6] == 0x1A && bytes[7] == 0x0A;
        if ((type == "image/jpeg" && !isJpeg) || (type == "image/png" && !isPng))
            throw HccException.Validation(wrongType);
        return (type, bytes);
    }

    /// <summary>DEC-078 handover: the opener's count of the drawer they take over minus the cash counted at the previous close.</summary>
    public static Paise HandoverDifference(Paise handoverCount, Paise carriedFloat) => handoverCount - carriedFloat;

    private static string Text(string? value, int max, string message)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw HccException.Validation(message);
        if (v.Length > max) throw HccException.Validation($"Keep it to {max} characters.");
        return v;
    }
}
