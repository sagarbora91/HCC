using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Access;

/// <summary>Credential type. DEC-062: PIN + device binding for staff; password for Owner, Accountant and Tech admin.</summary>
public enum CredentialKind
{
    Password,
    Pin,
}

public static class CredentialPolicy
{
    /// <summary>
    /// DEC-062: a user holding any password role (OWN, ACC, TAD) — active, future or expired assignment alike — signs in
    /// with a password only; every other user signs in with a PIN on a registered device. Null for a user without roles
    /// (cannot sign in).
    /// </summary>
    public static CredentialKind? RequiredKind(IEnumerable<string> assignedRoleCodes)
    {
        ArgumentNullException.ThrowIfNull(assignedRoleCodes);
        var roles = assignedRoleCodes.ToList();
        if (roles.Count == 0) return null;
        return roles.Any(RoleCodes.PasswordRoles.Contains) ? CredentialKind.Password : CredentialKind.Pin;
    }

    public static string ToWire(CredentialKind kind) => kind == CredentialKind.Password ? "PASSWORD" : "PIN";

    public static CredentialKind FromWire(string value) => value switch
    {
        "PASSWORD" => CredentialKind.Password,
        "PIN" => CredentialKind.Pin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown credential kind."),
    };
}

/// <summary>
/// PIN rules (DEC-062): exactly <see cref="Length"/> digits, not trivial (all one digit, ascending/descending runs,
/// repeated 1–3 digit patterns, common keypad patterns). Reuse of recent PINs is checked by the store against hashes.
/// </summary>
public static class PinRules
{
    public const int Length = 6;

    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "112233", "102030", "111222", "121314", "123321", "147258", "147852", "159753", "159357", "258369", "258456",
        "369258", "741852", "789456", "456789", "963852", "852456", "696969", "100200", "998877", "778899", "010203",
    };

    public static void Validate(string? pin)
    {
        if (pin is null || pin.Length != Length || !pin.All(char.IsAsciiDigit))
            throw HccException.Validation($"The PIN must be exactly {Length} digits.");
        if (IsTrivial(pin)) throw HccException.Validation("This PIN is too easy to guess. Choose one without runs or repeats.");
    }

    public static bool IsTrivial(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (Common.Contains(pin)) return true;
        var d = pin.Select(c => c - '0').ToArray();
        var asc = true;
        var desc = true;
        for (var i = 1; i < d.Length; i++)
        {
            asc &= d[i] == d[i - 1] + 1;
            desc &= d[i] == d[i - 1] - 1;
        }

        if (asc || desc) return true;
        foreach (var period in new[] { 1, 2, 3 })
        {
            if (pin.Length % period != 0) continue;
            var unit = pin[..period];
            if (string.Concat(Enumerable.Repeat(unit, pin.Length / period)) == pin) return true;
        }

        // Mirror halves (e.g. 123321) and at most two distinct digits (e.g. 110011) are trivial too.
        return pin.SequenceEqual(pin.Reverse()) || pin.Distinct().Count() <= 2;
    }
}

/// <summary>Password rules for password roles (DEC-062): 12–128 characters, not containing the login name, not one repeated character.</summary>
public static class PasswordRules
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static void Validate(string? password, string loginName)
    {
        ArgumentNullException.ThrowIfNull(loginName);
        if (password is null || password.Length < MinLength || password.Length > MaxLength)
            throw HccException.Validation($"The password must be {MinLength}–{MaxLength} characters.");
        if (password.Contains(loginName, StringComparison.OrdinalIgnoreCase))
            throw HccException.Validation("The password must not contain the sign-in name.");
        if (password.Distinct().Count() < 4)
            throw HccException.Validation("The password is too simple.");
    }
}

/// <summary>
/// A06-01 (F-M03 residual): a password or PIN set by an administrator (SetCredential) must be changed by its user before anything
/// else. While the flag is set the session may only read its own capabilities, change the credential (ChangeOwnCredential) and sign
/// out; every other request is refused with <see cref="Refusal"/> (403 FORBIDDEN, details.reason <see cref="Reason"/>).
/// </summary>
public static class CredentialChangeGate
{
    public const string Reason = "MUST_CHANGE_CREDENTIAL";

    public static string Message(CredentialKind kind) => kind == CredentialKind.Pin
        ? "Change your PIN first. An administrator set it for you, so choose your own PIN before you continue."
        : "Change your password first. An administrator set it for you, so choose your own password before you continue.";

    public static HccException Refusal(CredentialKind kind) => new(HccErrorCode.FORBIDDEN, Message(kind),
        new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["reason"] = JsonSerializer.SerializeToElement(Reason) });
}

/// <summary>Self-service change (ChangeOwnCredential): the new secret must differ from the current one (an administrator may know it).</summary>
public static class OwnCredentialRules
{
    public static void EnsureNew(string current, string next, CredentialKind kind)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        if (string.Equals(current, next, StringComparison.Ordinal))
            throw HccException.Validation(kind == CredentialKind.Pin ? "Choose a new PIN: it must be different from the current one." : "Choose a new password: it must be different from the current one.");
    }

    /// <summary>The self-service lockout message, with the minutes left (rounded up, at least 1).</summary>
    public static string LockedMessage(TimeSpan left)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        return $"Too many wrong attempts. Try again in {minutes} minute{(minutes == 1 ? string.Empty : "s")}.";
    }
}

/// <summary>Session lifetime rule (contracts §1): idle 30 min, absolute 12 h by default.</summary>
public static class SessionLifetime
{
    public static bool IsAlive(DateTime createdUtc, DateTime lastSeenUtc, DateTime nowUtc, TimeSpan idle, TimeSpan absolute) =>
        nowUtc - lastSeenUtc < idle && nowUtc - createdUtc < absolute;
}
