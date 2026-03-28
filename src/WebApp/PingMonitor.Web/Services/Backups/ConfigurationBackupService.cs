using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupService
{
    Task<CreateConfigurationBackupResponse> CreateBackupAsync(CreateConfigurationBackupRequest request, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupService : IConfigurationBackupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly PingMonitorDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;
    private readonly IConfigurationBackupFileNameGenerator _fileNameGenerator;

    public ConfigurationBackupService(
        PingMonitorDbContext dbContext,
        IWebHostEnvironment environment,
        IOptions<BackupOptions> options,
        IConfigurationBackupFileNameGenerator fileNameGenerator)
    {
        _dbContext = dbContext;
        _environment = environment;
        _options = options.Value;
        _fileNameGenerator = fileNameGenerator;
    }

    public async Task<CreateConfigurationBackupResponse> CreateBackupAsync(CreateConfigurationBackupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BackupName))
        {
            throw new InvalidOperationException("Backup name is required.");
        }

        if (request.SelectedSections.Count == 0)
        {
            throw new InvalidOperationException("At least one section must be selected.");
        }

        var selectedSections = request.SelectedSections
            .Select(section => section.Trim().ToLowerInvariant())
            .Where(section => ConfigurationBackupSections.All.Contains(section, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedSections.Length == 0)
        {
            throw new InvalidOperationException("At least one valid section must be selected.");
        }

        var exportedAtUtc = DateTimeOffset.UtcNow;
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var metadata = new ConfigurationBackupMetadata
        {
            AppVersion = appVersion,
            BackupName = request.BackupName.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ExportedAtUtc = exportedAtUtc,
            ExportedBy = request.ExportedBy,
            MachineName = Environment.MachineName
        };

        var sections = new ConfigurationBackupSectionData
        {
            Agents = selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal)
                ? await LoadAgentsAsync(cancellationToken)
                : null,
            Endpoints = selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal)
                ? await LoadEndpointsAsync(cancellationToken)
                : null,
            Assignments = selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal)
                ? await LoadAssignmentsAsync(cancellationToken)
                : null,
            Identity = selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal)
                ? await LoadIdentityAsync(cancellationToken)
                : null
        };

        var document = ConfigurationBackupDocument.Create(metadata, sections);

        var storagePath = ResolveStoragePath();
        Directory.CreateDirectory(storagePath);

        var fileName = _fileNameGenerator.CreateFileName(metadata.BackupName, metadata.AppVersion, metadata.ExportedAtUtc);
        var fullPath = Path.Combine(storagePath, fileName);

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);

        return new CreateConfigurationBackupResponse
        {
            FileName = fileName,
            FileId = fileName,
            BackupName = metadata.BackupName,
            ExportedAtUtc = metadata.ExportedAtUtc,
            IncludedSections = selectedSections
        };
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private async Task<IReadOnlyList<BackupAgentRecord>> LoadAgentsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Agents
            .AsNoTracking()
            .OrderBy(x => x.InstanceId)
            .Select(x => new BackupAgentRecord
            {
                AgentId = x.AgentId,
                InstanceId = x.InstanceId,
                Name = x.Name,
                Site = x.Site,
                Enabled = x.Enabled,
                AgentVersion = x.AgentVersion,
                Platform = x.Platform,
                MachineName = x.MachineName,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BackupEndpointRecord>> LoadEndpointsAsync(CancellationToken cancellationToken)
    {
        var dependencies = await _dbContext.EndpointDependencies
            .AsNoTracking()
            .OrderBy(x => x.EndpointId)
            .ThenBy(x => x.DependsOnEndpointId)
            .ToListAsync(cancellationToken);

        var dependencyLookup = dependencies
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.DependsOnEndpointId).ToArray(),
                StringComparer.Ordinal);

        var endpoints = await _dbContext.Endpoints
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return endpoints
            .Select(x => new BackupEndpointRecord
            {
                EndpointId = x.EndpointId,
                Name = x.Name,
                Target = x.Target,
                IconKey = x.IconKey,
                Enabled = x.Enabled,
                Tags = x.Tags,
                DependsOnEndpointIds = dependencyLookup.TryGetValue(x.EndpointId, out var dependsOnIds)
                    ? dependsOnIds
                    : [],
                Notes = x.Notes,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<BackupAssignmentRecord>> LoadAssignmentsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.MonitorAssignments
            .AsNoTracking()
            .OrderBy(x => x.AgentId)
            .ThenBy(x => x.EndpointId)
            .Select(x => new BackupAssignmentRecord
            {
                AssignmentId = x.AssignmentId,
                AgentId = x.AgentId,
                EndpointId = x.EndpointId,
                CheckType = x.CheckType.ToString().ToLowerInvariant(),
                Enabled = x.Enabled,
                PingIntervalSeconds = x.PingIntervalSeconds,
                RetryIntervalSeconds = x.RetryIntervalSeconds,
                TimeoutMs = x.TimeoutMs,
                FailureThreshold = x.FailureThreshold,
                RecoveryThreshold = x.RecoveryThreshold,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<BackupIdentitySection> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Select(x => new BackupIdentityUserRecord
            {
                Id = x.Id,
                UserName = x.UserName,
                NormalizedUserName = x.NormalizedUserName,
                Email = x.Email,
                NormalizedEmail = x.NormalizedEmail,
                EmailConfirmed = x.EmailConfirmed,
                LockoutEnabled = x.LockoutEnabled,
                LockoutEnd = x.LockoutEnd,
                AccessFailedCount = x.AccessFailedCount
            })
            .ToListAsync(cancellationToken);

        var roles = await _dbContext.Roles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BackupIdentityRoleRecord
            {
                Id = x.Id,
                Name = x.Name,
                NormalizedName = x.NormalizedName
            })
            .ToListAsync(cancellationToken);

        var userRoles = await _dbContext.UserRoles
            .AsNoTracking()
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.RoleId)
            .Select(x => new BackupIdentityUserRoleRecord
            {
                UserId = x.UserId,
                RoleId = x.RoleId
            })
            .ToListAsync(cancellationToken);

        return new BackupIdentitySection
        {
            Users = users,
            Roles = roles,
            UserRoles = userRoles
        };
    }
}
