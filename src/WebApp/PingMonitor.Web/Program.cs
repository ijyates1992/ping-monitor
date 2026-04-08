using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using System.Net;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Agents;
using PingMonitor.Web.Services.Backups;
using PingMonitor.Web.Services.BufferedResults;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Groups;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.State;
using PingMonitor.Web.Services.Background;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.Status;
using PingMonitor.Web.Services.Security;
using PingMonitor.Web.Services.SmtpNotifications;
using PingMonitor.Web.Services.BrowserNotifications;
using PingMonitor.Web.Services.DatabaseStatus;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Telegram;
using PingMonitor.Web.Support;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<AgentApiOptions>(builder.Configuration.GetSection(AgentApiOptions.SectionName));
builder.Services.Configure<DevelopmentSeedAgentOptions>(builder.Configuration.GetSection(DevelopmentSeedAgentOptions.SectionName));
builder.Services.Configure<StartupGateOptions>(builder.Configuration.GetSection(StartupGateOptions.SectionName));
builder.Services.Configure<AgentProvisioningOptions>(builder.Configuration.GetSection(AgentProvisioningOptions.SectionName));
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<ResultBufferOptions>(builder.Configuration.GetSection(ResultBufferOptions.SectionName));
builder.Services.Configure<DatabaseMaintenanceOptions>(builder.Configuration.GetSection(DatabaseMaintenanceOptions.SectionName));
builder.Services.Configure<ApplicationMetadataOptions>(builder.Configuration.GetSection(ApplicationMetadataOptions.SectionName));

builder.Services.AddSingleton<IDbContextFactory<PingMonitorDbContext>, DynamicPingMonitorDbContextFactory>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PingMonitorDbContext>>().CreateDbContext());
builder.Services.AddSingleton<IDbActivityTracker, DbActivityTracker>();
builder.Services.AddSingleton<IDbActivityScope, DbActivityScope>();
builder.Services.AddSingleton<DbActivityCommandInterceptor>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<PingMonitorDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter());
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiErrorDetail(
                ModelStateFieldNameFormatter.ToCamelCase(entry.Key),
                string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value is invalid." : error.ErrorMessage)))
            .ToArray();

        return ApiErrorResponses.BadRequest(
            context.HttpContext,
            "invalid_request",
            "One or more fields are invalid.",
            details);
    };
});

builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;
    options.RequireHeaderSymmetry = true;

    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

    if (knownProxies.Length > 0 || knownNetworks.Length > 0)
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var proxy in knownProxies)
        {
            if (!IPAddress.TryParse(proxy?.Trim(), out var parsedProxy))
            {
                throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownProxies entry '{proxy}'.");
            }

            options.KnownProxies.Add(parsedProxy);
        }

        foreach (var network in knownNetworks)
        {
            var normalized = network?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!System.Net.IPNetwork.TryParse(normalized, out var parsedNetwork))
            {
                throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownNetworks entry '{network}'. Use CIDR format (for example 10.0.0.0/8).");
            }

            options.KnownIPNetworks.Add(parsedNetwork);
        }
    }
});

