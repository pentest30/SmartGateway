using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Host.ConfigProvider;
using SmartGateway.Integration.Tests.Infrastructure;

namespace SmartGateway.Integration.Tests.Proxy;

[Collection("SqlServer")]
public class WebSocketProxyTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private readonly string _dbName = $"WS_{Guid.NewGuid():N}";
    private TestWebSocketServer _wsServer = default!;
    private HostTestFactory _hostFactory = default!;

    public WebSocketProxyTests(SqlServerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.CreateAndMigrateAsync(_dbName);

        _wsServer = new TestWebSocketServer();
        await _wsServer.StartAsync();

        ctx.Clusters.Add(new GatewayCluster { ClusterId = "ws-cluster" });
        ctx.Destinations.Add(new GatewayDestination
        {
            ClusterId = "ws-cluster",
            DestinationId = "ws-dest",
            Address = _wsServer.BaseUrl
        });
        ctx.Routes.Add(new GatewayRoute
        {
            RouteId = "ws-route",
            ClusterId = "ws-cluster",
            PathPattern = "/ws/{**catch-all}"
        });
        await ctx.SaveChangesAsync();

        _hostFactory = new HostTestFactory(_fixture.GetConnectionString(_dbName));
        var provider = _hostFactory.Services.GetRequiredService<DatabaseProxyConfigProvider>();
        provider.SignalReload();
    }

    public async Task DisposeAsync()
    {
        await _hostFactory.DisposeAsync();
        await _wsServer.DisposeAsync();
    }

    [Fact(Skip = "WebSocket proxying requires real TCP connection — TestServer WebSocketClient does not traverse YARP proxy pipeline")]
    public async Task WebSocket_ShouldProxyThroughYarp()
    {
        // Get the host server's URL
        var server = _hostFactory.Server;
        var wsUrl = server.BaseAddress.ToString().Replace("https://", "wss://").Replace("http://", "ws://") + "ws/echo";

        var client = server.CreateWebSocketClient();
        using var ws = await client.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        ws.State.Should().Be(WebSocketState.Open);

        // Send message
        var sendBytes = Encoding.UTF8.GetBytes("hello-gateway");
        await ws.SendAsync(sendBytes, WebSocketMessageType.Text, true, CancellationToken.None);

        // Receive echo
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var response = Encoding.UTF8.GetString(buffer, 0, result.Count);

        response.Should().Be("echo:hello-gateway");

        // Close
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact(Skip = "WebSocket proxying requires real TCP connection — TestServer WebSocketClient does not traverse YARP proxy pipeline")]
    public async Task WebSocket_ShouldSupportMultipleMessages()
    {
        var server = _hostFactory.Server;
        var wsUrl = server.BaseAddress.ToString().Replace("https://", "wss://").Replace("http://", "ws://") + "ws/echo";

        var client = server.CreateWebSocketClient();
        using var ws = await client.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            var msg = $"msg-{i}";
            await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[4096];
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            response.Should().Be($"echo:{msg}");
        }

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        _wsServer.MessageCount.Should().Be(5);
    }
}
