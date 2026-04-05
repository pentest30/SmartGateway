using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Host.LoadBalancing;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class WeightedLoadBalancingTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"WLB_{Guid.NewGuid():N}";
    private TestUpstreamServer _upstreamA = default!;
    private TestUpstreamServer _upstreamB = default!;
    private HostTestFactory _hostFactory = default!;
    private HttpClient _client = default!;

    public WeightedLoadBalancingTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        _upstreamA = new TestUpstreamServer("wlb-A");
        _upstreamB = new TestUpstreamServer("wlb-B");
        await _upstreamA.StartAsync();
        await _upstreamB.StartAsync();

        ctx.Clusters.Add(new GatewayCluster { ClusterId = "wlb-cluster", LoadBalancing = "Weighted" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "wlb-cluster", DestinationId = "wlb-a",
            Address = _upstreamA.BaseUrl, Weight = 90
        });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "wlb-cluster", DestinationId = "wlb-b",
            Address = _upstreamB.BaseUrl, Weight = 10
        });
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "wlb-route", ClusterId = "wlb-cluster",
            PathPattern = "/api/wlb/{**catch-all}"
        });
        await ctx.SaveChangesAsync();

        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        _client = _hostFactory.CreateClient();

        // Refresh weight provider and reload YARP config
        var weightProvider = _hostFactory.Services.GetRequiredService<IDestinationWeightProvider>() as DatabaseDestinationWeightProvider;
        weightProvider?.Refresh();
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _hostFactory.DisposeAsync();
        await _upstreamA.DisposeAsync();
        await _upstreamB.DisposeAsync();
    }

    [Fact]
    public async Task Weighted_90_10_ShouldDistributeAccordingly()
    {
        const int total = 500;
        for (int i = 0; i < total; i++)
            await _client.GetAsync("/api/wlb/test");

        var aCount = _upstreamA.TotalRequests;
        var bCount = _upstreamB.TotalRequests;
        (aCount + bCount).Should().Be(total);

        var aRatio = (double)aCount / total;
        aRatio.Should().BeInRange(0.75, 0.99, "server A (weight 90) should get ~90% of traffic");
    }

    [Fact]
    public async Task Weighted_SingleDestination_ShouldRouteAll()
    {
        // Mark B as unhealthy so only A receives traffic
        await using var ctx = _fixture.CreateDbContext(_dbName);
        var destB = await ctx.Destinations.FirstAsync(d => d.DestinationId == "wlb-b");
        destB.IsHealthy = false;
        await ctx.SaveChangesAsync();

        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();

        for (int i = 0; i < 50; i++)
            await _client.GetAsync("/api/wlb/single");

        _upstreamA.GetRequestCount("/api/wlb/single").Should().Be(50);

        // Restore
        destB.IsHealthy = true;
        await ctx.SaveChangesAsync();
        provider.SignalReload();
    }
}
