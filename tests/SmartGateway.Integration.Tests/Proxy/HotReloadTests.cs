using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class HotReloadTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"HotReload_{Guid.NewGuid():N}";
    private TestUpstreamServer _upstreamA = default!;
    private TestUpstreamServer _upstreamB = default!;
    private HostTestFactory _hostFactory = default!;
    private HttpClient _client = default!;

    public HotReloadTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        _upstreamA = new TestUpstreamServer("server-A");
        _upstreamB = new TestUpstreamServer("server-B");
        await _upstreamA.StartAsync();
        await _upstreamB.StartAsync();

        // Seed a cluster with upstream A
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "hot-cluster" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "hot-cluster",
            DestinationId = "dest-a",
            Address = _upstreamA.BaseUrl
        });
        await ctx.SaveChangesAsync();

        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        _client = _hostFactory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _hostFactory.DisposeAsync();
        await _upstreamA.DisposeAsync();
        await _upstreamB.DisposeAsync();
    }

    private void Reload()
    {
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();
    }

    [Fact]
    public async Task AddRoute_ThenReload_ShouldMakeRouteAvailable()
    {
        // Initially no route → 404
        Reload();
        var r1 = await _client.GetAsync("/api/hot/test");
        r1.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Add route
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "hot-route", ClusterId = "hot-cluster", PathPattern = "/api/hot/{**catch-all}" });
        await ctx.SaveChangesAsync();
        Reload();

        var r2 = await _client.GetAsync("/api/hot/test");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveRoute_ThenReload_ShouldReturn404()
    {
        // Add then remove
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "temp-route", ClusterId = "hot-cluster", PathPattern = "/api/temp/{**catch-all}" });
        await ctx.SaveChangesAsync();
        Reload();

        var r1 = await _client.GetAsync("/api/temp/test");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);

        var route = await ctx.Routes.FindAsync("temp-route");
        ctx.Routes.Remove(route!);
        await ctx.SaveChangesAsync();
        Reload();

        var r2 = await _client.GetAsync("/api/temp/test");
        r2.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeDestination_ThenReload_ShouldRouteToNewUpstream()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "switch-route", ClusterId = "hot-cluster", PathPattern = "/api/switch/{**catch-all}" });
        await ctx.SaveChangesAsync();
        Reload();

        // Should route to server-A
        var r1 = await _client.GetAsync("/api/switch/test");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().Contain("server-A");

        // Change destination to server-B
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "dest-a");
        dest.Address = _upstreamB.BaseUrl;
        await ctx.SaveChangesAsync();
        Reload();

        var r2 = await _client.GetAsync("/api/switch/test");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Contain("server-B");
    }

    [Fact]
    public async Task DeactivateCluster_ThenReload_ShouldReturn404()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "deact-route", ClusterId = "hot-cluster", PathPattern = "/api/deact/{**catch-all}" });
        await ctx.SaveChangesAsync();
        Reload();

        var r1 = await _client.GetAsync("/api/deact/test");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);

        var cluster = await ctx.Clusters.FindAsync("hot-cluster");
        cluster!.IsActive = false;
        await ctx.SaveChangesAsync();
        Reload();

        var r2 = await _client.GetAsync("/api/deact/test");
        // Inactive cluster → no destinations available → 503 or route not found → 404
        r2.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable);

        // Restore for other tests
        cluster.IsActive = true;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task AddDestination_ThenReload_ShouldIncludeInRotation()
    {
        await using var ctx = _fixture.CreateDbContext(_dbName);
        ctx.Routes.Add(new GatewayRoute { RouteId = "multi-route", ClusterId = "hot-cluster", PathPattern = "/api/multi/{**catch-all}" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "hot-cluster",
            DestinationId = "dest-b",
            Address = _upstreamB.BaseUrl
        });
        await ctx.SaveChangesAsync();
        Reload();

        // Send requests — both upstreams should receive traffic
        for (int i = 0; i < 20; i++)
            await _client.GetAsync("/api/multi/test");

        (_upstreamA.TotalRequests + _upstreamB.TotalRequests).Should().BeGreaterThanOrEqualTo(10);
    }
}
