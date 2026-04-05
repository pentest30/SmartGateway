using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;

namespace SmartGateway.Integration.Tests;

public class RoutesApiTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;
    private readonly ApiTestFactory _factory;

    public RoutesApiTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedCluster(string clusterId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGatewayDbContext>();
        var existing = await db.Clusters.FindAsync(clusterId);
        if (existing == null)
        {
            db.Clusters.Add(new GatewayCluster { ClusterId = clusterId });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetAll_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/admin/api/routes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_ShouldReturnCreated()
    {
        var clusterId = $"rc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);

        var route = new { RouteId = $"r-{Guid.NewGuid():N}", ClusterId = clusterId, PathPattern = "/api/test/{**catch-all}" };
        var response = await _client.PostAsJsonAsync("/admin/api/routes", route);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Delete_ShouldReturnNoContent()
    {
        var clusterId = $"rc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);

        var routeId = $"r-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/routes", new { RouteId = routeId, ClusterId = clusterId, PathPattern = "/x" });

        var response = await _client.DeleteAsync($"/admin/api/routes/{routeId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
