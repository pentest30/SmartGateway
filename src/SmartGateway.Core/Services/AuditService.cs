using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartGateway.Core.Data;
using SmartGateway.Core.Entities;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Core.Services;

public class AuditService : IAuditService
{
    private readonly SmartGatewayDbContext _context;

    public AuditService(SmartGatewayDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string entityType, string entityId, string action, string changedBy,
        object? oldValues, object? newValues)
    {
        var entry = new GatewayAuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedBy = changedBy,
            OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
            NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
            ChangedAt = DateTime.UtcNow
        };

        _context.AuditLogs.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<List<GatewayAuditLog>> GetLogsAsync(
        string? entityType = null,
        string? action = null,
        int take = 100)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(l => l.EntityType == entityType);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(l => l.Action == action);

        return await query
            .OrderByDescending(l => l.ChangedAt)
            .Take(take)
            .ToListAsync();
    }
}
