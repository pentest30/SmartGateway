using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Registry;

[Collection("SqlServer")]
public class ServiceRegistryFlowTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"Registry_{Guid.NewGuid():N}";
    private HostTestFactory _hostFactory = default!;

    public ServiceRegistryFlowTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "reg-cluster" });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_hostFactory != null)
            await _hostFactory.DisposeAsync();
    }

    [Fact]
    public async Task Register_ShouldAddDestination_AndAppearInProxyConfig()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "reg-cluster",
            DestinationId = "reg-dest-1",
            Address = "https://10.0.1.10:8080",
            TtlSeconds = 30,
            LastHeartbeat = DateTime.UtcNow
        });
        ctx.Routes.Add(new GatewayRoute { RouteId = "reg-route", ClusterId = "reg-cluster", PathPattern = "/api/reg/{**catch-all}" });
        await ctx.SaveChangesAsync();

        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();

        var config = provider.GetConfig();
        var cluster = config.Clusters.FirstOrDefault(c => c.ClusterId == "reg-cluster");
        cluster.Should().NotBeNull();
        cluster!.Destinations.Should().ContainKey("reg-dest-1");
    }

    [Fact]
    public async Task Register_ShouldSetLastHeartbeatAndTtl()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        var now = DateTime.UtcNow;
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "reg-cluster",
            DestinationId = "ttl-dest",
            Address = "https://10.0.1.11:8080",
            TtlSeconds = 30,
            LastHeartbeat = now
        });
        await ctx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateDbContext(_dbName);
        var dest = await readCtx.Destinations.FirstAsync(d => d.DestinationId == "ttl-dest");
        dest.TtlSeconds.Should().Be(30);
        dest.LastHeartbeat.Should().BeCloseTo(now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Heartbeat_ShouldRefreshLastHeartbeat()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        var oldTime = DateTime.UtcNow.AddMinutes(-5);
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "reg-cluster",
            DestinationId = "hb-dest",
            Address = "https://10.0.1.12:8080",
            TtlSeconds = 30,
            LastHeartbeat = oldTime
        });
        await ctx.SaveChangesAsync();

        // Simulate heartbeat
        await using var updateCtx = _fixture.CreateDbContext(_dbName);
        var dest = await updateCtx.Destinations.FirstAsync(d => d.DestinationId == "hb-dest");
        dest.LastHeartbeat = DateTime.UtcNow;
        dest.IsHealthy = true;
        await updateCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateDbContext(_dbName);
        var updated = await readCtx.Destinations.FirstAsync(d => d.DestinationId == "hb-dest");
        updated.LastHeartbeat.Should().BeAfter(oldTime);
    }

    [Fact]
    public async Task Deregister_ShouldRemoveDestination()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "reg-cluster",
            DestinationId = "dereg-dest",
            Address = "https://10.0.1.13:8080"
        });
        await ctx.SaveChangesAsync();

        // Remove
        await using var delCtx = _fixture.CreateDbContext(_dbName);
        var dest = await delCtx.Destinations.FirstAsync(d => d.DestinationId == "dereg-dest");
        delCtx.Destinations.Remove(dest);
        await delCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateDbContext(_dbName);
        var exists = await readCtx.Destinations.AnyAsync(d => d.DestinationId == "dereg-dest");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task TtlExpiry_ShouldNotAffect_DestinationsWithZeroTtl()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "reg-cluster",
            DestinationId = "no-ttl-dest",
            Address = "https://10.0.1.14:8080",
            TtlSeconds = 0,
            LastHeartbeat = DateTime.UtcNow.AddHours(-1),
            IsHealthy = true
        });
        await ctx.SaveChangesAsync();

        // TTL=0 means no expiry, should remain healthy
        await using var readCtx = _fixture.CreateDbContext(_dbName);
        var dest = await readCtx.Destinations.FirstAsync(d => d.DestinationId == "no-ttl-dest");
        dest.IsHealthy.Should().BeTrue();
    }
}
