using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Host.LoadBalancing;

public class DatabaseDestinationWeightProvider : IDestinationWeightProvider
{
    private readonly IDbContextFactory<SmartGatewayDbContext> _contextFactory;
    private Dictionary<string, int> _weights = new();

    public DatabaseDestinationWeightProvider(IDbContextFactory<SmartGatewayDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
        Refresh();
    }

    public int GetWeight(string clusterId, string destinationId)
    {
        var key = $"{clusterId}:{destinationId}";
        return _weights.TryGetValue(key, out var weight) ? weight : 100;
    }

    public void Refresh()
    {
        using var context = _contextFactory.CreateDbContext();
        _weights = context.Destinations
            .AsNoTracking()
            .ToDictionary(d => $"{d.ClusterId}:{d.DestinationId}", d => d.Weight);
    }
}
