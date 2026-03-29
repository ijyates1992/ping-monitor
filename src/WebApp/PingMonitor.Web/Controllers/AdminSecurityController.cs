using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.Security;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/security")]
public sealed class AdminSecurityController : Controller
{
    private const int DefaultLogLimit = 100;
    private readonly ISecurityAuthLogQueryService _securityAuthLogQueryService;

    public AdminSecurityController(ISecurityAuthLogQueryService securityAuthLogQueryService)
    {
        _securityAuthLogQueryService = securityAuthLogQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] bool includeSuccessfulUsers = false, [FromQuery] bool includeSuccessfulAgents = false, CancellationToken cancellationToken = default)
    {
        var userAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.User,
                IncludeSuccessful = includeSuccessfulUsers,
                Limit = DefaultLogLimit
            },
            cancellationToken);

        var agentAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.Agent,
                IncludeSuccessful = includeSuccessfulAgents,
                Limit = DefaultLogLimit
            },
            cancellationToken);

        return View("Index", new AdminSecurityPageViewModel
        {
            IncludeSuccessfulUserAttempts = includeSuccessfulUsers,
            IncludeSuccessfulAgentAttempts = includeSuccessfulAgents,
            UserAttempts = userAttempts,
            AgentAttempts = agentAttempts
        });
    }
}
