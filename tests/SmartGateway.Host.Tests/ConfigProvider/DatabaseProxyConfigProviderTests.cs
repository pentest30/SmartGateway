using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;

namespace SmartGateway.Host.Tests.ConfigProvider;

public class DatabaseProxyConfigProviderTests : IDisposable
{
    private readonly SmartGatewayDbContext _context;
    private readonly DatabaseProxyConfigProvider _provider;

    public DatabaseProxyConfigProviderTests()
    {
        var options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new SmartGatewayDbContext(options);

        var factory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_context);
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_context);

        var logger = Substitute.For<ILogger<DatabaseProxyConfigProvider>>();
        _provider = new DatabaseProxyConfigProvider(factory, logger);
    }

    [Fact]
    public void GetConfig_ShouldReturnEmptyConfig_WhenNoData()
    {
        var config = _provider.GetConfig();

        config.Should().NotBeNull();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConfig_ShouldReturnRoutes_WhenRoutesExistInDb()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        _context.Clusters.Add(cluster);
        _context.Routes.Add(new GatewayRoute
        {
            RouteId = "r1",
            ClusterId = "c1",
            PathPattern = "/api/test/{**catch-all}",
            IsActive = true
        });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://localhost:5000"
        });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        config.Routes.Should().HaveCount(1);
        config.Routes.First().RouteId.Should().Be("r1");
        config.Routes.First().Match.Path.Should().Be("/api/test/{**catch-all}");
    }

    [Fact]
    public async Task GetConfig_ShouldReturnClusters_WithDestinations()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1", LoadBalancing = "RoundRobin" });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://host1:8080",
            IsHealthy = true
        });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d2",
            Address = "https://host2:8080",
            IsHealthy = true
        });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        config.Clusters.Should().HaveCount(1);
        var cluster = config.Clusters.First();
        cluster.ClusterId.Should().Be("c1");
        cluster.Destinations.Should().HaveCount(2);
        cluster.Destinations.Should().ContainKey("d1");
        cluster.Destinations.Should().ContainKey("d2");
    }

    [Fact]
    public async Task GetConfig_ShouldExcludeInactiveRoutes()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Routes.Add(new GatewayRoute { RouteId = "active", ClusterId = "c1", IsActive = true, PathPattern = "/a" });
        _context.Routes.Add(new GatewayRoute { RouteId = "inactive", ClusterId = "c1", IsActive = false, PathPattern = "/b" });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        config.Routes.Should().HaveCount(1);
        config.Routes.First().RouteId.Should().Be("active");
    }

    [Fact]
    public async Task GetConfig_ShouldExcludeInactiveClusters()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "active", IsActive = true });
        _context.Clusters.Add(new GatewayCluster { ClusterId = "inactive", IsActive = false });
        _context.Destinations.Add(new GatewayDestination { ClusterId = "active", DestinationId = "d1", Address = "https://h1" });
        _context.Destinations.Add(new GatewayDestination { ClusterId = "inactive", DestinationId = "d2", Address = "https://h2" });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        config.Clusters.Should().HaveCount(1);
        config.Clusters.First().ClusterId.Should().Be("active");
    }

    [Fact]
    public async Task GetConfig_ShouldExcludeUnhealthyDestinations()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Destinations.Add(new GatewayDestination { ClusterId = "c1", DestinationId = "healthy", Address = "https://h1", IsHealthy = true });
        _context.Destinations.Add(new GatewayDestination { ClusterId = "c1", DestinationId = "unhealthy", Address = "https://h2", IsHealthy = false });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        var cluster = config.Clusters.First();
        cluster.Destinations.Should().HaveCount(1);
        cluster.Destinations.Should().ContainKey("healthy");
    }

    [Fact]
    public void SignalReload_ShouldTriggerChangeToken()
    {
        var config1 = _provider.GetConfig();
        var token = config1.ChangeToken;

        bool changeTriggered = false;
        token.RegisterChangeCallback(_ => changeTriggered = true, null);

        _provider.SignalReload();

        changeTriggered.Should().BeTrue();
    }

    [Fact]
    public void SignalReload_ShouldReturnNewConfig()
    {
        var config1 = _provider.GetConfig();
        _provider.SignalReload();
        var config2 = _provider.GetConfig();

        config2.Should().NotBeSameAs(config1);
    }

    [Fact]
    public async Task GetConfig_ShouldMapLoadBalancingPolicy()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1", LoadBalancing = "LeastRequests" });
        _context.Destinations.Add(new GatewayDestination { ClusterId = "c1", DestinationId = "d1", Address = "https://h1" });
        await _context.SaveChangesAsync();

        _provider.SignalReload();
        var config = _provider.GetConfig();

        config.Clusters.First().LoadBalancingPolicy.Should().Be("LeastRequests");
    }

    public void Dispose() => _context.Dispose();
}
