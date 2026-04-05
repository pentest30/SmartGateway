using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Api.Controllers;

[ApiController]
[Route("admin/api/services")]
public class ServicesController : ControllerBase
{
    private readonly SmartGatewayDbContext _context;
    private readonly IAuditService _audit;
    private readonly IConfigReloadNotifier _reloadNotifier;

    public ServicesController(SmartGatewayDbContext context, IAuditService audit, IConfigReloadNotifier reloadNotifier)
    {
        _context = context;
        _audit = audit;
        _reloadNotifier = reloadNotifier;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var destinations = await _context.Destinations.AsNoTracking()
            .OrderBy(d => d.ClusterId).ThenBy(d => d.DestinationId)
            .ToListAsync();
        return Ok(destinations);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _context.Destinations
            .FirstOrDefaultAsync(d => d.ClusterId == request.ClusterId && d.DestinationId == request.DestinationId);

        if (existing != null)
        {
            existing.Address = request.Address;
            existing.TtlSeconds = request.TtlSeconds;
            existing.LastHeartbeat = DateTime.UtcNow;
            existing.IsHealthy = true;
        }
        else
        {
            _context.Destinations.Add(new GatewayDestination
            {
                ClusterId = request.ClusterId,
                DestinationId = request.DestinationId,
                Address = request.Address,
                TtlSeconds = request.TtlSeconds,
                LastHeartbeat = DateTime.UtcNow,
                IsHealthy = true
            });
        }

        await _context.SaveChangesAsync();
        await _audit.LogAsync("Destination", request.DestinationId, "REGISTER", "service",
            null, new { request.ClusterId, request.Address, request.TtlSeconds });
        await _reloadNotifier.NotifyConfigChangedAsync();

        return Ok();
    }

    [HttpPut("{id}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string id)
    {
        var dest = await _context.Destinations.FirstOrDefaultAsync(d => d.DestinationId == id);
        if (dest == null) return NotFound();

        dest.LastHeartbeat = DateTime.UtcNow;
        dest.IsHealthy = true;
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deregister(string id)
    {
        var dest = await _context.Destinations.FirstOrDefaultAsync(d => d.DestinationId == id);
        if (dest == null) return NotFound();

        _context.Destinations.Remove(dest);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Destination", id, "DEREGISTER", "service",
            new { dest.ClusterId, dest.Address }, null);
        await _reloadNotifier.NotifyConfigChangedAsync();

        return NoContent();
    }
}

public record RegisterRequest(string ClusterId, string DestinationId, string Address, int TtlSeconds = 30);
