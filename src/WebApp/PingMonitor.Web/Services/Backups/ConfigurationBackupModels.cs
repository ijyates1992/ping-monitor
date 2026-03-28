using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace PingMonitor.Web.Services.Backups;

public static class ConfigurationBackupSections
{
    public const string Agents = "agents";
    public const string Endpoints = "endpoints";
    public const string Assignments = "assignments";
    public const string Identity = "identity";

    public static readonly string[] All = [Agents, Endpoints, Assignments, Identity];
}

public static class ConfigurationRestoreModes
{
    public const string Merge = "merge";
    public const string Replace = "replace";
    public const string ReplaceConfirmationText = "REPLACE";

    public static readonly string[] All = [Merge, Replace];
}

public static class ConfigurationBackupSources
{
    public const string Manual = "manual";
    public const string Uploaded = "uploaded";
    public const string AutomaticScheduled = "automatic_scheduled";
    public const string AutomaticConfigChange = "automatic_config_change";

    public static readonly string[] All = [Manual, Uploaded, AutomaticScheduled, AutomaticConfigChange];

    public static bool IsAutomatic(string source)
    {
        return string.Equals(source, AutomaticScheduled, StringComparison.Ordinal)
            || string.Equals(source, AutomaticConfigChange, StringComparison.Ordinal);
    }
}

public static class BackupDeleteModes
{
    public const string Single = "single";
    public const string Bulk = "bulk";
    public const string SingleConfirmationText = "DELETE";
    public const string BulkConfirmationText = "DELETE";
}

public sealed class ConfigurationBackupMetadata
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public string AppVersion { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
    public string? ExportedBy { get; init; }
    public string MachineName { get; init; } = string.Empty;
    public string BackupSource { get; init; } = ConfigurationBackupSources.Manual;
}

public sealed class ConfigurationBackupDocument
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; init; } = string.Empty;

    [JsonPropertyOrder(3)]
    [JsonPropertyName("backupName")]
    public string BackupName { get; init; } = string.Empty;

    [JsonPropertyOrder(4)]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName("exportedAtUtc")]
    public DateTimeOffset ExportedAtUtc { get; init; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName("exportedBy")]
    public string? ExportedBy { get; init; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("machineName")]
    public string MachineName { get; init; } = string.Empty;

    [JsonPropertyOrder(8)]
    [JsonPropertyName("backupSource")]
    public string BackupSource { get; init; } = ConfigurationBackupSources.Manual;

    [JsonPropertyOrder(9)]
    [JsonPropertyName("sections")]
    public ConfigurationBackupSectionData Sections { get; init; } = new();

    public static ConfigurationBackupDocument Create(ConfigurationBackupMetadata metadata, ConfigurationBackupSectionData sections)
    {
        return new ConfigurationBackupDocument
        {
            FormatVersion = metadata.FormatVersion,
            AppVersion = metadata.AppVersion,
            BackupName = metadata.BackupName,
            Notes = metadata.Notes,
            ExportedAtUtc = metadata.ExportedAtUtc,
            ExportedBy = metadata.ExportedBy,
            MachineName = metadata.MachineName,
            BackupSource = metadata.BackupSource,
            Sections = sections
        };
    }
}

public sealed class ConfigurationBackupSectionData
{
    [JsonPropertyName("agents")]
    public IReadOnlyList<BackupAgentRecord>? Agents { get; init; }

    [JsonPropertyName("endpoints")]
    public IReadOnlyList<BackupEndpointRecord>? Endpoints { get; init; }

    [JsonPropertyName("assignments")]
    public IReadOnlyList<BackupAssignmentRecord>? Assignments { get; init; }

    [JsonPropertyName("identity")]
    public BackupIdentitySection? Identity { get; init; }
}

