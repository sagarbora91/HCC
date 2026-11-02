using System.Net;
using Hcc.Api.Endpoints;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.Egress;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Hcc.Egress.Tests;

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Settings source with switchable values; Throw = the settings store fails; Reads counts store reads.</summary>
public sealed class FakeSource : IEgressSettingsSource
{
    public bool? Master { get; set; }

    public Dictionary<EgressIntegration, bool?> Switches { get; } = EgressKeys.All.ToDictionary(i => i, _ => (bool?)false);

    public string? Window { get; set; }

    public bool Throw { get; set; }

    public int Reads { get; private set; }

    public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");

    public Task<EgressSettings> ReadAsync(CancellationToken ct = default)
    {
        Reads++;
        if (Throw) throw new InvalidOperationException("settings store down");
        return Task.FromResult(new EgressSettings(Master, new Dictionary<EgressIntegration, bool?>(Switches), Window));
    }

    public FakeSource AllOn()
    {
        Master = true;
        foreach (var i in EgressKeys.All) Switches[i] = true;
        return this;
    }
}

public sealed class CountingHandler : HttpMessageHandler
{
    public int Sends { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sends++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

public sealed class MemoryAudit : IEgressAudit
{
    public List<EgressAuditEntry> Rows { get; } = [];

    public bool Fail { get; set; }

    public Task WriteAsync(EgressAuditEntry entry, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("audit down");
        Rows.Add(entry);
        return Task.CompletedTask;
    }
}

public sealed class Gate : IDisposable
{
    public FakeSource Source { get; } = new();

    public CountingHandler Inner { get; } = new();

    public MemoryAudit Audit { get; } = new();

    // Monday 2 Nov 2026 10:00 IST
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromMinutes(330)));

    public ServiceProvider Services { get; }

    public Gate()
    {
        var s = new ServiceCollection();
        s.AddSingleton<TimeProvider>(Clock);
        s.AddSingleton<IEgressSettingsSource>(Source);
        s.AddSingleton<IEgressAudit>(Audit);
        s.AddHccEgress();
        s.AddHttpClient(EgressKeys.ClientName).ConfigurePrimaryHttpMessageHandler(() => Inner);
        Services = s.BuildServiceProvider();
    }

    public EgressPolicy Policy => Services.GetRequiredService<EgressPolicy>();

    public HttpClient Client() => Services.GetRequiredService<IHttpClientFactory>().CreateClient(EgressKeys.ClientName);

    public async Task<HccException?> SendAsync(EgressIntegration? integration, string url = "https://api.example.test/send")
    {
        Policy.Invalidate();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (integration is { } i) req.For(i);
        try
        {
            using var _ = await Client().SendAsync(req, TestContext.Current.CancellationToken);
            return null;
        }
        catch (HccException ex)
        {
            return ex;
        }
    }

    public void Dispose() => Services.Dispose();
}

public sealed class GateTests
{
    [Fact]
    public async Task Default_off_makes_zero_sends_and_refuses_with_NET_OFFLINE()
    {
        using var g = new Gate();
        g.Source.Master = false;
        foreach (var i in EgressKeys.All)
        {
            var ex = await g.SendAsync(i);
            Assert.NotNull(ex);
            Assert.Equal(HccErrorCode.NET_OFFLINE, ex!.Code);
            Assert.Equal("master-off", ex.Details!["reason"].GetString());
            Assert.Equal(EgressKeys.Name(i), ex.Details["integration"].GetString());
        }

        Assert.Equal(0, g.Inner.Sends);
        Assert.Equal(503, HccErrorCatalog.HttpStatus(HccErrorCode.NET_OFFLINE));
    }

    [Fact]
    public async Task All_on_sends_every_request()
    {
        using var g = new Gate();
        g.Source.AllOn();
        for (var n = 0; n < 5; n++) Assert.Null(await g.SendAsync(EgressIntegration.Ota));
        Assert.Equal(5, g.Inner.Sends);
        Assert.All(g.Audit.Rows, r => Assert.True(r.Allowed));
        Assert.Equal("api.example.test", g.Audit.Rows[0].Host);
    }

    [Fact]
    public async Task Each_sub_switch_gates_only_its_integration()
    {
        foreach (var off in EgressKeys.All)
        {
            using var g = new Gate();
            g.Source.AllOn();
            g.Source.Switches[off] = false;
            foreach (var i in EgressKeys.All)
            {
                var ex = await g.SendAsync(i);
                if (i == off) Assert.Equal("integration-off", ex!.Details!["reason"].GetString());
                else Assert.Null(ex);
            }

            Assert.Equal(EgressKeys.All.Count - 1, g.Inner.Sends);
        }
    }

