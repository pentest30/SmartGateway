using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Models;

public class GatewayAuditLogTests
{
    [Fact]
    public void NewAuditLog_ShouldHaveTimestamp()
    {
        var log = new GatewayAuditLog
        {
            EntityType = "Route",
            EntityId = "r1",
            Action = "CREATE",
            ChangedBy = "admin@test.com"
        };

        log.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AuditLog_ShouldStoreOldAndNewValues()
    {
        var log = new GatewayAuditLog
        {
            EntityType = "Route",
            EntityId = "r1",
            Action = "UPDATE",
            ChangedBy = "admin@test.com",
            OldValues = """{"PathPattern":"/old"}""",
            NewValues = """{"PathPattern":"/new"}"""
        };

        log.OldValues.Should().Contain("/old");
        log.NewValues.Should().Contain("/new");
    }
}
