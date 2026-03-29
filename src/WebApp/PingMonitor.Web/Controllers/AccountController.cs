using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Security;

namespace PingMonitor.Web.Controllers;

[AllowAnonymous]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityAuthLogService _securityAuthLogService;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ISecurityAuthLogService securityAuthLogService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _securityAuthLogService = securityAuthLogService;
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        return View("Login", new LoginViewModel { ReturnUrl = returnUrl ?? "/status" });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginViewModel model)
    {
        var normalizedUserName = model.UserName?.Trim() ?? string.Empty;

        if (!ModelState.IsValid)
        {
            return View("Login", model);
        }

        var user = await _userManager.FindByNameAsync(normalizedUserName) ??
                   await _userManager.FindByEmailAsync(normalizedUserName);

        var signInIdentifier = user?.UserName ?? normalizedUserName;
        var result = await _signInManager.PasswordSignInAsync(signInIdentifier, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await _securityAuthLogService.LogUserAttemptAsync(
                new UserAuthLogWriteRequest
                {
                    SubjectIdentifier = normalizedUserName,
                    UserId = user?.Id,
                    SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Success = false,
                    FailureReason = BuildFailureReason(result, user)
                },
                HttpContext.RequestAborted);

            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View("Login", model);
        }

        await _securityAuthLogService.LogUserAttemptAsync(
            new UserAuthLogWriteRequest
            {
                SubjectIdentifier = normalizedUserName,
                UserId = user?.Id,
                SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Success = true,
                FailureReason = null
            },
            HttpContext.RequestAborted);

        return LocalRedirect(string.IsNullOrWhiteSpace(model.ReturnUrl) ? "/status" : model.ReturnUrl);
    }

    private static string BuildFailureReason(Microsoft.AspNetCore.Identity.SignInResult result, ApplicationUser? user)
    {
        if (result.IsLockedOut)
        {
            return "account_locked";
        }

        if (result.IsNotAllowed)
        {
            return "login_not_allowed";
        }

        if (result.RequiresTwoFactor)
        {
            return "two_factor_required";
        }

        if (user is null)
        {
            return "unknown_user";
        }

        return "invalid_password";
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public sealed class LoginViewModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string ReturnUrl { get; set; } = "/status";
    }
}
