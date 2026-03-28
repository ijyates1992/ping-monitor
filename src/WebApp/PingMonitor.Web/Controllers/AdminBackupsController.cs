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
    private readonly IConfigurationRestorePreviewService _restorePreviewService;
    private readonly IConfigurationRestoreService _restoreService;

    public AdminBackupsController(
        IConfigurationBackupService backupService,
        IConfigurationBackupQueryService backupQueryService,
        IConfigurationRestorePreviewService restorePreviewService,
        IConfigurationRestoreService restoreService)
    {
        _backupService = backupService;
        _backupQueryService = backupQueryService;
        _restorePreviewService = restorePreviewService;
        _restoreService = restoreService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var viewModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new RestorePreviewForm(), new RestoreApplyForm(), statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
        return View("Index", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateBackupPageForm form, CancellationToken cancellationToken)
    {
        var selectedSections = GetSelectedExportSections(form);
        if (selectedSections.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one configuration section to export.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(form, new RestorePreviewForm(), new RestoreApplyForm(), statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
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
            var invalidModel = await BuildPageViewModelAsync(form, new RestorePreviewForm(), new RestoreApplyForm(), statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }
    }

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewRestore([FromForm] RestorePreviewForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), form, new RestoreApplyForm(), statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            var preview = await _restorePreviewService.GetPreviewAsync(form.SelectedFileId, cancellationToken);
            var previewViewModel = ToPreviewViewModel(preview);
            var applyForm = new RestoreApplyForm
            {
                SelectedFileId = preview.FileId,
                IncludeAgents = preview.IncludedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal),
                IncludeEndpoints = preview.IncludedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal),
                IncludeAssignments = preview.IncludedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal),
                IncludeIdentity = false
            };

            var model = await BuildPageViewModelAsync(new CreateBackupPageForm(), form, applyForm, statusMessage: null, preview: previewViewModel, restoreSummary: null, cancellationToken);
            return View("Index", model);
        }
        catch (FileNotFoundException)
        {
            ModelState.AddModelError(string.Empty, "Backup file was not found.");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        var failedModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), form, new RestoreApplyForm(), statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
        return View("Index", failedModel);
    }

    [HttpPost("restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore([FromForm] RestoreApplyForm form, CancellationToken cancellationToken)
    {
        var selectedSections = GetSelectedRestoreSections(form);
        if (selectedSections.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one section for merge restore.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new RestorePreviewForm { SelectedFileId = form.SelectedFileId }, form, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            var response = await _restoreService.RestoreMergeAsync(
                new RestoreConfigurationRequest
                {
                    FileId = form.SelectedFileId,
                    SelectedSections = selectedSections
                },
                cancellationToken);

            var preview = await _restorePreviewService.GetPreviewAsync(form.SelectedFileId, cancellationToken);
            var model = await BuildPageViewModelAsync(
                new CreateBackupPageForm(),
                new RestorePreviewForm { SelectedFileId = form.SelectedFileId },
                form,
                statusMessage: "Merge restore completed.",
                preview: ToPreviewViewModel(preview),
                restoreSummary: ToRestoreSummaryViewModel(response),
                cancellationToken);
            return View("Index", model);
        }
        catch (FileNotFoundException)
        {
            ModelState.AddModelError(string.Empty, "Backup file was not found.");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        var failedModel = await BuildPageViewModelAsync(
            new CreateBackupPageForm(),
            new RestorePreviewForm { SelectedFileId = form.SelectedFileId },
            form,
            statusMessage: null,
            preview: null,
            restoreSummary: null,
            cancellationToken);
        return View("Index", failedModel);
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

    private async Task<AdminBackupsPageViewModel> BuildPageViewModelAsync(
        CreateBackupPageForm form,
        RestorePreviewForm restorePreviewForm,
        RestoreApplyForm restoreApplyForm,
        string? statusMessage,
        BackupRestorePreviewViewModel? preview,
        BackupRestoreSummaryViewModel? restoreSummary,
        CancellationToken cancellationToken)
    {
        var backups = await _backupQueryService.ListBackupsAsync(cancellationToken);
        return new AdminBackupsPageViewModel
        {
            Form = form,
            RestorePreviewForm = restorePreviewForm,
            RestoreApplyForm = restoreApplyForm,
            Preview = preview,
            RestoreSummary = restoreSummary,
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

    private static List<string> GetSelectedExportSections(CreateBackupPageForm form)
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

    private static List<string> GetSelectedRestoreSections(RestoreApplyForm form)
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

    private static BackupRestorePreviewViewModel ToPreviewViewModel(ConfigurationBackupPreview preview)
    {
        return new BackupRestorePreviewViewModel
        {
            FileId = preview.FileId,
            BackupName = preview.Metadata.BackupName,
            Notes = preview.Metadata.Notes,
            ExportedAtUtc = preview.Metadata.ExportedAtUtc,
            AppVersion = preview.Metadata.AppVersion,
            FormatVersion = preview.Metadata.FormatVersion,
            IncludedSections = preview.IncludedSections,
            Counts = new ConfigurationBackupSectionCountViewModel
            {
                Agents = preview.Counts.Agents,
                Endpoints = preview.Counts.Endpoints,
                Assignments = preview.Counts.Assignments,
                IdentityUsers = preview.Counts.IdentityUsers,
                IdentityRoles = preview.Counts.IdentityRoles,
                IdentityUserRoles = preview.Counts.IdentityUserRoles
            }
        };
    }

    private static BackupRestoreSummaryViewModel ToRestoreSummaryViewModel(RestoreConfigurationResponse response)
    {
        return new BackupRestoreSummaryViewModel
        {
            FileId = response.FileId,
            BackupName = response.BackupName,
            SelectedSections = response.SelectedSections,
            Sections = response.SectionResults.Select(x => new BackupRestoreSectionResultViewModel
            {
                Section = x.Section,
                InsertedCount = x.InsertedCount,
                UpdatedCount = x.UpdatedCount,
                SkippedCount = x.SkippedCount,
                ErrorCount = x.ErrorCount,
                Warnings = x.Warnings
            }).ToArray()
        };
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
