using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.SqlServer;

[Collection("SqlServer")]
public class DatabaseSchemaTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private SmartGatewayDbContext _context = default!;
    private readonly string _dbName = $"SchemaTest_{Guid.NewGuid():N}";

    public DatabaseSchemaTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.CreateAndMigrateAsync(_dbName);
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureCreated_ShouldCreateAllTables()
    {
        var tables = await _context.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        // EF Core uses DbSet property names as table names
        tables.Should().HaveCountGreaterThanOrEqualTo(6);
        // Verify we can query all entity sets
        (await _context.Clusters.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await _context.Destinations.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await _context.Routes.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await _context.ResiliencePolicies.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await _context.AuditLogs.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await _context.ApiKeys.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ForeignKey_Destination_ToCluster_ShouldEnforce()
    {
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "nonexistent",
            DestinationId = "d1",
            Address = "https://host:8080"
        });

        var act = () => _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ForeignKey_Route_ToCluster_ShouldEnforce()
    {
        _context.Routes.Add(new GatewayRoute
        {
            RouteId = "r1",
            ClusterId = "nonexistent",
            PathPattern = "/api/test"
        });

        var act = () => _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DefaultValues_ShouldApply_OnSqlServer()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "defaults-test" });
        await _context.SaveChangesAsync();

        var cluster = await _context.Clusters.AsNoTracking().FirstAsync(c => c.ClusterId == "defaults-test");
        cluster.LoadBalancing.Should().Be("RoundRobin");
        cluster.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentWrites_ShouldNotCorrupt()
    {
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var ctx = _fixture.CreateDbContext(_dbName);
            ctx.Clusters.Add(new GatewayCluster { ClusterId = $"concurrent-{i}" });
            await ctx.SaveChangesAsync();
        });

        await Task.WhenAll(tasks);

        var count = await _context.Clusters.CountAsync(c => c.ClusterId.StartsWith("concurrent-"));
        count.Should().Be(10);
    }
}
