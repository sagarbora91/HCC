using System.Text.Json;

namespace Hcc.Contracts;

/// <summary>Error body <c>{code, message, details?}</c> (contracts package §2). <c>Code</c> is the <see cref="HccErrorCode"/> name.</summary>
public sealed record ErrorResponse(string Code, string Message, IReadOnlyDictionary<string, JsonElement>? Details = null);
