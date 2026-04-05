using System.Text.Json;
using FluentAssertions;
using SmartGateway.Host.RateLimiting;

namespace SmartGateway.Host.Tests.RateLimiting;

public class RateLimitConfigTests
{
    [Fact]
    public void Parse_ShouldDeserializeFixedWindow()
    {
        var json = """{"Policy":"FixedWindow","PermitLimit":100,"WindowSeconds":60}""";
        var config = JsonSerializer.Deserialize<RateLimitConfig>(json);

        config.Should().NotBeNull();
        config!.Policy.Should().Be("FixedWindow");
        config.PermitLimit.Should().Be(100);
        config.WindowSeconds.Should().Be(60);
    }

    [Fact]
    public void Parse_ShouldDeserializeSlidingWindow()
    {
        var json = """{"Policy":"SlidingWindow","PermitLimit":500,"WindowSeconds":60,"SegmentsPerWindow":6}""";
        var config = JsonSerializer.Deserialize<RateLimitConfig>(json);

        config!.Policy.Should().Be("SlidingWindow");
        config.SegmentsPerWindow.Should().Be(6);
    }

    [Fact]
    public void Parse_ShouldDeserializeTokenBucket()
    {
        var json = """{"Policy":"TokenBucket","TokenLimit":200,"ReplenishmentPeriodSeconds":10,"TokensPerPeriod":20}""";
        var config = JsonSerializer.Deserialize<RateLimitConfig>(json);

        config!.Policy.Should().Be("TokenBucket");
        config.TokenLimit.Should().Be(200);
        config.TokensPerPeriod.Should().Be(20);
        config.ReplenishmentPeriodSeconds.Should().Be(10);
    }

    [Fact]
    public void Parse_ShouldHandleDefaults()
    {
        var json = """{}""";
        var config = JsonSerializer.Deserialize<RateLimitConfig>(json);

        config!.Policy.Should().BeNull();
        config.PermitLimit.Should().Be(0);
    }

    [Fact]
    public void BuildPolicy_FixedWindow_ShouldReturnPartition()
    {
        var config = new RateLimitConfig
        {
            Policy = "FixedWindow",
            PermitLimit = 100,
            WindowSeconds = 60
        };

        var policy = RateLimitPolicyBuilder.Build(config);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void BuildPolicy_SlidingWindow_ShouldReturnPartition()
    {
        var config = new RateLimitConfig
        {
            Policy = "SlidingWindow",
            PermitLimit = 500,
            WindowSeconds = 60,
            SegmentsPerWindow = 6
        };

        var policy = RateLimitPolicyBuilder.Build(config);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void BuildPolicy_TokenBucket_ShouldReturnPartition()
    {
        var config = new RateLimitConfig
        {
            Policy = "TokenBucket",
            TokenLimit = 200,
            ReplenishmentPeriodSeconds = 10,
            TokensPerPeriod = 20
        };

        var policy = RateLimitPolicyBuilder.Build(config);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void BuildPolicy_Null_ShouldReturnNull()
    {
        var policy = RateLimitPolicyBuilder.Build(null);
        policy.Should().BeNull();
    }
}