public sealed class BackupAgentRecord
{
    public string AgentId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Site { get; init; }
    public bool Enabled { get; init; }
    public string? AgentVersion { get; init; }
    public string? Platform { get; init; }
    public string? MachineName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class BackupEndpointRecord
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> DependsOnEndpointIds { get; init; } = [];
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class BackupAssignmentRecord
{
    public string AssignmentId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string CheckType { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class BackupIdentitySection
{
    public IReadOnlyList<BackupIdentityUserRecord> Users { get; init; } = [];
    public IReadOnlyList<BackupIdentityRoleRecord> Roles { get; init; } = [];
    public IReadOnlyList<BackupIdentityUserRoleRecord> UserRoles { get; init; } = [];
}

public sealed class BackupIdentityUserRecord
{
    public string Id { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string? NormalizedUserName { get; init; }
    public string? Email { get; init; }
    public string? NormalizedEmail { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool LockoutEnabled { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public int AccessFailedCount { get; init; }
}

public sealed class BackupIdentityRoleRecord
{
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? NormalizedName { get; init; }
}

public sealed class BackupIdentityUserRoleRecord
{
    public string UserId { get; init; } = string.Empty;
    public string RoleId { get; init; } = string.Empty;
}

public sealed class BackupFileListItem
{
    public string FileName { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public DateTimeOffset FileCreatedAtUtc { get; init; }
    public DateTimeOffset? ExportedAtUtc { get; init; }
    public string? BackupName { get; init; }
    public string? AppVersion { get; init; }
    public string BackupSource { get; init; } = ConfigurationBackupSources.Manual;
    public IReadOnlyList<string> IncludedSections { get; init; } = [];
    public string? NotesSummary { get; init; }
    public long FileSizeBytes { get; init; }
}

public sealed class CreateConfigurationBackupRequest
{
    [Required(ErrorMessage = "Backup name is required.")]
    public string BackupName { get; init; } = string.Empty;

    public string? Notes { get; init; }

    public required IReadOnlyList<string> SelectedSections { get; init; }

    public string? ExportedBy { get; init; }
    public string BackupSource { get; init; } = ConfigurationBackupSources.Manual;
}

public sealed class DeleteConfigurationBackupRequest
{
    [Required]
    public string FileId { get; init; } = string.Empty;

    public string? ConfirmationText { get; init; }
}

public sealed class BulkDeleteConfigurationBackupsRequest
{
    public IReadOnlyList<string> FileIds { get; init; } = [];
    public string? ConfirmationText { get; init; }
}

public sealed class DeleteConfigurationBackupResponse
{
    public string FileId { get; init; } = string.Empty;
    public bool Deleted { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class BulkDeleteConfigurationBackupsResponse
{
    public int RequestedCount { get; init; }
    public int DeletedCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
}

public sealed class CreateConfigurationBackupResponse
{
    public string FileName { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; init; }
    public IReadOnlyList<string> IncludedSections { get; init; } = [];
}

public sealed class UploadConfigurationBackupRequest
{
    [Required(ErrorMessage = "Backup file is required.")]
    public IFormFile? File { get; init; }

    public string? UploadedBy { get; init; }
}

public sealed class UploadConfigurationBackupResponse
{
    public string FileName { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; }
    public string? UploadedBy { get; init; }
}

public sealed class RestorePreviewMetadata
{
    public string BackupName { get; init; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public int FormatVersion { get; init; }
    public string? Notes { get; init; }
}

public sealed class ConfigurationBackupSectionCounts
{
    public int Agents { get; init; }
    public int Endpoints { get; init; }
    public int Assignments { get; init; }
    public int IdentityUsers { get; init; }
    public int IdentityRoles { get; init; }
    public int IdentityUserRoles { get; init; }
}

public sealed class ConfigurationBackupPreview
{
    public string FileId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public RestorePreviewMetadata Metadata { get; init; } = new();
    public IReadOnlyList<string> IncludedSections { get; init; } = [];
    public ConfigurationBackupSectionCounts Counts { get; init; } = new();
}

public sealed class RestoreConfigurationRequest
{
    [Required(ErrorMessage = "Backup file is required.")]
    public string FileId { get; init; } = string.Empty;

    [Required]
    public IReadOnlyList<string> SelectedSections { get; init; } = [];

    [Required]
    public string RestoreMode { get; init; } = ConfigurationRestoreModes.Merge;

    public string? ConfirmationText { get; init; }
}

public sealed class RestoreSectionResult
{
    public string Section { get; init; } = string.Empty;
    public int DeletedCount { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class RestoreConfigurationResponse
{
    public string FileId { get; init; } = string.Empty;
    public string BackupName { get; init; } = string.Empty;
    public string RestoreMode { get; init; } = ConfigurationRestoreModes.Merge;
    public IReadOnlyList<string> SelectedSections { get; init; } = [];
    public IReadOnlyList<RestoreSectionResult> SectionResults { get; init; } = [];
}
