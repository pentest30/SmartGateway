using Yarp.ReverseProxy.Configuration;

namespace SmartGateway.Host.ConfigProvider;

public static class ReverseProxyBuilderExtensions
{
    public static IReverseProxyBuilder LoadFromCustomProvider(this IReverseProxyBuilder builder)
    {
        builder.Services.AddSingleton<IProxyConfigProvider>(sp =>
            sp.GetRequiredService<DatabaseProxyConfigProvider>());
        return builder;
    }
}
