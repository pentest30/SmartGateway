using System.Collections.Concurrent;
using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using SmartGateway.Core.Entities;

namespace SmartGateway.Resilience;

public static class ResiliencePipelineFactory
{
    public static ResiliencePipeline<HttpResponseMessage> CreatePipeline(GatewayResiliencePolicy policy)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        var retryStatusCodes = ParseStatusCodes(policy.RetryOnStatusCodes);

        if (policy.RetryMaxAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = policy.RetryMaxAttempts,
                Delay = TimeSpan.FromMilliseconds(policy.RetryDelayMs),
                BackoffType = policy.RetryBackoffType?.Equals("Exponential", StringComparison.OrdinalIgnoreCase) == true
                    ? DelayBackoffType.Exponential
                    : DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => retryStatusCodes.Contains(response.StatusCode))
            });
        }

        if (policy.CircuitEnabled)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = policy.CircuitFailureRatio,
                SamplingDuration = TimeSpan.FromMilliseconds(policy.CircuitSamplingMs),
                BreakDuration = TimeSpan.FromMilliseconds(policy.CircuitBreakMs),
                MinimumThroughput = 2,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => retryStatusCodes.Contains(response.StatusCode))
            });
        }

        if (policy.TimeoutMs > 0)
        {
            builder.AddTimeout(TimeSpan.FromMilliseconds(policy.TimeoutMs));
        }

        return builder.Build();
    }

    private static HashSet<HttpStatusCode> ParseStatusCodes(string? statusCodes)
    {
        var codes = new HashSet<HttpStatusCode>();
        if (string.IsNullOrWhiteSpace(statusCodes))
            return codes;

        foreach (var part in statusCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var code))
                codes.Add((HttpStatusCode)code);
        }
        return codes;
    }
}

public class ResiliencePipelineRegistry
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _pipelines = new();

    public ResiliencePipeline<HttpResponseMessage> GetOrCreate(GatewayResiliencePolicy policy)
    {
        return _pipelines.GetOrAdd(policy.ClusterId, _ => ResiliencePipelineFactory.CreatePipeline(policy));
    }

    public void Invalidate(string clusterId)
    {
        _pipelines.TryRemove(clusterId, out _);
    }

    public void Clear() => _pipelines.Clear();
}
