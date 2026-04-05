using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartGateway.Core.Data;

namespace SmartGateway.Integration.Tests.Infrastructure;

public class HostTestFactory : WebApplicationFactory<SmartGateway.Host.HostProgram>
{
    private readonly string _connectionString;

    public HostTestFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SKIP_DB_MIGRATION"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove all EF/DbContext registrations
            var toRemove = services
                .Where(d =>
                    d.ServiceType.FullName?.Contains("EntityFramework") == true ||
                    d.ServiceType.FullName?.Contains("DbContext") == true ||
                    d.ImplementationType?.FullName?.Contains("EntityFramework") == true ||
                    d.ImplementationType?.FullName?.Contains("SqlServer") == true)
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            // Re-add with test SQL Server (factory only — Host uses IDbContextFactory)
            services.AddDbContextFactory<SmartGatewayDbContext>(options =>
                options.UseSqlServer(_connectionString));

            // Remove background services to avoid interference
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                           (d.ImplementationType?.Name == "HealthProbeService" ||
                            d.ImplementationType?.Name == "TtlExpiryService"))
                .ToList();
            foreach (var d in hostedServices)
                services.Remove(d);
        });
    }
}
