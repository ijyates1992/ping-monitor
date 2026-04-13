namespace PingMonitor.Web.Options;

public sealed class ApplicationUpdateOptions
{
    public const string SectionName = "ApplicationUpdate";

    public bool Enabled { get; set; } = true;
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
    public string Owner { get; set; } = "ijyates1992";
    public string Repository { get; set; } = "ping-monitor";
    public bool IncludePrerelease { get; set; }
}
