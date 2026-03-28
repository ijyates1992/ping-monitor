using System.ComponentModel.DataAnnotations;

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
    public DateTimeOffset? ExportedAtUtc { get; init; }
    public DateTimeOffset FileCreatedAtUtc { get; init; }
    public string IncludedSectionsDisplay { get; init; } = string.Empty;
    public string? NotesSummary { get; init; }
}

public sealed class AdminBackupsPageViewModel
{
    public required CreateBackupPageForm Form { get; init; }
    public required IReadOnlyList<BackupRowViewModel> Backups { get; init; } = [];
    public string? StatusMessage { get; init; }
}
