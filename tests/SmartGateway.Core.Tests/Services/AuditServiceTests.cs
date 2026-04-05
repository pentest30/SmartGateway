using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;
using SmartGateway.Core.Services;

namespace SmartGateway.Core.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly SmartGatewayDbContext _context;
    private readonly IAuditService _auditService;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new SmartGatewayDbContext(options);
        _auditService = new AuditService(_context);
    }

    [Fact]
    public async Task LogCreate_ShouldStoreAuditEntry()
    {
        await _auditService.LogAsync("Cluster", "c1", "CREATE", "admin@test.com",
            oldValues: null,
            newValues: new { ClusterId = "c1", LoadBalancing = "RoundRobin" });

        var logs = await _context.AuditLogs.ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].EntityType.Should().Be("Cluster");
        logs[0].EntityId.Should().Be("c1");
        logs[0].Action.Should().Be("CREATE");
        logs[0].ChangedBy.Should().Be("admin@test.com");
        logs[0].OldValues.Should().BeNull();
        logs[0].NewValues.Should().Contain("RoundRobin");
    }

    [Fact]
    public async Task LogUpdate_ShouldStoreOldAndNewValues()
    {
        await _auditService.LogAsync("Route", "r1", "UPDATE", "ops@test.com",
            oldValues: new { PathPattern = "/old" },
            newValues: new { PathPattern = "/new" });

        var log = await _context.AuditLogs.FirstAsync();
        log.OldValues.Should().Contain("/old");
        log.NewValues.Should().Contain("/new");
    }

    [Fact]
    public async Task LogDelete_ShouldStoreOldValues()
    {
        await _auditService.LogAsync("Destination", "d1", "DELETE", "admin@test.com",
            oldValues: new { Address = "https://host:8080" },
            newValues: null);

        var log = await _context.AuditLogs.FirstAsync();
        log.Action.Should().Be("DELETE");
        log.OldValues.Should().Contain("host:8080");
        log.NewValues.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_ShouldSetTimestamp()
    {
        await _auditService.LogAsync("Cluster", "c1", "CREATE", "admin@test.com", null, null);

        var log = await _context.AuditLogs.FirstAsync();
        log.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByEntityType()
    {
        await _auditService.LogAsync("Cluster", "c1", "CREATE", "admin", null, null);
        await _auditService.LogAsync("Route", "r1", "CREATE", "admin", null, null);
        await _auditService.LogAsync("Cluster", "c2", "UPDATE", "admin", null, null);

        var logs = await _auditService.GetLogsAsync(entityType: "Cluster");
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.EntityType == "Cluster");
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByAction()
    {
        await _auditService.LogAsync("Cluster", "c1", "CREATE", "admin", null, null);
        await _auditService.LogAsync("Route", "r1", "UPDATE", "admin", null, null);

        var logs = await _auditService.GetLogsAsync(action: "UPDATE");
        logs.Should().HaveCount(1);
        logs[0].EntityId.Should().Be("r1");
    }

    [Fact]
    public async Task GetLogsAsync_ShouldReturnOrderedByDescendingTime()
    {
        await _auditService.LogAsync("Cluster", "c1", "CREATE", "admin", null, null);
        await Task.Delay(10);
        await _auditService.LogAsync("Route", "r1", "CREATE", "admin", null, null);

        var logs = await _auditService.GetLogsAsync();
        logs.First().EntityId.Should().Be("r1");
    }

    [Fact]
    public async Task GetLogsAsync_ShouldLimitResults()
    {
        for (int i = 0; i < 50; i++)
            await _auditService.LogAsync("Cluster", $"c{i}", "CREATE", "admin", null, null);

        var logs = await _auditService.GetLogsAsync(take: 10);
        logs.Should().HaveCount(10);
    }

    public void Dispose() => _context.Dispose();
}
