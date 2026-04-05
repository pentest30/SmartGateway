var serviceName = args.Length > 0 ? args[0] : "service-default";
var port = args.Length > 1 ? args[1] : "3000";

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapFallback(async ctx =>
{
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new
    {
        service = serviceName,
        path = ctx.Request.Path.Value,
        method = ctx.Request.Method,
        time = DateTime.UtcNow.ToString("HH:mm:ss.fff"),
        headers = ctx.Request.Headers
            .Where(h => h.Key.StartsWith("X-"))
            .ToDictionary(h => h.Key, h => h.Value.ToString())
    });
});

Console.WriteLine($"[{serviceName}] listening on http://localhost:{port}");
app.Run();
