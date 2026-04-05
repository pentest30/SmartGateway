using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Api.Controllers;

[ApiController]
[Route("admin/api/clusters")]
public class ClustersController : ControllerBase
{
    private readonly SmartGatewayDbContext _context;
    private readonly IAuditService _audit;
    private readonly IConfigReloadNotifier _reloadNotifier;

    public ClustersController(SmartGatewayDbContext context, IAuditService audit, IConfigReloadNotifier reloadNotifier)
    {
        _context = context;
        _audit = audit;
        _reloadNotifier = reloadNotifier;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var clusters = await _context.Clusters.AsNoTracking()
            .Include(c => c.Destinations)
            .OrderBy(c => c.ClusterId)
            .ToListAsync();
        return Ok(clusters);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GatewayCluster cluster)
    {
        if (await _context.Clusters.AnyAsync(c => c.ClusterId == cluster.ClusterId))
            return Conflict($"Cluster '{cluster.ClusterId}' already exists.");

        _context.Clusters.Add(cluster);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Cluster", cluster.ClusterId, "CREATE", "api",
            null, new { cluster.ClusterId, cluster.LoadBalancing });
        await _reloadNotifier.NotifyConfigChangedAsync();

        return CreatedAtAction(nameof(GetAll), new { id = cluster.ClusterId }, cluster);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] GatewayCluster updated)
    {
        var cluster = await _context.Clusters.FindAsync(id);
        if (cluster == null) return NotFound();

        var old = new { cluster.LoadBalancing, cluster.IsActive };
        cluster.LoadBalancing = updated.LoadBalancing;
        cluster.IsActive = updated.IsActive;
        cluster.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Cluster", id, "UPDATE", "api", old,
            new { cluster.LoadBalancing, cluster.IsActive });
        await _reloadNotifier.NotifyConfigChangedAsync();

        return Ok(cluster);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var cluster = await _context.Clusters.FindAsync(id);
        if (cluster == null) return NotFound();

        _context.Clusters.Remove(cluster);
        await _context.SaveChangesAsync();
        await _audit.LogAsync("Cluster", id, "DELETE", "api",
            new { cluster.ClusterId, cluster.LoadBalancing }, null);
        await _reloadNotifier.NotifyConfigChangedAsync();

        return NoContent();
    }
}
