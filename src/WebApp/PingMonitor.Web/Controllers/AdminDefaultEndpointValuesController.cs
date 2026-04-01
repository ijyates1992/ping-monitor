using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/default-endpoint-values")]
public sealed class AdminDefaultEndpointValuesController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;

    public AdminDefaultEndpointValuesController(IApplicationSettingsService applicationSettingsService)
    {
        _applicationSettingsService = applicationSettingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        return View("Index", ToViewModel(settings, saved: false));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([FromForm] DefaultEndpointValuesPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var current = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var updated = await _applicationSettingsService.UpdateAsync(
            new UpdateApplicationSettingsCommand
            {
                SiteUrl = current.SiteUrl,
                DefaultPingIntervalSeconds = model.DefaultPingIntervalSeconds,
                DefaultRetryIntervalSeconds = model.DefaultRetryIntervalSeconds,
                DefaultTimeoutMs = model.DefaultTimeoutMs,
                DefaultFailureThreshold = model.DefaultFailureThreshold,
                DefaultRecoveryThreshold = model.DefaultRecoveryThreshold
            },
            cancellationToken);

        return View("Index", ToViewModel(updated, saved: true));
    }

    private static DefaultEndpointValuesPageViewModel ToViewModel(ApplicationSettingsDto settings, bool saved)
    {
        return new DefaultEndpointValuesPageViewModel
        {
            DefaultPingIntervalSeconds = settings.DefaultPingIntervalSeconds,
            DefaultRetryIntervalSeconds = settings.DefaultRetryIntervalSeconds,
            DefaultTimeoutMs = settings.DefaultTimeoutMs,
            DefaultFailureThreshold = settings.DefaultFailureThreshold,
            DefaultRecoveryThreshold = settings.DefaultRecoveryThreshold,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            Saved = saved
        };
    }
}
