using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Data;

public class SmartGatewayDbContextTests : IDisposable
{
    private readonly SmartGatewayDbContext _context;

    public SmartGatewayDbContextTests()
    {
        var options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new SmartGatewayDbContext(options);
    }

    [Fact]
    public async Task CanAddAndRetrieveCluster()
    {
        var cluster = new GatewayCluster { ClusterId = "test-cluster" };
        _context.Clusters.Add(cluster);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Clusters.FindAsync("test-cluster");
        retrieved.Should().NotBeNull();
        retrieved!.ClusterId.Should().Be("test-cluster");
    }

    [Fact]
    public async Task CanAddDestinationToCluster()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        _context.Clusters.Add(cluster);

        var dest = new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://localhost:5000"
        };
        _context.Destinations.Add(dest);
        await _context.SaveChangesAsync();

        var loaded = await _context.Clusters
            .Include(c => c.Destinations)
            .FirstAsync(c => c.ClusterId == "c1");

        loaded.Destinations.Should().HaveCount(1);
        loaded.Destinations.First().Address.Should().Be("https://localhost:5000");
    }

    [Fact]
    public async Task CanAddRouteToCluster()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        _context.Clusters.Add(cluster);

        var route = new GatewayRoute
        {
            RouteId = "r1",
            ClusterId = "c1",
            PathPattern = "/api/test/{**catch-all}"
        };
        _context.Routes.Add(route);
        await _context.SaveChangesAsync();

        var loaded = await _context.Clusters
            .Include(c => c.Routes)
            .FirstAsync(c => c.ClusterId == "c1");

        loaded.Routes.Should().HaveCount(1);
        loaded.Routes.First().PathPattern.Should().Be("/api/test/{**catch-all}");
    }

    [Fact]
    public async Task CanAddResiliencePolicy()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        _context.Clusters.Add(cluster);

        var policy = new GatewayResiliencePolicy { ClusterId = "c1" };
        _context.ResiliencePolicies.Add(policy);
        await _context.SaveChangesAsync();

        var loaded = await _context.ResiliencePolicies.FindAsync("c1");
        loaded.Should().NotBeNull();
        loaded!.RetryMaxAttempts.Should().Be(3);
    }

    [Fact]
    public async Task CanAddAuditLog()
    {
        var log = new GatewayAuditLog
        {
            EntityType = "Cluster",
            EntityId = "c1",
            Action = "CREATE",
            ChangedBy = "test@test.com",
            NewValues = """{"ClusterId":"c1"}"""
        };
        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();

        var logs = await _context.AuditLogs.ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be("CREATE");
    }

    public void Dispose() => _context.Dispose();
}
