using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Contracts.Diagnostics;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("internal/dev/state")]
public sealed class DevelopmentStateDiagnosticsController : ControllerBase
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public DevelopmentStateDiagnosticsController(
        PingMonitorDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpGet("assignments/{assignmentId}")]
    [ProducesResponseType(typeof(AssignmentStateDiagnosticResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentStateDiagnosticResponse>> GetAssignmentStateAsync(
        string assignmentId,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var normalizedAssignmentId = assignmentId.Trim();
        var assignment = await _dbContext.MonitorAssignments
            .SingleOrDefaultAsync(x => x.AssignmentId == normalizedAssignmentId, cancellationToken);

        if (assignment is null)
        {
            return NotFound();
        }

        var state = await _dbContext.EndpointStates
            .SingleOrDefaultAsync(x => x.AssignmentId == normalizedAssignmentId, cancellationToken);

        var transitions = await _dbContext.StateTransitions
            .Where(x => x.AssignmentId == normalizedAssignmentId)
            .OrderByDescending(x => x.TransitionAtUtc)
            .Select(x => new StateTransitionDiagnosticItem(
                x.TransitionId,
                x.PreviousState,
                x.NewState,
                x.TransitionAtUtc,
                x.ReasonCode,
                x.DependencyEndpointId))
            .ToArrayAsync(cancellationToken);

        return Ok(new AssignmentStateDiagnosticResponse(
            assignment.AssignmentId,
            assignment.AgentId,
            assignment.EndpointId,
            state?.CurrentState ?? EndpointStateKind.Unknown,
            state?.ConsecutiveFailureCount ?? 0,
            state?.ConsecutiveSuccessCount ?? 0,
            state?.LastCheckUtc,
            state?.LastStateChangeUtc,
            state?.SuppressedByEndpointId,
            transitions));
    }
}
