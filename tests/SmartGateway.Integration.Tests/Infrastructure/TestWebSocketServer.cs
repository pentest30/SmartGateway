using System.Net.WebSockets;
using System.Text;

namespace SmartGateway.Integration.Tests.Infrastructure;

public class TestWebSocketServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string BaseUrl { get; private set; } = default!;
    public int MessageCount { get; private set; }

    public TestWebSocketServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.UseWebSockets();

        _app.Map("/ws/echo", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[4096];
                while (true)
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }
                    MessageCount++;
                    // Echo back with prefix
                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var echo = Encoding.UTF8.GetBytes($"echo:{msg}");
                    await ws.SendAsync(echo, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        });

        // Health endpoint for YARP destination
        _app.MapGet("/health", () => Results.Ok("healthy"));
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
