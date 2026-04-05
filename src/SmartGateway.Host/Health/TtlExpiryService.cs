using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartGateway.Core.Data;

namespace SmartGateway.Host.Health;

public class TtlExpiryService : BackgroundService
{
    private readonly IDbContextFactory<SmartGatewayDbContext> _contextFactory;
    private readonly ILogger<TtlExpiryService> _logger;

    public TtlExpiryService(
        IDbContextFactory<SmartGatewayDbContext> contextFactory,
        ILogger<TtlExpiryService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvictExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "TTL expiry cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task EvictExpiredAsync(CancellationToken ct)
    {
        using var context = _contextFactory.CreateDbContext();
        var now = DateTime.UtcNow;

        var expired = await context.Destinations
            .Where(d => d.TtlSeconds > 0
                && d.IsHealthy
                && d.LastHeartbeat != null
                && EF.Functions.DateDiffSecond(d.LastHeartbeat!.Value, now) > d.TtlSeconds)
            .ToListAsync(ct);

        foreach (var dest in expired)
        {
            dest.IsHealthy = false;
            _logger.LogWarning("Destination {DestId} in {ClusterId} expired (TTL: {Ttl}s)",
                dest.DestinationId, dest.ClusterId, dest.TtlSeconds);
        }

        if (expired.Count > 0)
            await context.SaveChangesAsync(ct);
    }
}
