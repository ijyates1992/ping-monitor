namespace PingMonitor.Web.Options;

public sealed class ApplicationUpdaterOptions
{
    public const string SectionName = "ApplicationUpdater";

    public bool UpdateChecksEnabled { get; set; } = true;
    public bool EnableAutomaticUpdateChecks { get; set; } = true;
    public int AutomaticUpdateCheckIntervalMinutes { get; set; } = 15;
    public bool AutomaticallyDownloadAndStageUpdates { get; set; }
    public string GitHubOwner { get; set; } = "ijyates1992";
    public string GitHubRepository { get; set; } = "ping-monitor";
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
    public bool AllowPreviewReleases { get; set; }
    public string ReleasePackagePrefix { get; set; } = "PingMonitor";
    public string RuntimeIdentifier { get; set; } = "win-x64";
    public string ChecksumAssetName { get; set; } = "SHA256.txt";
    public string StagingStoragePath { get; set; } = "App_Data/Updater";
    public string BootstrapperRelativePath { get; set; } = "Updater/run-staged-update-bootstrapper.ps1";
    public string StagedReleaseBootstrapperPath { get; set; } = "app/Updater/run-staged-update-bootstrapper.ps1";
    public string PowerShellExecutablePath { get; set; } = "pwsh.exe";
    public string IisSiteName { get; set; } = string.Empty;
    public string IisAppPoolName { get; set; } = string.Empty;
}
