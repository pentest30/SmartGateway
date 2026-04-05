using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Api.Services;
using SmartGateway.Core.Data;
using SmartGateway.Core.Interfaces;
using SmartGateway.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddOpenApi();

builder.Services.AddDbContext<SmartGatewayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SmartGateway")));

builder.Services.AddScoped<IAuditService, AuditService>();

builder.Services.AddHttpClient<IConfigReloadNotifier, HttpConfigReloadNotifier>(client =>
{
    var hostUrl = builder.Configuration["GatewayHost:Url"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(hostUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Admin API key auth middleware with RBAC
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/admin/api", StringComparison.OrdinalIgnoreCase))
    {
        var configuredKey = app.Configuration["AdminApi:Key"];
        var providedKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing X-Admin-Key");
            return;
        }

        // Static super-admin key — full access
        if (!string.IsNullOrEmpty(configuredKey) && providedKey == configuredKey)
        {
            await next();
            return;
        }

        // Database-backed key with role check
        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGatewayDbContext>();
        var keyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(providedKey)));
        var apiKey = await db.ApiKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive);

        if (apiKey == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid X-Admin-Key");
            return;
        }

        // Role-based access: "readonly" can only GET, "admin" has full access
        var isWriteOperation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
        if (isWriteOperation && apiKey.Role != "admin")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Insufficient permissions — admin role required for write operations");
            return;
        }
    }
    await next();
});

app.MapControllers();

app.Run();

namespace SmartGateway.Api { public partial class ApiProgram { } }
