using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationUpdate;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/application-update")]
public sealed class AdminApplicationUpdateController : Controller
{
    private readonly IApplicationUpdateCheckService _applicationUpdateCheckService;
    private readonly ApplicationUpdateOptions _options;

    public AdminApplicationUpdateController(
        IApplicationUpdateCheckService applicationUpdateCheckService,
        IOptions<ApplicationUpdateOptions> options)
    {
        _applicationUpdateCheckService = applicationUpdateCheckService;
        _options = options.Value;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View("Index", BuildPageModel(_applicationUpdateCheckService.BuildNotPerformedResult()));
    }

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckNow(CancellationToken cancellationToken)
    {
        var result = await _applicationUpdateCheckService.CheckForUpdatesAsync(cancellationToken);
        return View("Index", BuildPageModel(result));
    }

    private ApplicationUpdatePageViewModel BuildPageModel(ApplicationUpdateCheckResult result)
    {
        return new ApplicationUpdatePageViewModel
        {
            Result = result,
            ChecksEnabled = _options.Enabled,
            RepositoryDisplayName = $"{_options.Owner}/{_options.Repository}"
        };
    }
}
