using Hcc.Domain.Customers;

namespace Hcc.Application.Customers;

/// <summary>
/// CU-PER: a small bounded in-memory cache for resolving an inbound <c>ck_</c> token to its raw customer key. Without it every token costs one HMAC per customer
/// key of the property (a forged token costs all of them, every request). Per property the cache holds one immutable entry: the raw key to token and the token to
/// raw key maps of the property's key set, built under one secret.
///
/// Correct by construction, not by timing: an entry is used only when its secret equals the property's current secret (rotation rebuilds it from nothing) and its
/// fingerprint (count and two order-independent 64-bit sums over the keys) equals the request's own key set, so a new, renamed or removed customer key rebuilds
/// it (re-hashing only the keys that are new) and a token that was refused for an old key set is never refused from memory for a new one. A merge changes no key
/// (the merged-away key stays in the set as an alias), and <see cref="Invalidate"/> is still called on every approved merge and is the hook for a secret rotation.
/// Bounds: at most <see cref="MaxProperties"/> properties (least recently used goes first) and <see cref="MaxKeys"/> keys per property (a larger property is
/// resolved by the plain scan, as before). Static, so no registration is needed; tests may call <see cref="Clear"/>.
/// </summary>
internal static class CustomerKeyTokenCache
{
    public const int MaxProperties = 16, MaxKeys = 250_000;

    private sealed class Entry(byte[] secret, int count, ulong sum, ulong mix, Dictionary<string, string> byToken, Dictionary<string, string> byRaw)
    {
        public byte[] Secret { get; } = secret;
        public int Count { get; } = count;
        public ulong Sum { get; } = sum;
        public ulong Mix { get; } = mix;
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
        if (keys.Count > MaxKeys)
        {
            foreach (var raw in keys)
            {
                Interlocked.Increment(ref _hashes);
                if (string.Equals(CustomerKeyToken.Make(secret, raw), token, StringComparison.Ordinal)) return raw;
            }

            return null;
        }

        var (sum, mix) = Fingerprint(keys);
        Entry? old;
        lock (Gate) Entries.TryGetValue(propertyId, out old);
        var sameSecret = old is not null && old.Secret.AsSpan().SequenceEqual(secret);
        Entry entry;
        if (sameSecret && old!.Count == keys.Count && old.Sum == sum && old.Mix == mix)
        {
            entry = old;
            Interlocked.Increment(ref _hits);
        }
        else
        {
            entry = Build(secret, keys, sum, mix, sameSecret ? old : null);
            lock (Gate)
            {
                if (!Entries.ContainsKey(propertyId) && Entries.Count >= MaxProperties)
                    Entries.Remove(Entries.MinBy(e => e.Value.LastUsed).Key);
                Entries[propertyId] = entry;
            }
        }

        lock (Gate) entry.LastUsed = ++_tick;
        return entry.ByToken.GetValueOrDefault(token);
    }

    private static Entry Build(byte[] secret, IReadOnlyList<string> keys, ulong sum, ulong mix, Entry? reuse)
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

        return new Entry(secretCopy, keys.Count, sum, mix, byToken, byRaw);
    }

    // FNV-1a over the UTF-16 code units, summed (order-independent), and a second sum of a mixed copy of each hash.
    private static (ulong Sum, ulong Mix) Fingerprint(IReadOnlyList<string> keys)
    {
        ulong sum = 0, mix = 0;
        foreach (var k in keys)
        {
            var h = 14695981039346656037UL;
            foreach (var c in k) h = (h ^ c) * 1099511628211UL;
            sum += h;
            var m = h;
            m ^= m >> 33;
            m *= 0xff51afd7ed558ccdUL;
            m ^= m >> 33;
            m *= 0xc4ceb9fe1a85ec53UL;
            m ^= m >> 33;
            mix += m;
        }

        return (sum, mix);
    }
}
