namespace SmartGateway.Core.Entities;

public class GatewayCluster
{
    public string ClusterId { get; set; } = default!;
    public string LoadBalancing { get; set; } = "RoundRobin";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GatewayDestination> Destinations { get; set; } = new List<GatewayDestination>();
    public ICollection<GatewayRoute> Routes { get; set; } = new List<GatewayRoute>();
    public GatewayResiliencePolicy? ResiliencePolicy { get; set; }
}
