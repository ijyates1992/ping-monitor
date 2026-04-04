using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.ViewModels.About;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("about")]
public sealed class AboutController : Controller
{
    private readonly IApplicationMetadataProvider _applicationMetadataProvider;

    public AboutController(IApplicationMetadataProvider applicationMetadataProvider)
    {
        _applicationMetadataProvider = applicationMetadataProvider;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var metadata = _applicationMetadataProvider.GetSnapshot();
        var model = new AboutPageViewModel
        {
            ApplicationName = metadata.ApplicationName,
            Description = metadata.Description,
            Attribution = metadata.Attribution,
            Version = metadata.Version,
            Licence = metadata.Licence,
            RepositoryUrl = metadata.RepositoryUrl,
            PreviewNote = metadata.PreviewNote
        };

        return View("Index", model);
    }
}
