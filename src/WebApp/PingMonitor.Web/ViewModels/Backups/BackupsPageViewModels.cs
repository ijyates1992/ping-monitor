using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using PingMonitor.Web.Services.Backups;

namespace PingMonitor.Web.ViewModels.Backups;

public sealed class CreateBackupPageForm
{
    [Required(ErrorMessage = "Backup name is required.")]
    [Display(Name = "Backup name")]
    public string BackupName { get; set; } = string.Empty;

    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Include agents")]
    public bool IncludeAgents { get; set; } = true;

    [Display(Name = "Include endpoints")]
    public bool IncludeEndpoints { get; set; } = true;

    [Display(Name = "Include assignments")]
    public bool IncludeAssignments { get; set; } = true;

    [Display(Name = "Include identity")]
    public bool IncludeIdentity { get; set; }
}

public sealed class BackupRowViewModel
{
    public required string FileName { get; init; }
    public required string FileId { get; init; }
    public string BackupName { get; init; } = "(unknown)";
    public string? AppVersion { get; init; }
    public string BackupSource { get; init; } = ConfigurationBackupSources.Manual;
    public DateTimeOffset? ExportedAtUtc { get; init; }
    public DateTimeOffset FileCreatedAtUtc { get; init; }
    public long FileSizeBytes { get; init; }
    public string IncludedSectionsDisplay { get; init; } = string.Empty;
    public string? NotesSummary { get; init; }
}

public sealed class AdminBackupsPageViewModel
{
    public required CreateBackupPageForm Form { get; init; }
    public required UploadBackupPageForm UploadForm { get; init; }
    public required IReadOnlyList<BackupRowViewModel> Backups { get; init; } = [];
    public string? StatusMessage { get; init; }
    public RestorePreviewForm RestorePreviewForm { get; init; } = new();
    public RestoreApplyForm RestoreApplyForm { get; init; } = new();
    public BackupDeleteSingleForm DeleteSingleForm { get; init; } = new();
    public BackupDeleteBulkForm DeleteBulkForm { get; init; } = new();
    public string NameFilter { get; init; } = string.Empty;
    public string SourceFilter { get; init; } = string.Empty;
    public BackupRestorePreviewViewModel? Preview { get; init; }
    public BackupRestoreSummaryViewModel? RestoreSummary { get; init; }
}

public sealed class BackupDeleteSingleForm
{
    [Required]
    public string FileId { get; set; } = string.Empty;

    public bool ConfirmDelete { get; set; }
}

public sealed class BackupDeleteBulkForm
{
    public List<string> SelectedFileIds { get; set; } = [];

    public string? ConfirmationText { get; set; }
}

public sealed class UploadBackupPageForm
{
    [Required(ErrorMessage = "Backup file is required.")]
    [Display(Name = "Backup file (.json)")]
    public IFormFile? BackupFile { get; set; }
}

public sealed class RestorePreviewForm
{
    [Required(ErrorMessage = "Backup file is required.")]
    [Display(Name = "Backup file")]
    public string SelectedFileId { get; set; } = string.Empty;
}

public sealed class RestoreApplyForm
{
    [Required(ErrorMessage = "Backup file is required.")]
    public string SelectedFileId { get; set; } = string.Empty;

    [Display(Name = "Restore agents")]
    public bool IncludeAgents { get; set; } = true;

    [Display(Name = "Restore endpoints")]
    public bool IncludeEndpoints { get; set; } = true;

    [Display(Name = "Restore assignments")]
    public bool IncludeAssignments { get; set; } = true;

    [Display(Name = "Restore identity")]
    public bool IncludeIdentity { get; set; }

    [Display(Name = "Restore mode")]
    public string RestoreMode { get; set; } = ConfigurationRestoreModes.Merge;

    [Display(Name = "Typed confirmation")]
    public string? ConfirmationText { get; set; }
}

public sealed class BackupRestorePreviewViewModel
{
    public string FileId { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public int FormatVersion { get; init; }
    public IReadOnlyList<string> IncludedSections { get; init; } = [];
    public ConfigurationBackupSectionCountViewModel Counts { get; init; } = new();
}

public sealed class ConfigurationBackupSectionCountViewModel
{
    public int Agents { get; init; }
    public int Endpoints { get; init; }
    public int Assignments { get; init; }
    public int IdentityUsers { get; init; }
    public int IdentityRoles { get; init; }
    public int IdentityUserRoles { get; init; }
}

public sealed class BackupRestoreSectionResultViewModel
{
    public string Section { get; init; } = string.Empty;
    public int DeletedCount { get; init; }
    public int InsertedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int SkippedCount { get; init; }
    public int ErrorCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class BackupRestoreSummaryViewModel
{
    public string FileId { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public string RestoreMode { get; init; } = ConfigurationRestoreModes.Merge;
    public IReadOnlyList<string> SelectedSections { get; init; } = [];
    public IReadOnlyList<BackupRestoreSectionResultViewModel> Sections { get; init; } = [];
}
