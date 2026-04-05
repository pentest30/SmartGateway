using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Host.Health;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Health;

[Collection("SqlServer")]
public class HealthProbeIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"Health_{Guid.NewGuid():N}";
    private TestUpstreamServer _upstream = default!;

    public HealthProbeIntegrationTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);
        _upstream = new TestUpstreamServer("health-node");
        await _upstream.StartAsync();

        ctx.Clusters.Add(new GatewayCluster { ClusterId = "hc-cluster" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "hc-cluster",
            DestinationId = "hc-dest",
            Address = _upstream.BaseUrl,
            IsHealthy = true
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _upstream.DisposeAsync();

    private HealthProbeService CreateProbeService()
    {
        var connStr = _fixture.GetConnectionString(_dbName);
        var optionsBuilder = new DbContextOptionsBuilder<SmartGatewayDbContext>().UseSqlServer(connStr);
        var factory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_ => new SmartGatewayDbContext(optionsBuilder.Options));

        var httpFactory = new DefaultHttpClientFactory();
        var logger = Substitute.For<ILogger<HealthProbeService>>();
        return new HealthProbeService(factory, httpFactory, logger);
    }

    [Fact]
    public async Task ProbeAll_HealthyUpstream_ShouldKeepHealthyInDb()
    {
        _upstream.SetHealthy(true);
        var service = CreateProbeService();
        await service.ProbeAllAsync(CancellationToken.None);

        await using var ctx = _fixture.CreateDbContext(_dbName);
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "hc-dest");
        dest.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAll_UnhealthyUpstream_ShouldMarkUnhealthyInDb()
    {
        _upstream.SetHealthy(false);
        var service = CreateProbeService();
        await service.ProbeAllAsync(CancellationToken.None);

        await using var ctx = _fixture.CreateDbContext(_dbName);
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "hc-dest");
        dest.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAll_RecoveredUpstream_ShouldReAdmit()
    {
        // First mark unhealthy
        _upstream.SetHealthy(false);
        var service = CreateProbeService();
        await service.ProbeAllAsync(CancellationToken.None);

        // Now recover
        _upstream.SetHealthy(true);
        await service.ProbeAllAsync(CancellationToken.None);

        await using var ctx = _fixture.CreateDbContext(_dbName);
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "hc-dest");
        dest.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAll_ThenReload_UnhealthyShouldBeExcludedFromProxy()
    {
        _upstream.SetHealthy(false);
        var service = CreateProbeService();
        await service.ProbeAllAsync(CancellationToken.None);

        // Start host and verify the unhealthy destination is excluded from config
        using var hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        var provider = hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();

        // Seed a route
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "hc-route", ClusterId = "hc-cluster", PathPattern = "/api/hc/{**catch-all}" });
        await ctx.SaveChangesAsync();
        provider.SignalReload();

        var config = provider.GetConfig();
        var cluster = config.Clusters.FirstOrDefault(c => c.ClusterId == "hc-cluster");
        cluster.Should().NotBeNull();
        cluster!.Destinations.Should().BeEmpty(); // unhealthy dest excluded
    }

    [Fact]
    public async Task ProbeAll_WithUnreachableHost_ShouldMarkUnhealthy()
    {
        // Stop the upstream to simulate unreachable
        await _upstream.DisposeAsync();

        var service = CreateProbeService();
        await service.ProbeAllAsync(CancellationToken.None);

        await using var ctx = _fixture.CreateDbContext(_dbName);
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "hc-dest");
        dest.IsHealthy.Should().BeFalse();
    }
}

// Simple IHttpClientFactory implementation for tests
internal class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(3) };
}
