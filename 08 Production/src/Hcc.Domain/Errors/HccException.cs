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

    public ErrorResponse ToResponse() => new(Code.ToString(), Message, Details);
}
