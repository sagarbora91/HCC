using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Yield;
using Hcc.Domain.Errors;
using Hcc.Domain.Yield;

namespace Hcc.Application.Yield;

/// <summary>
/// THE port through which published rates and restrictions reach P2 (inventory/reservations). The SQL adapter
/// (<c>Hcc.Infrastructure.SqlServer/Yield/SqlRatePublicationPort</c>) writes the effective-dated BAR rows P2 already reads for
/// GetAvailability/QuoteStay (<c>inv.room_type_tariff</c>) and mirrors house restrictions into <c>inv.stay_restriction</c>
/// (enforced by CreateReservation/AmendReservation, RESTRICTED). Both calls run inside the caller's command transaction.
/// When P2 owns a writer of its own, implement this interface there and replace the registration; nothing else changes.
/// </summary>
public interface IRatePublicationPort
{
    /// <summary>Upsert BAR tariff rows (room type + effective-from → rate). Existing bookings keep their agreed rates (P2 snapshots them).</summary>
    Task WriteTariffsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<TariffRow> rows, CancellationToken ct = default);

    /// <summary>Create or replace the stay restriction with this code (room type null = all types).</summary>
    Task UpsertRestrictionAsync(ICommandTransaction tx, int propertyId, YieldRestrictionDef restriction, CancellationToken ct = default);

    /// <summary>Deactivate (remove) the restriction with this code.</summary>
    Task RemoveRestrictionAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);
}

/// <summary>
/// Read port for P2's quote/availability to consume rate-plan prices and restriction issues without touching yield tables
/// (server-side; the caller authorizes). P2 today quotes BAR from <c>inv.room_type_tariff</c> only; plan codes other than BAR can be
/// priced through this port when P2 adds rate plans.
/// </summary>
public interface IYieldRateQuery
{
    Task<YieldQuoteResult> QuoteAsync(int propertyId, string planCode, int roomTypeId, DateOnly arrival, int nights, CancellationToken ct = default);
}

public sealed record YieldProposalRecord(long ProposalId, DateOnly From, DateOnly To, string Status, int ProposedBy, DateTime ProposedUtc, DateTime ExpiresUtc,
    string Reason, string Signature, int ChangedNights, int? DecidedBy, DateTime? DecidedUtc, string? DecisionNote, long? PublishId, IReadOnlyList<int> RoomTypeIds,
    IReadOnlyList<(int RoomTypeId, DateOnly Night, long RatePaise)> Rates, byte[] RowVersion);

public sealed record YieldProposalDraft(DateOnly From, DateOnly To, int ProposedBy, DateTime ProposedUtc, DateTime ExpiresUtc, string Reason, string Signature,
    int ChangedNights, IReadOnlyList<int> RoomTypeIds, IReadOnlyList<(int RoomTypeId, DateOnly Night, long RatePaise)> Rates);

/// <summary>Reads and writes of the yld schema (SQL adapter in Hcc.Infrastructure.SqlServer/Yield).</summary>
public interface IYieldStore
{
    /// <summary>Creates the property's settings row and the five default rate plans when missing; also takes the property's yield lock for the transaction.</summary>
    Task EnsureSeededAsync(ICommandTransaction tx, int propertyId, bool lockProperty, CancellationToken ct = default);

    Task<YieldConfig> LoadConfigAsync(int propertyId, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<YieldConfigResult> GetConfigViewAsync(int propertyId, int historyLimit, CancellationToken ct = default);

    /// <summary>Inventory facts for the nights from (null = business date) for the given count (business date, types, tariffs, sellable, on the books, forecast, yield-owned rows).</summary>
    Task<YieldInputs> LoadInputsAsync(int propertyId, DateOnly? from, int nights, DateTime nowUtc, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<string> UpdatePlanAsync(ICommandTransaction tx, int propertyId, YieldPlan plan, byte[] expected, CancellationToken ct = default);

    Task<(int Id, string Version)> SaveSeasonAsync(ICommandTransaction tx, int propertyId, int? id, byte[]? expected, string name, DateOnly from, DateOnly to,
        int adjBps, IReadOnlyList<int> roomTypeIds, int priority, CancellationToken ct = default);

    Task DeleteSeasonAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default);

    Task<string> SetDowAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> dow, byte[] expected, CancellationToken ct = default);

