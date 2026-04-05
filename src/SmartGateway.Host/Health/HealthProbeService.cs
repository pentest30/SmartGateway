using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartGateway.Core.Data;

namespace SmartGateway.Host.Health;

public class HealthProbeService : BackgroundService
{
    private readonly IDbContextFactory<SmartGatewayDbContext> _contextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthProbeService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    public HealthProbeService(
        IDbContextFactory<SmartGatewayDbContext> contextFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<HealthProbeService> logger)
    {
        _contextFactory = contextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProbeAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Health probe cycle failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    public async Task ProbeAllAsync(CancellationToken ct)
    {
        using var context = _contextFactory.CreateDbContext();
        var destinations = await context.Destinations.ToListAsync(ct);

        var client = _httpClientFactory.CreateClient("HealthProbe");
        client.Timeout = TimeSpan.FromSeconds(5);

        foreach (var dest in destinations)
        {
            var healthy = await ProbeAsync(client, dest.Address, ct);
            if (dest.IsHealthy != healthy)
            {
                dest.IsHealthy = healthy;
                _logger.LogInformation("Destination {DestId} in {ClusterId} is now {Status}",
                    dest.DestinationId, dest.ClusterId, healthy ? "HEALTHY" : "UNHEALTHY");
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private async Task<bool> ProbeAsync(HttpClient client, string address, CancellationToken ct)
    {
        try
        {
            var uri = address.TrimEnd('/') + "/health";
            var response = await client.GetAsync(uri, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
