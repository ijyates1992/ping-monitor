using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.Metrics;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/admin/metrics24h")]
public sealed class AdminMetricsController : ControllerBase
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;

    public AdminMetricsController(PingMonitorDbContext dbContext, IAssignmentMetrics24hService assignmentMetrics24hService)
    {
        _dbContext = dbContext;
        _assignmentMetrics24hService = assignmentMetrics24hService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var summary = new
        {
            rowCount = await _dbContext.AssignmentMetrics24h.AsNoTracking().CountAsync(cancellationToken),
            lastUpdatedAtUtc = await _dbContext.AssignmentMetrics24h.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => (DateTimeOffset?)x.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
        };

        return Ok(summary);
    }

    [HttpPost("rebuild/{assignmentId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RebuildAssignment([FromRoute] string assignmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return BadRequest(new { message = "Assignment ID is required." });
        }

        await _assignmentMetrics24hService.RefreshAssignmentAsync(assignmentId.Trim(), cancellationToken);
        return Ok(new { rebuilt = true, assignmentId = assignmentId.Trim() });
    }

    [HttpPost("rebuild-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RebuildAll(CancellationToken cancellationToken)
    {
        await _assignmentMetrics24hService.RebuildAllAsync(cancellationToken);
        return Ok(new { rebuilt = true, scope = "all" });
    }
}
