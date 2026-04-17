namespace PingMonitor.Web.Options;

public sealed class ApplicationUpdaterOptions
{
    public const string SectionName = "ApplicationUpdater";

    public bool UpdateChecksEnabled { get; set; } = true;
    public string GitHubOwner { get; set; } = "ijyates1992";
    public string GitHubRepository { get; set; } = "ping-monitor";
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
    public bool AllowPreviewReleases { get; set; }
}
