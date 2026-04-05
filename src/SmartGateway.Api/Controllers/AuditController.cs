using Microsoft.AspNetCore.Mvc;
using SmartGateway.Core.Interfaces;

namespace SmartGateway.Api.Controllers;

[ApiController]
[Route("admin/api/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] int take = 100)
    {
        var logs = await _audit.GetLogsAsync(entityType, action, take);
        return Ok(logs);
    }
}