builder.Services.AddScoped<IAgentApiKeyHasher, AgentApiKeyHasher>();
builder.Services.AddScoped<IAgentAuthenticationService, AgentAuthenticationService>();
builder.Services.AddScoped<IAgentHelloService, AgentHelloService>();
builder.Services.AddScoped<IAgentConfigurationService, AgentConfigurationService>();
builder.Services.AddScoped<IResultIngestionService, ResultIngestionService>();
builder.Services.AddSingleton<IngestRateTracker>();
builder.Services.AddSingleton<IBufferedResultIngestionService, BufferedResultIngestionService>();
builder.Services.AddScoped<IHeartbeatService, AgentHeartbeatService>();
builder.Services.AddScoped<IStateEvaluationService, StateEvaluationService>();
builder.Services.AddSingleton<IAssignmentTopologyCache, AssignmentTopologyCache>();
builder.Services.AddSingleton<IAssignmentCurrentStateCache, AssignmentCurrentStateCache>();
builder.Services.AddSingleton<IAssignmentProcessingQueue, AssignmentProcessingQueue>();
builder.Services.AddScoped<IEventLogService, EventLogService>();
builder.Services.AddScoped<IEventLogQueryService, EventLogQueryService>();
builder.Services.AddScoped<ISecurityAuthLogService, SecurityAuthLogService>();
builder.Services.AddScoped<ISecurityAuthLogQueryService, SecurityAuthLogQueryService>();
builder.Services.AddScoped<ISecuritySettingsService, SecuritySettingsService>();
builder.Services.AddScoped<ISecurityLogRetentionService, SecurityLogRetentionService>();
builder.Services.AddScoped<ISecurityIpBlockService, SecurityIpBlockService>();
builder.Services.AddScoped<ISecurityOperatorActionService, SecurityOperatorActionService>();
builder.Services.AddScoped<ISecurityEnforcementService, SecurityEnforcementService>();
builder.Services.AddScoped<IEndpointStatusQueryService, EndpointStatusQueryService>();
builder.Services.AddScoped<IStartupGateService, StartupGateService>();
builder.Services.AddSingleton<IStartupGateRuntimeState, StartupGateRuntimeState>();
builder.Services.AddSingleton<IStartupGateDiagnosticsLogger, StartupGateDiagnosticsLogger>();
builder.Services.AddSingleton<IStartupDatabaseConfigurationStore, FileStartupDatabaseConfigurationStore>();
builder.Services.AddSingleton<ILocalRequestEvaluator, LocalRequestEvaluator>();
builder.Services.AddScoped<IStartupSchemaService, StartupSchemaService>();
builder.Services.AddScoped<IStartupAdminBootstrapService, StartupAdminBootstrapService>();
builder.Services.AddScoped<DevelopmentAgentSeeder>();
builder.Services.AddScoped<IAgentPackageBuilder, AgentPackageBuilder>();
builder.Services.AddScoped<IAgentProvisioningService, AgentProvisioningService>();
builder.Services.AddScoped<IApplicationSettingsService, ApplicationSettingsService>();
builder.Services.AddSingleton<IApplicationMetadataProvider, ApplicationMetadataProvider>();
builder.Services.AddScoped<IDatabaseStatusQueryService, DatabaseStatusQueryService>();
builder.Services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
builder.Services.AddSingleton<IDatabaseMaintenanceProgressTracker, DatabaseMaintenanceProgressTracker>();
builder.Services.AddScoped<INotificationSettingsService, NotificationSettingsService>();
builder.Services.AddScoped<IUserNotificationSettingsService, UserNotificationSettingsService>();
builder.Services.AddScoped<INotificationSuppressionService, NotificationSuppressionService>();
builder.Services.AddScoped<ISmtpNotificationSender, SmtpNotificationSender>();
builder.Services.AddScoped<IBrowserNotificationQueryService, BrowserNotificationQueryService>();
builder.Services.AddScoped<ITelegramLinkService, TelegramLinkService>();
builder.Services.AddScoped<ITelegramBotIdentityResolver, TelegramBotIdentityResolver>();
builder.Services.AddScoped<ITelegramMessageProcessor, TelegramMessageProcessor>();
builder.Services.AddScoped<ITelegramPollingService, TelegramPollingService>();
builder.Services.AddScoped<ITelegramNotificationSender, TelegramNotificationSender>();
builder.Services.AddScoped<IEndpointCreationQueryService, EndpointCreationQueryService>();
builder.Services.AddScoped<IEndpointManagementQueryService, EndpointManagementQueryService>();
builder.Services.AddScoped<IEndpointManagementService, EndpointManagementService>();
builder.Services.AddScoped<IEndpointPerformanceQueryService, EndpointPerformanceQueryService>();
builder.Services.AddScoped<IGroupManagementService, GroupManagementService>();
builder.Services.AddScoped<IAgentManagementQueryService, AgentManagementQueryService>();
builder.Services.AddSingleton<IRollingAssignmentWindowStore, RollingAssignmentWindowStore>();
builder.Services.AddScoped<IAssignmentMetrics24hService, AssignmentMetrics24hService>();
builder.Services.AddScoped<IAgentMetricsService, AgentMetricsService>();
builder.Services.AddScoped<IUserAccessScopeService, UserAccessScopeService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IConfigurationBackupFileNameGenerator, ConfigurationBackupFileNameGenerator>();
builder.Services.AddScoped<IConfigurationBackupService, ConfigurationBackupService>();
builder.Services.AddScoped<IConfigurationBackupDocumentValidator, ConfigurationBackupDocumentValidator>();
builder.Services.AddSingleton<IConfigurationBackupCatalogService, ConfigurationBackupCatalogService>();
builder.Services.AddScoped<IConfigurationBackupDocumentLoader, ConfigurationBackupDocumentLoader>();
builder.Services.AddScoped<IConfigurationBackupQueryService, ConfigurationBackupQueryService>();
builder.Services.AddScoped<IConfigurationBackupUploadService, ConfigurationBackupUploadService>();
builder.Services.AddScoped<IConfigurationBackupManagementService, ConfigurationBackupManagementService>();
builder.Services.AddScoped<IConfigurationBackupRetentionService, ConfigurationBackupRetentionService>();
builder.Services.AddScoped<IConfigurationRestorePreviewService, ConfigurationRestorePreviewService>();
builder.Services.AddScoped<IConfigurationRestoreService, ConfigurationRestoreService>();
builder.Services.AddSingleton<ConfigurationAutoBackupBackgroundService>();
builder.Services.AddSingleton<IConfigurationChangeBackupSignal>(sp => sp.GetRequiredService<ConfigurationAutoBackupBackgroundService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConfigurationAutoBackupBackgroundService>());
builder.Services.AddHostedService<AgentStatusTransitionBackgroundService>();
builder.Services.AddHostedService<BufferedResultFlushBackgroundService>();
builder.Services.AddHostedService<AssignmentProcessingBackgroundService>();
builder.Services.AddHostedService<TelegramPollingBackgroundService>();

