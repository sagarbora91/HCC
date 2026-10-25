using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.GuestIds;

/// <summary>
/// Keys for guest ID encryption (DEC-068). 256-bit AES keys by id; <see cref="CurrentKeyId"/> seals new numbers, every listed key
/// opens old ones (rotation: add a new key, make it current, run RekeyGuestIds, then retire the old key). Keys never live in git or
/// in the database; the production ring reads DPAPI-protected values from configuration (Hcc.Infrastructure.SqlServer.GuestIds).
/// </summary>
public interface IGuestIdKeyRing
{
    string CurrentKeyId { get; }

    /// <summary>The 32-byte key for <paramref name="keyId"/>, or null when this ring does not hold it.</summary>
    byte[]? Key(string keyId);
}

/// <summary>A sealed number as stored: key id + blob (format 1 | nonce 12 | tag 16 | ciphertext).</summary>
public sealed record SealedGuestId(string KeyId, byte[] Sealed);

/// <summary>
/// Application-level AES-256-GCM for full guest ID numbers (SQL Server Always Encrypted is not available on every Express
/// install). Associated data binds a blob to its property, guest and key id, so a blob copied onto another guest row fails to
/// open. Errors never contain the number.
/// </summary>
public sealed class GuestIdProtector(IGuestIdKeyRing keys)
{
    private const byte Format = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string CurrentKeyId => keys.CurrentKeyId;

    private byte[] KeyOrThrow(string keyId) => keys.Key(keyId) is { Length: 32 } k
        ? k
        : throw new HccException(HccErrorCode.UNAVAILABLE, "Guest ID numbers cannot be opened: the encryption key " + keyId + " is not configured on this server.");

    private static byte[] Aad(int propertyId, long guestId, string keyId)
        => Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"HCC-GUESTID-v1|{propertyId}|{guestId}|{keyId}"));

    public SealedGuestId Seal(int propertyId, long guestId, string normalisedNumber)
    {
        ArgumentException.ThrowIfNullOrEmpty(normalisedNumber);
        var keyId = keys.CurrentKeyId;
        var plain = Encoding.ASCII.GetBytes(normalisedNumber);
        var blob = new byte[1 + NonceSize + TagSize + plain.Length];
        blob[0] = Format;
        var nonce = blob.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(KeyOrThrow(keyId), TagSize))
            aes.Encrypt(nonce, plain, blob.AsSpan(1 + NonceSize + TagSize), blob.AsSpan(1 + NonceSize, TagSize), Aad(propertyId, guestId, keyId));
        CryptographicOperations.ZeroMemory(plain);
        return new SealedGuestId(keyId, blob);
    }

    public string Open(int propertyId, long guestId, SealedGuestId stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var blob = stored.Sealed;
        if (blob.Length <= 1 + NonceSize + TagSize || blob[0] != Format)
            throw new HccException(HccErrorCode.UNAVAILABLE, "The stored guest ID number is not in a readable format.");
        var plain = new byte[blob.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(KeyOrThrow(stored.KeyId), TagSize);
            aes.Decrypt(blob.AsSpan(1, NonceSize), blob.AsSpan(1 + NonceSize + TagSize), blob.AsSpan(1 + NonceSize, TagSize), plain, Aad(propertyId, guestId, stored.KeyId));
            return Encoding.ASCII.GetString(plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new HccException(HccErrorCode.UNAVAILABLE, "The stored guest ID number could not be decrypted with key " + stored.KeyId + " (wrong key or altered data).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// Keyed fingerprint of a number (HMAC-SHA256 under a subkey derived from the current key) — used instead of the number in the
    /// idempotency payload hash, so <c>plat.idempotency.payload_sha256</c> cannot be brute-forced back to an Aadhaar number.
    /// </summary>
    public string Fingerprint(string normalisedNumber)
    {
        ArgumentNullException.ThrowIfNull(normalisedNumber);
        var sub = HKDF.DeriveKey(HashAlgorithmName.SHA256, KeyOrThrow(keys.CurrentKeyId), 32, info: Encoding.ASCII.GetBytes("HCC-GUESTID-fingerprint-v1"));
        var mac = HMACSHA256.HashData(sub, Encoding.ASCII.GetBytes(normalisedNumber));
        CryptographicOperations.ZeroMemory(sub);
        return "hmac:" + Convert.ToBase64String(mac);
    }
}

/// <summary>Fixed in-memory ring (tests; development with a plain key). Copies the keys it is given.</summary>
public sealed class StaticGuestIdKeyRing : IGuestIdKeyRing
{
    private readonly Dictionary<string, byte[]> _keys;

    public StaticGuestIdKeyRing(string currentKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys.ToDictionary(k => k.Key, k => (byte[])k.Value.Clone(), StringComparer.Ordinal);
        if (!_keys.TryGetValue(currentKeyId, out var current) || current.Length != 32)
            throw new ArgumentException("The current guest ID key must be a 32-byte key in the ring.", nameof(currentKeyId));
        CurrentKeyId = currentKeyId;
    }

    public string CurrentKeyId { get; }

    public byte[]? Key(string keyId) => _keys.TryGetValue(keyId, out var k) ? k : null;
}
