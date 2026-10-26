using System.Net;
using Hcc.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Hcc.Operations.Tests;

/// <summary>
/// F-H04 / F-L11 / F-L02: the three production listeners on real Kestrel (loopback, ephemeral ports). The public listener serves only
/// the booking routes and honours forwarded headers from the configured proxy only; the setup listener serves only setup; loopback-only
/// routes refuse proxied requests everywhere.
/// </summary>
public sealed class ListenerTests
{
    private sealed record Ports(int Lan, int Setup, int Public);

    private static async Task<(WebApplication App, Ports Ports)> StartAsync(CancellationToken ct, params string[] proxies)
    {
        var builder = WebApplication.CreateBuilder();
        for (var i = 0; i < proxies.Length; i++) builder.Configuration[$"Hcc:Hosting:KnownProxies:{i}"] = proxies[i];
        ListenOptions? lan = null, setup = null, pub = null;
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, 0, o => lan = o.MarkAs(HccListenerKind.Lan));
            k.Listen(IPAddress.Loopback, 0, o => setup = o.MarkAs(HccListenerKind.Setup));
            k.Listen(IPAddress.Loopback, 0, o => pub = o.MarkAs(HccListenerKind.Public));
        });
        var app = builder.Build();
        app.UseHccHostingGuards();
        static string Echo(HttpContext http) => http.Connection.RemoteIpAddress + "|" + http.Request.IsHttps;
        app.MapGet("/api/v1/public/booking/quote", (HttpContext http) => Echo(http));
        app.MapPost("/api/v1/auth/sign-in", () => "signed-in");
        app.MapGet("/api/v1/setup/status", () => "setup");
        app.MapPost("/api/v1/auth/bootstrap-owner", () => "bootstrap");
        app.MapGet("/api/v1/system/health", () => "ok");
        app.MapGet("/api/v1/staff/rooms", () => "rooms");
        await app.StartAsync(ct);
        return (app, new Ports(lan!.IPEndPoint!.Port, setup!.IPEndPoint!.Port, pub!.IPEndPoint!.Port));
    }

    private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> SendAsync(int port, HttpMethod method, string path, CancellationToken ct, params (string Name, string Value)[] headers)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var req = new HttpRequestMessage(method, path);
        foreach (var (n, v) in headers) req.Headers.TryAddWithoutValidation(n, v);
        var res = await http.SendAsync(req, ct);
        return (res.StatusCode, await res.Content.ReadAsStringAsync(ct), res);
    }

    [Fact]
    public async Task Public_listener_serves_only_booking_routes_and_trusts_only_the_configured_proxy()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, p) = await StartAsync(ct, "127.0.0.1");
        await using (app)
        {
            // Staff API, sign-in, setup and bootstrap do not exist on the public listener.
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Public, HttpMethod.Post, "/api/v1/auth/sign-in", ct)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Public, HttpMethod.Get, "/api/v1/setup/status", ct)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Public, HttpMethod.Post, "/api/v1/auth/bootstrap-owner", ct)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Public, HttpMethod.Get, "/api/v1/staff/rooms", ct)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Public, HttpMethod.Get, "/", ct)).Status);

            // Not HTTPS end to end (no X-Forwarded-Proto from the proxy) -> refused.
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(p.Public, HttpMethod.Get, "/api/v1/public/booking/quote", ct)).Status);

            // Through the proxy: the route sees the real client (rate limiter key) and HTTPS.
            var ok = await SendAsync(p.Public, HttpMethod.Get, "/api/v1/public/booking/quote", ct, ("X-Forwarded-For", "203.0.113.9"), ("X-Forwarded-Proto", "https"));
            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal("203.0.113.9|True", ok.Body);
            Assert.Equal("no-store", ok.Response.Headers.CacheControl!.ToString());
            Assert.Equal("nosniff", ok.Response.Headers.GetValues("X-Content-Type-Options").Single());

            // Only the last hop is taken: a client-supplied chain cannot pick its own address.
            var chain = await SendAsync(p.Public, HttpMethod.Get, "/api/v1/public/booking/quote", ct, ("X-Forwarded-For", "10.9.9.9, 198.51.100.7"), ("X-Forwarded-Proto", "https"));
            Assert.Equal("198.51.100.7|True", chain.Body);
        }
    }

    [Fact]
    public async Task Forwarded_headers_from_an_unlisted_proxy_are_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, p) = await StartAsync(ct, "10.1.2.3"); // the test client (127.0.0.1) is not the configured proxy
        await using (app)
        {
            var r = await SendAsync(p.Public, HttpMethod.Get, "/api/v1/public/booking/quote", ct, ("X-Forwarded-For", "203.0.113.9"), ("X-Forwarded-Proto", "https"));
            Assert.Equal(HttpStatusCode.Forbidden, r.Status); // still plain HTTP from loopback: refused, never treated as the client
        }
    }

    [Fact]
    public async Task Setup_listener_serves_only_setup_health_and_discovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, p) = await StartAsync(ct, "127.0.0.1");
        await using (app)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(p.Setup, HttpMethod.Get, "/api/v1/setup/status", ct)).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(p.Setup, HttpMethod.Post, "/api/v1/auth/bootstrap-owner", ct)).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(p.Setup, HttpMethod.Get, "/api/v1/system/health", ct)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Setup, HttpMethod.Post, "/api/v1/auth/sign-in", ct)).Status); // no cleartext sign-in
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(p.Setup, HttpMethod.Get, "/api/v1/staff/rooms", ct)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(p.Setup, HttpMethod.Get, "/api/v1/setup/status", ct, ("X-Forwarded-For", "203.0.113.9"))).Status);
        }
    }

    [Fact]
    public async Task Lan_listener_serves_staff_routes_ignores_forwarded_headers_and_refuses_proxied_setup()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, p) = await StartAsync(ct, "127.0.0.1");
        await using (app)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(p.Lan, HttpMethod.Post, "/api/v1/auth/sign-in", ct)).Status);
            Assert.Equal("127.0.0.1|False", (await SendAsync(p.Lan, HttpMethod.Get, "/api/v1/public/booking/quote", ct, ("X-Forwarded-For", "203.0.113.9"))).Body);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(p.Lan, HttpMethod.Get, "/api/v1/setup/status", ct, ("Forwarded", "for=203.0.113.9"))).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(p.Lan, HttpMethod.Post, "/api/v1/auth/bootstrap-owner", ct, ("X-Forwarded-For", "203.0.113.9"))).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(p.Lan, HttpMethod.Get, "/api/v1/setup/status", ct)).Status);
        }
    }

    [Fact]
    public void Forwarded_options_drop_the_loopback_defaults()
    {
        var o = HccListeners.ForwardedOptions(new HccHostingOptions { KnownProxies = ["192.168.1.5"], KnownNetworks = ["10.0.0.0/24", "bad"] });
        Assert.Equal([IPAddress.Parse("192.168.1.5")], o.KnownProxies);
        Assert.Single(o.KnownIPNetworks);
        Assert.Equal(1, o.ForwardLimit);
        var none = HccListeners.ForwardedOptions(new HccHostingOptions());
        Assert.Empty(none.KnownProxies);
        Assert.Empty(none.KnownIPNetworks);
    }

    [Fact]
    public void Listener_defaults_are_off()
    {
        var h = new ConfigurationBuilder().Build().GetSection(HccHostingOptions.Section).Get<HccHostingOptions>() ?? new HccHostingOptions();
        Assert.Equal(0, h.LoopbackHttpPort);
        Assert.Equal(0, h.PublicPort);
        Assert.Equal(8, h.MaxRequestBodyMegabytes);
    }
}
