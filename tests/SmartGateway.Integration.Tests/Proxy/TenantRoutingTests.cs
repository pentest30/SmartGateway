using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class TenantRoutingTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"Tenant_{Guid.NewGuid():N}";
    private TestUpstreamServer _tenantA = default!;
    private TestUpstreamServer _tenantB = default!;
    private TestUpstreamServer _fallback = default!;
    private HostTestFactory _hostFactory = default!;
    private HttpClient _client = default!;

    public TenantRoutingTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        _tenantA = new TestUpstreamServer("tenant-A");
        _tenantB = new TestUpstreamServer("tenant-B");
        _fallback = new TestUpstreamServer("fallback");
        await _tenantA.StartAsync();
        await _tenantB.StartAsync();
        await _fallback.StartAsync();

        // Cluster per tenant
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "tenant-a-backend" });
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "tenant-b-backend" });
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "shared-backend" });

        ctx.Destinations.Add(new GatewayDestination { ClusterId = "tenant-a-backend", DestinationId = "ta-1", Address = _tenantA.BaseUrl });
        ctx.Destinations.Add(new GatewayDestination { ClusterId = "tenant-b-backend", DestinationId = "tb-1", Address = _tenantB.BaseUrl });
        ctx.Destinations.Add(new GatewayDestination { ClusterId = "shared-backend", DestinationId = "fb-1", Address = _fallback.BaseUrl });

        // Header-matched routes (higher priority)
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "tenant-a-route",
            ClusterId = "tenant-a-backend",
            PathPattern = "/api/tenant/{**catch-all}",
            MatchHeader = "X-Tenant-Id",
            MatchHeaderValue = "tenant-a",
            Priority = -1
        });
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "tenant-b-route",
            ClusterId = "tenant-b-backend",
            PathPattern = "/api/tenant/{**catch-all}",
            MatchHeader = "X-Tenant-Id",
            MatchHeaderValue = "tenant-b",
            Priority = -1
        });
        // Fallback route (lower priority)
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "tenant-fallback",
            ClusterId = "shared-backend",
            PathPattern = "/api/tenant/{**catch-all}",
            Priority = 10
        });

        await ctx.SaveChangesAsync();

        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        _client = _hostFactory.CreateClient();

        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _hostFactory.DisposeAsync();
        await _tenantA.DisposeAsync();
        await _tenantB.DisposeAsync();
        await _fallback.DisposeAsync();
    }

    [Fact]
    public async Task Request_WithTenantAHeader_ShouldRouteToTenantABackend()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/data");
        request.Headers.Add("X-Tenant-Id", "tenant-a");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenant-A");
    }

    [Fact]
    public async Task Request_WithTenantBHeader_ShouldRouteToTenantBBackend()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/data");
        request.Headers.Add("X-Tenant-Id", "tenant-b");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenant-B");
    }

    [Fact]
    public async Task Request_WithNoTenantHeader_ShouldRouteToFallback()
    {
        var response = await _client.GetAsync("/api/tenant/data");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("fallback");
    }

    [Fact]
    public async Task Request_WithUnknownTenant_ShouldRouteToFallback()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/data");
        request.Headers.Add("X-Tenant-Id", "tenant-unknown");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("fallback");
    }
}
