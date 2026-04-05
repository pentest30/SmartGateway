using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;

namespace SmartGateway.Integration.Tests;

public class ApiTestFactory : WebApplicationFactory<SmartGateway.Api.ApiProgram>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove ALL EF-related registrations
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
                options.UseInMemoryDatabase(_dbName));
        });
    }
}
