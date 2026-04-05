using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.LoadBalancing;

namespace SmartGateway.Host.Plugins;

public static class PluginLoader
{
    public static void LoadPlugins(IServiceCollection services, string pluginDirectory, ILogger? logger = null)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            logger?.LogInformation("Plugin directory {Dir} does not exist, skipping plugin loading", pluginDirectory);
            return;
        }

        var dlls = Directory.GetFiles(pluginDirectory, "*.dll");
        logger?.LogInformation("Scanning {Count} DLLs in {Dir} for plugins", dlls.Length, pluginDirectory);

        foreach (var dll in dlls)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var policyTypes = assembly.GetTypes()
                    .Where(t => typeof(ILoadBalancingPolicy).IsAssignableFrom(t)
                             && t is { IsAbstract: false, IsInterface: false });

                foreach (var type in policyTypes)
                {
                    services.AddSingleton(typeof(ILoadBalancingPolicy), type);
                    logger?.LogInformation("Loaded LB policy plugin: {Type} from {Dll}",
                        type.FullName, Path.GetFileName(dll));
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to load plugin from {Dll}", dll);
            }
        }
    }
}
