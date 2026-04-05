using Microsoft.AspNetCore.Http;
using SmartGateway.Core.Interfaces;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace SmartGateway.Host.LoadBalancing;

public class WeightedLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly IDestinationWeightProvider _weightProvider;

    [ThreadStatic]
    private static Random? _random;
    private static Random Random => _random ??= new Random();

    public WeightedLoadBalancingPolicy(IDestinationWeightProvider weightProvider)
    {
        _weightProvider = weightProvider;
    }

    public string Name => "Weighted";

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0)
            return null;

        if (availableDestinations.Count == 1)
            return availableDestinations[0];

        int totalWeight = 0;
        Span<int> cumulativeWeights = availableDestinations.Count <= 64
            ? stackalloc int[availableDestinations.Count]
            : new int[availableDestinations.Count];

        for (int i = 0; i < availableDestinations.Count; i++)
        {
            var weight = _weightProvider.GetWeight(cluster.ClusterId, availableDestinations[i].DestinationId);
            totalWeight += Math.Max(0, weight);
            cumulativeWeights[i] = totalWeight;
        }

        if (totalWeight == 0)
            return availableDestinations[Random.Next(availableDestinations.Count)];

        var target = Random.Next(totalWeight);
        for (int i = 0; i < cumulativeWeights.Length; i++)
        {
            if (target < cumulativeWeights[i])
                return availableDestinations[i];
        }

        return availableDestinations[^1];
    }
}
