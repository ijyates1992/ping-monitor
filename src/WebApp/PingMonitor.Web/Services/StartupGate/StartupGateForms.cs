using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.Services.StartupGate;

public sealed class StartupDatabaseConfigurationForm
{
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 3306;

    [Required]
    [Display(Name = "Database name")]
    [StringLength(255)]
    public string DatabaseName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Username")]
    [StringLength(255)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(255)]
    public string Password { get; set; } = string.Empty;
}

public sealed class StartupAdminBootstrapForm
{
    [Required]
    [StringLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class StartupDatabaseBackupUploadForm
{
    [Required(ErrorMessage = "Select a DATABASE backup file to upload.")]
    [Display(Name = "DATABASE backup SQL file")]
    public IFormFile? BackupFile { get; set; }
}

public sealed class StartupDatabaseBackupRestoreForm
{
    [Required(ErrorMessage = "Select a DATABASE backup file to restore.")]
    [Display(Name = "DATABASE backup to restore")]
    public string FileId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type RESTORE to confirm.")]
    [Display(Name = "Typed confirmation")]
    public string ConfirmationText { get; set; } = string.Empty;
}
