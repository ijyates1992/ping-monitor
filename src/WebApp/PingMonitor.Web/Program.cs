using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Agents;
using PingMonitor.Web.Services.Backups;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Groups;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.Status;
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

builder.Services.AddSingleton<IDbContextFactory<PingMonitorDbContext>, DynamicPingMonitorDbContextFactory>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PingMonitorDbContext>>().CreateDbContext());

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
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddScoped<IAgentApiKeyHasher, AgentApiKeyHasher>();
builder.Services.AddScoped<IAgentAuthenticationService, AgentAuthenticationService>();
builder.Services.AddScoped<IAgentHelloService, AgentHelloService>();
builder.Services.AddScoped<IAgentConfigurationService, AgentConfigurationService>();
builder.Services.AddScoped<IResultIngestionService, ResultIngestionService>();
builder.Services.AddScoped<IHeartbeatService, AgentHeartbeatService>();
builder.Services.AddScoped<IStateEvaluationService, StateEvaluationService>();
builder.Services.AddScoped<IEventLogService, EventLogService>();
builder.Services.AddScoped<IEventLogQueryService, EventLogQueryService>();
builder.Services.AddScoped<IEndpointStatusQueryService, EndpointStatusQueryService>();
builder.Services.AddScoped<IStartupGateService, StartupGateService>();
builder.Services.AddSingleton<IStartupDatabaseConfigurationStore, FileStartupDatabaseConfigurationStore>();
builder.Services.AddSingleton<ILocalRequestEvaluator, LocalRequestEvaluator>();
builder.Services.AddScoped<IStartupSchemaService, StartupSchemaService>();
builder.Services.AddScoped<IStartupAdminBootstrapService, StartupAdminBootstrapService>();
builder.Services.AddScoped<DevelopmentAgentSeeder>();
builder.Services.AddScoped<IAgentPackageBuilder, AgentPackageBuilder>();
builder.Services.AddScoped<IAgentProvisioningService, AgentProvisioningService>();
builder.Services.AddScoped<IApplicationSettingsService, ApplicationSettingsService>();
builder.Services.AddScoped<IEndpointCreationQueryService, EndpointCreationQueryService>();
builder.Services.AddScoped<IEndpointManagementQueryService, EndpointManagementQueryService>();
builder.Services.AddScoped<IEndpointManagementService, EndpointManagementService>();
builder.Services.AddScoped<IEndpointPerformanceQueryService, EndpointPerformanceQueryService>();
builder.Services.AddScoped<IGroupManagementService, GroupManagementService>();
builder.Services.AddScoped<IAgentManagementQueryService, AgentManagementQueryService>();
builder.Services.AddScoped<IEndpointMetricsService, EndpointMetricsService>();
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

var app = builder.Build();

var identityCookieOptionsMonitor = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
var identityApplicationCookieName = identityCookieOptionsMonitor.Get(IdentityConstants.ApplicationScheme).Cookie.Name;

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    var startupGateService = context.RequestServices.GetRequiredService<IStartupGateService>();
    var status = await startupGateService.EvaluateAsync(context, context.RequestAborted);
    context.Items[typeof(StartupGateStatus).FullName!] = status;

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
                .Select(cookieHeader => string.Join("; ", cookieHeader
                    .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(cookiePart => !cookiePart.StartsWith($"{identityApplicationCookieName}=", StringComparison.Ordinal))))
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
