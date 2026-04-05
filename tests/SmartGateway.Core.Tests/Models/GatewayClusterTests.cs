using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Models;

public class GatewayClusterTests
{
    [Fact]
    public void NewCluster_ShouldHaveDefaultValues()
    {
        var cluster = new GatewayCluster
        {
            ClusterId = "test-cluster"
        };

        cluster.LoadBalancing.Should().Be("RoundRobin");
        cluster.IsActive.Should().BeTrue();
        cluster.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        cluster.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Cluster_ShouldHaveDestinationsCollection()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        cluster.Destinations.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Cluster_ShouldHaveRoutesCollection()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        cluster.Routes.Should().NotBeNull().And.BeEmpty();
    }
}
