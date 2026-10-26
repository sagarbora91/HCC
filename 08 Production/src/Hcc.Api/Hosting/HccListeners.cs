using System.Net;
using Hcc.Contracts;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Hcc.Api.Hosting;

/// <summary>Which Kestrel listener a request arrived on (F-H04, F-L11).</summary>
public enum HccListenerKind
{
    /// <summary>Not one of the production listeners (development URLs, tests): no listener rules apply.</summary>
    Unclassified,

    /// <summary>LAN HTTPS (all interfaces, <c>Hcc:Hosting:HttpsPort</c>): staff web app, APK, staff API.</summary>
    Lan,

    /// <summary>Plain HTTP on 127.0.0.1 (<c>Hcc:Hosting:LoopbackHttpPort</c>), opt-in for first-time setup only.</summary>
    Setup,

    /// <summary>The internet-facing booking listener (<c>Hcc:Hosting:PublicPort</c>): only <c>/api/v1/public/booking/*</c>.</summary>
    Public,
}

/// <summary>
/// Listener separation and hosting guards. Every production listener is tagged when Kestrel binds it; a small middleware then
/// enforces what each listener may serve:
/// <list type="bullet">
/// <item><b>Public</b>: only the anonymous booking routes; every other path answers 404, so the staff API, sign-in and setup are
/// never reachable on it. Forwarded headers (<c>X-Forwarded-For</c>/<c>-Proto</c>) are honoured on this listener only, and only
/// from the addresses in <c>Hcc:Hosting:KnownProxies</c>/<c>KnownNetworks</c> (the defaults that trust loopback are removed), so the
/// rate limiter keys on the real client and a reverse proxy never makes an internet caller look like the server PC. A request
/// that is not HTTPS end to end (TLS on the listener or <c>X-Forwarded-Proto: https</c> from a known proxy) is refused.</item>
/// <item><b>Setup</b> (plain-HTTP loopback): only first-time setup, the first-Owner bootstrap, health and discovery; sign-in and the
/// staff API are refused, so no session cookie is ever issued over cleartext.</item>
/// <item>Loopback-only routes (<c>/setup/*</c>, <c>/auth/bootstrap-owner</c>) are refused on any listener when the request carries
/// a forwarding header: a proxied request is never "the server PC", even if the proxy connects from 127.0.0.1.</item>
/// </list>
/// Also sets the API security headers (F-L02): <c>nosniff</c>, <c>no-referrer</c>, no framing, <c>Cache-Control: no-store</c> on
/// <c>/api/v1</c> unless the route set its own, and HSTS on HTTPS responses in Production.
/// </summary>
public static class HccListeners
{
    private const string ItemKey = "hcc.listener";

    /// <summary>Ports bound by <see cref="HccHostingExtensions.UseHccHosting"/> (fallback when the connection tag is not visible).</summary>
    public sealed class Map
    {
        private readonly Dictionary<int, HccListenerKind> _ports = [];

        public void Add(int port, HccListenerKind kind)
        {
            if (port > 0) _ports[port] = kind;
        }

        public HccListenerKind Of(int port) => _ports.TryGetValue(port, out var k) ? k : HccListenerKind.Unclassified;
    }

    /// <summary>Tags every connection accepted by this listener (Kestrel connection middleware).</summary>
    public static ListenOptions MarkAs(this ListenOptions listen, HccListenerKind kind)
    {
        ArgumentNullException.ThrowIfNull(listen);
        listen.Use(next => connection =>
        {
            connection.Items[ItemKey] = kind;
            return next(connection);
        });
        return listen;
    }

    public static HccListenerKind KindOf(HttpContext http, Map? ports = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var items = http.Features.Get<IConnectionItemsFeature>()?.Items;
        if (items is not null && items.TryGetValue(ItemKey, out var v) && v is HccListenerKind k) return k;
        return ports?.Of(http.Connection.LocalPort) ?? HccListenerKind.Unclassified;
    }