    [Fact]
    public async Task Master_off_wins_over_every_switch()
    {
        using var g = new Gate();
        g.Source.AllOn();
        g.Source.Master = false;
        Assert.Equal("master-off", (await g.SendAsync(EgressIntegration.Messaging))!.Details!["reason"].GetString());
        Assert.Equal(0, g.Inner.Sends);
    }

    [Fact]
    public async Task Request_without_integration_is_refused_before_the_policy()
    {
        using var g = new Gate();
        g.Source.AllOn();
        var ex = await g.SendAsync(null);
        Assert.Equal("integration-missing", ex!.Details!["reason"].GetString());
        Assert.Equal(0, g.Inner.Sends);
        Assert.Equal(0, g.Source.Reads);
    }

    [Fact]
    public async Task Fail_closed_on_settings_exception_missing_value_and_bad_window()
    {
        using var g = new Gate();
        g.Source.AllOn();
        g.Source.Throw = true;
        Assert.Equal("settings-unreadable", (await g.SendAsync(EgressIntegration.Email))!.Details!["reason"].GetString());
        g.Source.Throw = false;
        g.Source.Master = null;
        Assert.Equal("settings-unreadable", (await g.SendAsync(EgressIntegration.Email))!.Details!["reason"].GetString());
        g.Source.Master = true;
        g.Source.Switches[EgressIntegration.Email] = null;
        Assert.Equal("settings-unreadable", (await g.SendAsync(EgressIntegration.Email))!.Details!["reason"].GetString());
        g.Source.Switches[EgressIntegration.Email] = true;
        g.Source.Window = "{\"from\":\"25:00\"}";
        Assert.Equal("settings-unreadable", (await g.SendAsync(EgressIntegration.Email))!.Details!["reason"].GetString());
        Assert.Equal(0, g.Inner.Sends);
        g.Source.Window = null;
        Assert.Null(await g.SendAsync(EgressIntegration.Email));
        Assert.Equal(1, g.Inner.Sends);
    }

    [Fact]
    public async Task Stale_snapshot_is_ignored_and_the_cache_lasts_five_seconds()
    {
        using var g = new Gate();
        g.Source.AllOn();
        await g.Policy.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(g.Policy.Evaluate(EgressIntegration.Ota, g.Clock.Now).Allowed);
        var reads = g.Source.Reads;
        g.Clock.Now += TimeSpan.FromSeconds(4);
        await g.Policy.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(reads, g.Source.Reads);
        g.Clock.Now += TimeSpan.FromSeconds(2);
        g.Source.Master = false;
        await g.Policy.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("master-off", g.Policy.Evaluate(EgressIntegration.Ota, g.Clock.Now).Reason);
        g.Source.Master = true;
        await g.Policy.RefreshAsync(TestContext.Current.CancellationToken);   // still cached
        g.Clock.Now += EgressPolicy.StaleAfter;   // nobody refreshed: the snapshot is too old → OFF
        Assert.Equal("settings-unreadable", g.Policy.Evaluate(EgressIntegration.Ota, g.Clock.Now).Reason);
    }

    [Fact]
    public async Task Refusals_are_audited_once_per_integration_per_minute_and_audit_failure_keeps_the_refusal()
    {
        using var g = new Gate();
        g.Source.Master = false;
        for (var n = 0; n < 3; n++) await g.SendAsync(EgressIntegration.Ota);
        await g.SendAsync(EgressIntegration.Email);
        Assert.Equal(2, g.Audit.Rows.Count);
        g.Clock.Now += TimeSpan.FromMinutes(1);
        await g.SendAsync(EgressIntegration.Ota);
        Assert.Equal(3, g.Audit.Rows.Count);
        Assert.All(g.Audit.Rows, r => Assert.False(r.Allowed));
        g.Audit.Fail = true;
        g.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(HccErrorCode.NET_OFFLINE, (await g.SendAsync(EgressIntegration.Ota))!.Code);
        Assert.Equal(0, g.Inner.Sends);
        Assert.Equal("master-off", g.Services.GetRequiredService<EgressActivity>().LastRefusals["ota"].Reason);
    }

