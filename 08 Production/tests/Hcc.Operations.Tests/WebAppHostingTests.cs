using System.Net;
using Hcc.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Operations.Tests;

/// <summary>W4 F3 (H1): the API host serves the production web build (single index.html) on the same origin.</summary>
public sealed class WebAppHostingTests : IDisposable
{
    private const string Page = "<!doctype html><html><head><style>body{margin:0}</style></head><body><div id=\"root\"></div><script>window.hcc=1;</script></body></html>";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HccWeb_" + Guid.NewGuid().ToString("N"));

    public WebAppHostingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* temp folder, best effort */ }
    }

    private async Task<(WebApplication App, HttpClient Http)> StartAsync(string? webRoot, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
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
        var (app, http) = await StartAsync(Path.Combine(_root, "nope"), ct);
        using (http)
        {
            var res = await http.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Contains("not installed", await res.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            Assert.Equal("no-store", res.Headers.CacheControl!.ToString());
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

    [Fact]
    public void Default_web_root_is_the_web_folder_next_to_the_program()
        => Assert.Equal(Path.Combine(AppContext.BaseDirectory, "web"), WebAppHosting.WebRoot(new ConfigurationBuilder().Build()));
}
