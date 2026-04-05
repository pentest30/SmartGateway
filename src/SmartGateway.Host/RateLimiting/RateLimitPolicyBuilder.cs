using System.Threading.RateLimiting;

namespace SmartGateway.Host.RateLimiting;

public static class RateLimitPolicyBuilder
{
    public static RateLimiter? Build(RateLimitConfig? config)
    {
        if (config?.Policy == null)
            return null;

        return config.Policy switch
        {
            "FixedWindow" => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.PermitLimit,
                Window = TimeSpan.FromSeconds(config.WindowSeconds),
                QueueLimit = 0
            }),
            "SlidingWindow" => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = config.PermitLimit,
                Window = TimeSpan.FromSeconds(config.WindowSeconds),
                SegmentsPerWindow = config.SegmentsPerWindow,
                QueueLimit = 0
            }),
            "TokenBucket" => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = config.TokenLimit,
                ReplenishmentPeriod = TimeSpan.FromSeconds(config.ReplenishmentPeriodSeconds),
                TokensPerPeriod = config.TokensPerPeriod,
                QueueLimit = 0
            }),
            _ => null
        };
    }
}
