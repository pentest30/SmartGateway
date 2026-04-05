using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.Health;

namespace SmartGateway.Host.Tests.Health;

public class ActiveHealthCheckServiceTests : IDisposable
{
    private readonly SmartGatewayDbContext _context;

    private readonly DbContextOptions<SmartGatewayDbContext> _options;

    public ActiveHealthCheckServiceTests()
    {
        _options = new DbContextOptionsBuilder<SmartGatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new SmartGatewayDbContext(_options);
    }

    private HealthProbeService CreateService(IHttpClientFactory? httpFactory = null)
    {
        var factory = Substitute.For<IDbContextFactory<SmartGatewayDbContext>>();
        factory.CreateDbContext().Returns(_ => new SmartGatewayDbContext(_options));
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => new SmartGatewayDbContext(_options));

        var logger = Substitute.For<ILogger<HealthProbeService>>();
        httpFactory ??= Substitute.For<IHttpClientFactory>();

        return new HealthProbeService(factory, httpFactory, logger);
    }

    [Fact]
    public async Task ProbeDestinations_ShouldMarkUnhealthy_WhenProbeFails()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://unreachable:9999",
            IsHealthy = true
        });
        await _context.SaveChangesAsync();

        var httpFactory = Substitute.For<IHttpClientFactory>();
        var handler = new FakeHttpHandler(HttpStatusCode.ServiceUnavailable);
        httpFactory.CreateClient("HealthProbe").Returns(new HttpClient(handler));

        var service = CreateService(httpFactory);
        await service.ProbeAllAsync(CancellationToken.None);

        using var assertCtx = new SmartGatewayDbContext(_options);
        var dest = await assertCtx.Destinations.FirstAsync(d => d.DestinationId == "d1");
        dest.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeDestinations_ShouldKeepHealthy_WhenProbeSucceeds()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://healthy:8080",
            IsHealthy = true
        });
        await _context.SaveChangesAsync();

        var httpFactory = Substitute.For<IHttpClientFactory>();
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        httpFactory.CreateClient("HealthProbe").Returns(new HttpClient(handler));

        var service = CreateService(httpFactory);
        await service.ProbeAllAsync(CancellationToken.None);

        using var assertCtx = new SmartGatewayDbContext(_options);
        var dest = await assertCtx.Destinations.FirstAsync(d => d.DestinationId == "d1");
        dest.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeDestinations_ShouldReAdmit_WhenPreviouslyUnhealthyNowOk()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://recovered:8080",
            IsHealthy = false
        });
        await _context.SaveChangesAsync();

        var httpFactory = Substitute.For<IHttpClientFactory>();
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        httpFactory.CreateClient("HealthProbe").Returns(new HttpClient(handler));

        var service = CreateService(httpFactory);
        await service.ProbeAllAsync(CancellationToken.None);

        using var assertCtx = new SmartGatewayDbContext(_options);
        var dest = await assertCtx.Destinations.FirstAsync(d => d.DestinationId == "d1");
        dest.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeDestinations_ShouldHandleTimeout()
    {
        _context.Clusters.Add(new GatewayCluster { ClusterId = "c1" });
        _context.Destinations.Add(new GatewayDestination
        {
            ClusterId = "c1",
            DestinationId = "d1",
            Address = "https://timeout:8080",
            IsHealthy = true
        });
        await _context.SaveChangesAsync();

        var httpFactory = Substitute.For<IHttpClientFactory>();
        var handler = new FakeHttpHandler(throws: true);
        httpFactory.CreateClient("HealthProbe").Returns(new HttpClient(handler));

        var service = CreateService(httpFactory);
        await service.ProbeAllAsync(CancellationToken.None);

        using var assertCtx = new SmartGatewayDbContext(_options);
        var dest = await assertCtx.Destinations.FirstAsync(d => d.DestinationId == "d1");
        dest.IsHealthy.Should().BeFalse();
    }

    public void Dispose() => _context.Dispose();
}

internal class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly bool _throws;

    public FakeHttpHandler(HttpStatusCode statusCode = HttpStatusCode.OK, bool throws = false)
    {
        _statusCode = statusCode;
        _throws = throws;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_throws) throw new TaskCanceledException("Timeout");
        return Task.FromResult(new HttpResponseMessage(_statusCode));
    }
}
