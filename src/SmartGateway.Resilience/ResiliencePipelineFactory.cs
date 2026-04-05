using System.Collections.Concurrent;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using SmartGateway.Core.Entities;

namespace SmartGateway.Resilience;

public static class ResiliencePipelineFactory
{
    public static ResiliencePipeline<string> CreatePipeline(GatewayResiliencePolicy policy)
    {
        var builder = new ResiliencePipelineBuilder<string>();

        if (policy.RetryMaxAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<string>
            {
                MaxRetryAttempts = policy.RetryMaxAttempts,
                Delay = TimeSpan.FromMilliseconds(policy.RetryDelayMs),
                BackoffType = policy.RetryBackoffType?.Equals("Exponential", StringComparison.OrdinalIgnoreCase) == true
                    ? DelayBackoffType.Exponential
                    : DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder<string>().Handle<HttpRequestException>()
            });
        }

        if (policy.CircuitEnabled)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<string>
            {
                FailureRatio = policy.CircuitFailureRatio,
                SamplingDuration = TimeSpan.FromMilliseconds(policy.CircuitSamplingMs),
                BreakDuration = TimeSpan.FromMilliseconds(policy.CircuitBreakMs),
                MinimumThroughput = 2,
                ShouldHandle = new PredicateBuilder<string>().Handle<HttpRequestException>()
            });
        }

        if (policy.TimeoutMs > 0)
        {
            builder.AddTimeout(TimeSpan.FromMilliseconds(policy.TimeoutMs));
        }

        return builder.Build();
    }
}

public class ResiliencePipelineRegistry
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline<string>> _pipelines = new();

    public ResiliencePipeline<string> GetOrCreate(GatewayResiliencePolicy policy)
    {
        return _pipelines.GetOrAdd(policy.ClusterId, _ => ResiliencePipelineFactory.CreatePipeline(policy));
    }

    public void Invalidate(string clusterId)
    {
        _pipelines.TryRemove(clusterId, out _);
    }

    public void Clear() => _pipelines.Clear();
}
