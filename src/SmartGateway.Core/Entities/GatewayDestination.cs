namespace SmartGateway.Core.Entities;

public class GatewayDestination
{
    public int Id { get; set; }
    public string ClusterId { get; set; } = default!;
    public string DestinationId { get; set; } = default!;
    public string Address { get; set; } = default!;
    public bool IsHealthy { get; set; } = true;
    public int Weight { get; set; } = 100;
    public DateTime? LastHeartbeat { get; set; }
    public int TtlSeconds { get; set; } = 0;

    public GatewayCluster? Cluster { get; set; }
}
