using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class EndToEndProxyTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"E2E_{Guid.NewGuid():N}";
    private TestUpstreamServer _upstream = default!;
    private HostTestFactory _hostFactory = default!;
    private HttpClient _client = default!;

    public EndToEndProxyTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // Create DB schema
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        // Start upstream
        _upstream = new TestUpstreamServer("upstream-1");
        await _upstream.StartAsync();

        // Seed data
        ctx.Clusters.Add(new GatewayCluster { ClusterId = "echo-cluster" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "echo-cluster",
            DestinationId = "echo-dest",
            Address = _upstream.BaseUrl
        });
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "echo-route",
            ClusterId = "echo-cluster",
            PathPattern = "/api/echo/{**catch-all}"
        });
        await ctx.SaveChangesAsync();

        // Start host
        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        _client = _hostFactory.CreateClient();

        // Trigger YARP reload
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _hostFactory.DisposeAsync();
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task Request_ShouldReachUpstream_ThroughProxy()
    {
        var response = await _client.GetAsync("/api/echo/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("upstream-1");
    }

    [Fact]
    public async Task Request_WithPathParameters_ShouldForward()
    {
        var response = await _client.GetAsync("/api/echo/foo/bar/baz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("foo/bar/baz");
    }

    [Fact]
    public async Task Request_ToUnknownRoute_ShouldReturn404()
    {
        var response = await _client.GetAsync("/api/nonexistent/path");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Request_ShouldForwardHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/echo/headers");
        request.Headers.Add("X-Test-Header", "hello-world");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("hello-world");
    }

    [Fact]
    public async Task Request_WhenUpstreamDown_ShouldReturnError()
    {
        await _upstream.DisposeAsync();

        var response = await _client.GetAsync("/api/echo/test");

        // YARP returns 502 when upstream is unreachable
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Request_WhenNoHealthyDestinations_ShouldReturn503()
    {
        // Mark destination unhealthy
        await using var ctx = _fixture.CreateDbContext(_dbName);
        var dest = await ctx.Destinations.FirstAsync(d => d.DestinationId == "echo-dest");
        dest.IsHealthy = false;
        await ctx.SaveChangesAsync();

        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();

        var response = await _client.GetAsync("/api/echo/test");

        // No destinations available — YARP returns 503
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
