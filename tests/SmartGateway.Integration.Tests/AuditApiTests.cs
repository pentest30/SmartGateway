using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Integration.Tests;

public class AuditApiTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;

    public AuditApiTests(ApiTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAuditLogs_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/admin/api/audit");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateCluster_ShouldGenerateAuditLog()
    {
        var id = $"audit-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });

        var response = await _client.GetAsync("/admin/api/audit?entityType=Cluster");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        logs.Should().Contain(l => l.EntityId == id && l.Action == "CREATE");
    }

    [Fact]
    public async Task GetAuditLogs_ShouldFilterByAction()
    {
        var response = await _client.GetAsync("/admin/api/audit?action=DELETE");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
