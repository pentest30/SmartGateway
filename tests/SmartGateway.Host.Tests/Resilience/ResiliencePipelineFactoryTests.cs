using FluentAssertions;
using Polly;
using Polly.Timeout;
using SmartGateway.Core.Entities;
using SmartGateway.Resilience;

namespace SmartGateway.Host.Tests.Resilience;

public class ResiliencePipelineFactoryTests
{
    [Fact]
    public void CreatePipeline_ShouldReturnPipeline_WithDefaultPolicy()
    {
        var policy = new GatewayResiliencePolicy { ClusterId = "c1" };

        var pipeline = ResiliencePipelineFactory.CreatePipeline(policy);

        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task Pipeline_ShouldRetryOnFailure()
    {
        var policy = new GatewayResiliencePolicy
        {
            ClusterId = "c1",
            RetryMaxAttempts = 3,
            RetryBackoffType = "Linear",
            RetryDelayMs = 10,
            CircuitEnabled = false
        };

        var pipeline = ResiliencePipelineFactory.CreatePipeline(policy);
        int attempts = 0;

        var result = await pipeline.ExecuteAsync(async ct =>
        {
            attempts++;
            if (attempts < 3)
                throw new HttpRequestException("Transient failure");
            return "success";
        });

        result.Should().Be("success");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_ShouldTimeout_WhenExceedingLimit()
    {
        var policy = new GatewayResiliencePolicy
        {
            ClusterId = "c1",
            TimeoutMs = 50,
            RetryMaxAttempts = 0,
            CircuitEnabled = false
        };

        var pipeline = ResiliencePipelineFactory.CreatePipeline(policy);

        var act = () => pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(5000, ct);
            return "done";
        }).AsTask();

        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public void CreatePipeline_ShouldHandleExponentialBackoff()
    {
        var policy = new GatewayResiliencePolicy
        {
            ClusterId = "c1",
            RetryBackoffType = "Exponential",
            RetryMaxAttempts = 2,
            RetryDelayMs = 10
        };

        var pipeline = ResiliencePipelineFactory.CreatePipeline(policy);
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void CreatePipeline_ShouldWorkWithCircuitDisabled()
    {
        var policy = new GatewayResiliencePolicy
        {
            ClusterId = "c1",
            CircuitEnabled = false
        };

        var pipeline = ResiliencePipelineFactory.CreatePipeline(policy);
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void GetOrCreatePipeline_ShouldCacheByClusterId()
    {
        var policy = new GatewayResiliencePolicy { ClusterId = "c1" };
        var factory = new ResiliencePipelineRegistry();

        var pipeline1 = factory.GetOrCreate(policy);
        var pipeline2 = factory.GetOrCreate(policy);

        pipeline1.Should().BeSameAs(pipeline2);
    }

    [Fact]
    public void InvalidatePipeline_ShouldRemoveFromCache()
    {
        var policy = new GatewayResiliencePolicy { ClusterId = "c1" };
        var factory = new ResiliencePipelineRegistry();

        var pipeline1 = factory.GetOrCreate(policy);
        factory.Invalidate("c1");
        var pipeline2 = factory.GetOrCreate(policy);

        pipeline2.Should().NotBeSameAs(pipeline1);
    }
}
