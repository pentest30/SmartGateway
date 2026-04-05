namespace SmartGateway.Core.Entities;

public class GatewayAuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string ChangedBy { get; set; } = default!;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
