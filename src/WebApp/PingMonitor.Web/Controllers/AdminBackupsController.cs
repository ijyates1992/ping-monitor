using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Backups;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Backups;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/backups")]
public sealed class AdminBackupsController : Controller
{
    private readonly IConfigurationBackupService _backupService;
    private readonly IConfigurationBackupQueryService _backupQueryService;

    public AdminBackupsController(
        IConfigurationBackupService backupService,
        IConfigurationBackupQueryService backupQueryService)
    {
        _backupService = backupService;
        _backupQueryService = backupQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var viewModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), statusMessage: null, cancellationToken);
        return View("Index", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateBackupPageForm form, CancellationToken cancellationToken)
    {
        var selectedSections = GetSelectedSections(form);
        if (selectedSections.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one configuration section to export.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(form, statusMessage: null, cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            await _backupService.CreateBackupAsync(
                new CreateConfigurationBackupRequest
                {
                    BackupName = form.BackupName,
                    Notes = form.Notes,
                    SelectedSections = selectedSections,
                    ExportedBy = User?.Identity?.Name
                },
                cancellationToken);

            return RedirectToAction(nameof(Index), new { status = "Backup created successfully." });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildPageViewModelAsync(form, statusMessage: null, cancellationToken);
            return View("Index", invalidModel);
        }
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string id)
    {
        try
        {
            var fullPath = _backupQueryService.ResolveDownloadPath(id);
            var fileName = Path.GetFileName(fullPath);
            return PhysicalFile(fullPath, "application/json", fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task<AdminBackupsPageViewModel> BuildPageViewModelAsync(CreateBackupPageForm form, string? statusMessage, CancellationToken cancellationToken)
    {
        var backups = await _backupQueryService.ListBackupsAsync(cancellationToken);
        return new AdminBackupsPageViewModel
        {
            Form = form,
            StatusMessage = statusMessage ?? Request.Query["status"],
            Backups = backups
                .Select(item => new BackupRowViewModel
                {
                    FileName = item.FileName,
                    FileId = item.FileId,
                    BackupName = string.IsNullOrWhiteSpace(item.BackupName) ? "(unknown)" : item.BackupName,
                    AppVersion = item.AppVersion,
                    ExportedAtUtc = item.ExportedAtUtc,
                    FileCreatedAtUtc = item.FileCreatedAtUtc,
                    IncludedSectionsDisplay = item.IncludedSections.Count == 0
                        ? "(not detected)"
                        : string.Join(", ", item.IncludedSections.Select(ToDisplaySectionName)),
                    NotesSummary = item.NotesSummary
                })
                .ToArray()
        };
    }

    private static List<string> GetSelectedSections(CreateBackupPageForm form)
    {
        var sections = new List<string>();
        if (form.IncludeAgents)
        {
            sections.Add(ConfigurationBackupSections.Agents);
        }

        if (form.IncludeEndpoints)
        {
            sections.Add(ConfigurationBackupSections.Endpoints);
        }

        if (form.IncludeAssignments)
        {
            sections.Add(ConfigurationBackupSections.Assignments);
        }

        if (form.IncludeIdentity)
        {
            sections.Add(ConfigurationBackupSections.Identity);
        }

        return sections;
    }

    private static string ToDisplaySectionName(string section)
    {
        return section switch
        {
            ConfigurationBackupSections.Agents => "Agents",
            ConfigurationBackupSections.Endpoints => "Endpoints",
            ConfigurationBackupSections.Assignments => "Assignments",
            ConfigurationBackupSections.Identity => "Identity",
            _ => section
        };
    }
}
