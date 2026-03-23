using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PingMonitor.Web.Models;
using EndpointModel = PingMonitor.Web.Models.Endpoint;
using System.Text.Json;

namespace PingMonitor.Web.Data;

public sealed class PingMonitorDbContext : DbContext
{
    public PingMonitorDbContext(DbContextOptions<PingMonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<EndpointModel> Endpoints => Set<EndpointModel>();
    public DbSet<MonitorAssignment> MonitorAssignments => Set<MonitorAssignment>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<ResultBatch> ResultBatches => Set<ResultBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = new ValueConverter<List<string>, string>(
            tags => JsonSerializer.Serialize(tags, (JsonSerializerOptions?)null),
            value => string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>());
        var stringListComparer = new ValueComparer<List<string>>(
            (left, right) => left!.SequenceEqual(right!),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
            value => value.ToList());

        var agent = modelBuilder.Entity<Agent>();
        agent.ToTable("Agents");
        agent.HasKey(x => x.AgentId);
        agent.Property(x => x.AgentId).HasMaxLength(64);
        agent.Property(x => x.InstanceId).HasMaxLength(255).IsRequired();
        agent.Property(x => x.Name).HasMaxLength(255);
        agent.Property(x => x.Site).HasMaxLength(255);
        agent.Property(x => x.ApiKeyHash).IsRequired();
        agent.Property(x => x.AgentVersion).HasMaxLength(50);
        agent.Property(x => x.Platform).HasMaxLength(50);
        agent.Property(x => x.MachineName).HasMaxLength(255);
        agent.Property(x => x.CreatedAtUtc).IsRequired();
        agent.Property(x => x.ApiKeyCreatedAtUtc).IsRequired();
        agent.Property(x => x.Enabled).HasDefaultValue(true);
        agent.Property(x => x.ApiKeyRevoked).HasDefaultValue(false);
        agent.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        agent.HasIndex(x => x.InstanceId).IsUnique();

        var endpoint = modelBuilder.Entity<EndpointModel>();
        endpoint.ToTable("Endpoints");
        endpoint.HasKey(x => x.EndpointId);
        endpoint.Property(x => x.EndpointId).HasMaxLength(64);
        endpoint.Property(x => x.Name).HasMaxLength(255).IsRequired();
        endpoint.Property(x => x.Target).HasMaxLength(255).IsRequired();
        endpoint.Property(x => x.DependsOnEndpointId).HasMaxLength(64);
        endpoint.Property(x => x.Notes).HasMaxLength(2048);
        endpoint.Property(x => x.CreatedAtUtc).IsRequired();
        endpoint.Property(x => x.Tags).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
        endpoint.Property(x => x.Tags).IsRequired();

        var assignment = modelBuilder.Entity<MonitorAssignment>();
        assignment.ToTable("MonitorAssignments");
        assignment.HasKey(x => x.AssignmentId);
        assignment.Property(x => x.AssignmentId).HasMaxLength(64);
        assignment.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        assignment.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        assignment.Property(x => x.CheckType).HasConversion<string>().HasMaxLength(32).IsRequired();
        assignment.Property(x => x.CreatedAtUtc).IsRequired();
        assignment.Property(x => x.UpdatedAtUtc).IsRequired();
        assignment.HasIndex(x => new { x.AgentId, x.EndpointId }).IsUnique();

        var checkResult = modelBuilder.Entity<CheckResult>();
        checkResult.ToTable("CheckResults");
        checkResult.HasKey(x => x.CheckResultId);
        checkResult.Property(x => x.CheckResultId).HasMaxLength(64);
        checkResult.Property(x => x.AssignmentId).HasMaxLength(64).IsRequired();
        checkResult.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        checkResult.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        checkResult.Property(x => x.ErrorCode).HasMaxLength(128);
        checkResult.Property(x => x.ErrorMessage).HasMaxLength(2048);
        checkResult.Property(x => x.BatchId).HasMaxLength(128).IsRequired();
        checkResult.Property(x => x.CheckedAtUtc).IsRequired();
        checkResult.Property(x => x.ReceivedAtUtc).IsRequired();
        checkResult.HasIndex(x => new { x.AgentId, x.BatchId });

        var resultBatch = modelBuilder.Entity<ResultBatch>();
        resultBatch.ToTable("ResultBatches");
        resultBatch.HasKey(x => x.ResultBatchId);
        resultBatch.Property(x => x.ResultBatchId).HasMaxLength(64);
        resultBatch.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        resultBatch.Property(x => x.BatchId).HasMaxLength(128).IsRequired();
        resultBatch.Property(x => x.ReceivedAtUtc).IsRequired();
        resultBatch.Property(x => x.AcceptedCount).IsRequired();
        resultBatch.HasIndex(x => new { x.AgentId, x.BatchId }).IsUnique();
    }
}
