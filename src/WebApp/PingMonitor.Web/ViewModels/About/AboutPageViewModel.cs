namespace PingMonitor.Web.ViewModels.About;

public sealed class AboutPageViewModel
{
    public string ApplicationName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Licence { get; init; } = string.Empty;
    public string? RepositoryUrl { get; init; }
    public string? PreviewNote { get; init; }
}
