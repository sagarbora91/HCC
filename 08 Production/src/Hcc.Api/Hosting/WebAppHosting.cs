using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Api.Hosting;

/// <summary>
/// Serves the production web build (a single self-contained <c>index.html</c>, built by <c>npm run build:production</c>) from the API host,
/// so browsers on the hotel LAN open <c>https://&lt;server&gt;:5443/</c> and the app talks to <c>/api/v1</c> on the SAME origin (no CORS, no proxy).
/// The file lives in <c>Hcc:Hosting:WebRoot</c> (default: the <c>web</c> folder next to <c>Hcc.Api.exe</c>) and is re-read when it changes,
/// so an upgrade needs no restart. Only <c>/</c> and <c>/index.html</c> are served; the app has no other files and no client-side URL routes.
/// </summary>
public static partial class WebAppHosting
{
    public const string FileName = "index.html";

    [GeneratedRegex(@"<(script|style)(?<attrs>[^>]*)>(?<body>.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineBlock();

    /// <summary>Folder that holds index.html.</summary>
    public static string WebRoot(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[HccHostingOptions.Section + ":WebRoot"];
        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "web") : configured.Trim();
    }

    /// <summary>
    /// Content-Security-Policy for a single-file app: the inline script is allowed by its SHA-256 only (nothing else may run), styles may be
    /// inline (React style attributes), and the page may talk only to its own origin. No framing, no plugins, no base-tag tricks.
    /// </summary>
    public static string ContentSecurityPolicy(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var scripts = new List<string>();
        foreach (Match m in InlineBlock().Matches(html))
        {
            if (!m.Groups[1].Value.Equals("script", StringComparison.OrdinalIgnoreCase)) continue;
            scripts.Add("'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups["body"].Value))) + "'");
        }

        var scriptSrc = scripts.Count == 0 ? "'self'" : "'self' " + string.Join(' ', scripts);
        return "default-src 'none'; script-src " + scriptSrc + "; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; "
            + "connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:; manifest-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
    }

    private sealed record Loaded(DateTime Stamp, long Length, byte[] Body, string ETag, string Csp);

    public static IEndpointRouteBuilder MapHccWebApp(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);
        var path = Path.Combine(WebRoot(configuration), FileName);
        var gate = new object();
        Loaded? cache = null;

        Loaded? Load()
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            lock (gate)
            {
                if (cache is not null && cache.Stamp == info.LastWriteTimeUtc && cache.Length == info.Length) return cache;
                var body = File.ReadAllBytes(path);
                var etag = "\"" + Convert.ToHexString(SHA256.HashData(body))[..32].ToLowerInvariant() + "\"";
                return cache = new Loaded(info.LastWriteTimeUtc, info.Length, body, etag, ContentSecurityPolicy(Encoding.UTF8.GetString(body)));
            }
        }

        IResult Serve(HttpContext http)
        {
            var page = Load();
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h["X-Frame-Options"] = "DENY";
            if (page is null)
            {
                h.CacheControl = "no-store";
                return Results.Text("The HCC web app is not installed on this server. Copy index.html (from npm run build:production) to " + WebRoot(configuration) + " (see deployment/windows/INSTALL.md, section 'Web app').", "text/plain", Encoding.UTF8, StatusCodes.Status404NotFound);
            }

            // The page itself is revalidated on every open (ETag, 304 when unchanged), so an upgrade reaches every browser at once.
            h.CacheControl = "no-cache";
            h.ETag = page.ETag;
            h.ContentSecurityPolicy = page.Csp;
            if (http.Request.Headers.IfNoneMatch.Contains(page.ETag)) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Bytes(page.Body, "text/html; charset=utf-8");
        }

        app.MapMethods("/", ["GET", "HEAD"], (HttpContext http) => Serve(http)).ExcludeFromDescription();
        app.MapMethods("/" + FileName, ["GET", "HEAD"], (HttpContext http) => Serve(http)).ExcludeFromDescription();
        return app;
    }
}
