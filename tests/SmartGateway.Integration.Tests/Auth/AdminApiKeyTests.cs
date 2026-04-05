using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;

namespace SmartGateway.Integration.Tests.Auth;

public class AdminApiKeyTests
{
    [Fact]
    public async Task Request_WithNoKey_WhenKeyConfigured_ShouldReturn401()
    {
        var factory = CreateFactory("test-admin-secret");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/api/clusters");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithCorrectKey_ShouldReturn200()
    {
        var factory = CreateFactory("test-admin-secret");
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/clusters");
        request.Headers.Add("X-Admin-Key", "test-admin-secret");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithWrongKey_ShouldReturn401()
    {
        var factory = CreateFactory("correct-key");
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/clusters");
        request.Headers.Add("X-Admin-Key", "wrong-key");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithNoKeyConfigured_ShouldAllowAccess()
    {
        var factory = CreateFactory("");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/api/clusters");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static WebApplicationFactory<SmartGateway.Api.ApiProgram> CreateFactory(string adminKey)
    {
        return new WebApplicationFactory<SmartGateway.Api.ApiProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AdminApi:Key"] = adminKey
                    });
                });
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
                        options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                });
            });
    }
}
