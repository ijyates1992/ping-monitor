using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin")]
public sealed class AdminController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View("Index");
    }
}
