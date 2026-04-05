using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SmartGateway.Core.Interfaces;
using SmartGateway.Host.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace SmartGateway.Host.Tests.LoadBalancing;

public class WeightedLoadBalancingPolicyTests
{
    private readonly IDestinationWeightProvider _weightProvider;
    private readonly WeightedLoadBalancingPolicy _policy;

    public WeightedLoadBalancingPolicyTests()
    {
        _weightProvider = Substitute.For<IDestinationWeightProvider>();
        _policy = new WeightedLoadBalancingPolicy(_weightProvider);
    }

    [Fact]
    public void Name_ShouldBeWeighted()
    {
        _policy.Name.Should().Be("Weighted");
    }

    [Fact]
    public void PickDestination_ShouldReturnNull_WhenNoDestinations()
    {
        var context = new DefaultHttpContext();
        var cluster = new ClusterState("c1");
        var destinations = Array.Empty<DestinationState>();

        var result = _policy.PickDestination(context, cluster, destinations);

        result.Should().BeNull();
    }

    [Fact]
    public void PickDestination_ShouldReturnOnlyDestination_WhenSingle()
    {
        var context = new DefaultHttpContext();
        var cluster = new ClusterState("c1");
        var dest = new DestinationState("d1");

        var result = _policy.PickDestination(context, cluster, new[] { dest });

        result.Should().BeSameAs(dest);
    }

    [Fact]
    public void PickDestination_ShouldRespectWeights_StatisticalTest()
    {
        var context = new DefaultHttpContext();
        var cluster = new ClusterState("c1");

        _weightProvider.GetWeight("c1", "d1").Returns(90);
        _weightProvider.GetWeight("c1", "d2").Returns(10);

        var d1 = new DestinationState("d1");
        var d2 = new DestinationState("d2");
        var destinations = new[] { d1, d2 };

        var picks = new Dictionary<string, int> { ["d1"] = 0, ["d2"] = 0 };
        const int iterations = 10000;

        for (int i = 0; i < iterations; i++)
        {
            var result = _policy.PickDestination(context, cluster, destinations);
            picks[result!.DestinationId]++;
        }

        var d1Ratio = (double)picks["d1"] / iterations;
        d1Ratio.Should().BeInRange(0.85, 0.95);
    }

    [Fact]
    public void PickDestination_ShouldHandleEqualWeights()
    {
        var context = new DefaultHttpContext();
        var cluster = new ClusterState("c1");

        _weightProvider.GetWeight("c1", "d1").Returns(50);
        _weightProvider.GetWeight("c1", "d2").Returns(50);

        var d1 = new DestinationState("d1");
        var d2 = new DestinationState("d2");
        var destinations = new[] { d1, d2 };

        var picks = new Dictionary<string, int> { ["d1"] = 0, ["d2"] = 0 };
        const int iterations = 10000;

        for (int i = 0; i < iterations; i++)
        {
            var result = _policy.PickDestination(context, cluster, destinations);
            picks[result!.DestinationId]++;
        }

        var d1Ratio = (double)picks["d1"] / iterations;
        d1Ratio.Should().BeInRange(0.45, 0.55);
    }

    [Fact]
    public void PickDestination_ShouldHandleZeroWeights()
    {
        var context = new DefaultHttpContext();
        var cluster = new ClusterState("c1");

        _weightProvider.GetWeight("c1", "d1").Returns(0);
        _weightProvider.GetWeight("c1", "d2").Returns(0);

        var d1 = new DestinationState("d1");
        var d2 = new DestinationState("d2");
        var destinations = new[] { d1, d2 };

        // Should still return something (random fallback)
        var result = _policy.PickDestination(context, cluster, destinations);
        result.Should().NotBeNull();
    }
}
