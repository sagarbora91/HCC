using System.Security.Cryptography;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Api.Hosting;
using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Messaging;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Hcc.Messaging.Tests.Kernel;

/// <summary>
/// A09-08: the simulated messaging gateway and OCR engine are for Development and the tests only; in the Production environment the
/// "not configured" adapters are registered and refuse clearly. No database is needed here: only the registrations and the adapters.
/// </summary>
public class NotConfiguredAdapterTests
{
    private sealed class TestEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Hcc.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Provider(string? environment, bool withGuestIdKey = false)
    {
        var services = new ServiceCollection();
        if (environment is not null) services.AddSingleton<IHostEnvironment>(new TestEnv(environment));
        if (withGuestIdKey) services.AddSingleton<GuestIdProtector>(_ => throw new InvalidOperationException("never resolved here"));
        services.AddHccMessaging();
        services.AddDocumentsModule();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    [InlineData("Testing", true)]
    public void Simulators_are_allowed_everywhere_except_Production(string environment, bool allowed)
    {
        using var sp = Provider(environment);
        Assert.Equal(allowed, SimulatedAdapters.Allowed(sp));
    }

    [Fact]
    public void A_service_collection_without_a_host_gets_the_simulators_because_it_cannot_be_Production()
    {
        using var sp = Provider(null);
        Assert.True(SimulatedAdapters.Allowed(sp));
        Assert.IsType<FakeMessageGateway>(sp.GetRequiredService<IMessageGateway>());
    }

    [Fact]
    public void Development_and_the_tests_keep_the_simulated_gateway_and_ocr_engine()
    {
        foreach (var env in new[] { "Development", "Testing" })
        {
            using var sp = Provider(env, withGuestIdKey: true);
            var gateway = sp.GetRequiredService<IMessageGateway>();
            Assert.IsType<FakeMessageGateway>(gateway);
            Assert.True(gateway.IsConfigured);
            Assert.True(gateway.IsSimulated);
            var ocr = sp.GetRequiredService<IIdOcrEngine>();
            Assert.IsType<SimulatedIdOcrEngine>(ocr);
            Assert.True(ocr.IsConfigured);
        }
    }

    [Fact]
    public void A_Production_environment_resolves_the_not_configured_gateway_and_ocr_engine()
    {
        using var sp = Provider("Production", withGuestIdKey: true);
        var gateway = sp.GetRequiredService<IMessageGateway>();
        Assert.IsType<NotConfiguredMessageGateway>(gateway);
        Assert.False(gateway.IsConfigured);
        Assert.False(gateway.IsSimulated);
        var ocr = sp.GetRequiredService<IIdOcrEngine>();
        Assert.IsType<NotConfiguredIdOcrEngine>(ocr);
        Assert.False(ocr.IsConfigured);
    }

    [Fact]
    public void The_whole_server_composition_in_a_Production_host_registers_the_not_configured_adapters()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hcc:GuestIdKeys:Current"] = "dev1",
            ["Hcc:GuestIdKeys:Keys:dev1"] = "plain:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Hcc:GuestIdKeys:AllowPlainKeys"] = "true",
        });
        builder.Services.AddHcc(builder.Configuration, new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(".", "HccTest_never_connected"), BackupDirectory = "NUL" }));
        using var app = builder.Build();
        Assert.IsType<NotConfiguredMessageGateway>(app.Services.GetRequiredService<IMessageGateway>());
        Assert.IsType<NotConfiguredIdOcrEngine>(app.Services.GetRequiredService<IIdOcrEngine>());
    }

    [Fact]
    public void The_not_configured_gateway_refuses_with_a_stable_reason_and_never_returns_an_outcome()
    {
        var e = Assert.Throws<HccException>(() => new NotConfiguredMessageGateway().Submit(new OutboundMessage(1, MsgChannel.WhatsApp, true)));
        Assert.Equal(HccErrorCode.UNAVAILABLE, e.Code);
        Assert.Equal("Messaging is not configured on this server: no message was sent.", e.Message);
        Assert.Equal("NOT_CONFIGURED", e.Details!["reason"].GetString());
        Assert.Equal("messaging", e.Details["feature"].GetString());
    }

    [Fact]
    public async Task The_not_configured_ocr_engine_refuses_with_a_stable_reason_instead_of_inventing_an_id_number()
    {
        var e = await Assert.ThrowsAsync<HccException>(() => new NotConfiguredIdOcrEngine().ExtractAsync(new byte[] { 1, 2, 3 }, "image/png", "aadhaar", TestContext.Current.CancellationToken));
        Assert.Equal(HccErrorCode.UNAVAILABLE, e.Code);
        Assert.Equal("ID scanning (OCR) is not configured on this server: enter the details by hand.", e.Message);
        Assert.Equal("NOT_CONFIGURED", e.Details!["reason"].GetString());
        Assert.Equal("ocr", e.Details["feature"].GetString());
    }
}