    Task<string> SetGuardAsync(ICommandTransaction tx, int propertyId, YieldGuard guard, byte[] expected, CancellationToken ct = default);

    Task<(int Id, string Version)> SaveOccRuleAsync(ICommandTransaction tx, int propertyId, int? id, byte[]? expected, string op, int occ, int adjBps, CancellationToken ct = default);

    Task DeleteOccRuleAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default);

    /// <summary>Returns the previous restriction (null on create), the restriction as stored (code assigned on create) and its new version.</summary>
    Task<(YieldRestrictionDef? Before, YieldRestrictionDef After, int Id, string Version)> SaveRestrictionAsync(ICommandTransaction tx, int propertyId, int? id, byte[]? expected,
        int? roomTypeId, DateOnly from, DateOnly to, int minStay, bool cta, string note, CancellationToken ct = default);

    Task<YieldRestrictionDef> DeleteRestrictionAsync(ICommandTransaction tx, int propertyId, int id, byte[] expected, CancellationToken ct = default);

    Task<bool> SetOverrideAsync(ICommandTransaction tx, int propertyId, int roomTypeId, DateOnly night, long? ratePaise, string reason, int actor, DateTime nowUtc, CancellationToken ct = default);

    Task SetForecastAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<(DateOnly Night, int RoomsSold)> nights, CancellationToken ct = default);

    Task AddHistoryAsync(ICommandTransaction tx, int propertyId, DateTime nowUtc, int actor, string area, string before, string after, string reason, CancellationToken ct = default);

    Task MarkOwnedAsync(ICommandTransaction tx, IReadOnlyList<TariffRow> rows, CancellationToken ct = default);

    Task<long> InsertPublishAsync(ICommandTransaction tx, int propertyId, DateOnly from, DateOnly to, int actor, DateTime nowUtc, string reason, long? proposalId,
        IReadOnlyList<YieldPublishRow> rows, CancellationToken ct = default);

    Task<long> InsertProposalAsync(ICommandTransaction tx, int propertyId, YieldProposalDraft draft, CancellationToken ct = default);

    Task<bool> HasLivePendingProposalAsync(ICommandTransaction tx, int propertyId, DateTime nowUtc, CancellationToken ct = default);

    Task<YieldProposalRecord?> GetProposalAsync(int propertyId, long proposalId, ICommandTransaction? tx = null, CancellationToken ct = default);

    Task<string> CompleteProposalAsync(ICommandTransaction tx, long proposalId, string status, int decidedBy, DateTime nowUtc, string? note, long? publishId, CancellationToken ct = default);

    Task<IReadOnlyList<YieldProposalRecord>> ListProposalsAsync(int propertyId, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<YieldPublishView>> ListPublishesAsync(int propertyId, int limit, CancellationToken ct = default);
}

/// <summary>Version token helpers for the yield module.</summary>
public static class YieldVersion
{
    public static byte[] Parse(string? token, string field = "expectedVersion")
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(token ?? string.Empty);
        }
        catch (FormatException)
        {
            throw HccException.Validation($"{field} is not a valid version token.");
        }

        if (bytes.Length != 8) throw HccException.Validation($"{field} is not a valid version token.");
        return bytes;
    }

    public static string ToWire(byte[] version) => Convert.ToBase64String(version);

    public static HccException Stale(string what) => new(HccErrorCode.STALE_VERSION, $"{what} was changed by someone else. Reload and try again.");

    public static HccException NotFound(string what) => new(HccErrorCode.NOT_FOUND, $"{what} not found.");
}