    [Fact]
    public void The_gate_is_the_outermost_handler_of_the_egress_client()
    {
        using var g = new Gate();
        var options = g.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(EgressKeys.ClientName);
        var builder = new FakeBuilder(g.Services);
        foreach (var a in options.HttpMessageHandlerBuilderActions) a(builder);
        Assert.IsType<EgressGateHandler>(builder.AdditionalHandlers[0]);
    }

    private sealed class FakeBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = EgressKeys.ClientName;

        public override HttpMessageHandler PrimaryHandler { get; set; } = new CountingHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services { get; } = services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}

public sealed class WindowTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static DateTimeOffset At(int day, int h, int m) => new(2026, 11, day, h, m, 0, Ist);   // 1 Nov 2026 = Sunday

    private static EgressWindow W(string json)
    {
        Assert.True(EgressRules.TryParseWindow(json, out var w));
        return w!;
    }

    [Fact]
    public void From_inclusive_to_exclusive()
    {
        var w = W("{\"from\":\"09:00\",\"to\":\"18:00\",\"days\":[1,2,3,4,5]}");
        Assert.True(EgressRules.InWindow(w, At(2, 9, 0)));     // Monday 09:00
        Assert.True(EgressRules.InWindow(w, At(2, 17, 59)));
        Assert.False(EgressRules.InWindow(w, At(2, 18, 0)));
        Assert.False(EgressRules.InWindow(w, At(2, 8, 59)));
        Assert.False(EgressRules.InWindow(w, At(1, 10, 0)));   // Sunday not listed
    }

    [Fact]
    public void Across_midnight_counts_for_the_start_day()
    {
        var w = W("{\"from\":\"22:00\",\"to\":\"06:00\",\"days\":[5]}");   // Friday night
        Assert.True(EgressRules.InWindow(w, At(6, 22, 0)));     // Fri 22:00
        Assert.True(EgressRules.InWindow(w, At(7, 5, 59)));     // Sat 05:59
        Assert.False(EgressRules.InWindow(w, At(7, 6, 0)));
        Assert.False(EgressRules.InWindow(w, At(7, 22, 0)));    // Sat night not listed
        Assert.False(EgressRules.InWindow(w, At(6, 5, 0)));     // Fri early = Thursday's night
    }

    [Fact]
    public void Outside_the_window_is_off_and_no_window_means_switches_alone()
    {
        var s = new EgressSettings(true, EgressKeys.All.ToDictionary(i => i, _ => (bool?)true), "{\"from\":\"09:00\",\"to\":\"10:00\",\"days\":[0,1,2,3,4,5,6]}");
        Assert.Equal("outside-window", EgressRules.Evaluate(s, EgressIntegration.Ota, At(2, 10, 0)).Reason);
        Assert.Equal("on", EgressRules.Evaluate(s, EgressIntegration.Ota, At(2, 9, 30)).Reason);
        Assert.Equal("on", EgressRules.Evaluate(s with { WindowJson = "" }, EgressIntegration.Ota, At(2, 23, 0)).Reason);
        Assert.Equal("settings-unreadable", EgressRules.Evaluate(null, EgressIntegration.Ota, At(2, 9, 30)).Reason);
    }

    [Fact]
    public void Window_validation()
    {
        Assert.Null(EgressRules.WindowProblem(null));
        Assert.Null(EgressRules.WindowProblem("{\"from\":\"00:00\",\"to\":\"23:59\",\"days\":[0]}"));
        Assert.NotNull(EgressRules.WindowProblem("{\"from\":\"9\",\"to\":\"10:00\",\"days\":[1]}"));
        Assert.NotNull(EgressRules.WindowProblem("{\"from\":\"09:00\",\"to\":\"10:00\",\"days\":[7]}"));
        Assert.NotNull(EgressRules.WindowProblem("{\"from\":\"09:00\",\"to\":\"10:00\",\"days\":[]}"));
        Assert.NotNull(EgressRules.WindowProblem("not json"));
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void Internet_group_is_owner_only_and_every_switch_defaults_off()
    {
        var registry = new SettingsRegistry([EgressEndpoints.Provider]);
        var group = ConfigCatalog.Group(ConfigGroupIds.Internet)!;
        Assert.True(group.Money);
        foreach (var key in EgressKeys.All.Select(EgressKeys.Of).Prepend(EgressKeys.Master))
        {
            var s = registry.Get(key);
            Assert.Equal(ConfigGroupIds.Internet, s.Group.Id);
            Assert.Equal(ConfigValue.Of(false), s.Default);
        }

        Assert.Equal(ConfigGroupIds.Internet, registry.Get(EgressKeys.Window).Group.Id);
    }
}
