namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupQueryService
{
    Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken);
    string ResolveDownloadPath(string fileId);
}

public sealed class ConfigurationBackupQueryService : IConfigurationBackupQueryService
{
    private readonly IConfigurationBackupDocumentLoader _documentLoader;

    public ConfigurationBackupQueryService(IConfigurationBackupDocumentLoader documentLoader)
    {
        _documentLoader = documentLoader;
    }

    public Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken)
    {
        return _documentLoader.ListBackupsAsync(cancellationToken);
    }

    public string ResolveDownloadPath(string fileId)
    {
        return _documentLoader.ResolveBackupPath(fileId);
    }
}
