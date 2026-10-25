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
            if (status >= 500) LogUnhandled(logger, ex);
            context.Response.Clear();
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled server error")]
    private static partial void LogUnhandled(ILogger logger, Exception ex);
}
