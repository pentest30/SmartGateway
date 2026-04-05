using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;

namespace SmartGateway.Integration.Tests;

public class ServicesApiTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;
    private readonly ApiTestFactory _factory;

    public ServicesApiTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedCluster(string clusterId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGatewayDbContext>();
        if (await db.Clusters.FindAsync(clusterId) == null)
        {
            db.Clusters.Add(new GatewayCluster { ClusterId = clusterId });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Register_ShouldReturnOk()
    {
        var clusterId = $"svc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);

        var response = await _client.PostAsJsonAsync("/admin/api/services/register", new
        {
            ClusterId = clusterId,
            DestinationId = $"d-{Guid.NewGuid():N}",
            Address = "https://10.0.1.10:8080",
            TtlSeconds = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_ShouldUpdateExisting()
    {
        var clusterId = $"svc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);
        var destId = $"d-{Guid.NewGuid():N}";

        await _client.PostAsJsonAsync("/admin/api/services/register", new
        {
            ClusterId = clusterId, DestinationId = destId,
            Address = "https://10.0.1.10:8080", TtlSeconds = 30
        });

        var response = await _client.PostAsJsonAsync("/admin/api/services/register", new
        {
            ClusterId = clusterId, DestinationId = destId,
            Address = "https://10.0.1.11:8080", TtlSeconds = 60
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_ShouldReturnOk()
    {
        var clusterId = $"svc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);
        var destId = $"d-{Guid.NewGuid():N}";

        await _client.PostAsJsonAsync("/admin/api/services/register", new
        {
            ClusterId = clusterId, DestinationId = destId,
            Address = "https://10.0.1.10:8080", TtlSeconds = 30
        });

        var response = await _client.PutAsync($"/admin/api/services/{destId}/heartbeat", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deregister_ShouldReturnNoContent()
    {
        var clusterId = $"svc-{Guid.NewGuid():N}";
        await SeedCluster(clusterId);
        var destId = $"d-{Guid.NewGuid():N}";

        await _client.PostAsJsonAsync("/admin/api/services/register", new
        {
            ClusterId = clusterId, DestinationId = destId,
            Address = "https://10.0.1.10:8080", TtlSeconds = 30
        });

        var response = await _client.DeleteAsync($"/admin/api/services/{destId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetAll_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/admin/api/services");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
