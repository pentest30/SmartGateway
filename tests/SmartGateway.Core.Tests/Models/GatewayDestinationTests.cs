using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Models;

public class GatewayDestinationTests
{
    [Fact]
    public void NewDestination_ShouldHaveDefaultValues()
    {
        var dest = new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://localhost:5000"
        };

        dest.IsHealthy.Should().BeTrue();
        dest.Weight.Should().Be(100);
        dest.TtlSeconds.Should().Be(0);
    }

    [Fact]
    public void Destination_ShouldReferenceCluster()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        var dest = new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://localhost:5000",
            Cluster = cluster
        };

        dest.Cluster.Should().BeSameAs(cluster);
    }
}
