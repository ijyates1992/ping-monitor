using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.ViewModels.StartupGate;

namespace PingMonitor.Web.Controllers;

[Route("startup-gate")]
public sealed class StartupGateController : Controller
{
    private readonly IStartupGateService _startupGateService;
    private readonly IStartupDatabaseConfigurationStore _configurationStore;
    private readonly IStartupSchemaService _schemaService;
    private readonly IStartupAdminBootstrapService _adminBootstrapService;
    private readonly StartupGateOptions _options;
    private readonly ILogger<StartupGateController> _logger;

    public StartupGateController(
        IStartupGateService startupGateService,
        IStartupDatabaseConfigurationStore configurationStore,
        IStartupSchemaService schemaService,
        IStartupAdminBootstrapService adminBootstrapService,
        IOptions<StartupGateOptions> options,
        ILogger<StartupGateController> logger)
    {
        _startupGateService = startupGateService;
        _configurationStore = configurationStore;
        _schemaService = schemaService;
        _adminBootstrapService = adminBootstrapService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", BuildViewModel(status, null, null));
    }

    [HttpPost("database")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDatabaseConfiguration([FromForm] StartupDatabaseConfigurationForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildViewModel(status, form, null, errorMessage: "Database configuration could not be saved."));
        }

        _logger.LogInformation("Startup gate database configuration save attempt for {Host}:{Port}/{DatabaseName}.", form.Host, form.Port, form.DatabaseName);

        try
        {
            await _configurationStore.SaveAsync(new StartupDatabaseConfigurationInput
            {
                Host = form.Host,
                Port = form.Port,
                DatabaseName = form.DatabaseName,
                Username = form.Username,
                Password = form.Password
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate database configuration save failed.");
            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", BuildViewModel(status, form, null, errorMessage: $"Database configuration save failed: {exception.Message}"));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", BuildViewModel(status, null, null, statusMessage: "Database configuration saved. Password values are not shown after saving."));
    }

    [HttpPost("schema/apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplySchema(CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        try
        {
            await _schemaService.ApplySchemaAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate schema apply failed.");
            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", BuildViewModel(status, null, null, errorMessage: $"Schema apply failed: {exception.Message}"));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", BuildViewModel(status, null, null, statusMessage: "Schema apply completed."));
    }

    [HttpPost("admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInitialAdmin([FromForm] StartupAdminBootstrapForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildViewModel(status, null, form, errorMessage: "Initial admin could not be created."));
        }

        var result = await _adminBootstrapService.CreateInitialAdminAsync(form, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", BuildViewModel(status, null, form, errorMessage: "Initial admin could not be created."));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", BuildViewModel(status, null, null, statusMessage: "Initial admin created."));
    }

    private StartupGatePageViewModel BuildViewModel(
        StartupGateStatus status,
        StartupDatabaseConfigurationForm? databaseForm,
        StartupAdminBootstrapForm? adminForm,
        string? statusMessage = null,
        string? errorMessage = null)
    {
        var existingConfig = status.DatabaseConfiguration;
        return new StartupGatePageViewModel
        {
            Status = status,
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage,
            DatabaseForm = databaseForm ?? new StartupDatabaseConfigurationForm
            {
                Host = existingConfig?.Host ?? string.Empty,
                Port = existingConfig?.Port > 0 ? existingConfig.Port : _options.DefaultMySqlPort,
                DatabaseName = existingConfig?.DatabaseName ?? string.Empty,
                Username = existingConfig?.Username ?? string.Empty
            },
            AdminForm = adminForm ?? new StartupAdminBootstrapForm()
        };
    }
}
