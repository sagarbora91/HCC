using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.Accounting;

/// <summary>
/// ACC-1 party sub-ledgers (02 Planning/agents/ACC1-POSTING-CONTRACT.md §3; rulebook §3.3; prototype <c>accounting-parties.ts</c>). A party
/// ledger id is <c>&lt;control code&gt;/&lt;party id&gt;</c> (e.g. <c>1110/CMP-7</c>, <c>2200/V-MORNING-SUPPLIES</c>, <c>1030/BANK-1</c>,
/// <c>1120/UD-9876543210</c>). A source entry carries the party it names (<see cref="SourceEntry.Party"/>); the mapping resolves the key to a
/// ledger code, then <see cref="Compose"/> appends the party when that code is a control ledger. A control entry without a party stays on the
/// control and the day gets a <c>no-party</c> warning (never blocks a lock).
/// </summary>
public static partial class PartyLedgers
{
    /// <summary>Control ledgers (contract §2 / §3): cash (tills), bank accounts, company receivables, OTA receivables, Udhaar, staff advances, vendors, loans.</summary>
    public static readonly IReadOnlyList<string> Controls = ["1000", "1030", "1110", "1115", "1120", "1200", "1210", "2200", "2400"]; // 1210: ACC-2 ruling 7 (staff parties)

    /// <summary>Longest party id stored (<c>acc.source_entry.party_id</c>, migration 4210).</summary>
    public const int MaxPartyLength = 40;

    /// <summary>Longest ledger id (<c>acc.locked_entry.account_code</c>, widened by migration 4211): 4-digit code + "/" + party.</summary>
    public const int MaxAccountLength = 45;

    public static bool IsControl(string? code) => code is not null && Controls.Contains(code, StringComparer.Ordinal);

    [GeneratedRegex(@"^\d{4}/.+$")]
    private static partial Regex PartyPattern();

    /// <summary>True for a party ledger id <c>&lt;4-digit control&gt;/&lt;party&gt;</c>.</summary>
    public static bool IsParty(string? account) => account is not null && PartyPattern().IsMatch(account);

    /// <summary>Control code of a ledger id (the id itself for a plain code).</summary>
    public static string ControlOf(string account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var i = account.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? account : account[..i];
    }

    /// <summary>Party id of a party ledger id (null for a plain code).</summary>
    public static string? PartyOf(string account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var i = account.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? null : account[(i + 1)..];
    }

    /// <summary>The ledger id an entry posts to: <paramref name="code"/>, or <c>code/party</c> when the code is a control ledger and a party is named.</summary>
    public static string Compose(string code, string? party)
        => string.IsNullOrEmpty(party) || !IsControl(code) ? code : code + "/" + party;

    /// <summary>Display name of a ledger id: a party ledger is "&lt;control name&gt; · &lt;party id&gt;" (prototype <c>accEntryName</c> without state).</summary>
    public static string DisplayName(string account)
        => IsParty(account) ? ChartOfAccounts.NameOf(ControlOf(account)) + " · " + PartyOf(account) : ChartOfAccounts.NameOf(account);

    /// <summary>Company account party id (server company ids are numbers).</summary>
    public static string Company(long companyId) => "CMP-" + companyId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Staff party id (server staff ids are numbers).</summary>
    public static string Staff(long staffId) => "STF-" + staffId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Slug of a name for a party id (prototype <c>accPartySlug</c>): capitals, digits and '-', at most 36 characters.</summary>
    public static string Slug(string? name)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in (name ?? "").ToUpperInvariant())
        {
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }

        var s = sb.ToString().TrimEnd('-');
        if (s.Length > 36) s = s[..36].TrimEnd('-');
        return s.Length == 0 ? "X" : s;
    }

    /// <summary>Udhaar customer party id (contract §3; prototype <c>accUdhaarPartyId</c>): <c>UD-&lt;10-digit mobile&gt;</c>, else <c>UD-&lt;slug of name&gt;</c>.</summary>
    public static string Udhaar(string? name, string? mobile)
    {
        var digits = new string((mobile ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 10 ? "UD-" + digits[^10..] : "UD-" + Slug(name);
    }

    /// <summary>A party id cut to the stored length (never longer than <see cref="MaxPartyLength"/>).</summary>
    public static string? Clip(string? party) => string.IsNullOrEmpty(party) ? null : party.Length <= MaxPartyLength ? party : party[..MaxPartyLength];
}

/// <summary>
/// The parties a source names (contract §3 "party resolution per source"), applied to its entries by map key (<see cref="SourceRules.WithParties"/>):
/// <c>method:*</c> → the tender's settles-to account (till / bank); <c>funding:*</c> → that account, else the vendor (vendor credit);
/// <c>receipt:company-credit</c> → the company; <c>receipt:udhaar</c> → the Udhaar customer; <c>receipt:ota</c> → the OTA channel;
/// <c>payroll:advances</c> / <c>payroll:fnf-recoverable</c> → the staff member; <c>closing:bank-deposit</c> → the bank account banked into.
/// </summary>
public sealed record PartyContext(string? Tender = null, string? Company = null, string? Udhaar = null, string? Staff = null, string? Vendor = null,
    string? Bank = null, string? Channel = null)
{
    public static readonly PartyContext None = new();

    public string? For(string mapKey)
    {
        ArgumentNullException.ThrowIfNull(mapKey);
        var prefix = mapKey.Split(':')[0];
        return prefix switch
        {
            "method" => Tender,
            "funding" => Tender ?? Vendor,
            _ => mapKey switch
            {
                "receipt:company-credit" => Company,
                SourceRules.UdhaarKey => Udhaar,
                "receipt:ota" => Channel,
                "payroll:advances" or "payroll:fnf-recoverable" => Staff,
                "closing:bank-deposit" => Bank,
                SourceRules.VendorPayableKey => Vendor, // ACC-2 acc2-purchase: vendor payment / advance (2200/<vendor>)
                _ => null,
            },
        };
    }
}
