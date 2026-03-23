using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Support;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<AgentApiOptions>(builder.Configuration.GetSection(AgentApiOptions.SectionName));
builder.Services.Configure<DevelopmentSeedAgentOptions>(builder.Configuration.GetSection(DevelopmentSeedAgentOptions.SectionName));
builder.Services.Configure<StartupGateOptions>(builder.Configuration.GetSection(StartupGateOptions.SectionName));

builder.Services.AddDbContextFactory<PingMonitorDbContext>((serviceProvider, options) =>
{
    var configurationStore = serviceProvider.GetRequiredService<IStartupDatabaseConfigurationStore>();
    var startupGateOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StartupGateOptions>>().Value;
    var configuration = configurationStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
    var password = configuration is null ? null : configurationStore.LoadPasswordAsync(CancellationToken.None).GetAwaiter().GetResult();
    var connectionString = configuration is not null && configuration.IsComplete && !string.IsNullOrWhiteSpace(password)
        ? configurationStore.BuildConnectionString(configuration, password)
        : $"Server=localhost;Port={startupGateOptions.DefaultMySqlPort};Database=pingmonitor_placeholder;User ID=placeholder;Password=placeholder;SslMode=Preferred;AllowPublicKeyRetrieval=True";

    options.UseMySQL(connectionString);
});

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
    .AddDefaultTokenProviders();

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

builder.Services.AddScoped<IAgentApiKeyHasher, AgentApiKeyHasher>();
builder.Services.AddScoped<IAgentAuthenticationService, AgentAuthenticationService>();
builder.Services.AddScoped<IAgentHelloService, AgentHelloService>();
builder.Services.AddScoped<IAgentConfigurationService, AgentConfigurationService>();
builder.Services.AddScoped<IResultIngestionService, ResultIngestionService>();
builder.Services.AddScoped<IHeartbeatService, AgentHeartbeatService>();
builder.Services.AddScoped<IStateEvaluationService, PlaceholderStateEvaluationService>();
builder.Services.AddScoped<IStartupGateService, StartupGateService>();
builder.Services.AddSingleton<IStartupDatabaseConfigurationStore, FileStartupDatabaseConfigurationStore>();
builder.Services.AddSingleton<ILocalRequestEvaluator, LocalRequestEvaluator>();
builder.Services.AddScoped<IStartupSchemaService, StartupSchemaService>();
builder.Services.AddScoped<IStartupAdminBootstrapService, StartupAdminBootstrapService>();
builder.Services.AddScoped<DevelopmentAgentSeeder>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
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

app.MapControllers();
app.MapDefaultControllerRoute();

app.Run();
