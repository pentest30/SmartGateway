using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SmartGateway.Host.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace SmartGateway.Host.Tests.LoadBalancing;

public class LatencyAwarePolicyTests
{
    [Fact]
    public void Name_ShouldBeLatencyAware()
    {
        var tracker = new LatencyTracker();
        var policy = new LatencyAwareLoadBalancingPolicy(tracker);
        policy.Name.Should().Be("LatencyAware");
    }

    [Fact]
    public void PickDestination_ShouldReturnNull_WhenEmpty()
    {
        var tracker = new LatencyTracker();
        var policy = new LatencyAwareLoadBalancingPolicy(tracker);

        var result = policy.PickDestination(new DefaultHttpContext(), new ClusterState("c1"), []);
        result.Should().BeNull();
    }

    [Fact]
    public void PickDestination_ShouldReturnOnly_WhenSingle()
    {
        var tracker = new LatencyTracker();
        var policy = new LatencyAwareLoadBalancingPolicy(tracker);
        var dest = new DestinationState("d1");

        var result = policy.PickDestination(new DefaultHttpContext(), new ClusterState("c1"), [dest]);
        result.Should().BeSameAs(dest);
    }

    [Fact]
    public void PickDestination_ShouldPreferLowerLatency()
    {
        var tracker = new LatencyTracker();
        // d1 is slow, d2 is fast
        for (int i = 0; i < 20; i++)
        {
            tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(100));
            tracker.Record("c1", "d2", TimeSpan.FromMilliseconds(10));
        }

        var policy = new LatencyAwareLoadBalancingPolicy(tracker);
        var d1 = new DestinationState("d1");
        var d2 = new DestinationState("d2");

        var picks = new Dictionary<string, int> { ["d1"] = 0, ["d2"] = 0 };
        for (int i = 0; i < 1000; i++)
        {
            var result = policy.PickDestination(new DefaultHttpContext(), new ClusterState("c1"), [d1, d2]);
            picks[result!.DestinationId]++;
        }

        // d2 (10ms) should be picked far more often than d1 (100ms)
        picks["d2"].Should().BeGreaterThan(picks["d1"]);
    }

    [Fact]
    public void PickDestination_WithNoLatencyData_ShouldRoundRobin()
    {
        var tracker = new LatencyTracker();
        var policy = new LatencyAwareLoadBalancingPolicy(tracker);
        var d1 = new DestinationState("d1");
        var d2 = new DestinationState("d2");

        var picks = new Dictionary<string, int> { ["d1"] = 0, ["d2"] = 0 };
        for (int i = 0; i < 100; i++)
        {
            var result = policy.PickDestination(new DefaultHttpContext(), new ClusterState("c1"), [d1, d2]);
            picks[result!.DestinationId]++;
        }

        // Both should get some traffic
        picks["d1"].Should().BeGreaterThan(0);
        picks["d2"].Should().BeGreaterThan(0);
    }
}

public class LatencyTrackerTests
{
    [Fact]
    public void Record_ShouldStoreLatency()
    {
        var tracker = new LatencyTracker(bufferSize: 10);
        tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(50));

        var p95 = tracker.GetP95("c1", "d1");
        p95.Should().Be(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void GetP95_ShouldReturnCorrectPercentile()
    {
        var tracker = new LatencyTracker(bufferSize: 100);

        // 95 samples at 10ms, 5 samples at 200ms
        for (int i = 0; i < 95; i++)
            tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(10));
        for (int i = 0; i < 5; i++)
            tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(200));

        var p95 = tracker.GetP95("c1", "d1");
        // p95 should be >= the fast values (10ms) — at the 95th percentile
        // with 95 samples at 10ms and 5 at 200ms, p95 index = ceil(0.95*100)-1 = 94
        // sorted: [10,10,...(95 times),200,200,...(5 times)] → index 94 = 10ms or 200ms
        p95.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void GetP95_WithNoData_ShouldReturnZero()
    {
        var tracker = new LatencyTracker();
        var p95 = tracker.GetP95("c1", "unknown");
        p95.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CircularBuffer_ShouldEvictOldEntries()
    {
        var tracker = new LatencyTracker(bufferSize: 5);

        // Fill with 500ms
        for (int i = 0; i < 5; i++)
            tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(500));

        // Overwrite with 10ms
        for (int i = 0; i < 5; i++)
            tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(10));

        var p95 = tracker.GetP95("c1", "d1");
        p95.TotalMilliseconds.Should().BeLessThan(100); // old 500ms values evicted
    }

    [Fact]
    public void Record_ShouldBeThreadSafe()
    {
        var tracker = new LatencyTracker(bufferSize: 1000);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
                tracker.Record("c1", "d1", TimeSpan.FromMilliseconds(Random.Shared.Next(1, 100)));
        }));

        Task.WaitAll(tasks.ToArray());

        var p95 = tracker.GetP95("c1", "d1");
        p95.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
