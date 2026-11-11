using Hcc.Domain.Customers;

namespace Hcc.Application.Customers;

/// <summary>
/// CU-PER: a small bounded in-memory cache for resolving an inbound <c>ck_</c> token to its raw customer key. Without it every token costs one HMAC per customer
/// key of the property (a forged token costs all of them, every request). Per property the cache holds one immutable entry: the raw key to token and the token to
/// raw key maps of the property's key set, built under one secret.
///
/// Exact, not probabilistic: an entry is used only when its secret equals the property's current secret (rotation rebuilds it from nothing) and the request's key
/// set is exactly the entry's (same count, every key present, same number of distinct keys), so a new, renamed or removed customer key rebuilds the entry
/// (re-hashing only the keys that are new) and a token refused for an old key set is never refused from memory for a new one. A merge changes no key
/// (the merged-away key stays in the set as an alias), and <see cref="Invalidate"/> is still called on every approved merge and is the hook for a secret rotation.
/// Bounds: <see cref="MaxProperties"/> properties, <see cref="MaxKeys"/> keys for one property (a larger property is resolved by the plain scan, as before) and
/// <see cref="TotalKeyBudget"/> keys over all properties (least recently used property is evicted first). The entries keep raw customer keys (mobile numbers) and a
/// copy of the property secret in process memory until evicted. Static, so no registration is needed; tests may call <see cref="Clear"/>.
/// </summary>
internal static class CustomerKeyTokenCache
{
    public const int MaxProperties = 16, MaxKeys = 250_000;

    /// <summary>Keys held over all properties (default 300,000, about 70 MB worst case); tests lower it.</summary>
    public static int TotalKeyBudget { get; set; } = 300_000;

    private sealed class Entry(byte[] secret, int count, Dictionary<string, string> byToken, Dictionary<string, string> byRaw)
    {
        public byte[] Secret { get; } = secret;
        public int Count { get; } = count;
        public Dictionary<string, string> ByToken { get; } = byToken;
        public Dictionary<string, string> ByRaw { get; } = byRaw;
        public long LastUsed { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<int, Entry> Entries = [];
    private static long _tick;
    private static long _hashes, _hits, _builds;

    /// <summary>HMACs computed by the cache so far (tests assert a repeated or forged lookup adds none).</summary>
    public static long Hashes => Interlocked.Read(ref _hashes);

    public static long Hits => Interlocked.Read(ref _hits);

    public static long Builds => Interlocked.Read(ref _builds);

    public static int PropertyCount
    {
        get
        {
            lock (Gate) return Entries.Count;
        }
    }

    /// <summary>Distinct keys held over all properties.</summary>
    public static int TotalKeys
    {
        get
        {
            lock (Gate) return Entries.Values.Sum(e => e.ByRaw.Count);
        }
    }

    /// <summary>Drops what is cached for the property (called on every approved merge; the hook for a secret rotation).</summary>
    public static void Invalidate(int propertyId)
    {
        lock (Gate) Entries.Remove(propertyId);
    }

    public static void Clear()
    {
        lock (Gate) Entries.Clear();
    }

    /// <summary>The raw key whose token is <paramref name="token"/> among <paramref name="rawKeys"/> (first one in enumeration order), or null.</summary>
    public static string? Resolve(int propertyId, byte[] secret, IEnumerable<string> rawKeys, string token)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(rawKeys);
        var keys = rawKeys as IReadOnlyList<string> ?? rawKeys.ToList();
        if (keys.Count > MaxKeys || keys.Count > TotalKeyBudget)
        {
            foreach (var raw in keys)
            {
                Interlocked.Increment(ref _hashes);
                if (string.Equals(CustomerKeyToken.Make(secret, raw), token, StringComparison.Ordinal)) return raw;
            }

            return null;
        }

        Entry? old;
        lock (Gate) Entries.TryGetValue(propertyId, out old);
        var sameSecret = old is not null && old.Secret.AsSpan().SequenceEqual(secret);
        Entry entry;
        if (sameSecret && IsSameKeySet(old!, keys))
        {
            entry = old!;
            Interlocked.Increment(ref _hits);
        }
        else
        {
            entry = Build(secret, keys, sameSecret ? old : null);
            lock (Gate)
            {
                Entries.Remove(propertyId);
                while (Entries.Count > 0 && (Entries.Count >= MaxProperties || Entries.Values.Sum(e => e.ByRaw.Count) + entry.ByRaw.Count > TotalKeyBudget))
                    Entries.Remove(Entries.MinBy(e => e.Value.LastUsed).Key);
                Entries[propertyId] = entry;
            }
        }

        lock (Gate) entry.LastUsed = ++_tick;
        return entry.ByToken.GetValueOrDefault(token);
    }

    // Exact: same count, every key known to the entry, and as many distinct keys as the entry holds.
    private static bool IsSameKeySet(Entry e, IReadOnlyList<string> keys)
    {
        if (e.Count != keys.Count) return false;
        var distinct = new HashSet<string>(keys.Count, StringComparer.Ordinal);
        foreach (var k in keys)
        {
            if (!e.ByRaw.ContainsKey(k)) return false;
            distinct.Add(k);
        }

        return distinct.Count == e.ByRaw.Count;
    }

    private static Entry Build(byte[] secret, IReadOnlyList<string> keys, Entry? reuse)
    {
        Interlocked.Increment(ref _builds);
        var secretCopy = (byte[])secret.Clone();
        var byRaw = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        var byToken = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        foreach (var raw in keys)
        {
            if (byRaw.ContainsKey(raw)) continue;
            string? tok = null;
            if (reuse is null || !reuse.ByRaw.TryGetValue(raw, out tok))
            {
                Interlocked.Increment(ref _hashes);
                tok = CustomerKeyToken.Make(secretCopy, raw);
            }

            byRaw[raw] = tok;
            byToken.TryAdd(tok, raw);
        }

        return new Entry(secretCopy, keys.Count, byToken, byRaw);
    }
}