    private static readonly PathString Api = new(ApiConventions.BasePath);
    private static readonly PathString PublicBooking = new(ApiConventions.BasePath + "/public/booking");
    private static readonly PathString[] LoopbackOnly = [new(ApiConventions.BasePath + "/setup"), new(ApiConventions.BasePath + "/auth/bootstrap-owner")];
    private static readonly PathString[] SetupListenerAlso = [new(ApiConventions.BasePath + "/system/health"), new(ApiConventions.BasePath + "/system/discovery")];
    private static readonly string[] ForwardingHeaders = ["X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "Forwarded", "X-Real-IP"];

    public static bool IsPublicBookingPath(PathString path) => path.StartsWithSegments(PublicBooking);

    public static bool IsLoopbackOnlyPath(PathString path) => LoopbackOnly.Any(p => path.StartsWithSegments(p));

    public static bool IsSetupListenerPath(PathString path) => IsLoopbackOnlyPath(path) || SetupListenerAlso.Any(p => path.StartsWithSegments(p));

    public static bool HasForwardingHeader(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ForwardingHeaders.Any(h => request.Headers.ContainsKey(h));
    }

    /// <summary>Forwarded-headers options for the public listener: only the configured proxies are trusted (loopback defaults removed), one hop.</summary>
    public static ForwardedHeadersOptions ForwardedOptions(HccHostingOptions h)
    {
        ArgumentNullException.ThrowIfNull(h);
        var o = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1, RequireHeaderSymmetry = false };
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
        foreach (var p in h.KnownProxies ?? []) if (IPAddress.TryParse(p?.Trim(), out var ip)) o.KnownProxies.Add(ip);
        foreach (var n in h.KnownNetworks ?? []) if (System.Net.IPNetwork.TryParse(n?.Trim(), out var net)) o.KnownIPNetworks.Add(net);
        return o;
    }

    /// <summary>Adds the listener rules, forwarded-header handling (public listener only) and security headers. First in the pipeline.</summary>
    public static WebApplication UseHccHostingGuards(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var h = app.Configuration.GetSection(HccHostingOptions.Section).Get<HccHostingOptions>() ?? new HccHostingOptions();
        var ports = app.Services.GetService<Map>();
        var production = app.Environment.IsProduction();
        if (production && h.LoopbackHttpPort != 0)
            app.Logger.LogWarning("The plain-HTTP setup listener http://127.0.0.1:{Port} is on. It is for first-time setup only: set Hcc:Hosting:LoopbackHttpPort to 0 (Initialize-HccProperty.ps1 does this) and restart the service.", h.LoopbackHttpPort);

        var forwarded = ForwardedOptions(h);
        app.UseWhen(http => KindOf(http, ports) == HccListenerKind.Public, b => b.UseForwardedHeaders(forwarded));
        app.Use(async (http, next) =>
        {
            var kind = KindOf(http, ports);
            AddSecurityHeaders(http, production);
            var refusal = Check(kind, http.Request);
            if (refusal is not null)
            {
                await refusal.ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            await next(http).ConfigureAwait(false);
        });
        return app;
    }

    /// <summary>The listener rule for one request; null = allowed. Public for the tests.</summary>
    public static IResult? Check(HccListenerKind kind, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.Path;
        if (kind == HccListenerKind.Public)
        {
            if (!IsPublicBookingPath(path)) return NotFound("Not found.");
            if (!request.IsHttps) return Refused("The booking service must be reached over HTTPS (the reverse proxy must send X-Forwarded-Proto: https and be listed in Hcc:Hosting:KnownProxies).");
            return null;
        }

        if (IsLoopbackOnlyPath(path) && HasForwardingHeader(request))
            return Refused("First-time setup is done on the server PC itself, never through a proxy.");
        if (kind == HccListenerKind.Setup && !IsSetupListenerPath(path))
            return NotFound("This local address only serves first-time setup. Use https://<server>:5443/ for HCC.");
        return null;
    }

    private static IResult NotFound(string message) => Results.Json(new ErrorResponse(nameof(HccErrorCode.NOT_FOUND), message), statusCode: StatusCodes.Status404NotFound);

    private static IResult Refused(string message) => Results.Json(new ErrorResponse(nameof(HccErrorCode.FORBIDDEN), message), statusCode: StatusCodes.Status403Forbidden);

    private static void AddSecurityHeaders(HttpContext http, bool production)
    {
        var api = http.Request.Path.StartsWithSegments(Api);
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            if (!h.ContainsKey("X-Content-Type-Options")) h["X-Content-Type-Options"] = "nosniff";
            if (production && http.Request.IsHttps && !h.ContainsKey("Strict-Transport-Security")) h["Strict-Transport-Security"] = "max-age=31536000";
            if (api)
            {
                if (!h.ContainsKey("Referrer-Policy")) h["Referrer-Policy"] = "no-referrer";
                if (!h.ContainsKey("X-Frame-Options")) h["X-Frame-Options"] = "DENY";
                if (string.IsNullOrEmpty(h.CacheControl)) h.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
    }
}
