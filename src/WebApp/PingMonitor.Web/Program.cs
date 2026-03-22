using PingMonitor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<IAgentAuthenticationService, PlaceholderAgentAuthenticationService>();
builder.Services.AddSingleton<IAgentConfigurationService, PlaceholderAgentConfigurationService>();
builder.Services.AddSingleton<IResultIngestionService, PlaceholderResultIngestionService>();
builder.Services.AddSingleton<IHeartbeatService, PlaceholderHeartbeatService>();
builder.Services.AddSingleton<IStateEvaluationService, PlaceholderStateEvaluationService>();

var app = builder.Build();

app.UseHttpsRedirection();

// TODO: Add agent authentication middleware once server-side credential validation is implemented.
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
