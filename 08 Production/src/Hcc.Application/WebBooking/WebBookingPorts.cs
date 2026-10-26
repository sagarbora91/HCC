using Hcc.Application.Commands;

namespace Hcc.Application.WebBooking;

/// <summary>Booking-website settings. The public routes are OFF until the owner enables them (<c>Hcc:WebBooking:Enabled</c>).</summary>
public sealed class WebBookingOptions
{
    public bool Enabled { get; set; }

    /// <summary>Share of the quoted total asked as deposit when staff do not type an amount (basis points).</summary>
    public int DepositBps { get; set; } = Hcc.Domain.WebBooking.WebBookingRules.DefaultDepositBps;

    public TimeSpan DepositLife { get; set; } = Hcc.Domain.WebBooking.WebBookingRules.DefaultDepositLife;

    /// <summary>Per client address: quotes per minute and requests per ten minutes.</summary>
    public int QuotesPerMinute { get; set; } = 30;

    public int RequestsPerTenMinutes { get; set; } = 5;

    /// <summary>Global cap of open (Pending) web requests; new requests are refused above it so spam cannot fill the staff queue (F-L05).</summary>
    public int MaxPendingRequests { get; set; } = 100;
}

/// <summary>Fixed actor of the anonymous public path in audit and idempotency rows (no staff account is involved).</summary>
public static class WebBookingActors
{
    public const int PublicWeb = 2_000_000_000;
}

public sealed record NewWebRequest(int PropertyId, int RoomTypeId, DateOnly Arrival, DateOnly Departure, int Adults, int Children, string Guest,
    string Phone, string? Email, string? Notes, long NetPaise, long TaxPaise, string QuoteNightsJson, DateTime NowUtc);

public sealed record WebRequestRow(long RequestId, int PropertyId, string PublicNo, int RoomTypeId, string RoomType, DateOnly Arrival, DateOnly Departure,
    int Adults, int Children, string Guest, string Phone, string? Email, string? Notes, long NetPaise, long TaxPaise, long TotalPaise, string QuoteNightsJson,
    string Status, string? Reason, int? ClaimedBy, int? DecidedBy, DateTime? DecidedUtc, long? ReservationId, string? ReservationNo, DateTime CreatedUtc, byte[] RowVersion);

public sealed record DepositLinkRow(long LinkId, long RequestId, long AmountPaise, string Status, DateTime ExpiresUtc, DateTime CreatedUtc, DateTime? PaidUtc,
    string? PaidRef, long? ReceiptId, byte[] RowVersion);

/// <summary>Persistence of the web module (schema web, migration 2000). Writers take the command transaction.</summary>
public interface IWebBookingStore
{
    /// <summary>The property the public site sells (the only / lowest-numbered one) and its name; null when none exists.</summary>
    Task<(int PropertyId, string Name)?> PublicPropertyAsync(CancellationToken ct = default);

    Task<int> PendingForPhoneAsync(ICommandTransaction tx, int propertyId, string phone, CancellationToken ct = default);

    Task<WebRequestRow> InsertRequestAsync(ICommandTransaction tx, NewWebRequest request, CancellationToken ct = default);

    Task<WebRequestRow?> GetForUpdateAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default);

    Task SetStateAsync(ICommandTransaction tx, long requestId, string status, string? reason, int? claimedBy, int? decidedBy, DateTime? decidedUtc,
        long? reservationId, CancellationToken ct = default);

    Task<IReadOnlyList<WebRequestRow>> ListAsync(int propertyId, string? status, int limit, CancellationToken ct = default);

    Task<int> PendingCountAsync(int propertyId, CancellationToken ct = default);

    Task<WebRequestRow?> GetAsync(int propertyId, long requestId, CancellationToken ct = default);

    /// <summary>The live reservation (Held/Confirmed/InHouse) already made for this request number (confirm recovery); null when none.</summary>
    Task<long?> FindReservationIdAsync(int propertyId, string publicNo, CancellationToken ct = default);

    Task<WebRequestRow?> GetByNoAsync(int propertyId, string publicNo, CancellationToken ct = default);

    Task<DepositLinkRow> InsertLinkAsync(ICommandTransaction tx, long requestId, byte[] tokenHash, long amountPaise, DateTime expiresUtc, int createdBy,
        DateTime nowUtc, CancellationToken ct = default);

    Task<(DepositLinkRow Link, long RequestId)?> FindLinkForUpdateAsync(ICommandTransaction tx, int propertyId, byte[] tokenHash, CancellationToken ct = default);

    Task<(DepositLinkRow Link, long RequestId)?> FindLinkAsync(int propertyId, byte[] tokenHash, CancellationToken ct = default);

    /// <summary>The request's Open link under lock (null when none).</summary>
    Task<DepositLinkRow?> OpenLinkForUpdateAsync(ICommandTransaction tx, long requestId, CancellationToken ct = default);

    Task SetLinkStateAsync(ICommandTransaction tx, long linkId, string status, DateTime? paidUtc, int? paidBy, string? paidRef, long? receiptId,
        CancellationToken ct = default);

    Task<IReadOnlyList<DepositLinkRow>> LinksAsync(long requestId, CancellationToken ct = default);
}

/// <summary>Sliding-minute style fixed-window counter per client key (in memory; the LAN server is one process).</summary>
public sealed class PublicRateLimiter(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime WindowStart, int Count)> _hits = [];

    /// <summary>True when the call is allowed (and counted); false when the window's limit is used up.</summary>
    public bool TryAcquire(string bucket, System.Net.IPAddress? client, int limit, TimeSpan window, out TimeSpan retryAfter) =>
        TryAcquire(bucket, Access.ClientAddress.Key(client), limit, window, out retryAfter);   // F-L05: IPv6 keyed by /64

    public bool TryAcquire(string bucket, string client, int limit, TimeSpan window, out TimeSpan retryAfter)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var key = bucket + "|" + client;
        lock (_gate)
        {
            if (_hits.Count > 5000) Sweep(now);
            if (!_hits.TryGetValue(key, out var h) || now - h.WindowStart >= window) h = (now, 0);
            if (h.Count >= limit)
            {
                retryAfter = h.WindowStart + window - now;
                return false;
            }

            _hits[key] = (h.WindowStart, h.Count + 1);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private void Sweep(DateTime now)
    {
        foreach (var k in _hits.Where(x => now - x.Value.WindowStart > TimeSpan.FromMinutes(20)).Select(x => x.Key).ToList()) _hits.Remove(k);
    }
}
