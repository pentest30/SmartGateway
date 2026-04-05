using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Api.Controllers;

[ApiController]
[Route("admin/api/routes")]
public class RoutesController : ControllerBase
{
    private readonly SmartGatewayDbContext _context;
    private readonly IAuditService _audit;
    private readonly IConfigReloadNotifier _reloadNotifier;

    public RoutesController(SmartGatewayDbContext context, IAuditService audit, IConfigReloadNotifier reloadNotifier)
    {
        _context = context;
        _audit = audit;
        _reloadNotifier = reloadNotifier;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var routes = await _context.Routes.AsNoTracking()
            .OrderBy(r => r.Priority).ThenBy(r => r.RouteId)
            .ToListAsync();
        return Ok(routes);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GatewayRoute route)
    {
        if (await _context.Routes.AnyAsync(r => r.RouteId == route.RouteId))
            return Conflict($"Route '{route.RouteId}' already exists.");

        _context.Routes.Add(route);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Route", route.RouteId, "CREATE", "api",
            null, new { route.RouteId, route.ClusterId, route.PathPattern });
        await _reloadNotifier.NotifyConfigChangedAsync();

        return CreatedAtAction(nameof(GetAll), new { id = route.RouteId }, route);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] GatewayRoute updated)
    {
        var route = await _context.Routes.FindAsync(id);
        if (route == null) return NotFound();

        var old = new { route.PathPattern, route.ClusterId, route.IsActive };
        route.PathPattern = updated.PathPattern;
        route.ClusterId = updated.ClusterId;
        route.Methods = updated.Methods;
        route.Priority = updated.Priority;
        route.IsActive = updated.IsActive;
        route.RequiresAuth = updated.RequiresAuth;
        route.AuthPolicyName = updated.AuthPolicyName;
        route.MatchHeader = updated.MatchHeader;
        route.MatchHeaderValue = updated.MatchHeaderValue;
        route.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Route", id, "UPDATE", "api", old,
            new { route.PathPattern, route.ClusterId, route.IsActive });
        await _reloadNotifier.NotifyConfigChangedAsync();

        return Ok(route);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var route = await _context.Routes.FindAsync(id);
        if (route == null) return NotFound();

        _context.Routes.Remove(route);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Route", id, "DELETE", "api",
            new { route.RouteId, route.PathPattern }, null);
        await _reloadNotifier.NotifyConfigChangedAsync();

        return NoContent();
    }
}
