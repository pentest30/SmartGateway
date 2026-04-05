using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Integration.Tests;

public class ClustersApiTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public ClustersApiTests(ApiTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/admin/api/clusters");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var clusters = await response.Content.ReadFromJsonAsync<List<GatewayCluster>>();
        clusters.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ShouldReturnCreated()
    {
        var cluster = new { ClusterId = $"test-{Guid.NewGuid():N}", LoadBalancing = "RoundRobin" };
        var response = await _client.PostAsJsonAsync("/admin/api/clusters", cluster);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Duplicate_ShouldReturn409()
    {
        var id = $"dup-{Guid.NewGuid():N}";
        var cluster = new { ClusterId = id, LoadBalancing = "RoundRobin" };

        await _client.PostAsJsonAsync("/admin/api/clusters", cluster);
        var response = await _client.PostAsJsonAsync("/admin/api/clusters", cluster);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ShouldModifyCluster()
    {
        var id = $"upd-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });

        var response = await _client.PutAsJsonAsync($"/admin/api/clusters/{id}",
            new { ClusterId = id, LoadBalancing = "LeastRequests", IsActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<GatewayCluster>();
        updated!.LoadBalancing.Should().Be("LeastRequests");
    }

    [Fact]
    public async Task Delete_ShouldReturnNoContent()
    {
        var id = $"del-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });

        var response = await _client.DeleteAsync($"/admin/api/clusters/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NotFound_ShouldReturn404()
    {
        var response = await _client.DeleteAsync("/admin/api/clusters/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
