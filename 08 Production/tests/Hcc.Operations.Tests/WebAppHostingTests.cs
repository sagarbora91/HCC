using System.Net;
using Hcc.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hcc.Operations.Tests;

/// <summary>W4 F3 (H1): the API host serves the production web build (single index.html) on the same origin; A06-10: a missing build never reveals the folder.</summary>
public sealed class WebAppHostingTests : IDisposable
{
    private const string Page = "<!doctype html><html><head><style>body{margin:0}</style></head><body><div id=\"root\"></div><script>window.hcc=1;</script></body></html>";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HccWeb_" + Guid.NewGuid().ToString("N"));

    public WebAppHostingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* temp folder, best effort */ }
    }

    /// <summary>Collects the warnings the host writes (A06-10: the web-root folder goes to the log, never to the caller).</summary>
    private sealed class Warnings : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Sink(this);

        public void Dispose()
        {
        }

        private sealed class Sink(Warnings owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                lock (owner.Lines) owner.Lines.Add(formatter(state, exception));
            }
        }
    }

    private async Task<(WebApplication App, HttpClient Http)> StartAsync(string? webRoot, CancellationToken ct, Warnings? log = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (log is not null) builder.Logging.AddProvider(log);
        if (webRoot is not null) builder.Configuration["Hcc:Hosting:WebRoot"] = webRoot;
        var app = builder.Build();
        app.MapGroup("/api/v1").MapHccDiscovery();
        app.MapHccWebApp(app.Configuration);
        await app.StartAsync(ct);
        var addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(addr) });
    }

    [Fact]
    public async Task Index_html_is_served_at_root_with_revalidation_etag_and_a_hash_based_csp()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), Page, ct);
        var (app, http) = await StartAsync(_root, ct);
        using (http)
        {
            var res = await http.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("text/html; charset=utf-8", res.Content.Headers.ContentType!.ToString());
            Assert.Equal(Page, await res.Content.ReadAsStringAsync(ct));
            Assert.Equal("no-cache", res.Headers.CacheControl!.ToString());
            Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
            var csp = res.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("script-src 'self' 'sha256-", csp, StringComparison.Ordinal);
            Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
            Assert.Contains("connect-src 'self'", csp, StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("manifest-src 'self' data:", csp, StringComparison.Ordinal); // the web-app manifest and the empty @import are data: URLs
            Assert.Contains("style-src 'self' 'unsafe-inline' data:", csp, StringComparison.Ordinal);

            // Unchanged page: 304 on revalidation; the same page is also at /index.html; the API on the same origin still answers.
            using var again = new HttpRequestMessage(HttpMethod.Get, "/index.html");
            again.Headers.TryAddWithoutValidation("If-None-Match", res.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.NotModified, (await http.SendAsync(again, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/system/discovery", ct)).StatusCode);

            // An upgrade (new file) is picked up without a restart and gets a new ETag.
            await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), Page.Replace("hcc=1", "hcc=2", StringComparison.Ordinal), ct);
            var next = await http.GetAsync("/", ct);
            Assert.Contains("hcc=2", await next.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            Assert.NotEqual(res.Headers.ETag, next.Headers.ETag);
        }

        await app.DisposeAsync();
    }

    [Fact]
    public async Task Missing_index_html_answers_404_with_install_hint_and_never_caches()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_root, "nope");
        var log = new Warnings();
        var (app, http) = await StartAsync(folder, ct, log);
        using (http)
        {
            var res = await http.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync(ct);
            Assert.Contains("not installed", body, StringComparison.Ordinal);
            Assert.Equal("no-store", res.Headers.CacheControl!.ToString());

            // A06-10: the anonymous answer is the fixed text — no web-root folder, no drive or temp path, no file name of the server.
            Assert.Equal(WebAppHosting.NotInstalledText, body);
            Assert.DoesNotContain(folder, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.GetTempPath(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AppContext.BaseDirectory, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"[A-Za-z]:\\|\\\\|/home/|/tmp/", body);

            // The folder goes to the server log instead, once (not one line per anonymous request).
            using var again = await http.GetAsync("/index.html", ct);
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
            Assert.Equal(WebAppHosting.NotInstalledText, await again.Content.ReadAsStringAsync(ct));
            lock (log.Lines)
            {
                var line = Assert.Single(log.Lines, l => l.Contains("web app is not installed", StringComparison.Ordinal));
                Assert.Contains(folder, line, StringComparison.Ordinal);
            }
        }

        await app.DisposeAsync();
    }

    [Fact]
    public void Csp_allows_exactly_the_inline_scripts_of_the_page_by_hash()
    {
        var two = WebAppHosting.ContentSecurityPolicy("<script>a()</script><script type=\"module\">b()</script><style>x{}</style>");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(two, "'sha256-").Count);
        Assert.Matches("script-src 'self' 'sha256-[^;]*; style-src", two);
    }

    /// <summary>
    /// A10-05: the production build (03 Prototype/tools/csp.mjs) puts its own CSP meta into index.html for the APK, hashing each inline block
    /// over its UTF-8 bytes. A browser enforces both that meta and this header, so both must name the same hash. The known answers below are
    /// the values the browser reported and the ones pinned in 03 Prototype/tests/stream-csp.mjs.
    /// </summary>
    [Fact]
    public async Task A10_05_page_with_its_own_csp_meta_is_served_unchanged_and_the_header_names_the_same_utf8_script_hash()
    {
        const string hash = "'sha256-ocHLj6bOal4ZQL+5Og9MaBAMJpfXFKuP6jIxXg43t6I='"; // SHA-256 of the UTF-8 bytes of: window.r='₹ ·';
        var page = "<!doctype html><html lang=\"en\"><head><meta charset=\"UTF-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src "
            + hash + "; style-src 'sha256-IAdwN3biDCQ3brrgp1m8kBEsPQYyqfREKOEfeoREopc=' data:; connect-src 'self' https:; object-src 'none'; base-uri 'none'\"><style>body{margin:0}</style></head>"
            + "<body><div id=\"root\"></div><script>window.r='₹ ·';</script></body></html>";
        Assert.Contains("script-src 'self' 'sha256-wPLFDDlm0UuafW5zggSwu3dtgfgDZ0XpvOXam9dYF88=';", WebAppHosting.ContentSecurityPolicy(Page), StringComparison.Ordinal);

        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), page, new System.Text.UTF8Encoding(false), ct);
        var (app, http) = await StartAsync(_root, ct);
        using (http)
        {
            var res = await http.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(page, await res.Content.ReadAsStringAsync(ct));
            var csp = res.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("script-src 'self' " + hash + ";", csp, StringComparison.Ordinal);
            Assert.Contains("connect-src 'self';", csp, StringComparison.Ordinal);   // the header keeps the browser app on its own origin
            Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal); // header only: a meta policy cannot carry it
        }

        await app.DisposeAsync();
    }

    [Fact]
    public void Default_web_root_is_the_web_folder_next_to_the_program()
        => Assert.Equal(Path.Combine(AppContext.BaseDirectory, "web"), WebAppHosting.WebRoot(new ConfigurationBuilder().Build()));
}
