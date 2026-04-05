namespace SmartGateway.Core.Entities;

public class GatewayRoute
{
    public string RouteId { get; set; } = default!;
    public string ClusterId { get; set; } = default!;
    public string? PathPattern { get; set; }
    public string? Hosts { get; set; }
    public string? Methods { get; set; }
    public string? MatchHeader { get; set; }
    public string? MatchHeaderValue { get; set; }
    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public bool RequiresAuth { get; set; } = false;
    public string? AuthPolicyName { get; set; }
    public string? RateLimitConfig { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public GatewayCluster? Cluster { get; set; }
}
