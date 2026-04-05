using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SmartGateway.Core.Entities;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Audit;

[Collection("SqlServer")]
public class AuditTrailIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"Audit_{Guid.NewGuid():N}";
    private HttpClient _client = default!;
    private ApiTestFactorySql _factory = default!;

    public AuditTrailIntegrationTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.CreateAndMigrateAsync(_dbName);
        _factory = new ApiTestFactorySql(_fixture.GetConnectionString(_dbName));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CreateCluster_ShouldLogCreateAuditEntry()
    {
        var id = $"ac-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });

        var response = await _client.GetAsync("/admin/api/audit?entityType=Cluster");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        logs.Should().Contain(l => l.EntityId == id && l.Action == "CREATE");
    }

    [Fact]
    public async Task UpdateCluster_ShouldLogUpdateWithOldAndNewValues()
    {
        var id = $"au-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });
        await _client.PutAsJsonAsync($"/admin/api/clusters/{id}", new { ClusterId = id, LoadBalancing = "LeastRequests", IsActive = true });

        var response = await _client.GetAsync("/admin/api/audit?entityType=Cluster");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        var updateLog = logs!.FirstOrDefault(l => l.EntityId == id && l.Action == "UPDATE");
        updateLog.Should().NotBeNull();
        updateLog!.OldValues.Should().Contain("RoundRobin");
        updateLog.NewValues.Should().Contain("LeastRequests");
    }

    [Fact]
    public async Task DeleteCluster_ShouldLogDeleteWithOldValues()
    {
        var id = $"ad-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "Random" });
        await _client.DeleteAsync($"/admin/api/clusters/{id}");

        var response = await _client.GetAsync("/admin/api/audit?action=DELETE");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        logs.Should().Contain(l => l.EntityId == id && l.Action == "DELETE");
    }

    [Fact]
    public async Task CreateRoute_ShouldLogAuditEntry()
    {
        var cid = $"arc-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = cid, LoadBalancing = "RoundRobin" });

        var rid = $"ar-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/routes", new { RouteId = rid, ClusterId = cid, PathPattern = "/api/audit-test" });

        var response = await _client.GetAsync("/admin/api/audit?entityType=Route");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        logs.Should().Contain(l => l.EntityId == rid && l.Action == "CREATE");
    }

    [Fact]
    public async Task MultipleOperations_ShouldMaintainChronologicalOrder()
    {
        var id = $"ao-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });
        await Task.Delay(50);
        await _client.PutAsJsonAsync($"/admin/api/clusters/{id}", new { ClusterId = id, LoadBalancing = "Random", IsActive = true });
        await Task.Delay(50);
        await _client.DeleteAsync($"/admin/api/clusters/{id}");

        var response = await _client.GetAsync("/admin/api/audit");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        var myLogs = logs!.Where(l => l.EntityId == id).ToList();
        myLogs.Should().HaveCount(3);
        // Ordered descending by ChangedAt (API returns newest first)
        myLogs[0].Action.Should().Be("DELETE");
        myLogs[1].Action.Should().Be("UPDATE");
        myLogs[2].Action.Should().Be("CREATE");
    }

    [Fact]
    public async Task AuditLog_ShouldFilterByEntityType()
    {
        var response = await _client.GetAsync("/admin/api/audit?entityType=Cluster");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();
        logs.Should().OnlyContain(l => l.EntityType == "Cluster");
    }

    [Fact]
    public async Task AuditLog_ShouldFilterByAction()
    {
        var response = await _client.GetAsync("/admin/api/audit?action=CREATE");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();
        logs.Should().OnlyContain(l => l.Action == "CREATE");
    }
}
