namespace SmartGateway.Host.RateLimiting;

public class RateLimitConfig
{
    public string? Policy { get; set; }
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int SegmentsPerWindow { get; set; } = 4;
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }
}
