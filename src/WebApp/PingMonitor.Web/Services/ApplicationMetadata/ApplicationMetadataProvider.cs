using System.Reflection;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationMetadata;

public interface IApplicationMetadataProvider
{
    ApplicationMetadataSnapshot GetSnapshot();
}

public sealed class ApplicationMetadataProvider : IApplicationMetadataProvider
{
    private const string InternalDevBuildLabel = "Internal dev build";

    private readonly ApplicationMetadataOptions _options;

    public ApplicationMetadataProvider(IOptions<ApplicationMetadataOptions> options)
    {
        _options = options.Value;
    }

    public ApplicationMetadataSnapshot GetSnapshot()
    {
        return new ApplicationMetadataSnapshot
        {
            ApplicationName = _options.ApplicationName,
            Description = _options.Description,
            Attribution = _options.Attribution,
            Licence = _options.Licence,
            RepositoryUrl = _options.RepositoryUrl,
            PreviewNote = _options.PreviewNote,
            Version = ResolveVersion()
        };
    }

    private string ResolveVersion()
    {
        if (!string.IsNullOrWhiteSpace(_options.Version))
        {
            return _options.Version.Trim();
        }

        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is null)
        {
            return InternalDevBuildLabel;
        }

        var informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = entryAssembly.GetName().Version?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(assemblyVersion)
            || string.Equals(assemblyVersion, "0.0.0.0", StringComparison.Ordinal))
        {
            return InternalDevBuildLabel;
        }

        return assemblyVersion;
    }
}

public sealed class ApplicationMetadataSnapshot
{
    public string ApplicationName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Licence { get; init; } = string.Empty;
    public string? RepositoryUrl { get; init; }
    public string? PreviewNote { get; init; }
}
