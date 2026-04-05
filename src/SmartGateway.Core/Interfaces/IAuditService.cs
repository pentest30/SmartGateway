using SmartGateway.Core.Entities;

namespace SmartGateway.Core.Interfaces;

public interface IAuditService
{
    Task LogAsync(string entityType, string entityId, string action, string changedBy,
        object? oldValues, object? newValues);

    Task<List<GatewayAuditLog>> GetLogsAsync(
        string? entityType = null,
        string? action = null,
        int take = 100);
}
