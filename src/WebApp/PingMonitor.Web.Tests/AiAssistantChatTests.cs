using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.AiProviders;
using PingMonitor.Web.ViewModels.AiAssistant;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiAssistantChatTests
{
    [Fact]
    public void Controller_RequiresAuthentication()
    {
        Assert.Single(typeof(AiAssistantController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
    }

    [Fact]
    public async Task AuthenticatedUserCanAccessPageWhenEnabled()
    {
        var controller = CreateController(settings: EnabledSettings());
        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.True(model.AssistantEnabled);
        Assert.True(model.WebChatEnabled);
        Assert.Null(model.ErrorMessage);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DisabledFlagsShowMessageAndDoNotCallProvider(bool assistantEnabled, bool webChatEnabled)
    {
        var provider = new FakeAiProviderClient();
        var controller = CreateController(provider, EnabledSettings(assistantEnabled, webChatEnabled));
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "hello" }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains("disabled", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provider.LastRequest);
    }


    [Fact]
    public async Task TelegramChatDisabledDoesNotCallProviderForTelegramSource()
    {
        var provider = new FakeAiProviderClient();
        var settings = EnabledSettings();
        settings.TelegramChatEnabled = false;
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Telegram, UserMessage = "hello" }, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Contains("Telegram AI chat is disabled", response.ErrorMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task TelegramChatSourceUsesSharedSafetyPromptAndProviderPath()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "I am chat-only.", Model = "llama" } };
        var settings = EnabledSettings(webChatEnabled: false);
        settings.TelegramChatEnabled = true;
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Telegram, UserMessage = "How is my network looking today?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "system" && x.Content.Contains("live monitoring data", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "system" && x.Content.Contains("CheckResults", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyMessageIsRejected(string message)
    {
        var provider = new FakeAiProviderClient();
        var controller = CreateController(provider, EnabledSettings());
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = message }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains("Enter a message", model.ErrorMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task OverlongMessageIsRejected()
    {
        var provider = new FakeAiProviderClient();
        var controller = CreateController(provider, EnabledSettings());
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = new string('x', 4001) }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains("4000", model.ErrorMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task SuccessfulProviderResponseIsDisplayedAndHistoryIncludedOnFollowUp()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "Hello human", Model = "llama" } };
        var controller = CreateController(provider, EnabledSettings(globalPrompt: "Use short replies."));
        var first = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "Hello" }, CancellationToken.None));
        var firstModel = Assert.IsType<AiAssistantPageViewModel>(first.Model);
        Assert.Contains(firstModel.Messages, x => x.Role == "assistant" && x.Content == "Hello human");

        provider.Result = new AiProviderChatResult { Succeeded = true, ResponseText = "I remember you said Hello", Model = "llama" };
        await controller.Send(new AiAssistantPageViewModel { Message = "What did I say?", HistoryJson = firstModel.HistoryJson }, CancellationToken.None);

        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "user" && x.Content == "Hello");
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "assistant" && x.Content == "Hello human");
    }

    [Fact]
    public async Task ProviderFailureIsDisplayedClearly()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = false, ErrorMessage = "boom" } };
        var controller = CreateController(provider, EnabledSettings());
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "hello" }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains("AI provider did not return", model.ErrorMessage);
    }

    [Fact]
    public void ConversationHistoryIsBounded()
    {
        var messages = Enumerable.Range(0, 30).Select(i => new AiChatMessageDto { Role = i % 2 == 0 ? "user" : "assistant", Content = $"m{i}" });
        var bounded = AiAssistantController.BoundHistory(messages);
        Assert.Equal(20, bounded.Count);
        Assert.Equal("m10", bounded[0].Content);
    }

    [Fact]
    public void PromptLayeringIncludesSafetyPromptBeforeAdminPrompt()
    {
        var messages = AiChatService.BuildMessages("Site guidance", [], "How is my network looking today?");
        Assert.Contains("do not currently have access to live monitoring data", messages[0].Content);
        Assert.Contains("CheckResults", messages[0].Content);
        Assert.Contains("tools", messages[0].Content);
        Assert.Contains("Admin-configured site instructions", messages[1].Content);
        Assert.Contains("Site guidance", messages[1].Content);
    }

    [Fact]
    public async Task ApiKeyIsNotExposedInRenderedModelOrPrompt()
    {
        var provider = new FakeAiProviderClient();
        var controller = CreateController(provider, EnabledSettings());
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "hello" }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.DoesNotContain("runtime-secret", model.HistoryJson ?? string.Empty);
        Assert.DoesNotContain(provider.LastRequest!.Messages, x => x.Content.Contains("runtime-secret", StringComparison.Ordinal));
    }

    private static AiAssistantController CreateController(AiProviderChatResult? result = null, AiAssistantSettingsDto? settings = null)
    {
        var provider = new FakeAiProviderClient();
        if (result is not null) provider.Result = result;
        return CreateController(provider, settings ?? EnabledSettings());
    }

    private static AiAssistantController CreateController(FakeAiProviderClient provider, AiAssistantSettingsDto settings)
    {
        var settingsService = new FakeAiAssistantSettingsService(settings);
        var chat = new AiChatService(settingsService, provider, NullLogger<AiChatService>.Instance);
        return new AiAssistantController(settingsService, chat);
    }

    private static AiAssistantSettingsDto EnabledSettings(bool assistantEnabled = true, bool webChatEnabled = true, string globalPrompt = "") => new()
    {
        AssistantEnabled = assistantEnabled,
        WebChatEnabled = webChatEnabled,
        ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
        ProviderDisplayName = "Local Ollama",
        BaseUrl = "http://localhost:11434/v1",
        ModelName = "llama",
        RequestTimeoutSeconds = 180,
        MaxOutputTokens = 2048,
        Temperature = 0.2,
        GlobalSystemPrompt = globalPrompt,
        TelegramChatEnabled = true
    };

    private sealed class FakeAiAssistantSettingsService : IAiAssistantSettingsService
    {
        private readonly AiAssistantSettingsDto _settings;
        public FakeAiAssistantSettingsService(AiAssistantSettingsDto settings) => _settings = settings;
        public Task<AiAssistantSettingsDto> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);
        public Task<AiProviderRuntimeSettingsDto> GetProviderRuntimeSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new AiProviderRuntimeSettingsDto
        {
            ProviderDisplayName = _settings.ProviderDisplayName,
            ProviderType = _settings.ProviderType,
            BaseUrl = _settings.BaseUrl,
            ModelName = _settings.ModelName,
            ApiKey = "runtime-secret",
            RequestTimeoutSeconds = _settings.RequestTimeoutSeconds,
            MaxOutputTokens = _settings.MaxOutputTokens,
            Temperature = _settings.Temperature,
            ToolCallingEnabled = _settings.ToolCallingEnabled
        });
        public Task<AiAssistantSettingsDto> UpdateAsync(UpdateAiAssistantSettingsCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAiProviderClient : IAiProviderClient
    {
        public AiProviderChatRequest? LastRequest { get; private set; }
        public AiProviderChatResult Result { get; set; } = new() { Succeeded = true, ResponseText = "assistant response", Model = "llama" };
        public Task<AiProviderChatResult> SendChatAsync(AiProviderChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }
}

public sealed class TelegramAiConversationStoreTests
{
    [Fact]
    public void TelegramConversationHistoryIsBoundedToLastTenTurns()
    {
        using var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var store = new PingMonitor.Web.Services.Telegram.TelegramAiConversationStore(memoryCache);

        for (var i = 0; i < 12; i++)
        {
            store.AddTurn("chat-1", "user-1", $"user {i}", $"assistant {i}");
        }

        var history = store.GetHistory("chat-1", "user-1");
        Assert.Equal(20, history.Count);
        Assert.Equal("user 2", history[0].Content);
        Assert.Equal("assistant 11", history[^1].Content);
    }

    [Fact]
    public void ClearOnlyRemovesMatchingTelegramChatAndUserHistory()
    {
        using var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var store = new PingMonitor.Web.Services.Telegram.TelegramAiConversationStore(memoryCache);
        store.AddTurn("chat-1", "user-1", "hello", "hi");
        store.AddTurn("chat-1", "user-2", "other", "there");

        store.Clear("chat-1", "user-1");

        Assert.Empty(store.GetHistory("chat-1", "user-1"));
        Assert.NotEmpty(store.GetHistory("chat-1", "user-2"));
    }
}
