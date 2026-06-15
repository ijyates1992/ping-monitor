using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.AiTools;
using PingMonitor.Web.Services.AiProviders;
using PingMonitor.Web.Services.Telegram;
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
        var monitoring = new FakeAiMonitoringContextService();
        var controller = CreateController(provider, EnabledSettings(assistantEnabled, webChatEnabled), monitoring);
        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "hello" }, CancellationToken.None));
        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains("disabled", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provider.LastRequest);
        Assert.Equal(0, monitoring.CallCount);
    }


    [Fact]
    public async Task TelegramChatDisabledDoesNotCallProviderForTelegramSource()
    {
        var provider = new FakeAiProviderClient();
        var settings = EnabledSettings();
        settings.TelegramChatEnabled = false;
        var monitoring = new FakeAiMonitoringContextService();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, monitoring, new FakeAiEndpointLookupService(), new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Telegram, UserMessage = "hello" }, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Contains("Telegram AI chat is disabled", response.ErrorMessage);
        Assert.Null(provider.LastRequest);
        Assert.Equal(0, monitoring.CallCount);
    }

    [Fact]
    public async Task TelegramChatSourceUsesSharedSafetyPromptAndProviderPath()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "The network summary was used.", Model = "llama" } };
        var settings = EnabledSettings(webChatEnabled: false);
        settings.TelegramChatEnabled = true;
        var monitoring = new FakeAiMonitoringContextService();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, monitoring, new FakeAiEndpointLookupService(), new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Telegram, User = User(), UserMessage = "How is my network looking today?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(1, monitoring.CallCount);
        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "system" && x.Content.Contains("get_network_health_summary", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "system" && x.Content.Contains("Farm Router WAN", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "system" && x.Content.Contains("CheckResults", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebChatInjectsHealthSummaryForBroadNetworkStatusQuestion()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "Farm Router WAN is down.", Model = "llama" } };
        var monitoring = new FakeAiMonitoringContextService();
        var controller = CreateController(provider, EnabledSettings(), monitoring);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User() } };

        var result = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "Is anything down?" }, CancellationToken.None));

        var model = Assert.IsType<AiAssistantPageViewModel>(result.Model);
        Assert.Contains(model.Messages, x => x.Role == "assistant" && x.Content == "Farm Router WAN is down.");
        Assert.Equal(1, monitoring.CallCount);
        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "system" && x.Content.Contains("get_network_health_summary", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "system" && x.Content.Contains("Farm Router WAN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthSummaryUnavailableReturnsSafeAnswerWithoutProviderCall()
    {
        var provider = new FakeAiProviderClient();
        var monitoring = new FakeAiMonitoringContextService
        {
            Result = AiMonitoringContextResult.Unavailable("database unavailable")
        };
        var chat = new AiChatService(new FakeAiAssistantSettingsService(EnabledSettings()), provider, monitoring, new FakeAiEndpointLookupService(), new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { User = User(), UserMessage = "How is my network looking today?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("couldn't retrieve", response.AssistantMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not connected yet", response.AssistantMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provider.LastRequest);
        Assert.Equal(1, monitoring.CallCount);
    }

    [Fact]
    public async Task DetailedBaselineQuestionDoesNotRequestSummaryContext()
    {
        var provider = new FakeAiProviderClient();
        var monitoring = new FakeAiMonitoringContextService();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(EnabledSettings()), provider, monitoring, new FakeAiEndpointLookupService(), new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);

        await chat.SendAsync(new AiChatRequest { User = User(), UserMessage = "Is WFP WAN RTT higher than its 24h baseline?" }, CancellationToken.None);

        Assert.Equal(0, monitoring.CallCount);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains("bounded check-result", provider.LastRequest.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unrestricted raw CheckResults export", provider.LastRequest.Messages[0].Content, StringComparison.Ordinal);
    }



    [Fact]
    public async Task WebChatInjectsEndpointDiagnosticsForEndpointQuestion()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "WFP WAN is UP.", Model = "llama" } };
        var lookup = new FakeAiEndpointLookupService { Result = new AiEndpointLookupResult { Succeeded = true, Matches = [new AiEndpointLookupItem { EndpointId = "e-wfp", Name = "WFP WAN", Target = "1.2.3.4", Enabled = true }] } };
        var diagnostics = new FakeAiEndpointDiagnosticsService();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(EnabledSettings()), provider, new FakeAiMonitoringContextService(), lookup, diagnostics, NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { User = User(), UserMessage = "What is going on with WFP WAN?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal(1, lookup.CallCount);
        Assert.Equal("24h", diagnostics.RequestedWindow);
        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "system" && x.Content.Contains("get_endpoint_diagnostics_pack", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Content.Contains("WFP WAN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AmbiguousEndpointLookupReturnsClarificationWithoutProviderCall()
    {
        var provider = new FakeAiProviderClient();
        var lookup = new FakeAiEndpointLookupService { Result = new AiEndpointLookupResult { Ambiguous = true, Matches = [new AiEndpointLookupItem { Name = "Kitchen AP", Target = "10.0.0.2" }, new AiEndpointLookupItem { Name = "Kitchen Switch", Target = "10.0.0.3" }] } };
        var chat = new AiChatService(new FakeAiAssistantSettingsService(EnabledSettings()), provider, new FakeAiMonitoringContextService(), lookup, new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { User = User(), UserMessage = "Is Kitchen flapping?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("Which one", response.AssistantMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task TelegramProcessorPassesLinkedApplicationUserToSharedAiChat()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramAccounts.Add(new TelegramAccount
        {
            TelegramAccountId = "tga-1",
            UserId = "app-user-1",
            ChatId = "chat-1",
            Verified = true,
            IsActive = true,
            LinkedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var chat = new CapturingAiChatService();
        var processor = new TelegramMessageProcessor(
            new FakeTelegramLinkService(),
            new FakeNotificationSettingsService(),
            new FakeAiAssistantSettingsService(EnabledSettings()),
            chat,
            new FakeTelegramAiConversationStore(),
            dbContext);

        var result = await processor.ProcessAsync(new TelegramInboundMessage
        {
            ChatId = "chat-1",
            ChatType = "private",
            Text = "Is anything down?"
        }, CancellationToken.None);

        Assert.True(result.ShouldReply);
        Assert.NotNull(chat.LastRequest);
        Assert.Equal(AiChatSource.Telegram, chat.LastRequest!.Source);
        Assert.Equal("app-user-1", chat.LastRequest.User?.FindFirstValue(ClaimTypes.NameIdentifier));
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
        Assert.Contains("endpoint diagnostics packs", messages[0].Content);
        Assert.Contains("bounded check-result summaries", messages[0].Content);
        Assert.Contains("Diagram lookup", messages[0].Content);
        Assert.Contains("switch port/VLAN", messages[0].Content);
        Assert.Contains("memory", messages[0].Content);
        Assert.Contains("Do not invent endpoint state", messages[0].Content);
        Assert.Contains("Admin-configured site instructions", messages[1].Content);
        Assert.Contains("supplied monitoring-summary truth", messages[1].Content);
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

    private static AiAssistantController CreateController(
        FakeAiProviderClient provider,
        AiAssistantSettingsDto settings,
        FakeAiMonitoringContextService? monitoringContextService = null)
    {
        var settingsService = new FakeAiAssistantSettingsService(settings);
        var chat = new AiChatService(settingsService, provider, monitoringContextService ?? new FakeAiMonitoringContextService(), new FakeAiEndpointLookupService(), new FakeAiEndpointDiagnosticsService(), NullLogger<AiChatService>.Instance);
        return new AiAssistantController(settingsService, chat);
    }

    private static ClaimsPrincipal User() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
        authenticationType: "test"));

    private static PingMonitorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PingMonitorDbContext>()
            .UseInMemoryDatabase($"ai-chat-{Guid.NewGuid():N}")
            .Options;
        return new PingMonitorDbContext(options);
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

    private sealed class CapturingAiChatService : IAiChatService
    {
        public AiChatRequest? LastRequest { get; private set; }

        public Task<AiChatResponse> SendAsync(AiChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AiChatResponse
            {
                Succeeded = true,
                AssistantEnabled = true,
                WebChatEnabled = true,
                TelegramChatEnabled = true,
                AssistantMessage = "captured"
            });
        }
    }

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

    private sealed class FakeAiMonitoringContextService : IAiMonitoringContextService
    {
        public int CallCount { get; private set; }
        public AiMonitoringContextResult Result { get; set; } = AiMonitoringContextResult.Success(new AiNetworkHealthSummary
        {
            GeneratedAtUtc = DateTimeOffset.Parse("2026-06-15T12:00:00Z"),
            VisibleEndpointCount = 3,
            VisibleAssignmentCount = 3,
            StateCounts = new AiNetworkStateCounts
            {
                Up = 2,
                Down = 1
            },
            DownEndpoints =
            [
                new AiEndpointStateSummaryItem
                {
                    EndpointId = "endpoint-farm-router",
                    AssignmentId = "assignment-farm-router",
                    Name = "Farm Router WAN",
                    Target = "1.2.3.4",
                    State = EndpointStateKind.Down,
                    LastChangedUtc = DateTimeOffset.Parse("2026-06-15T11:42:00Z"),
                    AgentId = "agent-1",
                    AgentName = "Main Agent"
                }
            ],
            RecentStateChangeCount = 1,
            RecentStateChanges =
            [
                new AiRecentStateChangeSummaryItem
                {
                    EndpointId = "endpoint-farm-router",
                    AssignmentId = "assignment-farm-router",
                    EndpointName = "Farm Router WAN",
                    AgentId = "agent-1",
                    AgentName = "Main Agent",
                    PreviousState = EndpointStateKind.Up,
                    NewState = EndpointStateKind.Down,
                    TransitionAtUtc = DateTimeOffset.Parse("2026-06-15T11:42:00Z"),
                    ReasonCode = StateTransitionReasonCodes.FailureThresholdReached
                }
            ]
        });

        public Task<AiMonitoringContextResult> GetNetworkHealthSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            CallCount += 1;
            return Task.FromResult(Result);
        }
    }



    private sealed class FakeAiEndpointLookupService : IAiEndpointLookupService
    {
        public int CallCount { get; private set; }
        public AiEndpointLookupResult Result { get; set; } = new() { Succeeded = true, Matches = [new AiEndpointLookupItem { EndpointId = "endpoint-farm-router", Name = "Farm Router WAN", Target = "1.2.3.4", Enabled = true }] };
        public Task<AiEndpointLookupResult> SearchEndpointsAsync(ClaimsPrincipal user, string userMessage, CancellationToken cancellationToken)
        {
            CallCount += 1;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAiEndpointDiagnosticsService : IAiEndpointDiagnosticsService
    {
        public string? RequestedWindow { get; private set; }
        public Task<AiEndpointDiagnosticsResult> GetDiagnosticsPackAsync(ClaimsPrincipal user, string endpointId, string requestedWindow, CancellationToken cancellationToken)
        {
            RequestedWindow = requestedWindow;
            return Task.FromResult(new AiEndpointDiagnosticsResult { Succeeded = true, Pack = new AiEndpointDiagnosticsPack { GeneratedAtUtc = DateTimeOffset.Parse("2026-06-15T12:00:00Z"), Endpoint = new AiEndpointLookupItem { EndpointId = endpointId, Name = "WFP WAN", Target = "1.2.3.4", Enabled = true }, CurrentState = new AiEndpointCurrentStateInfo { State = EndpointStateKind.Up }, Checks = new AiEndpointCheckSummary { ReceivedSamples = 1, SuccessfulSamples = 1 }, Uptime = new AiEndpointUptimeSummary { UptimeSeconds = 3600, UptimePercent = 100 } } });
        }
    }

    private sealed class FakeNotificationSettingsService : INotificationSettingsService
    {
        public Task<NotificationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(new NotificationSettingsDto { TelegramEnabled = true });
        public Task<SmtpChannelSettingsDto> GetSmtpChannelAsync(CancellationToken cancellationToken) => Task.FromResult(new SmtpChannelSettingsDto());
        public Task<TelegramChannelSettingsDto> GetTelegramChannelAsync(CancellationToken cancellationToken) => Task.FromResult(new TelegramChannelSettingsDto { TelegramEnabled = true });
        public Task AdvanceTelegramLastProcessedUpdateIdAsync(long updateId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeTelegramLinkService : ITelegramLinkService
    {
        public Task<PendingTelegramLinkDto> GenerateCodeAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PendingTelegramLinkDto?> GetActiveCodeAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TelegramLinkConsumeResult> ConsumeCodeAsync(string code, string chatId, string chatType, string? username, string? displayName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TelegramAccountStatusDto?> GetAccountStatusAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UnlinkAccountAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeTelegramAiConversationStore : ITelegramAiConversationStore
    {
        public IReadOnlyList<AiChatMessageDto> GetHistory(string chatId, string userId) => [];
        public void AddTurn(string chatId, string userId, string userMessage, string assistantMessage)
        {
        }

        public void Clear(string chatId, string userId)
        {
        }
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
