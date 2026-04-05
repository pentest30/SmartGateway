namespace SmartGateway.Core.Entities;

public class GatewayApiKey
{
    public int Id { get; set; }
    public string KeyHash { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Scopes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
