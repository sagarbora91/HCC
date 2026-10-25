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

    /// <summary>DI registration (one line in Program.cs). Binds <c>Hcc:Access</c>; scope resolvers registered by other modules are picked up.</summary>
    public static IServiceCollection AddHccAccess(this IServiceCollection services, IConfiguration configuration, SqlPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection("Hcc:Access").Get<AccessOptions>() ?? new AccessOptions();
        services.AddSingleton(options);
        services.AddSingleton(sp => new AccessModule(platform, options, sp.GetRequiredService<TimeProvider>(), sp.GetServices<IAccessScopeResolver>()));
        services.AddSingleton<IAccessPolicy>(sp => sp.GetRequiredService<AccessModule>().Policy);
        services.AddSingleton<IAccessEvaluator>(sp => sp.GetRequiredService<AccessModule>().Policy);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Sessions);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Users);
        services.AddSingleton(sp => sp.GetRequiredService<AccessModule>().Capabilities);
        return services;
    }

    /// <summary>Validates the session (cookie or bearer token) before the handler runs; FORBIDDEN (reason SESSION_INVALID) otherwise.</summary>
    public static TBuilder RequireHccSession<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var sessions = http.RequestServices.GetRequiredService<ISessionService>();
            http.Items[SessionItem] = await sessions.ValidateAsync(TokenOf(http.Request), http.RequestAborted).ConfigureAwait(false);
            return await next(ctx).ConfigureAwait(false);
        });

    public static AuthenticatedSession GetHccSession(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[SessionItem] as AuthenticatedSession ?? throw new InvalidOperationException("Endpoint is missing RequireHccSession().");
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

        api.MapPost("/auth/sign-in", async (SignInRequest body, HttpContext http, ISessionService sessions, AccessOptions options, CancellationToken ct) =>
        {
            var (session, token) = await sessions.SignInAsync(body, http.Request.Headers[AccessConventions.DeviceTokenHeader].ToString(), ct).ConfigureAwait(false);
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

        var admin = api.MapGroup("/admin").RequireHccSession();
        admin.MapGet("/users", async (HttpContext http, IUserAdministration users, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await users.ListUsersAsync(http.GetHccSession(), page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false)));
        admin.MapGet("/devices", async (HttpContext http, IUserAdministration users, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await users.ListDevicesAsync(http.GetHccSession(), page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false)));

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
            if (result.Replayed) http.Response.Headers["Idempotent-Replayed"] = "true";
            return Results.Ok(result.Value);
        });

    private static string? TokenOf(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth["Bearer ".Length..].Trim();
        return request.Cookies.TryGetValue(AccessConventions.SessionCookie, out var cookie) ? cookie : null;
    }
}
