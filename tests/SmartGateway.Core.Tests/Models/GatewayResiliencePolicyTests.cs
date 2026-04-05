using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Models;

public class GatewayResiliencePolicyTests
{
    [Fact]
    public void NewPolicy_ShouldHaveDefaultValues()
    {
        var policy = new GatewayResiliencePolicy
        {
            ClusterId = "c1"
        };

        policy.RetryMaxAttempts.Should().Be(3);
        policy.RetryBackoffType.Should().Be("Exponential");
        policy.RetryDelayMs.Should().Be(200);
        policy.CircuitEnabled.Should().BeTrue();
        policy.CircuitFailureRatio.Should().Be(0.5);
        policy.CircuitSamplingMs.Should().Be(30000);
        policy.CircuitBreakMs.Should().Be(30000);
        policy.TimeoutMs.Should().Be(10000);
    }
}
