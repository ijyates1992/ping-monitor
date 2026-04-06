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
    private readonly IConfigurationBackupUploadService _backupUploadService;
    private readonly IConfigurationBackupManagementService _backupManagementService;
    private readonly IConfigurationRestorePreviewService _restorePreviewService;
    private readonly IConfigurationRestoreService _restoreService;

    public AdminBackupsController(
        IConfigurationBackupService backupService,
        IConfigurationBackupQueryService backupQueryService,
        IConfigurationBackupUploadService backupUploadService,
        IConfigurationBackupManagementService backupManagementService,
        IConfigurationRestorePreviewService restorePreviewService,
        IConfigurationRestoreService restoreService)
    {
        _backupService = backupService;
        _backupQueryService = backupQueryService;
        _backupUploadService = backupUploadService;
        _backupManagementService = backupManagementService;
        _restorePreviewService = restorePreviewService;
        _restoreService = restoreService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? name, [FromQuery] string? source, CancellationToken cancellationToken)
    {
        var viewModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new UploadBackupPageForm(), new RestorePreviewForm(), new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), name, source, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
        return View("Index", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm, Bind(Prefix = "Form")] CreateBackupPageForm form, CancellationToken cancellationToken)
    {
        var selectedSections = GetSelectedExportSections(form);
        if (selectedSections.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one configuration section to export.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(form, new UploadBackupPageForm(), new RestorePreviewForm(), new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
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
                    ExportedBy = User?.Identity?.Name,
                    BackupSource = ConfigurationBackupSources.Manual
                },
                cancellationToken);

            return RedirectToAction(nameof(Index), new { status = "Backup created successfully." });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildPageViewModelAsync(form, new UploadBackupPageForm(), new RestorePreviewForm(), new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload([FromForm, Bind(Prefix = "UploadForm")] UploadBackupPageForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), form, new RestorePreviewForm(), new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            var response = await _backupUploadService.UploadAsync(
                new UploadConfigurationBackupRequest
                {
                    File = form.BackupFile,
                    UploadedBy = User?.Identity?.Name
                },
                cancellationToken);

            return RedirectToAction(nameof(Index), new { status = $"Backup upload accepted: {response.FileName}" });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), form, new RestorePreviewForm(), new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }
    }

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewRestore([FromForm, Bind(Prefix = "RestorePreviewForm")] RestorePreviewForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new UploadBackupPageForm(), form, new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
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
                IncludeGroups = preview.IncludedSections.Contains(ConfigurationBackupSections.Groups, StringComparer.Ordinal),
                IncludeDependencies = preview.IncludedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal),
                IncludeAssignments = preview.IncludedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal),
                IncludeSecuritySettings = preview.IncludedSections.Contains(ConfigurationBackupSections.SecuritySettings, StringComparer.Ordinal),
                IncludeNotificationSettings = preview.IncludedSections.Contains(ConfigurationBackupSections.NotificationSettings, StringComparer.Ordinal),
                IncludeUserNotificationSettings = preview.IncludedSections.Contains(ConfigurationBackupSections.UserNotificationSettings, StringComparer.Ordinal),
                IncludeIdentity = false,
                RestoreMode = ConfigurationRestoreModes.Merge
            };

            var model = await BuildPageViewModelAsync(new CreateBackupPageForm(), new UploadBackupPageForm(), form, applyForm, new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: previewViewModel, restoreSummary: null, cancellationToken);
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

        var failedModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new UploadBackupPageForm(), form, new RestoreApplyForm(), new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
        return View("Index", failedModel);
    }

    [HttpPost("restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore([FromForm, Bind(Prefix = "RestoreApplyForm")] RestoreApplyForm form, CancellationToken cancellationToken)
    {
        var selectedSections = GetSelectedRestoreSections(form);
        if (selectedSections.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one section for restore.");
        }

        if (string.Equals(form.RestoreMode, ConfigurationRestoreModes.Replace, StringComparison.Ordinal)
            && !string.Equals(form.ConfirmationText, ConfigurationRestoreModes.ReplaceConfirmationText, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, $"Replace mode requires typed confirmation text '{ConfigurationRestoreModes.ReplaceConfirmationText}'.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildPageViewModelAsync(new CreateBackupPageForm(), new UploadBackupPageForm(), new RestorePreviewForm { SelectedFileId = form.SelectedFileId }, form, new BackupDeleteSingleForm(), new BackupDeleteBulkForm(), null, null, statusMessage: null, preview: null, restoreSummary: null, cancellationToken);
            return View("Index", invalidModel);
        }

        try
        {
            var response = await _restoreService.RestoreAsync(
                new RestoreConfigurationRequest
                {
                    FileId = form.SelectedFileId,
                    SelectedSections = selectedSections,
                    RestoreMode = string.IsNullOrWhiteSpace(form.RestoreMode) ? ConfigurationRestoreModes.Merge : form.RestoreMode,
                    ConfirmationText = form.ConfirmationText
                },
                cancellationToken);

            var preview = await _restorePreviewService.GetPreviewAsync(form.SelectedFileId, cancellationToken);
            var model = await BuildPageViewModelAsync(
                new CreateBackupPageForm(),
                new UploadBackupPageForm(),
                new RestorePreviewForm { SelectedFileId = form.SelectedFileId },
                form,
                new BackupDeleteSingleForm(),
                new BackupDeleteBulkForm(),
                null,
                null,
                statusMessage: $"{response.RestoreMode.ToUpperInvariant()} restore completed.",
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
            new UploadBackupPageForm(),
            new RestorePreviewForm { SelectedFileId = form.SelectedFileId },
            form,
            new BackupDeleteSingleForm(),
            new BackupDeleteBulkForm(),
            null,
            null,
            statusMessage: null,
            preview: null,
            restoreSummary: null,
            cancellationToken);
        return View("Index", failedModel);
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSingle([FromForm, Bind(Prefix = "DeleteSingleForm")] BackupDeleteSingleForm form, CancellationToken cancellationToken)
    {
        var result = await _backupManagementService.DeleteAsync(
            new DeleteConfigurationBackupRequest
            {
                FileId = form.FileId,
                ConfirmationText = form.ConfirmationText
            },
            cancellationToken);

        var status = result.Deleted ? "Backup deleted successfully." : result.Message;
        return RedirectToAction(nameof(Index), new { status });
    }

    [HttpPost("delete-bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBulk([FromForm, Bind(Prefix = "DeleteBulkForm")] BackupDeleteBulkForm form, CancellationToken cancellationToken)
    {
        var result = await _backupManagementService.BulkDeleteAsync(
            new BulkDeleteConfigurationBackupsRequest
            {
                FileIds = form.SelectedFileIds,
                ConfirmationText = form.ConfirmationText
            },
            cancellationToken);

        var status = $"Bulk delete complete. Requested={result.RequestedCount}, Deleted={result.DeletedCount}, Failed={result.FailedCount}.";
        return RedirectToAction(nameof(Index), new { status });
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
        UploadBackupPageForm uploadForm,
        RestorePreviewForm restorePreviewForm,
        RestoreApplyForm restoreApplyForm,
        BackupDeleteSingleForm deleteSingleForm,
        BackupDeleteBulkForm deleteBulkForm,
        string? nameFilter,
        string? sourceFilter,
        string? statusMessage,
        BackupRestorePreviewViewModel? preview,
        BackupRestoreSummaryViewModel? restoreSummary,
        CancellationToken cancellationToken)
    {
        var backups = await _backupQueryService.ListBackupsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            backups = backups.Where(x => (x.BackupName ?? x.FileName).Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(sourceFilter))
        {
            backups = backups.Where(x => string.Equals(x.BackupSource, sourceFilter, StringComparison.Ordinal)).ToArray();
        }

        return new AdminBackupsPageViewModel
        {
            Form = form,
            UploadForm = uploadForm,
            RestorePreviewForm = restorePreviewForm,
            RestoreApplyForm = restoreApplyForm,
            DeleteSingleForm = deleteSingleForm,
            DeleteBulkForm = deleteBulkForm,
            NameFilter = nameFilter?.Trim() ?? string.Empty,
            SourceFilter = sourceFilter?.Trim() ?? string.Empty,
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
                    BackupSource = item.BackupSource,
                    ExportedAtUtc = item.ExportedAtUtc,
                    FileCreatedAtUtc = item.FileCreatedAtUtc,
                    FileSizeBytes = item.FileSizeBytes,
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

        if (form.IncludeGroups)
        {
            sections.Add(ConfigurationBackupSections.Groups);
        }

        if (form.IncludeDependencies)
        {
            sections.Add(ConfigurationBackupSections.Dependencies);
        }

        if (form.IncludeSecuritySettings)
        {
            sections.Add(ConfigurationBackupSections.SecuritySettings);
        }

        if (form.IncludeNotificationSettings)
        {
            sections.Add(ConfigurationBackupSections.NotificationSettings);
        }

        if (form.IncludeUserNotificationSettings)
        {
            sections.Add(ConfigurationBackupSections.UserNotificationSettings);
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

        if (form.IncludeGroups)
        {
            sections.Add(ConfigurationBackupSections.Groups);
        }

        if (form.IncludeDependencies)
        {
            sections.Add(ConfigurationBackupSections.Dependencies);
        }

        if (form.IncludeSecuritySettings)
        {
            sections.Add(ConfigurationBackupSections.SecuritySettings);
        }

        if (form.IncludeNotificationSettings)
        {
            sections.Add(ConfigurationBackupSections.NotificationSettings);
        }

        if (form.IncludeUserNotificationSettings)
        {
            sections.Add(ConfigurationBackupSections.UserNotificationSettings);
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
                Groups = preview.Counts.Groups,
                GroupEndpointMemberships = preview.Counts.GroupEndpointMemberships,
                Dependencies = preview.Counts.Dependencies,
                Assignments = preview.Counts.Assignments,
                SecuritySettings = preview.Counts.SecuritySettings,
                NotificationSettings = preview.Counts.NotificationSettings,
                UserNotificationSettings = preview.Counts.UserNotificationSettings,
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
            RestoreMode = response.RestoreMode,
            SelectedSections = response.SelectedSections,
            Sections = response.SectionResults.Select(x => new BackupRestoreSectionResultViewModel
            {
                Section = x.Section,
                DeletedCount = x.DeletedCount,
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
            ConfigurationBackupSections.Groups => "Groups",
            ConfigurationBackupSections.Dependencies => "Dependencies",
            ConfigurationBackupSections.Assignments => "Assignments",
            ConfigurationBackupSections.SecuritySettings => "Security settings",
            ConfigurationBackupSections.NotificationSettings => "Notification infrastructure settings",
            ConfigurationBackupSections.UserNotificationSettings => "User notification settings",
            ConfigurationBackupSections.Identity => "Identity",
            _ => section
        };
    }
}