var app = builder.Build();

var identityCookieOptionsMonitor = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
var identityApplicationCookieName = identityCookieOptionsMonitor.Get(IdentityConstants.ApplicationScheme).Cookie?.Name;

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    var startupGateService = context.RequestServices.GetRequiredService<IStartupGateService>();
    var status = await startupGateService.EvaluateAsync(context, context.RequestAborted);
    context.Items[typeof(StartupGateStatus).FullName!] = status;

    var startupGateRuntimeState = context.RequestServices.GetRequiredService<IStartupGateRuntimeState>();
    startupGateRuntimeState.Update(status);

    if (status.Mode == StartupMode.Normal)
    {
        await next();
        return;
    }

    if (context.Request.Path.StartsWithSegments("/startup-gate", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrWhiteSpace(identityApplicationCookieName) &&
            context.Request.Headers.TryGetValue("Cookie", out var cookieHeaderValues))
        {
            var filteredCookieHeaders = cookieHeaderValues
                .Select(cookieHeader =>
                {
                    var nonNullCookieHeader = cookieHeader ?? string.Empty;
                    return string.Join("; ", nonNullCookieHeader
                        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Where(cookiePart => !cookiePart.StartsWith($"{identityApplicationCookieName}=", StringComparison.Ordinal)));
                })
                .Where(cookieHeader => !string.IsNullOrWhiteSpace(cookieHeader))
                .ToArray();

            if (filteredCookieHeaders.Length > 0)
            {
                context.Request.Headers["Cookie"] = filteredCookieHeaders;
            }
            else
            {
                context.Request.Headers.Remove("Cookie");
            }
        }

        await next();
        return;
    }

    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.Redirect("/startup-gate");
        return;
    }

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    await context.Response.WriteAsync("Startup gate is active. Use local loopback access to complete setup.");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Status}/{action=Index}/{id?}");

app.Run();
