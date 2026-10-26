using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Hcc.Application.Access;

/// <summary>Rate-limit key of a client address: the IPv4 address, or the /64 network of an IPv6 address (one LAN host or one ISP customer).</summary>
public static class ClientAddress
{
    public static string Key(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var b = address.GetAddressBytes();
        for (var i = 8; i < 16; i++) b[i] = 0;
        return new IPAddress(b).ToString() + "/64";
    }
}

/// <summary>What the sign-in throttle says about one attempt.</summary>
/// <param name="Allowed">False: answer 429 with <paramref name="RetryAfter"/>, without touching the database.</param>
/// <param name="Delay">Progressive delay to wait before the attempt (zero for most attempts).</param>
public readonly record struct SignInGate(bool Allowed, TimeSpan RetryAfter, TimeSpan Delay);

/// <summary>
/// F-M02 sign-in throttle (in memory: the LAN server is one process). Policy, over a sliding window (<see cref="AccessOptions.SignInWindow"/>, 15 min):
/// <list type="number">
/// <item>Per client address (IPv4 / IPv6 /64): after <see cref="AccessOptions.SignInFailuresPerSource"/> failures (20) the address gets 429 —
/// stops password spraying across accounts and flooding the audit log.</item>
/// <item>Per (address, sign-in name): after <see cref="AccessOptions.SignInFailuresPerSourceAndLogin"/> failures (5) that address gets 429 for that
/// name. This replaces the old per-account lockout: a LAN attacker locks only <i>their own</i> address out, never the Owner on another device.</item>
/// <item>Per sign-in name (all addresses): after <see cref="AccessOptions.SignInDelayAfterFailures"/> failures (5) each further attempt waits
/// 1 s, 2 s, 4 s … up to <see cref="AccessOptions.SignInMaxDelay"/> (8 s). A delay, never a refusal; attempts from the server PC itself
/// (loopback, no proxy header) are not delayed, so the Owner can always sign in there.</item>
/// </list>
/// Only failed sign-ins count (wrong or unknown name, wrong secret, unregistered device); a success clears that (address, name) pair
/// and the name's delay. Validation errors (both or neither secret) do not count.
/// </summary>
public sealed class SignInThrottle(AccessOptions options, TimeProvider clock)
{
    private const int SweepAbove = 10_000;
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTime>> _failures = new(StringComparer.Ordinal);

    public static string LoginKey(string? login)
    {
        var l = (login ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
        return l.Length <= 40 ? l : l[..40];
    }

    public SignInGate Check(string source, string? login, bool trustedLocal)
    {
        ArgumentNullException.ThrowIfNull(source);
        var now = clock.GetUtcNow().UtcDateTime;
        var name = LoginKey(login);
        lock (_gate)
        {
            if (_failures.Count > SweepAbove) Sweep(now);
            var retry = TimeSpan.Zero;
            var bySource = Recent("s|" + source, now);
            if (bySource.Count >= options.SignInFailuresPerSource) retry = Max(retry, bySource.Peek() + options.SignInWindow - now);
            var byPair = Recent("p|" + source + "|" + name, now);
            if (byPair.Count >= options.SignInFailuresPerSourceAndLogin) retry = Max(retry, byPair.Peek() + options.SignInWindow - now);
            if (retry > TimeSpan.Zero) return new SignInGate(false, retry, TimeSpan.Zero);

            var byLogin = Recent("l|" + name, now).Count;
            var over = byLogin - options.SignInDelayAfterFailures;
            if (trustedLocal || over < 0) return new SignInGate(true, TimeSpan.Zero, TimeSpan.Zero);
            var delay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(over, 10)));
            return new SignInGate(true, TimeSpan.Zero, delay > options.SignInMaxDelay ? options.SignInMaxDelay : delay);
        }
    }

    public void RecordFailure(string source, string? login)
    {
        ArgumentNullException.ThrowIfNull(source);
        var now = clock.GetUtcNow().UtcDateTime;
        var name = LoginKey(login);
        lock (_gate)
        {
            foreach (var key in new[] { "s|" + source, "p|" + source + "|" + name, "l|" + name })
            {
                var q = Recent(key, now);
                q.Enqueue(now);
                while (q.Count > 1000) q.Dequeue();
            }
        }
    }

    public void RecordSuccess(string source, string? login)
    {
        ArgumentNullException.ThrowIfNull(source);
        var name = LoginKey(login);
        lock (_gate)
        {
            _failures.Remove("p|" + source + "|" + name);
            _failures.Remove("l|" + name);
        }
    }

    private Queue<DateTime> Recent(string key, DateTime now)
    {
        if (!_failures.TryGetValue(key, out var q)) _failures[key] = q = new Queue<DateTime>();
        while (q.Count > 0 && now - q.Peek() >= options.SignInWindow) q.Dequeue();
        return q;
    }

    private void Sweep(DateTime now)
    {
        foreach (var key in _failures.Keys.ToList())
        {
            var q = Recent(key, now);
            if (q.Count == 0) _failures.Remove(key);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
