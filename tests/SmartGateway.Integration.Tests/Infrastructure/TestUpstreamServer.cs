using System.Collections.Concurrent;
using System.Text.Json;

namespace SmartGateway.Integration.Tests.Infrastructure;

public class TestUpstreamServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, int> _requestCounts = new();
    private volatile bool _healthy = true;

    public string ServerId { get; }
    public string BaseUrl { get; private set; } = default!;

    public TestUpstreamServer(string? serverId = null)
    {
        ServerId = serverId ?? Guid.NewGuid().ToString("N")[..8];

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/health", () =>
        {
            if (_healthy)
                return Results.Ok("healthy");
            return Results.StatusCode(503);
        });

        _app.MapFallback(async (HttpContext ctx) =>
        {
            var path = ctx.Request.Path.Value ?? "/";
            _requestCounts.AddOrUpdate(path, 1, (_, c) => c + 1);
            IncrementTotal();

            var headers = ctx.Request.Headers
                .ToDictionary(h => h.Key, h => h.Value.ToString());

            var response = new
            {
                server = ServerId,
                path,
                method = ctx.Request.Method,
                headers
            };

            ctx.Response.Headers["X-Server-Id"] = ServerId;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
        });
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();
    }

    public void SetHealthy(bool healthy) => _healthy = healthy;

    public int GetRequestCount(string? path = null)
    {
        if (path == null)
            return _requestCounts.GetValueOrDefault("__total", 0);
        return _requestCounts.GetValueOrDefault(path, 0);
    }

    public int TotalRequests => _requestCounts.GetValueOrDefault("__total", 0);

    private void IncrementTotal()
    {
        _requestCounts.AddOrUpdate("__total", 1, (_, c) => c + 1);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
