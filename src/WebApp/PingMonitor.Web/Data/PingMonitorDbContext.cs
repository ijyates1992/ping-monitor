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
    public DbSet<AssignmentMetrics24h> AssignmentMetrics24h => Set<AssignmentMetrics24h>();
    public DbSet<AssignmentRttMinuteBucket> AssignmentRttMinuteBuckets => Set<AssignmentRttMinuteBucket>();
    public DbSet<AssignmentStateInterval> AssignmentStateIntervals => Set<AssignmentStateInterval>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<SecurityAuthLog> SecurityAuthLogs => Set<SecurityAuthLog>();
    public DbSet<AppSchemaInfo> AppSchemaInfos => Set<AppSchemaInfo>();
    public DbSet<ApplicationSettings> ApplicationSettings => Set<ApplicationSettings>();
    public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();
    public DbSet<SecurityIpBlock> SecurityIpBlocks => Set<SecurityIpBlock>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<AiAssistantSettings> AiAssistantSettings => Set<AiAssistantSettings>();
    public DbSet<UserNotificationSettings> UserNotificationSettings => Set<UserNotificationSettings>();
    public DbSet<PendingTelegramLink> PendingTelegramLinks => Set<PendingTelegramLink>();
    public DbSet<TelegramAccount> TelegramAccounts => Set<TelegramAccount>();
    public DbSet<NetworkDiagram> NetworkDiagrams => Set<NetworkDiagram>();
    public DbSet<NetworkDiagramArea> NetworkDiagramAreas => Set<NetworkDiagramArea>();
    public DbSet<NetworkDiagramNode> NetworkDiagramNodes => Set<NetworkDiagramNode>();
    public DbSet<NetworkDiagramLink> NetworkDiagramLinks => Set<NetworkDiagramLink>();
    public DbSet<NetworkDiagramLinkVlan> NetworkDiagramLinkVlans => Set<NetworkDiagramLinkVlan>();

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
        agent.Property(x => x.EndpointUnknownAfterAgentOfflineSeconds)
            .IsRequired()
            .HasDefaultValue(Agent.DefaultEndpointUnknownAfterAgentOfflineSeconds);
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
        checkResult.Property(x => x.ErrorCode).HasMaxLength(128);
        checkResult.Property(x => x.ErrorMessage).HasMaxLength(2048);
        checkResult.Property(x => x.BatchId).HasMaxLength(128).IsRequired();
        checkResult.Property(x => x.RoundTripMs).HasPrecision(10, 3);
        checkResult.Property(x => x.CheckedAtUtc).IsRequired();
        checkResult.Property(x => x.ReceivedAtUtc).IsRequired();
        checkResult.HasIndex(x => new { x.AssignmentId, x.CheckedAtUtc });

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


        var assignmentMetrics24h = modelBuilder.Entity<AssignmentMetrics24h>();
        assignmentMetrics24h.ToTable("AssignmentMetrics24h");
        assignmentMetrics24h.HasKey(x => x.AssignmentId);
        assignmentMetrics24h.Property(x => x.AssignmentId).HasMaxLength(64);
        assignmentMetrics24h.Property(x => x.WindowStartUtc).IsRequired();
        assignmentMetrics24h.Property(x => x.WindowEndUtc).IsRequired();
        assignmentMetrics24h.Property(x => x.UptimeSeconds).IsRequired();
        assignmentMetrics24h.Property(x => x.DowntimeSeconds).IsRequired();
        assignmentMetrics24h.Property(x => x.UnknownSeconds).IsRequired();
        assignmentMetrics24h.Property(x => x.SuppressedSeconds).IsRequired();
        assignmentMetrics24h.Property(x => x.LastRttMs);
        assignmentMetrics24h.Property(x => x.HighestRttMs);
        assignmentMetrics24h.Property(x => x.LowestRttMs);
        assignmentMetrics24h.Property(x => x.AverageRttMs);
        assignmentMetrics24h.Property(x => x.JitterMs);
        assignmentMetrics24h.Property(x => x.LastSuccessfulCheckUtc);
        assignmentMetrics24h.Property(x => x.UpdatedAtUtc).IsRequired();
        assignmentMetrics24h.HasIndex(x => x.AssignmentId).IsUnique();

        var assignmentRttMinuteBucket = modelBuilder.Entity<AssignmentRttMinuteBucket>();
        assignmentRttMinuteBucket.ToTable("AssignmentRttMinuteBuckets");
        assignmentRttMinuteBucket.HasKey(x => new { x.AssignmentId, x.BucketStartUtc });
        assignmentRttMinuteBucket.Property(x => x.AssignmentId).HasMaxLength(64);
        assignmentRttMinuteBucket.Property(x => x.BucketStartUtc).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.SampleCount).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.SumRttMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.MinRttMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.MaxRttMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.FirstRttMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.LastRttMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.FirstSampleUtc).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.LastSampleUtc).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.IntraBucketDeltaSumMs).IsRequired();
        assignmentRttMinuteBucket.Property(x => x.UpdatedAtUtc).IsRequired();
        assignmentRttMinuteBucket.HasIndex(x => new { x.AssignmentId, x.LastSampleUtc });

        var assignmentStateInterval = modelBuilder.Entity<AssignmentStateInterval>();
        assignmentStateInterval.ToTable("AssignmentStateIntervals");
        assignmentStateInterval.HasKey(x => x.AssignmentStateIntervalId);
        assignmentStateInterval.Property(x => x.AssignmentStateIntervalId).HasMaxLength(64);
        assignmentStateInterval.Property(x => x.AssignmentId).HasMaxLength(64).IsRequired();
        assignmentStateInterval.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        assignmentStateInterval.Property(x => x.StartedAtUtc).IsRequired();
        assignmentStateInterval.Property(x => x.EndedAtUtc);
        assignmentStateInterval.Property(x => x.UpdatedAtUtc).IsRequired();
        assignmentStateInterval.HasIndex(x => new { x.AssignmentId, x.StartedAtUtc });
        assignmentStateInterval.HasIndex(x => new { x.AssignmentId, x.EndedAtUtc });

        var applicationUser = modelBuilder.Entity<ApplicationUser>();
        applicationUser.Property(x => x.DisplayTimeZoneId)
            .HasMaxLength(128)
            .IsRequired()
            .HasDefaultValue("UTC");

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
        securityAuthLog.HasIndex(x => new { x.AuthType, x.SourceIpAddress, x.OccurredAtUtc });

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
        appSettings.Property(x => x.DegradedEvaluationEnabled).IsRequired();
        appSettings.Property(x => x.DegradedBaselineLookbackMinutes).IsRequired();
        appSettings.Property(x => x.DegradedCurrentWindowMinutes).IsRequired();
        appSettings.Property(x => x.DegradedPacketLossIncreasePercentagePoints).IsRequired();
        appSettings.Property(x => x.DegradedRttIncreasePercent).IsRequired();
        appSettings.Property(x => x.DegradedJitterIncreasePercent).IsRequired();
        appSettings.Property(x => x.DegradedMinimumSamples).IsRequired();
        appSettings.Property(x => x.NetworkDiagramsEnabled).IsRequired();
        appSettings.Property(x => x.UpdatedAtUtc).IsRequired();

        var aiAssistantSettings = modelBuilder.Entity<AiAssistantSettings>();
        aiAssistantSettings.ToTable("AiAssistantSettings");
        aiAssistantSettings.HasKey(x => x.AiAssistantSettingsId);
        aiAssistantSettings.Property(x => x.AiAssistantSettingsId).ValueGeneratedNever();
        aiAssistantSettings.Property(x => x.AssistantEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.WebChatEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.TelegramChatEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.MemoryEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.DebugLoggingEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.ProviderDisplayName).HasMaxLength(128).IsRequired();
        aiAssistantSettings.Property(x => x.ProviderType).HasMaxLength(64).IsRequired();
        aiAssistantSettings.Property(x => x.BaseUrl).HasMaxLength(2048).IsRequired();
        aiAssistantSettings.Property(x => x.ModelName).HasMaxLength(255).IsRequired();
        aiAssistantSettings.Property(x => x.ApiKeyProtected).HasMaxLength(4096);
        aiAssistantSettings.Property(x => x.RequestTimeoutSeconds).IsRequired();
        aiAssistantSettings.Property(x => x.MaxOutputTokens).IsRequired();
        aiAssistantSettings.Property(x => x.Temperature).IsRequired();
        aiAssistantSettings.Property(x => x.ToolCallingEnabled).IsRequired();
        aiAssistantSettings.Property(x => x.GlobalSystemPrompt).HasColumnType("longtext").IsRequired();
        aiAssistantSettings.Property(x => x.UpdatedAtUtc).IsRequired();
        aiAssistantSettings.Property(x => x.UpdatedByUserId).HasMaxLength(255);

        var notificationSettings = modelBuilder.Entity<NotificationSettings>();
        notificationSettings.ToTable("NotificationSettings");
        notificationSettings.HasKey(x => x.NotificationSettingsId);
        notificationSettings.Property(x => x.NotificationSettingsId).ValueGeneratedNever();
        notificationSettings.Property(x => x.BrowserNotificationsEnabled).IsRequired();
        notificationSettings.Property(x => x.BrowserNotifyEndpointDown).IsRequired();
        notificationSettings.Property(x => x.BrowserNotifyEndpointRecovered).IsRequired();
        notificationSettings.Property(x => x.BrowserNotifyAgentOffline).IsRequired();
        notificationSettings.Property(x => x.BrowserNotifyAgentOnline).IsRequired();
        notificationSettings.Property(x => x.BrowserNotificationsPermissionState).HasMaxLength(16);
        notificationSettings.Property(x => x.TelegramEnabled).IsRequired();
        notificationSettings.Property(x => x.TelegramBotTokenProtected).HasMaxLength(4096);
        notificationSettings.Property(x => x.TelegramInboundMode).HasConversion<string>().HasMaxLength(16).IsRequired();
        notificationSettings.Property(x => x.TelegramPollIntervalSeconds).IsRequired();
        notificationSettings.Property(x => x.TelegramLastProcessedUpdateId).IsRequired();
        notificationSettings.Property(x => x.TelegramWebhookUrl).HasMaxLength(2048);
        notificationSettings.Property(x => x.TelegramWebhookSecretToken).HasMaxLength(512);
        notificationSettings.Property(x => x.QuietHoursEnabled).IsRequired();
        notificationSettings.Property(x => x.QuietHoursStartLocalTime).HasMaxLength(5).IsRequired();
        notificationSettings.Property(x => x.QuietHoursEndLocalTime).HasMaxLength(5).IsRequired();
        notificationSettings.Property(x => x.QuietHoursTimeZoneId).HasMaxLength(128).IsRequired();
        notificationSettings.Property(x => x.QuietHoursSuppressBrowserNotifications).IsRequired();
        notificationSettings.Property(x => x.QuietHoursSuppressSmtpNotifications).IsRequired();
        notificationSettings.Property(x => x.SmtpNotificationsEnabled).IsRequired();
        notificationSettings.Property(x => x.SmtpHost).HasMaxLength(255);
        notificationSettings.Property(x => x.SmtpPort).IsRequired();
        notificationSettings.Property(x => x.SmtpUseTls).IsRequired();
        notificationSettings.Property(x => x.SmtpUsername).HasMaxLength(255);
        notificationSettings.Property(x => x.SmtpPasswordProtected).HasMaxLength(4096);
        notificationSettings.Property(x => x.SmtpFromAddress).HasMaxLength(255);
        notificationSettings.Property(x => x.SmtpFromDisplayName).HasMaxLength(255);
        notificationSettings.Property(x => x.SmtpRecipientAddresses).HasMaxLength(4096);
        notificationSettings.Property(x => x.SmtpNotifyEndpointDown).IsRequired();
        notificationSettings.Property(x => x.SmtpNotifyEndpointRecovered).IsRequired();
        notificationSettings.Property(x => x.SmtpNotifyAgentOffline).IsRequired();
        notificationSettings.Property(x => x.SmtpNotifyAgentOnline).IsRequired();
        notificationSettings.Property(x => x.UpdatedAtUtc).IsRequired();
        notificationSettings.Property(x => x.UpdatedByUserId).HasMaxLength(255);

        var userNotificationSettings = modelBuilder.Entity<UserNotificationSettings>();
        userNotificationSettings.ToTable("UserNotificationSettings");
        userNotificationSettings.HasKey(x => x.UserId);
        userNotificationSettings.Property(x => x.UserId).HasMaxLength(255).ValueGeneratedNever();
        userNotificationSettings.Property(x => x.BrowserNotificationsEnabled).IsRequired();
        userNotificationSettings.Property(x => x.BrowserNotifyEndpointDown).IsRequired();
        userNotificationSettings.Property(x => x.BrowserNotifyEndpointRecovered).IsRequired();
        userNotificationSettings.Property(x => x.BrowserNotifyAgentOffline).IsRequired();
        userNotificationSettings.Property(x => x.BrowserNotifyAgentOnline).IsRequired();
        userNotificationSettings.Property(x => x.BrowserNotificationsPermissionState).HasMaxLength(16);
        userNotificationSettings.Property(x => x.SmtpNotificationsEnabled).IsRequired();
        userNotificationSettings.Property(x => x.SmtpNotifyEndpointDown).IsRequired();
        userNotificationSettings.Property(x => x.SmtpNotifyEndpointRecovered).IsRequired();
        userNotificationSettings.Property(x => x.SmtpNotifyAgentOffline).IsRequired();
        userNotificationSettings.Property(x => x.SmtpNotifyAgentOnline).IsRequired();
        userNotificationSettings.Property(x => x.TelegramNotificationsEnabled).IsRequired();
        userNotificationSettings.Property(x => x.TelegramNotifyEndpointDown).IsRequired();
        userNotificationSettings.Property(x => x.TelegramNotifyEndpointRecovered).IsRequired();
        userNotificationSettings.Property(x => x.TelegramNotifyAgentOffline).IsRequired();
        userNotificationSettings.Property(x => x.TelegramNotifyAgentOnline).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursEnabled).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursStartLocalTime).HasMaxLength(5).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursEndLocalTime).HasMaxLength(5).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursTimeZoneId).HasMaxLength(128).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursSuppressBrowserNotifications).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursSuppressSmtpNotifications).IsRequired();
        userNotificationSettings.Property(x => x.QuietHoursSuppressTelegramNotifications).IsRequired();
        userNotificationSettings.Property(x => x.UpdatedAtUtc).IsRequired();


        var pendingTelegramLink = modelBuilder.Entity<PendingTelegramLink>();
        pendingTelegramLink.ToTable("PendingTelegramLinks");
        pendingTelegramLink.HasKey(x => x.PendingTelegramLinkId);
        pendingTelegramLink.Property(x => x.PendingTelegramLinkId).HasMaxLength(64);
        pendingTelegramLink.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        pendingTelegramLink.Property(x => x.Code).HasMaxLength(16).IsRequired();
        pendingTelegramLink.Property(x => x.CreatedAtUtc).IsRequired();
        pendingTelegramLink.Property(x => x.ExpiresAtUtc).IsRequired();
        pendingTelegramLink.Property(x => x.ConsumedByChatId).HasMaxLength(64);
        pendingTelegramLink.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        pendingTelegramLink.HasIndex(x => new { x.Code, x.Status });

        var telegramAccount = modelBuilder.Entity<TelegramAccount>();
        telegramAccount.ToTable("TelegramAccounts");
        telegramAccount.HasKey(x => x.TelegramAccountId);
        telegramAccount.Property(x => x.TelegramAccountId).HasMaxLength(64);
        telegramAccount.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        telegramAccount.Property(x => x.ChatId).HasMaxLength(64).IsRequired();
        telegramAccount.Property(x => x.Verified).IsRequired();
        telegramAccount.Property(x => x.LinkedAtUtc).IsRequired();
        telegramAccount.Property(x => x.Username).HasMaxLength(255);
        telegramAccount.Property(x => x.DisplayName).HasMaxLength(255);
        telegramAccount.Property(x => x.IsActive).IsRequired();
        telegramAccount.HasIndex(x => x.UserId).IsUnique();
        telegramAccount.HasIndex(x => x.ChatId).IsUnique();

        var networkDiagram = modelBuilder.Entity<NetworkDiagram>();
        networkDiagram.ToTable("NetworkDiagrams");
        networkDiagram.HasKey(x => x.DiagramId);
        networkDiagram.Property(x => x.DiagramId).HasMaxLength(64);
        networkDiagram.Property(x => x.Name).HasMaxLength(255).IsRequired();
        networkDiagram.Property(x => x.Description).HasMaxLength(2048);
        networkDiagram.Property(x => x.CanvasWidth).IsRequired().HasDefaultValue(4000d);
        networkDiagram.Property(x => x.CanvasHeight).IsRequired().HasDefaultValue(2500d);
        networkDiagram.Property(x => x.ViewportPanX).IsRequired();
        networkDiagram.Property(x => x.ViewportPanY).IsRequired();
        networkDiagram.Property(x => x.ViewportZoom).IsRequired().HasDefaultValue(1d);
        networkDiagram.Property(x => x.CreatedAtUtc).IsRequired();
        networkDiagram.Property(x => x.UpdatedAtUtc).IsRequired();
        networkDiagram.Property(x => x.CreatedByUserId).HasMaxLength(255);
        networkDiagram.Property(x => x.UpdatedByUserId).HasMaxLength(255);
        networkDiagram.HasIndex(x => x.Name);

        var networkDiagramArea = modelBuilder.Entity<NetworkDiagramArea>();
        networkDiagramArea.ToTable("NetworkDiagramAreas");
        networkDiagramArea.HasKey(x => x.AreaId);
        networkDiagramArea.Property(x => x.AreaId).HasMaxLength(64);
        networkDiagramArea.Property(x => x.DiagramId).HasMaxLength(64).IsRequired();
        networkDiagramArea.Property(x => x.Label).HasMaxLength(255).IsRequired();
        networkDiagramArea.Property(x => x.Notes).HasMaxLength(2048);
        networkDiagramArea.Property(x => x.StyleKey).HasMaxLength(64);
        networkDiagramArea.Property(x => x.SortOrder).IsRequired();
        networkDiagramArea.Property(x => x.CreatedAtUtc).IsRequired();
        networkDiagramArea.Property(x => x.UpdatedAtUtc).IsRequired();
        networkDiagramArea.HasIndex(x => x.DiagramId);
        networkDiagramArea.HasOne(x => x.Diagram)
            .WithMany(x => x.Areas)
            .HasForeignKey(x => x.DiagramId)
            .OnDelete(DeleteBehavior.Cascade);

        var networkDiagramNode = modelBuilder.Entity<NetworkDiagramNode>();
        networkDiagramNode.ToTable("NetworkDiagramNodes");
        networkDiagramNode.HasKey(x => x.NodeId);
        networkDiagramNode.Property(x => x.NodeId).HasMaxLength(64);
        networkDiagramNode.Property(x => x.DiagramId).HasMaxLength(64).IsRequired();
        networkDiagramNode.Property(x => x.NodeType).HasConversion<string>().HasMaxLength(32).IsRequired();
        networkDiagramNode.Property(x => x.EndpointId).HasMaxLength(64);
        networkDiagramNode.Property(x => x.DisplayLabel).HasMaxLength(255).IsRequired();
        networkDiagramNode.Property(x => x.IconKey).HasMaxLength(64).IsRequired();
        networkDiagramNode.Property(x => x.X).IsRequired();
        networkDiagramNode.Property(x => x.Y).IsRequired();
        networkDiagramNode.Property(x => x.Width).IsRequired().HasDefaultValue(178d);
        networkDiagramNode.Property(x => x.Height).IsRequired().HasDefaultValue(78d);
        networkDiagramNode.Property(x => x.Notes).HasMaxLength(4096);
        networkDiagramNode.Property(x => x.MetadataJson).HasColumnType("longtext");
        networkDiagramNode.Property(x => x.CreatedAtUtc).IsRequired();
        networkDiagramNode.Property(x => x.UpdatedAtUtc).IsRequired();
        networkDiagramNode.HasIndex(x => new { x.DiagramId, x.EndpointId });
        networkDiagramNode.HasOne(x => x.Diagram)
            .WithMany(x => x.Nodes)
            .HasForeignKey(x => x.DiagramId)
            .OnDelete(DeleteBehavior.Cascade);

        var networkDiagramLink = modelBuilder.Entity<NetworkDiagramLink>();
        networkDiagramLink.ToTable("NetworkDiagramLinks");
        networkDiagramLink.HasKey(x => x.LinkId);
        networkDiagramLink.Property(x => x.LinkId).HasMaxLength(64);
        networkDiagramLink.Property(x => x.DiagramId).HasMaxLength(64).IsRequired();
        networkDiagramLink.Property(x => x.SourceNodeId).HasMaxLength(64).IsRequired();
        networkDiagramLink.Property(x => x.TargetNodeId).HasMaxLength(64).IsRequired();
        networkDiagramLink.Property(x => x.Label).HasMaxLength(255);
        networkDiagramLink.Property(x => x.SourcePortLabel).HasMaxLength(128);
        networkDiagramLink.Property(x => x.TargetPortLabel).HasMaxLength(128);
        networkDiagramLink.Property(x => x.Notes).HasMaxLength(4096);
        networkDiagramLink.Property(x => x.MediaType).HasMaxLength(64).IsRequired().HasDefaultValue("Copper");
        networkDiagramLink.Property(x => x.FibreSubtype).HasMaxLength(64);
        networkDiagramLink.Property(x => x.LinkType).HasMaxLength(64).IsRequired().HasDefaultValue("Standard");
        networkDiagramLink.Property(x => x.LinkSpeedValue).HasPrecision(10, 3);
        networkDiagramLink.Property(x => x.LinkSpeedUnit).HasMaxLength(16);
        networkDiagramLink.Property(x => x.LacpMemberPortsJson).HasColumnType("longtext");
        networkDiagramLink.Property(x => x.MetadataJson).HasColumnType("longtext");
        networkDiagramLink.Property(x => x.CreatedAtUtc).IsRequired();
        networkDiagramLink.Property(x => x.UpdatedAtUtc).IsRequired();
        networkDiagramLink.HasIndex(x => x.DiagramId);
        networkDiagramLink.HasIndex(x => new { x.DiagramId, x.SourceNodeId, x.TargetNodeId });
        networkDiagramLink.HasOne(x => x.Diagram)
            .WithMany(x => x.Links)
            .HasForeignKey(x => x.DiagramId)
            .OnDelete(DeleteBehavior.Cascade);


        var networkDiagramLinkVlan = modelBuilder.Entity<NetworkDiagramLinkVlan>();
        networkDiagramLinkVlan.ToTable("NetworkDiagramLinkVlans");
        networkDiagramLinkVlan.HasKey(x => x.LinkVlanId);
        networkDiagramLinkVlan.Property(x => x.LinkVlanId).HasMaxLength(64);
        networkDiagramLinkVlan.Property(x => x.LinkId).HasMaxLength(64).IsRequired();
        networkDiagramLinkVlan.Property(x => x.DiagramId).HasMaxLength(64).IsRequired();
        networkDiagramLinkVlan.Property(x => x.VlanId).IsRequired();
        networkDiagramLinkVlan.Property(x => x.Name).HasMaxLength(128);
        networkDiagramLinkVlan.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        networkDiagramLinkVlan.Property(x => x.Notes).HasMaxLength(512);
        networkDiagramLinkVlan.Property(x => x.SortOrder).IsRequired();
        networkDiagramLinkVlan.Property(x => x.CreatedAtUtc).IsRequired();
        networkDiagramLinkVlan.Property(x => x.UpdatedAtUtc).IsRequired();
        networkDiagramLinkVlan.HasIndex(x => x.DiagramId);
        networkDiagramLinkVlan.HasIndex(x => new { x.LinkId, x.VlanId }).IsUnique();
        networkDiagramLinkVlan.HasOne(x => x.Link)
            .WithMany(x => x.Vlans)
            .HasForeignKey(x => x.LinkId)
            .OnDelete(DeleteBehavior.Cascade);
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
        securitySettings.Property(x => x.SecurityLogRetentionEnabled).IsRequired();
        securitySettings.Property(x => x.SecurityLogRetentionDays).IsRequired();
        securitySettings.Property(x => x.SecurityLogAutoPruneEnabled).IsRequired();
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
