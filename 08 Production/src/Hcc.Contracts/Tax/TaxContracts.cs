namespace Hcc.Contracts.Tax;

// Wire contracts of the GST master (DEC-069). Rates in basis points (500 = 5 %), money in paise, booleans as 0/1; dates YYYY-MM-DD.
// Every rate is "Illustrative — CA to confirm".

/// <summary>POST /api/v1/commands/SetGstSetting (K). Owner only. <c>EffectiveFrom</c> defaults to the business date; never earlier.</summary>
public sealed record SetGstSettingRequest(string Key, long Value, string? EffectiveFrom, string Reason);

/// <summary>POST /api/v1/commands/WithdrawGstSetting (K). Owner only; only a change that has not taken effect yet.</summary>
public sealed record WithdrawGstSettingRequest(long VersionId, string Reason);

public sealed record GstSettingChangeResult(long VersionId, string Key, string EffectiveFrom, long Before, long After, string Status);

/// <summary>A setting as in force on the requested date.</summary>
public sealed record GstSettingView(string Key, string Label, string Kind, string Unit, string Help, long Value, string EffectiveFrom);

public sealed record GstHistoryRow(long VersionId, string Key, string EffectiveFrom, long Value, string Source, string Status, string? Reason,
    int? ChangedBy, string ChangedUtc);

/// <summary>[supply, rate, note] lines as the Master configuration panel shows them (prototype <c>gstMasterSummary</c>).</summary>
public sealed record GstSummaryLine(string Supply, int Bps, bool Itc, string Note);

/// <summary>GET /api/v1/tax/gst?date=YYYY-MM-DD — Owner and Tech admin (read-only).</summary>
public sealed record GstMasterView(string Date, string Note, bool CanChange, string FinancialYear, bool SpecifiedPremises, string SpecifiedReason,
    IReadOnlyList<GstSettingView> Settings, IReadOnlyList<GstSummaryLine> Summary, IReadOnlyList<GstHistoryRow> History);
