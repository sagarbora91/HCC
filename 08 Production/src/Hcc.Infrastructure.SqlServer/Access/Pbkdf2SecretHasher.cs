using System.Security.Cryptography;
using System.Text;
using Hcc.Application.Access;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>
/// PBKDF2-HMAC-SHA256 via .NET <see cref="Rfc2898DeriveBytes"/> (contracts §1; no donor identity stack): 16-byte random salt,
/// 32-byte hash, iteration count stored per credential so it can be raised later. Verification is constant-time.
/// ETP-REUSE: align with ETP credential hashing (if ETP stores user passwords) when V:\ETP is available.
/// </summary>
public sealed class Pbkdf2SecretHasher : ISecretHasher
{
    public const string Algorithm = "PBKDF2-SHA256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public HashedSecret Hash(string secret, int iterations)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1000);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new HashedSecret(Algorithm, iterations, salt, Derive(secret, salt, iterations));
    }

    public bool Verify(string secret, HashedSecret stored)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(stored);
        if (!string.Equals(stored.Algorithm, Algorithm, StringComparison.Ordinal) || stored.Hash.Length != HashBytes) return false;
        return CryptographicOperations.FixedTimeEquals(Derive(secret, stored.Salt, stored.Iterations), stored.Hash);
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
