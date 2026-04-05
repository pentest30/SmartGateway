using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;

namespace SmartGateway.Integration.Tests.Infrastructure;

public class ApiTestFactorySql : WebApplicationFactory<SmartGateway.Api.ApiProgram>
{
    private readonly string _connectionString;

    public ApiTestFactorySql(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType.FullName?.Contains("EntityFramework") == true ||
                    d.ServiceType.FullName?.Contains("DbContext") == true ||
                    d.ImplementationType?.FullName?.Contains("EntityFramework") == true ||
                    d.ImplementationType?.FullName?.Contains("SqlServer") == true)
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<SmartGatewayDbContext>(options =>
                options.UseSqlServer(_connectionString));
        });
    }
}
