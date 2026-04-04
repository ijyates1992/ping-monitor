namespace PingMonitor.Web.Options;

public sealed class ApplicationMetadataOptions
{
    public const string SectionName = "ApplicationMetadata";

    public string ApplicationName { get; set; } = "Ping Monitor";
    public string Description { get; set; } = "Control-plane web application for endpoint observability and alerting.";
    public string Attribution { get; set; } = "by ijyates1992";
    public string Licence { get; set; } = "Licensed under GNU Affero General Public License v3 (AGPLv3)";
    public string? RepositoryUrl { get; set; }
    public string? PreviewNote { get; set; } = "Early preview release";
    public string? Version { get; set; }
}
