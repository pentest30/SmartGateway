using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;

namespace SmartGateway.Host.Tests.ConfigProvider;

public class CachedConfigProviderTests
{
    private readonly DbContextOptions<SmartGatewayDbContext> _options;

    public CachedConfigProviderTests()
    {
        _options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private DatabaseProxyConfigProvider CreateProvider(IDbContextFactory<SmartGatewayDbContext>? factory = null)
    {
        factory ??= CreateFactory();
        var logger = Substitute.For<ILogger<DatabaseProxyConfigProvider>>();
        return new DatabaseProxyConfigProvider(factory, logger);
    }

    private IDbContextFactory<SmartGatewayDbContext> CreateFactory()
    {
        var factory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_ => new SmartGatewayDbContext(_options));
        return factory;
    }

    [Fact]
    public async Task SignalReload_ShouldCacheConfig()
    {
        // Seed data
        using (var ctx = new SmartGatewayDbContext(_options))
        {
            ctx.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
            ctx.Destinations.Add(new GatewayDestination { ClusterId = "c1", DestinationId = "d1", Address = "https://h1" });
            ctx.Routes.Add(new GatewayRoute { RouteId = "r1", ClusterId = "c1", PathPattern = "/api/test" });
            await ctx.SaveChangesAsync();
        }

        var provider = CreateProvider();
        provider.SignalReload();

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Clusters.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetConfig_ShouldServeCachedConfig_WhenDbFails()
    {
        // Seed data first
        using (var ctx = new SmartGatewayDbContext(_options))
        {
            ctx.Clusters.Add(new GatewayCluster { ClusterId = "cached" });
            ctx.Destinations.Add(new GatewayDestination { ClusterId = "cached", DestinationId = "d1", Address = "https://h1" });
            ctx.Routes.Add(new GatewayRoute { RouteId = "r1", ClusterId = "cached", PathPattern = "/api/cached" });
            await ctx.SaveChangesAsync();
        }

        var provider = CreateProvider();
        provider.SignalReload();

        // Verify config loaded
        var config1 = provider.GetConfig();
        config1.Routes.Should().HaveCount(1);

        // Now make DB throw on next reload
        var failingFactory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        failingFactory.CreateDbContext().Returns(_ => throw new Exception("DB unavailable"));

        var failingProvider = new DatabaseProxyConfigProvider(
            failingFactory,
            Substitute.For<ILogger<DatabaseProxyConfigProvider>>());

        // First load fails → empty config (no cache yet)
        failingProvider.SignalReload();
        var failConfig = failingProvider.GetConfig();
        failConfig.Routes.Should().BeEmpty();

        // But original provider still has its cached config
        var config2 = provider.GetConfig();
        config2.Routes.Should().HaveCount(1);
    }

    [Fact]
    public async Task SignalReload_WhenDbFails_ShouldKeepLastGoodConfig()
    {
        // Seed data
        using (var ctx = new SmartGatewayDbContext(_options))
        {
            ctx.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
            ctx.Destinations.Add(new GatewayDestination { ClusterId = "c1", DestinationId = "d1", Address = "https://h1" });
            ctx.Routes.Add(new GatewayRoute { RouteId = "r1", ClusterId = "c1", PathPattern = "/api/test" });
            await ctx.SaveChangesAsync();
        }

        // Create provider that will fail after first load
        int callCount = 0;
        var factory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_ =>
        {
            callCount++;
            if (callCount > 1)
                throw new Exception("DB down");
            return new SmartGatewayDbContext(_options);
        });

        var provider = CreateProvider(factory);

        // First reload succeeds
        provider.SignalReload();
        var config1 = provider.GetConfig();
        config1.Routes.Should().HaveCount(1);

        // Second reload fails — should keep last-good config
        provider.SignalReload();
        var config2 = provider.GetConfig();
        config2.Routes.Should().HaveCount(1); // Still has cached data
        config2.Routes.First().RouteId.Should().Be("r1");
    }
}
