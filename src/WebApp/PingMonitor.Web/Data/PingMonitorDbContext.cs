using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using EndpointModel = PingMonitor.Web.Models.Endpoint;

namespace PingMonitor.Web.Data;

public sealed class PingMonitorDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public PingMonitorDbContext(DbContextOptions<PingMonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentHeartbeatHistory> AgentHeartbeatHistories => Set<AgentHeartbeatHistory>();
    public DbSet<EndpointModel> Endpoints => Set<EndpointModel>();
    public DbSet<EndpointDependency> EndpointDependencies => Set<EndpointDependency>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<EndpointGroupMembership> EndpointGroupMemberships => Set<EndpointGroupMembership>();
    public DbSet<UserGroupAccess> UserGroupAccesses => Set<UserGroupAccess>();
    public DbSet<UserEndpointAccess> UserEndpointAccesses => Set<UserEndpointAccess>();
    public DbSet<MonitorAssignment> MonitorAssignments => Set<MonitorAssignment>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<ResultBatch> ResultBatches => Set<ResultBatch>();
    public DbSet<EndpointState> EndpointStates => Set<EndpointState>();
    public DbSet<StateTransition> StateTransitions => Set<StateTransition>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<SecurityAuthLog> SecurityAuthLogs => Set<SecurityAuthLog>();
    public DbSet<AppSchemaInfo> AppSchemaInfos => Set<AppSchemaInfo>();
    public DbSet<ApplicationSettings> ApplicationSettings => Set<ApplicationSettings>();
    public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();
    public DbSet<SecurityIpBlock> SecurityIpBlocks => Set<SecurityIpBlock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var stringListConverter = new ValueConverter<List<string>, string>(
            tags => JsonSerializer.Serialize(tags, (JsonSerializerOptions?)null),
            value => string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>());
        var stringListComparer = new ValueComparer<List<string>>(
            (left, right) => left!.SequenceEqual(right!),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
            value => value.ToList());

        var schemaInfo = modelBuilder.Entity<AppSchemaInfo>();
        schemaInfo.ToTable("AppSchemaInfo");
        schemaInfo.HasKey(x => x.AppSchemaInfoId);
        schemaInfo.Property(x => x.CurrentSchemaVersion).IsRequired();
        schemaInfo.Property(x => x.UpdatedAtUtc).IsRequired();

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
        agent.Property(x => x.LastHeartbeatEventLoggedAtUtc);
        agent.Property(x => x.ApiKeyCreatedAtUtc).IsRequired();
        agent.Property(x => x.Enabled).HasDefaultValue(true);
        agent.Property(x => x.ApiKeyRevoked).HasDefaultValue(false);
        agent.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        agent.HasIndex(x => x.InstanceId).IsUnique();

        var agentHeartbeatHistory = modelBuilder.Entity<AgentHeartbeatHistory>();
        agentHeartbeatHistory.ToTable("AgentHeartbeatHistory");
        agentHeartbeatHistory.HasKey(x => x.AgentHeartbeatHistoryId);
        agentHeartbeatHistory.Property(x => x.AgentHeartbeatHistoryId).HasMaxLength(64);
        agentHeartbeatHistory.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        agentHeartbeatHistory.Property(x => x.HeartbeatAtUtc).IsRequired();
        agentHeartbeatHistory.Property(x => x.RecordedAtUtc).IsRequired();
        agentHeartbeatHistory.HasIndex(x => new { x.AgentId, x.HeartbeatAtUtc });

        var endpoint = modelBuilder.Entity<EndpointModel>();
        endpoint.ToTable("Endpoints");
        endpoint.HasKey(x => x.EndpointId);
        endpoint.Property(x => x.EndpointId).HasMaxLength(64);
        endpoint.Property(x => x.Name).HasMaxLength(255).IsRequired();
        endpoint.Property(x => x.Target).HasMaxLength(255).IsRequired();
        endpoint.Property(x => x.IconKey).HasMaxLength(64).IsRequired().HasDefaultValue("generic");
        endpoint.Property(x => x.Notes).HasMaxLength(2048);
        endpoint.Property(x => x.CreatedAtUtc).IsRequired();
        endpoint.Property(x => x.Tags).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
        endpoint.Property(x => x.Tags).IsRequired();

        var endpointDependency = modelBuilder.Entity<EndpointDependency>();
        endpointDependency.ToTable("EndpointDependencies");
        endpointDependency.HasKey(x => x.EndpointDependencyId);
        endpointDependency.Property(x => x.EndpointDependencyId).HasMaxLength(64);
        endpointDependency.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        endpointDependency.Property(x => x.DependsOnEndpointId).HasMaxLength(64).IsRequired();
        endpointDependency.Property(x => x.CreatedAtUtc).IsRequired();
        endpointDependency.HasIndex(x => new { x.EndpointId, x.DependsOnEndpointId }).IsUnique();


        var group = modelBuilder.Entity<Group>();
        group.ToTable("Groups");
        group.HasKey(x => x.GroupId);
        group.Property(x => x.GroupId).HasMaxLength(64);
        group.Property(x => x.Name).HasMaxLength(255).IsRequired();
        group.Property(x => x.Description).HasMaxLength(2048);
        group.Property(x => x.CreatedAtUtc).IsRequired();
        group.HasIndex(x => x.Name).IsUnique();

        var endpointGroupMembership = modelBuilder.Entity<EndpointGroupMembership>();
        endpointGroupMembership.ToTable("EndpointGroupMemberships");
        endpointGroupMembership.HasKey(x => x.EndpointGroupMembershipId);
        endpointGroupMembership.Property(x => x.EndpointGroupMembershipId).HasMaxLength(64);
        endpointGroupMembership.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        endpointGroupMembership.Property(x => x.GroupId).HasMaxLength(64).IsRequired();
        endpointGroupMembership.Property(x => x.CreatedAtUtc).IsRequired();
        endpointGroupMembership.HasIndex(x => new { x.EndpointId, x.GroupId }).IsUnique();


        var userGroupAccess = modelBuilder.Entity<UserGroupAccess>();
        userGroupAccess.ToTable("UserGroupAccesses");
        userGroupAccess.HasKey(x => x.UserGroupAccessId);
        userGroupAccess.Property(x => x.UserGroupAccessId).HasMaxLength(64);
        userGroupAccess.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        userGroupAccess.Property(x => x.GroupId).HasMaxLength(64).IsRequired();
        userGroupAccess.Property(x => x.CreatedAtUtc).IsRequired();
        userGroupAccess.HasIndex(x => new { x.UserId, x.GroupId }).IsUnique();

        var userEndpointAccess = modelBuilder.Entity<UserEndpointAccess>();
        userEndpointAccess.ToTable("UserEndpointAccesses");
        userEndpointAccess.HasKey(x => x.UserEndpointAccessId);
        userEndpointAccess.Property(x => x.UserEndpointAccessId).HasMaxLength(64);
        userEndpointAccess.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        userEndpointAccess.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        userEndpointAccess.Property(x => x.CreatedAtUtc).IsRequired();
        userEndpointAccess.HasIndex(x => new { x.UserId, x.EndpointId }).IsUnique();

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

        var endpointState = modelBuilder.Entity<EndpointState>();
        endpointState.ToTable("EndpointStates");
        endpointState.HasKey(x => x.AssignmentId);
        endpointState.Property(x => x.AssignmentId).HasMaxLength(64);
        endpointState.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        endpointState.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        endpointState.Property(x => x.CurrentState).HasConversion<string>().HasMaxLength(16).IsRequired();
        endpointState.Property(x => x.LastStateChangeUtc);
        endpointState.Property(x => x.LastCheckUtc);
        endpointState.Property(x => x.SuppressedByEndpointId).HasMaxLength(64);

        var stateTransition = modelBuilder.Entity<StateTransition>();
        stateTransition.ToTable("StateTransitions");
        stateTransition.HasKey(x => x.TransitionId);
        stateTransition.Property(x => x.TransitionId).HasMaxLength(64);
        stateTransition.Property(x => x.AssignmentId).HasMaxLength(64).IsRequired();
        stateTransition.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        stateTransition.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        stateTransition.Property(x => x.PreviousState).HasConversion<string>().HasMaxLength(16).IsRequired();
        stateTransition.Property(x => x.NewState).HasConversion<string>().HasMaxLength(16).IsRequired();
        stateTransition.Property(x => x.TransitionAtUtc).IsRequired();
        stateTransition.Property(x => x.ReasonCode).HasMaxLength(64);
        stateTransition.Property(x => x.DependencyEndpointId).HasMaxLength(64);
        stateTransition.HasIndex(x => new { x.AssignmentId, x.TransitionAtUtc });

        var eventLog = modelBuilder.Entity<EventLog>();
        eventLog.ToTable("EventLogs");
        eventLog.HasKey(x => x.EventLogId);
        eventLog.Property(x => x.EventLogId).HasMaxLength(64);
        eventLog.Property(x => x.OccurredAtUtc).IsRequired();
        eventLog.Property(x => x.EventCategory).HasConversion<string>().HasMaxLength(32).IsRequired();
        eventLog.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        eventLog.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        eventLog.Property(x => x.AgentId).HasMaxLength(64);
        eventLog.Property(x => x.EndpointId).HasMaxLength(64);
        eventLog.Property(x => x.AssignmentId).HasMaxLength(64);
        eventLog.Property(x => x.Message).HasMaxLength(2048).IsRequired();
        eventLog.Property(x => x.DetailsJson).HasMaxLength(8192);
        eventLog.HasIndex(x => x.OccurredAtUtc);
        eventLog.HasIndex(x => new { x.EndpointId, x.OccurredAtUtc });
        eventLog.HasIndex(x => new { x.AgentId, x.OccurredAtUtc });
        eventLog.HasIndex(x => new { x.AssignmentId, x.OccurredAtUtc });

        var securityAuthLog = modelBuilder.Entity<SecurityAuthLog>();
        securityAuthLog.ToTable("SecurityAuthLogs");
        securityAuthLog.HasKey(x => x.SecurityAuthLogId);
        securityAuthLog.Property(x => x.SecurityAuthLogId).HasMaxLength(64);
        securityAuthLog.Property(x => x.OccurredAtUtc).IsRequired();
        securityAuthLog.Property(x => x.AuthType).HasConversion<string>().HasMaxLength(16).IsRequired();
        securityAuthLog.Property(x => x.SubjectIdentifier).HasMaxLength(255).IsRequired();
        securityAuthLog.Property(x => x.SourceIpAddress).HasMaxLength(64);
        securityAuthLog.Property(x => x.FailureReason).HasMaxLength(128);
        securityAuthLog.Property(x => x.UserId).HasMaxLength(255);
        securityAuthLog.Property(x => x.AgentId).HasMaxLength(64);
        securityAuthLog.Property(x => x.DetailsJson).HasMaxLength(4096);
        securityAuthLog.HasIndex(x => x.OccurredAtUtc);
        securityAuthLog.HasIndex(x => new { x.AuthType, x.OccurredAtUtc });
        securityAuthLog.HasIndex(x => new { x.AuthType, x.Success, x.OccurredAtUtc });

        var appSettings = modelBuilder.Entity<ApplicationSettings>();
        appSettings.ToTable("ApplicationSettings");
        appSettings.HasKey(x => x.ApplicationSettingsId);
        appSettings.Property(x => x.ApplicationSettingsId).ValueGeneratedNever();
        appSettings.Property(x => x.SiteUrl).HasMaxLength(2048).IsRequired();
        appSettings.Property(x => x.DefaultPingIntervalSeconds).IsRequired();
        appSettings.Property(x => x.DefaultRetryIntervalSeconds).IsRequired();
        appSettings.Property(x => x.DefaultTimeoutMs).IsRequired();
        appSettings.Property(x => x.DefaultFailureThreshold).IsRequired();
        appSettings.Property(x => x.DefaultRecoveryThreshold).IsRequired();
        appSettings.Property(x => x.UpdatedAtUtc).IsRequired();

        var securitySettings = modelBuilder.Entity<SecuritySettings>();
        securitySettings.ToTable("SecuritySettings");
        securitySettings.HasKey(x => x.SecuritySettingsId);
        securitySettings.Property(x => x.SecuritySettingsId).ValueGeneratedNever();
        securitySettings.Property(x => x.AgentFailedAttemptsBeforeTemporaryIpBlock).IsRequired();
        securitySettings.Property(x => x.AgentTemporaryIpBlockDurationMinutes).IsRequired();
        securitySettings.Property(x => x.AgentFailedAttemptsBeforePermanentIpBlock).IsRequired();
        securitySettings.Property(x => x.UserFailedAttemptsBeforeTemporaryIpBlock).IsRequired();
        securitySettings.Property(x => x.UserTemporaryIpBlockDurationMinutes).IsRequired();
        securitySettings.Property(x => x.UserFailedAttemptsBeforePermanentIpBlock).IsRequired();
        securitySettings.Property(x => x.UserFailedAttemptsBeforeTemporaryAccountLockout).IsRequired();
        securitySettings.Property(x => x.UserTemporaryAccountLockoutDurationMinutes).IsRequired();
        securitySettings.Property(x => x.UpdatedAtUtc).IsRequired();

        var securityIpBlock = modelBuilder.Entity<SecurityIpBlock>();
        securityIpBlock.ToTable("SecurityIpBlocks");
        securityIpBlock.HasKey(x => x.SecurityIpBlockId);
        securityIpBlock.Property(x => x.SecurityIpBlockId).HasMaxLength(64);
        securityIpBlock.Property(x => x.AuthType).HasConversion<string>().HasMaxLength(16).IsRequired();
        securityIpBlock.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
        securityIpBlock.Property(x => x.BlockType).HasConversion<string>().HasMaxLength(16).IsRequired();
        securityIpBlock.Property(x => x.BlockedAtUtc).IsRequired();
        securityIpBlock.Property(x => x.Reason).HasMaxLength(512);
        securityIpBlock.Property(x => x.CreatedByUserId).HasMaxLength(255);
        securityIpBlock.Property(x => x.RemovedByUserId).HasMaxLength(255);
        securityIpBlock.HasIndex(x => new { x.AuthType, x.IpAddress, x.RemovedAtUtc });
        securityIpBlock.HasIndex(x => x.BlockedAtUtc);

    }
}
