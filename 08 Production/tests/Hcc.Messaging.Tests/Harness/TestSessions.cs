using System.Globalization;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;

namespace Hcc.Messaging.Tests.Harness;

/// <summary>
/// Test stand-in for P1's <see cref="ISessionService"/> behind the real <c>RequireHccSession</c> filter: bearer token
/// <c>test-&lt;principal&gt;</c> is a valid session of that principal at the test property; anything else is UNAUTHENTICATED.
/// </summary>
public sealed class TestSessions(int propertyId) : ISessionService
{
    public static string Token(int principalId) => "test-" + principalId.ToString(CultureInfo.InvariantCulture);

    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default)
        => throw new NotSupportedException("Tests sign in with a bearer token.");

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var principal) && principal > 0)
            return Task.FromResult(new AuthenticatedSession(principal, principal, propertyId, "Test user " + t[5..], CredentialKind.Pin, null,
                DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}
