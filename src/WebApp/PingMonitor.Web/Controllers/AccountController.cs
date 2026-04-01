using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Security;
using System.Text;
using System.Text.Json;

namespace PingMonitor.Web.Controllers;

[AllowAnonymous]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityAuthLogService _securityAuthLogService;
    private readonly ISecurityEnforcementService _securityEnforcementService;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ISecurityAuthLogService securityAuthLogService,
        ISecurityEnforcementService securityEnforcementService,
        IEventLogService eventLogService,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _securityAuthLogService = securityAuthLogService;
        _securityEnforcementService = securityEnforcementService;
        _eventLogService = eventLogService;
        _logger = logger;
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

        // Enforcement order:
        // 1) source IP block check
        // 2) user lockout check (if user identified)
        // 3) password validation
        // 4) failed-attempt threshold evaluation for IP and user lockout
        var sourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ipBlockStatus = await _securityEnforcementService.GetIpBlockStatusAsync(SecurityAuthType.User, sourceIpAddress, HttpContext.RequestAborted);
        if (ipBlockStatus.IsBlocked)
        {
            await _securityAuthLogService.LogUserAttemptAsync(
                new UserAuthLogWriteRequest
                {
                    SubjectIdentifier = normalizedUserName,
                    UserId = user?.Id,
                    SourceIpAddress = sourceIpAddress,
                    Success = false,
                    FailureReason = ipBlockStatus.FailureReason
                },
                HttpContext.RequestAborted);

            ModelState.AddModelError(string.Empty, "Authentication attempt denied.");
            return View("Login", model);
        }

        if (user is not null)
        {
            var lockoutStatus = await _securityEnforcementService.GetUserLockoutStatusAsync(user, HttpContext.RequestAborted);
            if (lockoutStatus.IsLockedOut)
            {
                await _securityAuthLogService.LogUserAttemptAsync(
                    new UserAuthLogWriteRequest
                    {
                        SubjectIdentifier = normalizedUserName,
                        UserId = user.Id,
                        SourceIpAddress = sourceIpAddress,
                        Success = false,
                        FailureReason = "account_temporarily_locked"
                    },
                    HttpContext.RequestAborted);

                ModelState.AddModelError(string.Empty, "Your account is temporarily locked.");
                return View("Login", model);
            }
        }

        var signInIdentifier = user?.UserName ?? normalizedUserName;
        var result = await _signInManager.PasswordSignInAsync(signInIdentifier, model.Password, isPersistent: false, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            var failureReason = BuildFailureReason(result, user);
            await _securityAuthLogService.LogUserAttemptAsync(
                new UserAuthLogWriteRequest
                {
                    SubjectIdentifier = normalizedUserName,
                    UserId = user?.Id,
                    SourceIpAddress = sourceIpAddress,
                    Success = false,
                    FailureReason = failureReason
                },
                HttpContext.RequestAborted);

            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.User, sourceIpAddress, HttpContext.RequestAborted);
            if (user is not null)
            {
                await _securityEnforcementService.EvaluateFailedUserLockoutAsync(user, HttpContext.RequestAborted);
            }

            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View("Login", model);
        }

        await _securityAuthLogService.LogUserAttemptAsync(
            new UserAuthLogWriteRequest
            {
                SubjectIdentifier = normalizedUserName,
                UserId = user?.Id,
                SourceIpAddress = sourceIpAddress,
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

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string? userId, [FromQuery] string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return View("VerifyEmail", new VerifyEmailViewModel
            {
                Success = false,
                Message = "Verification link is invalid. Request a new verification email from your profile."
            });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return View("VerifyEmail", new VerifyEmailViewModel
            {
                Success = false,
                Message = "Verification link is invalid. Request a new verification email from your profile."
            });
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch
        {
            return View("VerifyEmail", new VerifyEmailViewModel
            {
                Success = false,
                Message = "Verification token is malformed. Request a new verification email from your profile."
            });
        }

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            await _eventLogService.WriteAsync(new EventLogWriteRequest
            {
                Category = EventCategory.Security,
                EventType = "email_verification_confirm_failed",
                Severity = EventSeverity.Warning,
                Message = $"Email verification failed for user {user.Id}.",
                DetailsJson = JsonSerializer.Serialize(new { errors = result.Errors.Select(x => x.Code).ToArray() })
            }, cancellationToken);

            return View("VerifyEmail", new VerifyEmailViewModel
            {
                Success = false,
                Message = "Verification failed or expired. Request a new verification email from your profile."
            });
        }

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = "email_verification_confirmed",
            Severity = EventSeverity.Info,
            Message = $"Email verified for user {user.Id}."
        }, cancellationToken);
        _logger.LogInformation("Email verification completed for user {UserId}.", user.Id);

        return View("VerifyEmail", new VerifyEmailViewModel
        {
            Success = true,
            Message = "Email verified successfully. SMTP notifications are now eligible for your account."
        });
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

    public sealed class VerifyEmailViewModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
