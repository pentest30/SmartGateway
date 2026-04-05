using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using SmartGateway.Core.Data;
using SmartGateway.Core.Interfaces;
using SmartGateway.Host.Auth;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Host.Health;
using SmartGateway.Host.LoadBalancing;
using SmartGateway.Host.RateLimiting;
using SmartGateway.Resilience;
using Yarp.ReverseProxy.LoadBalancing;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// EF Core
builder.Services.AddDbContextFactory<SmartGatewayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SmartGateway")));

// YARP with database-backed config
builder.Services.AddSingleton<DatabaseProxyConfigProvider>();
builder.Services.AddReverseProxy()
    .LoadFromCustomProvider();

// Custom LB policies
builder.Services.AddSingleton<IDestinationWeightProvider, DatabaseDestinationWeightProvider>();
builder.Services.AddSingleton<ILoadBalancingPolicy, WeightedLoadBalancingPolicy>();
builder.Services.AddSingleton<LatencyTracker>();
builder.Services.AddSingleton<ILoadBalancingPolicy, LatencyAwareLoadBalancingPolicy>();

// Resilience
builder.Services.AddSingleton<ResiliencePipelineRegistry>();

// Load external LB policy plugins
var pluginDir = Path.Combine(AppContext.BaseDirectory, "plugins");
SmartGateway.Host.Plugins.PluginLoader.LoadPlugins(builder.Services, pluginDir);

// HttpClient for health probes
builder.Services.AddHttpClient();

// Health check background services
builder.Services.AddHostedService<HealthProbeService>();
builder.Services.AddHostedService<TtlExpiryService>();

// API Key validator
builder.Services.AddSingleton<ApiKeyValidator>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global fallback policy
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10000,
            Window = TimeSpan.FromMinutes(1)
        });
    });
});

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

// Ensure database is migrated on startup
if (app.Configuration["SKIP_DB_MIGRATION"] != "true")
{
    using var scope = app.Services.CreateScope();
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SmartGatewayDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.Migrate();
}

// Load initial YARP config from DB
var configProvider = app.Services.GetRequiredService<DatabaseProxyConfigProvider>();
configProvider.SignalReload();

app.UseWebSockets();
app.UseSerilogRequestLogging();

// API Key auth middleware — validates X-Api-Key header for routes with RequiresAuth
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";

    // Check if the matched route requires auth by looking at DB config
    var config = configProvider.GetConfig();
    var matchedRoute = config.Routes.FirstOrDefault(r =>
        r.Match.Path != null && path.StartsWith(r.Match.Path.Replace("/{**catch-all}", "").Replace("{**catch-all}", "")));

    if (matchedRoute != null)
    {
        // Look up the route in DB to check RequiresAuth
        using var scope = context.RequestServices.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SmartGatewayDbContext>>();
        await using var db = dbFactory.CreateDbContext();
        var dbRoute = await db.Routes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RouteId == matchedRoute.RouteId);

        if (dbRoute?.RequiresAuth == true)
        {
            var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(apiKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("API key required");
                return;
            }

            var validator = context.RequestServices.GetRequiredService<ApiKeyValidator>();
            if (!await validator.ValidateAsync(apiKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid API key");
                return;
            }
        }
    }

    await next();
});

app.UseRateLimiter();

// Reload endpoint — triggers YARP to pick up DB changes
app.MapPost("/_admin/reload", () =>
{
    configProvider.SignalReload();
    var config = configProvider.GetConfig();
    return Results.Ok(new { routes = config.Routes.Count, clusters = config.Clusters.Count });
});

app.MapReverseProxy();

app.Run();

namespace SmartGateway.Host { public partial class HostProgram { } }
