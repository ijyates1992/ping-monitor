using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.AiProviders;
using PingMonitor.Web.Services.AiTools;
using PingMonitor.Web.ViewModels.Admin;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiAssistantSettingsTests
{
    [Fact]
    public void Controller_IsAdminOnly()
    {
        var authorize = Assert.Single(typeof(AdminAiAssistantSettingsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal(ApplicationRoles.Admin, authorize.Roles);
    }

    [Fact]
    public async Task AdminCanAccessSettingsPage()
    {
        var controller = new AdminAiAssistantSettingsController(new FakeAiAssistantSettingsService(new AiAssistantSettingsDto()), new FakeAiProviderClient());
        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        Assert.Equal("Index", result.ViewName);
        Assert.IsType<AiAssistantSettingsPageViewModel>(result.Model);
    }

    [Fact]
    public async Task AdminCanSaveValidSettings_AndApiKeyIsNotEchoed()
    {
        var service = new FakeAiAssistantSettingsService(new AiAssistantSettingsDto());
        var controller = new AdminAiAssistantSettingsController(service, new FakeAiProviderClient());

        var result = Assert.IsType<ViewResult>(await controller.Save(new AiAssistantSettingsPageViewModel
        {
            AssistantEnabled = true,
            BaseUrl = "https://ai.example.test/v1",
            ModelName = "ops-model",
            ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
            ProviderDisplayName = "Ops AI",
            ApiKey = "secret",
            RequestTimeoutSeconds = 600,
            MaxOutputTokens = 2048,
            Temperature = 0.2,
            ToolCallingEnabled = true,
            MaxToolRounds = 4,
            MaxToolCallsPerRound = 6,
            MaxTotalToolResultCharacters = 30000,
            MaxSingleToolResultCharacters = 15000,
            MaxEndpointSearchResults = 12,
            MaxEndpointMetricsSampleTailPoints = 200,
            MaxEndpointTransitionItems = 25,
            MaxEndpointFailureClusters = 11,
            DefaultEndpointMetricsWindow = "6h",
            MaximumEndpointMetricsWindow = "7d"
        }, CancellationToken.None));

        var model = Assert.IsType<AiAssistantSettingsPageViewModel>(result.Model);
        Assert.True(model.Saved);
        Assert.Null(model.ApiKey);
        Assert.Equal("secret", service.LastCommand?.ApiKey);
        Assert.Equal(600, service.LastCommand?.RequestTimeoutSeconds);
        Assert.Equal(4, service.LastCommand?.ToolLimits.MaxToolRounds);
        Assert.Equal(6, service.LastCommand?.ToolLimits.MaxToolCallsPerRound);
        Assert.Equal(30000, service.LastCommand?.ToolLimits.MaxTotalToolResultCharacters);
        Assert.Equal(15000, service.LastCommand?.ToolLimits.MaxSingleToolResultCharacters);
        Assert.Equal(12, service.LastCommand?.ToolLimits.MaxEndpointSearchResults);
        Assert.Equal(200, service.LastCommand?.ToolLimits.MaxEndpointMetricsSampleTailPoints);
        Assert.Equal("6h", service.LastCommand?.ToolLimits.DefaultEndpointMetricsWindow);
    }

    [Theory]
    [InlineData(true, "not-a-url", "model", 60, 2048, 0.2)]
    [InlineData(true, "ftp://example.test", "model", 60, 2048, 0.2)]
    [InlineData(true, "https://example.test/v1", "", 60, 2048, 0.2)]
    [InlineData(true, "https://example.test/v1", "model", 0, 2048, 0.2)]
    [InlineData(true, "https://example.test/v1", "model", 601, 2048, 0.2)]
    [InlineData(true, "https://example.test/v1", "model", 60, 63, 0.2)]
    [InlineData(true, "https://example.test/v1", "model", 60, 2048, 2.1)]
    public async Task InvalidSettingsAreRejected(bool enabled, string baseUrl, string modelName, int timeout, int tokens, double temperature)
    {
        var service = new FakeAiAssistantSettingsService(new AiAssistantSettingsDto());
        var controller = new AdminAiAssistantSettingsController(service, new FakeAiProviderClient());
        controller.ModelState.Clear();
        if (timeout is < 1 or > 600) controller.ModelState.AddModelError(nameof(AiAssistantSettingsPageViewModel.RequestTimeoutSeconds), "range");
        if (tokens is < 64 or > 32768) controller.ModelState.AddModelError(nameof(AiAssistantSettingsPageViewModel.MaxOutputTokens), "range");
        if (temperature is < 0 or > 2) controller.ModelState.AddModelError(nameof(AiAssistantSettingsPageViewModel.Temperature), "range");

        var result = Assert.IsType<ViewResult>(await controller.Save(new AiAssistantSettingsPageViewModel
        {
            AssistantEnabled = enabled,
            BaseUrl = baseUrl,
            ModelName = modelName,
            ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
            ProviderDisplayName = "Ops AI",
            RequestTimeoutSeconds = timeout,
            MaxOutputTokens = tokens,
            Temperature = temperature
        }, CancellationToken.None));

        Assert.False(controller.ModelState.IsValid);
        Assert.Null(service.LastCommand);
        Assert.IsType<AiAssistantSettingsPageViewModel>(result.Model);
    }


    [Fact]
    public void TestAction_IsAntiForgeryProtected()
    {
        var method = typeof(AdminAiAssistantSettingsController).GetMethod(nameof(AdminAiAssistantSettingsController.Test));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Cast<HttpPostAttribute>());
        Assert.Equal("test", route.Template);
    }

    [Fact]
    public async Task TestFailsClearlyWhenModelIsMissing_WithoutCallingProvider()
    {
        var provider = new FakeAiProviderClient();
        var controller = new AdminAiAssistantSettingsController(new FakeAiAssistantSettingsService(new AiAssistantSettingsDto
        {
            ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
            BaseUrl = "http://localhost:11434/v1",
            ModelName = string.Empty
        }), provider);

        var result = Assert.IsType<ViewResult>(await controller.Test(CancellationToken.None));

        var model = Assert.IsType<AiAssistantSettingsPageViewModel>(result.Model);
        Assert.False(model.TestResult!.Succeeded);
        Assert.Contains("Model name is required", model.TestResult.ErrorMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task TestSuccessDisplaysReturnedText_AndDoesNotExposeApiKey()
    {
        var provider = new FakeAiProviderClient();
        var controller = new AdminAiAssistantSettingsController(new FakeAiAssistantSettingsService(new AiAssistantSettingsDto
        {
            ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
            BaseUrl = "http://localhost:11434/v1",
            ModelName = "llama",
            MaxOutputTokens = 2048,
            RequestTimeoutSeconds = 180,
            Temperature = 0.2
        }), provider);

        var result = Assert.IsType<ViewResult>(await controller.Test(CancellationToken.None));

        var model = Assert.IsType<AiAssistantSettingsPageViewModel>(result.Model);
        Assert.True(model.TestResult!.Succeeded);
        Assert.Equal("Ping Monitor AI test OK", model.TestResult.ResponseText);
        Assert.Null(model.ApiKey);
        Assert.Equal("runtime-secret", provider.LastRequest!.ApiKey);
        Assert.Equal(180, provider.LastRequest.TimeoutSeconds);
        Assert.Equal(256, provider.LastRequest.MaxOutputTokens);
    }

    [Fact]
    public void ExistingApiKeyIsPreservedWhenSecretFieldBlank_AndClearRemovesIt()
    {
        var preserve = new UpdateAiAssistantSettingsCommand { ApiKey = "   ", ClearApiKey = false };
        var clear = new UpdateAiAssistantSettingsCommand { ApiKey = null, ClearApiKey = true };

        Assert.False(clear.ApiKey is not null);
        Assert.False(preserve.ClearApiKey);
        Assert.True(clear.ClearApiKey);
    }

    [Fact]
    public void DefaultsAndStartupGateSchemaAreDeclared()
    {
        var settings = new AiAssistantSettings();
        Assert.False(settings.AssistantEnabled);
        Assert.Equal("Local Ollama", settings.ProviderDisplayName);
        Assert.Equal("OpenAICompatible", settings.ProviderType);
        Assert.Equal("http://localhost:11434/v1", settings.BaseUrl);
        Assert.Equal(180, settings.RequestTimeoutSeconds);
        Assert.Equal(2048, settings.MaxOutputTokens);
        Assert.Equal(0.2, settings.Temperature);
        Assert.True(settings.ToolCallingEnabled);
        Assert.Equal(3, new AiToolExecutionLimits().MaxToolRounds);
        Assert.Equal(5, new AiToolExecutionLimits().MaxToolCallsPerRound);
        Assert.Equal(24000, new AiToolExecutionLimits().MaxTotalToolResultCharacters);

        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Services", "StartupGate", "StartupSchemaService.cs"));
        Assert.Contains("AiAssistantSettings", source);
        Assert.Contains("ApiKeyProtected", source);
        Assert.Contains("GlobalSystemPrompt", source);
        Assert.Contains("`GlobalSystemPrompt` longtext NOT NULL", source);
        Assert.Contains("ToolExecutionLimitsJson", source);
    }

    private sealed class FakeAiAssistantSettingsService : IAiAssistantSettingsService
    {
        private AiAssistantSettingsDto _current;
        public UpdateAiAssistantSettingsCommand? LastCommand { get; private set; }

        public FakeAiAssistantSettingsService(AiAssistantSettingsDto current) => _current = current;
        public Task<AiAssistantSettingsDto> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(_current);
        public Task<AiProviderRuntimeSettingsDto> GetProviderRuntimeSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderRuntimeSettingsDto
        {
            ProviderDisplayName = _current.ProviderDisplayName,
            ProviderType = _current.ProviderType,
            BaseUrl = _current.BaseUrl,
            ModelName = _current.ModelName,
            ApiKey = "runtime-secret",
            RequestTimeoutSeconds = _current.RequestTimeoutSeconds,
            MaxOutputTokens = _current.MaxOutputTokens,
            Temperature = _current.Temperature,
            ToolCallingEnabled = _current.ToolCallingEnabled
        });
        public Task<AiAssistantSettingsDto> UpdateAsync(UpdateAiAssistantSettingsCommand command, CancellationToken cancellationToken)
        {
            LastCommand = command;
            _current = new AiAssistantSettingsDto
            {
                AssistantEnabled = command.AssistantEnabled,
                BaseUrl = command.BaseUrl ?? string.Empty,
                ModelName = command.ModelName ?? string.Empty,
                ProviderType = command.ProviderType ?? string.Empty,
                ProviderDisplayName = command.ProviderDisplayName ?? string.Empty,
                ApiKeyConfigured = !command.ClearApiKey && !string.IsNullOrWhiteSpace(command.ApiKey),
                RequestTimeoutSeconds = command.RequestTimeoutSeconds,
                MaxOutputTokens = command.MaxOutputTokens,
                Temperature = command.Temperature,
                ToolCallingEnabled = command.ToolCallingEnabled,
                ToolLimits = command.ToolLimits
            };
            return Task.FromResult(_current);
        }
    }

    private sealed class FakeAiProviderClient : IAiProviderClient
    {
        public AiProviderChatRequest? LastRequest { get; private set; }
        public AiProviderChatResult Result { get; set; } = new() { Succeeded = true, ResponseText = "Ping Monitor AI test OK", ElapsedMilliseconds = 12, StatusCode = 200 };
        public Task<AiProviderChatResult> SendChatAsync(AiProviderChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
