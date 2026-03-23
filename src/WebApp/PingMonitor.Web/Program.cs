using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services;
using PingMonitor.Web.Support;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<AgentApiOptions>(builder.Configuration.GetSection(AgentApiOptions.SectionName));
builder.Services.Configure<DevelopmentSeedAgentOptions>(builder.Configuration.GetSection(DevelopmentSeedAgentOptions.SectionName));

builder.Services.AddDbContext<PingMonitorDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("MonitoringDatabase")));

builder.Services.AddControllers()
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
builder.Services.AddScoped<DevelopmentAgentSeeder>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    if (app.Environment.IsDevelopment())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentAgentSeeder>();
        await seeder.SeedAsync();
    }
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
