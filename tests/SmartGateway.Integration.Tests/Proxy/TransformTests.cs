using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class TransformTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"Transform_{Guid.NewGuid():N}";
    private TestUpstreamServer _upstream = default!;
    private HostTestFactory _hostFactory = default!;
    private HttpClient _client = default!;

    public TransformTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        _upstream = new TestUpstreamServer("transform-srv");
        await _upstream.StartAsync();

        ctx.Clusters.Add(new GatewayCluster { ClusterId = "tf-cluster" });
        ctx.Destinations.Add(new GatewayDestination { ClusterId = "tf-cluster", DestinationId = "tf-dest", Address = _upstream.BaseUrl });
        ctx.Routes.Add(new GatewayRoute { RouteId = "tf-route", ClusterId = "tf-cluster", PathPattern = "/api/transform/{**catch-all}" });

        // Add request header transform
        ctx.Transforms.Add(new GatewayTransform
        {
            RouteId = "tf-route",
            Type = "RequestHeader",
            Key = "X-Gateway-RequestId",
            Value = "smartgateway-123",
            Action = "Set"
        });

        // Add response header transform
        ctx.Transforms.Add(new GatewayTransform
        {
            RouteId = "tf-route",
            Type = "ResponseHeader",
            Key = "X-Powered-By",
            Value = "SmartGateway",
            Action = "Set"
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
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task RequestHeaderTransform_ShouldInjectHeader()
    {
        var response = await _client.GetAsync("/api/transform/test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The upstream echo returns all received headers
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("X-Gateway-RequestId");
        body.Should().Contain("smartgateway-123");
    }

    [Fact]
    public async Task ResponseHeaderTransform_ShouldAddHeader()
    {
        var response = await _client.GetAsync("/api/transform/test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("X-Powered-By", out var values).Should().BeTrue();
        values.Should().Contain("SmartGateway");
    }

    [Fact]
    public async Task ConfigProvider_ShouldLoadTransforms()
    {
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        var config = provider.GetConfig();

        var route = config.Routes.FirstOrDefault(r => r.RouteId == "tf-route");
        route.Should().NotBeNull();
        route!.Transforms.Should().NotBeNull();
        route.Transforms.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
