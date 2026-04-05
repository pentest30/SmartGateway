using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SmartGateway.Core.Entities;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.SqlServer;

[Collection("SqlServer")]
public class ApiWithSqlServerTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"ApiSql_{Guid.NewGuid():N}";
    private HttpClient _client = default!;
    private ApiTestFactorySql _factory = default!;

    public ApiWithSqlServerTests(SqlServerFixture fixture) => _fixture = fixture;

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
    public async Task CreateCluster_WithSqlServer_ShouldPersist()
    {
        var id = $"sql-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await _client.GetAsync("/admin/api/clusters");
        var clusters = await getResponse.Content.ReadFromJsonAsync<List<GatewayCluster>>();
        clusters.Should().Contain(c => c.ClusterId == id);
    }

    [Fact]
    public async Task CreateRoute_WithForeignKey_ShouldEnforceClusterExists()
    {
        var response = await _client.PostAsJsonAsync("/admin/api/routes", new
        {
            RouteId = $"fk-{Guid.NewGuid():N}",
            ClusterId = "nonexistent-cluster",
            PathPattern = "/api/fk-test"
        });

        // SQL Server enforces FK — should fail
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AuditLog_ShouldPersistWithRealTimestamp()
    {
        var id = $"ts-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/admin/api/clusters", new { ClusterId = id, LoadBalancing = "RoundRobin" });

        var response = await _client.GetAsync("/admin/api/audit?entityType=Cluster");
        var logs = await response.Content.ReadFromJsonAsync<List<GatewayAuditLog>>();

        var log = logs!.FirstOrDefault(l => l.EntityId == id);
        log.Should().NotBeNull();
        log!.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }
}
