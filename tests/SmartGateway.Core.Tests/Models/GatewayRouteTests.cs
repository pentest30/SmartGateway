using FluentAssertions;
using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Tests.Models;

public class GatewayRouteTests
{
    [Fact]
    public void NewRoute_ShouldHaveDefaultValues()
    {
        var route = new GatewayRoute
        {
            RouteId = "r1",
            ClusterId = "c1"
        };

        route.Priority.Should().Be(0);
        route.IsActive.Should().BeTrue();
        route.RequiresAuth.Should().BeFalse();
    }

    [Fact]
    public void Route_ShouldReferenceCluster()
    {
        var cluster = new GatewayCluster { ClusterId = "c1" };
        var route = new GatewayRoute
        {
            RouteId = "r1",
            ClusterId = "c1",
            PathPattern = "/api/{**catch-all}",
            Cluster = cluster
        };

        route.Cluster.Should().BeSameAs(cluster);
        route.PathPattern.Should().Be("/api/{**catch-all}");
    }
}
