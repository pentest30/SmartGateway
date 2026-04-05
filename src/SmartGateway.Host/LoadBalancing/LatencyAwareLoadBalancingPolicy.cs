using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace SmartGateway.Host.LoadBalancing;

public class LatencyAwareLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly LatencyTracker _tracker;

    public LatencyAwareLoadBalancingPolicy(LatencyTracker tracker)
    {
        _tracker = tracker;
    }

    public string Name => "LatencyAware";

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0)
            return null;

        if (availableDestinations.Count == 1)
            return availableDestinations[0];

        // Get p95 latency for each destination
        var latencies = new (DestinationState dest, double p95Ms)[availableDestinations.Count];
        bool hasData = false;

        for (int i = 0; i < availableDestinations.Count; i++)
        {
            var dest = availableDestinations[i];
            var p95 = _tracker.GetP95(cluster.ClusterId, dest.DestinationId);
            latencies[i] = (dest, p95.TotalMilliseconds);
            if (p95 > TimeSpan.Zero)
                hasData = true;
        }

        // No latency data yet — random selection
        if (!hasData)
            return availableDestinations[Random.Shared.Next(availableDestinations.Count)];

        // Inverse-latency weighted selection: lower latency = higher weight
        // Use 1/(p95+1) as weight to avoid division by zero
        double totalWeight = 0;
        Span<double> weights = availableDestinations.Count <= 64
            ? stackalloc double[availableDestinations.Count]
            : new double[availableDestinations.Count];

        for (int i = 0; i < latencies.Length; i++)
        {
            var p95 = latencies[i].p95Ms;
            // Destinations with no data get average weight
            if (p95 <= 0)
                p95 = latencies.Where(l => l.p95Ms > 0).Select(l => l.p95Ms).DefaultIfEmpty(10).Average();

            weights[i] = 1.0 / (p95 + 1);
            totalWeight += weights[i];
        }

        // Weighted random selection
        var target = Random.Shared.NextDouble() * totalWeight;
        double cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (target <= cumulative)
                return latencies[i].dest;
        }

        return latencies[^1].dest;
    }
}
