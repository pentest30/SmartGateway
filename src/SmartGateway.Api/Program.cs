using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Admin API key auth middleware
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/admin/api", StringComparison.OrdinalIgnoreCase))
    {
        var configuredKey = app.Configuration["AdminApi:Key"];
        if (!string.IsNullOrEmpty(configuredKey))
        {
            var providedKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();
            if (providedKey != configuredKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid or missing X-Admin-Key");
                return;
            }
        }
    }
    await next();
});

app.MapControllers();

app.Run();

namespace SmartGateway.Api { public partial class ApiProgram { } }
