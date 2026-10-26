using System.Text.Json;
using Hcc.Contracts;

namespace Hcc.Domain.Errors;

/// <summary>
/// Typed domain/application error, the production counterpart of the prototype's <c>HccError</c>
/// (<c>03 Prototype/src/domain/errors.ts</c>). The API maps it to <see cref="ErrorResponse"/> + HTTP status.
/// </summary>
public class HccException : Exception
{
    public HccException(HccErrorCode code, string message, IReadOnlyDictionary<string, JsonElement>? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Details = details;
    }

    public HccException() : this(HccErrorCode.VALIDATION, "Check the details") { }

    public HccException(string message) : this(HccErrorCode.VALIDATION, message) { }

    public HccException(string message, Exception innerException) : this(HccErrorCode.VALIDATION, message, null, innerException) { }

    public HccErrorCode Code { get; }

    public IReadOnlyDictionary<string, JsonElement>? Details { get; }

    public static HccException Validation(string message) => new(HccErrorCode.VALIDATION, message);

    /// <summary>
    /// A server-side setup step is missing (for example the guest-ID encryption key). Still UNAVAILABLE / HTTP 503 on the wire, but with
    /// <c>details.reason = "NOT_CONFIGURED"</c> and <c>details.feature</c>, so clients can tell it from a lost database or network
    /// connection and do not switch the whole app to read-only.
    /// </summary>
    public static HccException NotConfigured(string feature, string message) => new(HccErrorCode.UNAVAILABLE, message,
        new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement("NOT_CONFIGURED"),
            ["feature"] = JsonSerializer.SerializeToElement(feature),
        });

    public ErrorResponse ToResponse() => new(Code.ToString(), Message, Details);
}
