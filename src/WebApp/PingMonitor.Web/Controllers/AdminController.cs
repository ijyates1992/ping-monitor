using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([FromForm] AdminSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        ValidateAbsoluteSiteUrl(model);

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var updated = await _applicationSettingsService.UpdateAsync(
            new UpdateApplicationSettingsCommand
            {
                SiteUrl = model.SiteUrl,
                DefaultPingIntervalSeconds = model.DefaultPingIntervalSeconds,
                DefaultRetryIntervalSeconds = model.DefaultRetryIntervalSeconds,
                DefaultTimeoutMs = model.DefaultTimeoutMs,
                DefaultFailureThreshold = model.DefaultFailureThreshold,
                DefaultRecoveryThreshold = model.DefaultRecoveryThreshold
            },
            cancellationToken);

        return View("Index", ToViewModel(updated, saved: true));
    }

    private void ValidateAbsoluteSiteUrl(AdminSettingsPageViewModel model)
    {
        if (!Uri.TryCreate(model.SiteUrl?.Trim(), UriKind.Absolute, out _))
        {
            ModelState.AddModelError(nameof(AdminSettingsPageViewModel.SiteUrl), "Site URL must be a valid absolute URL.");
        }
    }

    private static AdminSettingsPageViewModel ToViewModel(ApplicationSettingsDto settings, bool saved)
    {
        return new AdminSettingsPageViewModel
        {
            SiteUrl = settings.SiteUrl,
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
