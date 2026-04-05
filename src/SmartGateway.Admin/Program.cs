using Microsoft.EntityFrameworkCore;
using SmartGateway.Admin.Components;
using SmartGateway.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<SmartGatewayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SmartGateway")));

builder.Services.AddHttpClient("GatewayHost", client =>
{
    var hostUrl = builder.Configuration["GatewayHost:Url"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(hostUrl);
    client.Timeout = TimeSpan.FromSeconds(1);
});

var app = builder.Build();

// Ensure database is created on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SmartGatewayDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
