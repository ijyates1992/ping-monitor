using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin")]
public sealed class AdminController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;

    public AdminController(IApplicationSettingsService applicationSettingsService)
    {
        _applicationSettingsService = applicationSettingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        return View("Index", ToViewModel(settings, saved: false));
    }

    [HttpPost("application-features")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApplicationFeatures(
        [FromForm] ApplicationFeatureSettingsPageViewModel model,
        CancellationToken cancellationToken)
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
                DefaultPingIntervalSeconds = current.DefaultPingIntervalSeconds,
                DefaultRetryIntervalSeconds = current.DefaultRetryIntervalSeconds,
                DefaultTimeoutMs = current.DefaultTimeoutMs,
                DefaultFailureThreshold = current.DefaultFailureThreshold,
                DefaultRecoveryThreshold = current.DefaultRecoveryThreshold,
                DegradedEvaluationEnabled = current.DegradedEvaluationEnabled,
                DegradedBaselineLookbackMinutes = current.DegradedBaselineLookbackMinutes,
                DegradedCurrentWindowMinutes = current.DegradedCurrentWindowMinutes,
                DegradedPacketLossIncreasePercentagePoints = current.DegradedPacketLossIncreasePercentagePoints,
                DegradedRttIncreasePercent = current.DegradedRttIncreasePercent,
                DegradedJitterIncreasePercent = current.DegradedJitterIncreasePercent,
                DegradedMinimumSamples = current.DegradedMinimumSamples,
                NetworkDiagramsEnabled = model.NetworkDiagramsEnabled
            },
            cancellationToken);

        return View("Index", ToViewModel(updated, saved: true));
    }

    private static ApplicationFeatureSettingsPageViewModel ToViewModel(ApplicationSettingsDto settings, bool saved)
    {
        return new ApplicationFeatureSettingsPageViewModel
        {
            NetworkDiagramsEnabled = settings.NetworkDiagramsEnabled,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            Saved = saved
        };
    }
}
