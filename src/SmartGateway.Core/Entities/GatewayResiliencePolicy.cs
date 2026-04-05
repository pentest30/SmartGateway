namespace SmartGateway.Core.Entities;

public class GatewayResiliencePolicy
{
    public string ClusterId { get; set; } = default!;
    public int RetryMaxAttempts { get; set; } = 3;
    public string RetryBackoffType { get; set; } = "Exponential";
    public int RetryDelayMs { get; set; } = 200;
    public bool CircuitEnabled { get; set; } = true;
    public double CircuitFailureRatio { get; set; } = 0.5;
    public int CircuitSamplingMs { get; set; } = 30000;
    public int CircuitBreakMs { get; set; } = 30000;
    public int TimeoutMs { get; set; } = 10000;
    public string? RetryOnStatusCodes { get; set; } = "502,503,504";

    public GatewayCluster? Cluster { get; set; }
}
