using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Api;

/// <summary>Maps exceptions to the contracts error body <c>{code, message, details?}</c> and HTTP status (contracts §2).</summary>
public sealed partial class HccErrorMiddleware(RequestDelegate next, ILogger<HccErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, body) = Map(ex);
            context.Response.Clear();
            if (status >= 500)
            {
                // F-L07: no exception message in the log (SQL Server duplicate-key messages carry the key value, e.g. a phone or a
                // login) and none on the wire. The correlation id links the client's error to the server log line.
                var correlationId = context.TraceIdentifier;
                var sql = SqlErrorNumber(ex);
                LogUnhandled(logger, correlationId, ex.GetType().FullName ?? ex.GetType().Name, ex.GetBaseException().GetType().Name, sql?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    context.Request.Method, context.Request.Path.Value ?? string.Empty, ex.StackTrace ?? string.Empty);
                context.Response.Headers["X-Correlation-Id"] = correlationId;
                body = body with { Details = WithCorrelation(body.Details, correlationId) };
            }

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(body).ConfigureAwait(false);
        }
    }

    /// <summary>HccException → its code's status; anything else → 500 without internal detail.</summary>
    public static (int Status, ErrorResponse Body) Map(Exception ex) => ex switch
    {
        HccException h => (HccErrorCatalog.HttpStatus(h.Code), h.ToResponse()),
        BadHttpRequestException => (HccErrorCatalog.HttpStatus(HccErrorCode.VALIDATION), new ErrorResponse(nameof(HccErrorCode.VALIDATION), "The request could not be read.")),
        _ => (500, new ErrorResponse("INTERNAL", "Something went wrong on the server. The error has been logged.")),
    };

    private static int? SqlErrorNumber(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException s) return s.Number;
        return null;
    }

    private static Dictionary<string, System.Text.Json.JsonElement> WithCorrelation(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? details, string correlationId)
    {
        var d = details is null ? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal) : new Dictionary<string, System.Text.Json.JsonElement>(details, StringComparer.Ordinal);
        d["correlationId"] = System.Text.Json.JsonSerializer.SerializeToElement(correlationId);
        return d;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Server error {CorrelationId}: {ExceptionType} (root {RootType}, SQL error {SqlNumber}) on {Method} {Path} | {StackTrace}")]
    private static partial void LogUnhandled(ILogger logger, string correlationId, string exceptionType, string rootType, string sqlNumber, string method, string path, string stackTrace);
}
