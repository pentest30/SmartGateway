using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SmartGateway.Core.Data;
using Yarp.ReverseProxy.Configuration;

namespace SmartGateway.Host.ConfigProvider;

public class DatabaseProxyConfigProvider : IProxyConfigProvider
{
    private readonly IDbContextFactory<SmartGatewayDbContext> _contextFactory;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private volatile DatabaseProxyConfig _config;

    public DatabaseProxyConfigProvider(
        IDbContextFactory<SmartGatewayDbContext> contextFactory,
        ILogger<DatabaseProxyConfigProvider> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _config = new DatabaseProxyConfig([], [], new CancellationTokenSource());
    }

    public IProxyConfig GetConfig() => _config;

    public void SignalReload()
    {
        var oldConfig = _config;
        try
        {
            var (routes, clusters) = LoadFromDatabase();
            var newCts = new CancellationTokenSource();
            _config = new DatabaseProxyConfig(routes, clusters, newCts);
            oldConfig.SignalChange();
            _logger.LogInformation("YARP config reloaded: {RouteCount} routes, {ClusterCount} clusters",
                routes.Count, clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload proxy config from database");
        }
    }

    private (IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) LoadFromDatabase()
    {
        using var context = _contextFactory.CreateDbContext();

        var dbClusters = context.Clusters
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Include(c => c.Destinations)
            .ToList();

        var dbRoutes = context.Routes
            .AsNoTracking()
            .Where(r => r.IsActive)
            .ToList();

        var dbTransforms = context.Transforms
            .AsNoTracking()
            .ToList()
            .GroupBy(t => t.RouteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var routes = dbRoutes.Select(r =>
        {
            var transforms = new List<Dictionary<string, string>>();
            if (dbTransforms.TryGetValue(r.RouteId, out var routeTransforms))
            {
                foreach (var t in routeTransforms)
                {
                    var dict = new Dictionary<string, string>();
                    switch (t.Type)
                    {
                        case "RequestHeader" when t.Action == "Set":
                            dict["RequestHeader"] = t.Key;
                            dict["Set"] = t.Value ?? "";
                            break;
                        case "RequestHeader" when t.Action == "Append":
                            dict["RequestHeader"] = t.Key;
                            dict["Append"] = t.Value ?? "";
                            break;
                        case "RequestHeader" when t.Action == "Remove":
                            dict["RequestHeaderRemove"] = t.Key;
                            break;
                        case "ResponseHeader" when t.Action == "Set":
                            dict["ResponseHeader"] = t.Key;
                            dict["Set"] = t.Value ?? "";
                            break;
                        case "ResponseHeader" when t.Action == "Remove":
                            dict["ResponseHeaderRemove"] = t.Key;
                            break;
                        case "PathPrefix":
                            dict["PathRemovePrefix"] = t.Value ?? "";
                            break;
                    }
                    if (dict.Count > 0)
                        transforms.Add(dict);
                }
            }

            return new RouteConfig
            {
                RouteId = r.RouteId,
                ClusterId = r.ClusterId,
                Match = new RouteMatch
                {
                    Path = r.PathPattern,
                    Hosts = string.IsNullOrEmpty(r.Hosts) ? null : r.Hosts.Split(',', StringSplitOptions.TrimEntries),
                    Methods = string.IsNullOrEmpty(r.Methods) ? null : r.Methods.Split(',', StringSplitOptions.TrimEntries),
                    Headers = string.IsNullOrEmpty(r.MatchHeader) ? null : new[]
                    {
                        new RouteHeader
                        {
                            Name = r.MatchHeader,
                            Values = new[] { r.MatchHeaderValue ?? "" }
                        }
                    }
                },
                Order = r.Priority,
                Transforms = transforms.Count > 0 ? transforms : null
            };
        }).ToList();

        var clusters = dbClusters.Select(c => new ClusterConfig
        {
            ClusterId = c.ClusterId,
            LoadBalancingPolicy = c.LoadBalancing,
            Destinations = c.Destinations
                .Where(d => d.IsHealthy)
                .ToDictionary(
                    d => d.DestinationId,
                    d => new DestinationConfig { Address = d.Address })
        }).ToList();

        return (routes, clusters);
    }
}

public class DatabaseProxyConfig : IProxyConfig
{
    private readonly CancellationTokenSource _cts;

    public DatabaseProxyConfig(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        CancellationTokenSource cts)
    {
        Routes = routes;
        Clusters = clusters;
        _cts = cts;
        ChangeToken = new CancellationChangeToken(_cts.Token);
    }

    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }

    public void SignalChange() => _cts.Cancel();
}
