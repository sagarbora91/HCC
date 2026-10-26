using System.Net;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Api;

/// <summary>
/// P1 access endpoints: sign-in/out, /me/capabilities, user/role/credential/exception/device administration (contracts §1–§2).
/// Other modules protect their groups with <see cref="RequireHccSession{TBuilder}"/> and read the actor with
/// <see cref="GetHccSession"/> (never from the request body).
/// </summary>
public static class AccessEndpoints
{
    private const string SessionItem = "hcc.session";

    /// <summary>Response header on a replayed command (same Idempotency-Key and body as an earlier request).</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>DI registration (one line in Program.cs). Binds <c>Hcc:Access</c>; scope resolvers registered by other modules are picked up.</summary>
    public static IServiceCollection AddHccAccess(this IServiceCollection services, IConfiguration configuration, SqlPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection("Hcc:Access").Get<AccessOptions>() ?? new AccessOptions();
        services.AddSingleton(options);
        services.AddSingleton(sp => new SignInThrottle(options, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new SqlRetentionPurge(platform.Connections, options, sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<RetentionWorker>();   // F-L03
        services.AddSingleton(sp => new AccessModule(platform, options, sp.GetRequiredService<TimeProvider>(), sp.GetServices<IAccessScopeResolver>()));
        services.AddSingleton<IAccessPolicy>(sp => sp.GetRequiredService<AccessModule>().Policy);
        services.AddSingleton<IAccessEvaluator>(sp => sp.GetRequiredService<AccessModule>().Policy);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Sessions);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Users);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Capabilities);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().AdminQueries);
        return services;
    }

    /// <summary>Validates the session (cookie or bearer token) before the handler runs; UNAUTHENTICATED (401, reason SESSION_INVALID) otherwise.</summary>
    public static TBuilder RequireHccSession<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var sessions = http.RequestServices.GetRequiredService<ISessionService>();
            http.Items[SessionItem] = await sessions.ValidateAsync(TokenOf(http.Request), DeviceTokenOf(http.Request), http.RequestAborted).ConfigureAwait(false);
            return await next(ctx).ConfigureAwait(false);
        });

    public static AuthenticatedSession GetHccSession(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[SessionItem] as AuthenticatedSession ?? throw new InvalidOperationException("Endpoint is missing RequireHccSession().");
    }

    /// <summary>
    /// A route group under <paramref name="api"/> whose endpoints all require a valid session (every module's endpoints go here;
    /// handlers read the actor with <see cref="HccActor"/> / <see cref="HccCommand"/>, never from the body).
    /// </summary>
    public static RouteGroupBuilder MapHccSecured(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.MapGroup(string.Empty).RequireHccSession();
    }

    /// <summary>The signed-in actor: (property, principal) of the validated session.</summary>
    public static (int PropertyId, int PrincipalId) HccActor(this HttpContext http)
    {
        var s = http.GetHccSession();
        return (s.PropertyId, s.UserId);
    }

    /// <summary>
    /// Command context from the session and the Idempotency-Key header (VALIDATION when missing/too long). With
    /// <paramref name="keyRequired"/> false (QuoteStay, contracts §3) a missing key gets a fresh server key, so a retry repeats the command.
    /// </summary>
    public static CommandContext HccCommand(this HttpContext http, string operation, bool keyRequired = true)
    {
        ArgumentNullException.ThrowIfNull(http);
        var session = http.GetHccSession();
        var key = http.Request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
        if (!keyRequired && string.IsNullOrWhiteSpace(key)) key = "auto-" + Guid.NewGuid().ToString("N");
        else key = http.Request.IdempotencyKey();
        var ctx = session.CommandContext(operation, key);
        ctx.Validate();
        return ctx;
    }

    /// <summary>200 with the command result; marks a replay with <see cref="ReplayedHeader"/>.</summary>
    public static IResult HccReply<T>(this HttpContext http, CommandResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Replayed) http.Response.Headers[ReplayedHeader] = "true";
        return Results.Ok(result.Value);
    }

    public static string IdempotencyKey(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > ApiConventions.IdempotencyKeyMaxLength)
            throw HccException.Validation($"{ApiConventions.IdempotencyKeyHeader} must be 1–{ApiConventions.IdempotencyKeyMaxLength} characters.");
        return key;
    }

    public static RouteGroupBuilder MapAccessEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost("/auth/sign-in", async (SignInRequest body, HttpContext http, ISessionService sessions, AccessOptions options, SignInThrottle throttle,
            TimeProvider clock, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(body);
            // F-M02: per-address and per-(address, name) limits, progressive delay per name (policy on SignInThrottle).
            var source = ClientAddress.Key(http.Connection.RemoteIpAddress);
            var gate = throttle.Check(source, body.LoginName, IsServerPc(http));
            if (!gate.Allowed)
            {
                http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(gate.RetryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(new ErrorResponse(nameof(HccErrorCode.FORBIDDEN), "Too many sign-in attempts from this device. Wait a few minutes and try again.",
                    new Dictionary<string, System.Text.Json.JsonElement> { ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement("TOO_MANY_ATTEMPTS") }), statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (gate.Delay > TimeSpan.Zero) await Task.Delay(gate.Delay, clock, ct).ConfigureAwait(false);
            AuthenticatedSession session;
            string token;
            try
            {
                (session, token) = await sessions.SignInAsync(body, DeviceTokenOf(http.Request), ct).ConfigureAwait(false);
            }
            catch (HccException ex) when (ex.Code == HccErrorCode.FORBIDDEN)
            {
                throttle.RecordFailure(source, body.LoginName);
                throw;
            }

            throttle.RecordSuccess(source, body.LoginName);
            http.Response.Cookies.Append(AccessConventions.SessionCookie, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,   // LAN HTTPS in production (P7); plain HTTP only in local development
                SameSite = SameSiteMode.Strict,
                Path = ApiConventions.BasePath,
                IsEssential = true,
            });
            var idle = (int)options.IdleTimeout.TotalSeconds;
            return Results.Ok(new SignInResult(session.UserId, session.DisplayName, Domain.Access.CredentialPolicy.ToWire(session.Kind), session.ToWire(idle), body.ReturnToken ? token : null));
        });

        api.MapPost("/auth/sign-out", async (HttpContext http, ISessionService sessions, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(TokenOf(http.Request), ct).ConfigureAwait(false);
            http.Response.Cookies.Delete(AccessConventions.SessionCookie, new CookieOptions { Path = ApiConventions.BasePath });
            return Results.NoContent();
        });

        // First Owner on an empty installation: only from the server PC itself (loopback) and only while no user exists.
        api.MapPost("/auth/bootstrap-owner", async (BootstrapOwnerRequest body, HttpContext http, IUserAdministration users, CancellationToken ct) =>
        {
            var remote = http.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote)) throw new HccException(HccErrorCode.FORBIDDEN, "The first Owner is created on the server PC itself.");
            return Results.Ok(await users.BootstrapOwnerAsync(body, ct).ConfigureAwait(false));
        });

        var me = api.MapGroup("/me").RequireHccSession();
        me.MapGet("/capabilities", async (HttpContext http, ICapabilitiesQuery query, CancellationToken ct) =>
            Results.Ok(await query.GetAsync(http.GetHccSession(), ct).ConfigureAwait(false)));

        // Names and ids only (no login names), so every role's screens can show who requested/prepared/decided something.
        me.MapGet("/directory", async (HttpContext http, IAccessAdminQueries q, CancellationToken ct) =>
            Results.Ok(await q.DirectoryAsync(http.GetHccSession(), ct).ConfigureAwait(false)));

        var admin = api.MapGroup("/admin").RequireHccSession();
        admin.MapGet("/users", async (HttpContext http, IUserAdministration users, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await users.ListUsersAsync(http.GetHccSession(), page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false)));
        admin.MapGet("/devices", async (HttpContext http, IUserAdministration users, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await users.ListDevicesAsync(http.GetHccSession(), page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false)));
        // W4-5: read-only role templates, user exceptions and the audit log for the Access control / Approvals & audit screens.
        admin.MapGet("/roles", async (HttpContext http, IAccessAdminQueries q, CancellationToken ct) =>
            Results.Ok(await q.RolesAsync(http.GetHccSession(), ct).ConfigureAwait(false)));
        admin.MapGet("/exceptions", async (HttpContext http, IAccessAdminQueries q, bool? includeRevoked, CancellationToken ct) =>
            Results.Ok(await q.ExceptionsAsync(http.GetHccSession(), includeRevoked ?? false, ct).ConfigureAwait(false)));
        admin.MapGet("/audit", async (HttpContext http, IAccessAdminQueries q, int? page, int? pageSize, string? action, int? principalId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            Results.Ok(await q.AuditAsync(http.GetHccSession(), page ?? 1, pageSize ?? 50, action, principalId, from, to, ct).ConfigureAwait(false)));

        var commands = api.MapGroup("/commands").RequireHccSession();
        Command<CreateUserRequest, UserVersionResult>(commands, "CreateUser", (u, s, k, b, ct) => u.CreateUserAsync(s, k, b, ct));
        Command<SetUserEnabledRequest, UserVersionResult>(commands, "SetUserEnabled", (u, s, k, b, ct) => u.SetUserEnabledAsync(s, k, b, ct));
        Command<AssignRoleRequest, UserVersionResult>(commands, "AssignRole", (u, s, k, b, ct) => u.AssignRoleAsync(s, k, b, ct));
        Command<RevokeRoleRequest, UserVersionResult>(commands, "RevokeRole", (u, s, k, b, ct) => u.RevokeRoleAsync(s, k, b, ct));
        Command<SetCredentialRequest, UserVersionResult>(commands, "SetCredential", (u, s, k, b, ct) => u.SetCredentialAsync(s, k, b, ct));
        Command<ChangeOwnCredentialRequest, UserVersionResult>(commands, "ChangeOwnCredential", (u, s, k, b, ct) => u.ChangeOwnCredentialAsync(s, k, b, ct));
        Command<GrantExceptionRequest, ExceptionResult>(commands, "GrantException", (u, s, k, b, ct) => u.GrantExceptionAsync(s, k, b, ct));
        Command<RevokeExceptionRequest, ExceptionResult>(commands, "RevokeException", (u, s, k, b, ct) => u.RevokeExceptionAsync(s, k, b, ct));
        Command<RegisterDeviceRequest, RegisterDeviceResult>(commands, "RegisterDevice", (u, s, k, b, ct) => u.RegisterDeviceAsync(s, k, b, ct));
        Command<RevokeDeviceRequest, DeviceRevokedResult>(commands, "RevokeDevice", (u, s, k, b, ct) => u.RevokeDeviceAsync(s, k, b, ct));
        return api;
    }

    private static void Command<TBody, TResult>(RouteGroupBuilder group, string name,
        Func<IUserAdministration, AuthenticatedSession, string, TBody, CancellationToken, Task<CommandResult<TResult>>> run) =>
        group.MapPost("/" + name, async (TBody body, HttpContext http, IUserAdministration users, CancellationToken ct) =>
        {
            var result = await run(users, http.GetHccSession(), http.Request.IdempotencyKey(), body, ct).ConfigureAwait(false);
            return http.HccReply(result);
        });

    /// <summary>
    /// The request comes from the server PC itself: a loopback address and no proxy header (a local reverse proxy makes every
    /// caller look like loopback; those requests carry X-Forwarded-For and are not trusted as local).
    /// </summary>
    public static bool IsServerPc(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var remote = http.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote) && !http.Request.Headers.ContainsKey("X-Forwarded-For");
    }

    private static string? DeviceTokenOf(HttpRequest request) =>
        request.Headers[AccessConventions.DeviceTokenHeader].ToString() is { Length: > 0 } d ? d : null;

    private static string? TokenOf(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth["Bearer ".Length..].Trim();
        return request.Cookies.TryGetValue(AccessConventions.SessionCookie, out var cookie) ? cookie : null;
    }
}
