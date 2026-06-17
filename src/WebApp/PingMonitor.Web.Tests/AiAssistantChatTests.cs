using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.AiProviders;
using PingMonitor.Web.Services.AiMemory;
using PingMonitor.Web.Services.AiTools;
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
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);

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
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Telegram, UserMessage = "How is my network looking today?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains(provider.LastRequest!.Messages, x => x.Role == "system" && x.Content!.Contains("current monitoring state", StringComparison.Ordinal));
        Assert.Contains(provider.LastRequest.Messages, x => x.Role == "system" && x.Content!.Contains("CheckResults", StringComparison.Ordinal));
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
        await controller.Send(new AiAssistantPageViewModel { Message = "What did I say?" }, CancellationToken.None);

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
    public async Task IndexLoadsRetainedMessagesFromSessionAndClearRemovesThem()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ResponseText = "First reply", Model = "llama" } };
        var controller = CreateController(provider, EnabledSettings());

        var sendResult = Assert.IsType<ViewResult>(await controller.Send(new AiAssistantPageViewModel { Message = "First question" }, CancellationToken.None));
        var sendModel = Assert.IsType<AiAssistantPageViewModel>(sendResult.Model);
        Assert.Equal(2, sendModel.Messages.Count);

        var indexResult = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var indexModel = Assert.IsType<AiAssistantPageViewModel>(indexResult.Model);
        Assert.Contains(indexModel.Messages, x => x.Role == "user" && x.Content == "First question");
        Assert.Contains(indexModel.Messages, x => x.Role == "assistant" && x.Content == "First reply");

        var clearResult = Assert.IsType<ViewResult>(await controller.Clear(CancellationToken.None));
        var clearModel = Assert.IsType<AiAssistantPageViewModel>(clearResult.Model);
        Assert.Empty(clearModel.Messages);

        var afterClearResult = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var afterClearModel = Assert.IsType<AiAssistantPageViewModel>(afterClearResult.Model);
        Assert.Empty(afterClearModel.Messages);
    }

    [Fact]
    public void ContextHistoryExcludesVisibleErrorMessages()
    {
        var history = AiAssistantController.BoundContextHistory([
            new AiChatMessageDto { Role = "user", Content = "hello" },
            new AiChatMessageDto { Role = "error", Content = "provider failed" },
            new AiChatMessageDto { Role = "assistant", Content = "hi" }
        ]);

        Assert.DoesNotContain(history, x => x.Role == "error");
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void AssistantMarkdownIsRenderedAndUnsafeHtmlIsSanitized()
    {
        var renderer = new AiMarkdownRenderer();

        var html = renderer.RenderAssistantMessage("### Heading\n\n**bold text**\n\n- item\n\n```bash\necho ok\n```\n\n<script>alert(1)</script><a href=\"javascript:alert(1)\" onclick=\"alert(2)\">bad</a> [safe](https://example.com)");

        Assert.Contains("<h3", html);
        Assert.Contains("<strong>bold text</strong>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<pre><code", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
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
        Assert.Contains("Use tools when you need current monitoring state", messages[0].Content);
        Assert.Contains("search_endpoints", messages[0].Content);
        Assert.Contains("get_endpoint_metrics_summary", messages[0].Content);
        Assert.Contains("UNKNOWN is not DOWN", messages[0].Content);
        Assert.Contains("SUPPRESSED is not downtime", messages[0].Content);
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
        Assert.DoesNotContain("runtime-secret", string.Join("\n", model.Messages.Select(x => x.Content)));
        Assert.DoesNotContain(provider.LastRequest!.Messages, x => x.Content!.Contains("runtime-secret", StringComparison.Ordinal));
    }



    [Fact]
    public void EndpointMetricsToolImplementationIncludesRequiredSafetyAndMetricGuards()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "EndpointMetricsAiTools.cs"));

        Assert.Contains("MaxTailSamples = 120", source);
        Assert.Contains("GetVisibleEndpointIdsOrNullForAdminAsync", source);
        Assert.Contains("UNKNOWN is not DOWN", source);
        Assert.Contains("SUPPRESSED is not downtime", source);
        Assert.Contains("Full unrestricted raw CheckResults export is not available", source);
        Assert.Contains("packetLossPercentOfReceived", source);
        Assert.Contains("absolute difference between consecutive successful RTT samples", source);
        Assert.Contains("NormalizeWindow", source);
        Assert.Contains("\"1h\"", source);
        Assert.Contains("\"6h\"", source);
        Assert.Contains("\"24h\"", source);
        Assert.Contains("\"7d\"", source);
    }



    [Fact]
    public void EndpointMetricsTool_UsesBoundedMySqlQueriesForCheckResults()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "EndpointMetricsAiTools.cs"));

        Assert.Contains("var checkScope = _dbContext.CheckResults.AsNoTracking()", source);
        Assert.Contains("GroupBy(_ => 1).Select(g => new { Received = g.Count()", source);
        Assert.Contains("Take(MaxRttStatisticSamples)", source);
        Assert.Contains("Take(MaxFailureClusterSamples)", source);
        Assert.Contains("Take(call.Limits.MaxEndpointMetricsSampleTailPoints)", source);
        Assert.Contains("Median, p95, jitter, and failure clusters are bounded", source);
        Assert.DoesNotContain("OrderBy(x => x.CheckedAtUtc).Select(x => new { x.CheckedAtUtc, x.Success, x.RoundTripMs, x.ErrorCode, x.ErrorMessage }).ToArrayAsync", source);
    }

    [Fact]
    public void DependencyAiTools_DoNotUseMySqlUnsafeEndpointIdArrayContainsQueries()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "DependencyAiTools.cs"));

        Assert.Contains("ApplyEndpointFilter(endpointQuery, visibleIds)", source);
        Assert.Contains("var visibleAssignments = ApplyEndpointFilter(DbContext.MonitorAssignments.AsNoTracking(), endpointIds)", source);
        Assert.Contains("ApplyDependencyParentFilter(DbContext.EndpointDependencies.AsNoTracking(), endpointIds)", source);
        Assert.Contains("ApplyDependencyEndpointFilter(", source);
        Assert.Contains("Expression.OrElse", source);
        Assert.DoesNotContain("visibleIds.Contains(x.EndpointId)", source);
        Assert.DoesNotContain("endpointIds.Contains(assignment.EndpointId)", source);
        Assert.DoesNotContain("endpointIds.Contains(x.EndpointId)", source);
        Assert.DoesNotContain("endpointIds.Contains(x.DependsOnEndpointId)", source);
    }

    [Fact]
    public void SearchEndpointsTool_DoesNotUseMySqlUnsafeEndpointIdArrayContainsQuery()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "EndpointMetricsAiTools.cs"));

        Assert.Contains("ApplyEndpointFilter(endpoints, visibleEndpointIds)", source);
        Assert.Contains("ApplyEndpointFilter(_dbContext.MonitorAssignments.AsNoTracking(), endpointIds)", source);
        Assert.Contains("Expression.OrElse", source);
        Assert.DoesNotContain("visibleEndpointIds.Contains(x.EndpointId)", source);
        Assert.DoesNotContain("endpointIds.Contains(assignment.EndpointId)", source);
    }

    [Fact]
    public void NetworkHealthSummaryTool_DoesNotUseMySqlUnsafeAgentIdArrayContainsQuery()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", "Services", "AiTools", "NetworkHealthSummaryAiTool.cs"));

        Assert.DoesNotContain("agentIds.Contains(x.AgentId)", source);
    }

    [Fact]
    public async Task ToolCallingExecutesToolAndReturnsFinalAnswer()
    {
        var provider = new FakeAiProviderClient();
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ToolCalls = { new AiProviderToolCall { Id = "call_1", Function = new AiProviderToolCallFunction { Name = "get_network_health_summary", Arguments = "{}" } } } });
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ResponseText = "Network looks healthy.", Model = "llama" });
        var settings = EnabledSettings();
        settings.ToolCallingEnabled = true;
        var registry = new FakeAiToolRegistry();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, registry, NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Web, UserMessage = "How is my network looking today?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("Network looks healthy.", response.AssistantMessage);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("auto", provider.Requests[0].ToolChoice);
        Assert.NotEmpty(provider.Requests[0].Tools);
        Assert.Contains(provider.Requests[1].Messages, x => x.Role == "tool" && x.ToolCallId == "call_1" && x.Content!.Contains("visibleEndpointCount", StringComparison.Ordinal));
        Assert.Single(registry.Calls);
    }

    [Fact]
    public async Task ToolCallingExecutesEndpointSearchThenMetricsAndReturnsFinalAnswer()
    {
        var provider = new FakeAiProviderClient();
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ToolCalls = { new AiProviderToolCall { Id = "call_search", Function = new AiProviderToolCallFunction { Name = "search_endpoints", Arguments = "{\"query\":\"WFP WAN\"}" } } } });
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ToolCalls = { new AiProviderToolCall { Id = "call_metrics", Function = new AiProviderToolCallFunction { Name = "get_endpoint_metrics_summary", Arguments = "{\"endpointId\":\"endpoint-1\",\"window\":\"24h\"}" } } } });
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ResponseText = "WFP WAN is UP over the 24h window.", Model = "llama" });
        var settings = EnabledSettings();
        settings.ToolCallingEnabled = true;
        var registry = new FakeAiToolRegistry();
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, registry, NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Web, UserMessage = "How is WFP WAN looking?" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("WFP WAN is UP over the 24h window.", response.AssistantMessage);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Collection(registry.Calls, x => Assert.Equal("search_endpoints", x.Name), x => Assert.Equal("get_endpoint_metrics_summary", x.Name));
        Assert.Contains(provider.Requests[2].Messages, x => x.Role == "tool" && x.ToolCallId == "call_search" && x.Content!.Contains("endpoint-1", StringComparison.Ordinal));
        Assert.Contains(provider.Requests[2].Messages, x => x.Role == "tool" && x.ToolCallId == "call_metrics" && x.Content!.Contains("packetLossPercentOfReceived", StringComparison.Ordinal));
    }

    [Fact]
    public void EndpointToolDefinitionsDoNotExposeSecretsAndDeclareBoundedReadOnlyTools()
    {
        var registry = new FakeAiToolRegistry();
        var definitions = registry.GetDefinitions();

        Assert.Contains(definitions, x => x.Name == "search_endpoints" && x.Description.Contains("visible", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(definitions, x => x.Name == "get_endpoint_metrics_summary" && x.Description.Contains("bounded read-only", StringComparison.OrdinalIgnoreCase));
        foreach (var definition in definitions)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(definition.ToProviderDefinition(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Assert.DoesNotContain("api", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ToolDefinitionsAreNotSentWhenDisabled()
    {
        var provider = new FakeAiProviderClient();
        var settings = EnabledSettings();
        settings.ToolCallingEnabled = false;
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);

        await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Web, UserMessage = "Is anything down?" }, CancellationToken.None);

        Assert.Empty(provider.LastRequest!.Tools);
        Assert.Null(provider.LastRequest.ToolChoice);
    }

    [Fact]
    public async Task UnknownToolNameIsRejectedSafely()
    {
        var provider = new FakeAiProviderClient();
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ToolCalls = { new AiProviderToolCall { Id = "call_bad", Function = new AiProviderToolCallFunction { Name = "unknown_tool", Arguments = "{}" } } } });
        provider.Results.Enqueue(new AiProviderChatResult { Succeeded = true, ResponseText = "I cannot access that tool.", Model = "llama" });
        var settings = EnabledSettings();
        settings.ToolCallingEnabled = true;
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Web, UserMessage = "run tool" }, CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains(provider.Requests[1].Messages, x => x.Role == "tool" && x.Content!.Contains("unknown_tool", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaxToolRoundsPreventsInfiniteLoop()
    {
        var provider = new FakeAiProviderClient { Result = new AiProviderChatResult { Succeeded = true, ToolCalls = { new AiProviderToolCall { Id = "call_loop", Function = new AiProviderToolCallFunction { Name = "get_network_health_summary", Arguments = "{}" } } } } };
        var settings = EnabledSettings();
        settings.ToolCallingEnabled = true;
        var chat = new AiChatService(new FakeAiAssistantSettingsService(settings), provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);

        var response = await chat.SendAsync(new AiChatRequest { Source = AiChatSource.Web, UserMessage = "loop" }, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Contains("too many tool rounds", response.ErrorMessage);
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
        var chat = new AiChatService(settingsService, provider, new FakeAiToolRegistry(), NullLogger<AiChatService>.Instance);
        var controller = new AiAssistantController(settingsService, chat, new FakeAiUserMemoryService());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        return controller;
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

    private sealed class FakeAiUserMemoryService : IAiUserMemoryService
    {
        public Task<AiUserMemoryMutationResult> CreateAsync(CreateAiUserMemoryCommand command, CancellationToken cancellationToken) => Task.FromResult(new AiUserMemoryMutationResult(false, "not implemented", null));
        public Task<IReadOnlyList<AiUserMemoryDto>> SearchAsync(SearchAiUserMemoriesQuery query, CancellationToken cancellationToken) => Task.FromResult((IReadOnlyList<AiUserMemoryDto>)Array.Empty<AiUserMemoryDto>());
        public Task<IReadOnlyList<AiUserMemoryDto>> ListAsync(string userId, CancellationToken cancellationToken) => Task.FromResult((IReadOnlyList<AiUserMemoryDto>)Array.Empty<AiUserMemoryDto>());
        public Task<AiUserMemoryMutationResult> DeleteAsync(DeleteAiUserMemoryCommand command, CancellationToken cancellationToken) => Task.FromResult(new AiUserMemoryMutationResult(false, "not implemented", null));
        public string? ResolveUserId(System.Security.Claims.ClaimsPrincipal? principal, string? applicationUserId) => applicationUserId;
    }

    private sealed class FakeAiProviderClient : IAiProviderClient
    {
        public AiProviderChatRequest? LastRequest { get; private set; }
        public List<AiProviderChatRequest> Requests { get; } = new();
        public Queue<AiProviderChatResult> Results { get; } = new();
        public AiProviderChatResult Result { get; set; } = new() { Succeeded = true, ResponseText = "assistant response", Model = "llama" };
        public Task<AiProviderChatResult> SendChatAsync(AiProviderChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
        }
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public IEnumerable<string> Keys => _values.Keys;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_values.TryGetValue(key, out var stored))
            {
                value = stored;
                return true;
            }

            value = [];
            return false;
        }
    }

    private sealed class FakeAiToolRegistry : IAiToolRegistry
    {
        public List<AiToolCall> Calls { get; } = new();
        public IReadOnlyList<AiToolDefinition> GetDefinitions() => [
            new AiToolDefinition { Name = "get_network_health_summary", Description = "summary", Parameters = new System.Text.Json.Nodes.JsonObject { ["type"] = "object", ["properties"] = new System.Text.Json.Nodes.JsonObject(), ["required"] = new System.Text.Json.Nodes.JsonArray() } },
            new AiToolDefinition { Name = "search_endpoints", Description = "Search Ping Monitor endpoints visible to the current user by endpoint name, target IP, or hostname.", Parameters = new System.Text.Json.Nodes.JsonObject { ["type"] = "object", ["properties"] = new System.Text.Json.Nodes.JsonObject(), ["required"] = new System.Text.Json.Nodes.JsonArray("query") } },
            new AiToolDefinition { Name = "get_endpoint_metrics_summary", Description = "Get a bounded read-only metrics summary for a visible endpoint, including current state, uptime, RTT, jitter, packet loss, and recent check sample tail.", Parameters = new System.Text.Json.Nodes.JsonObject { ["type"] = "object", ["properties"] = new System.Text.Json.Nodes.JsonObject(), ["required"] = new System.Text.Json.Nodes.JsonArray("endpointId") } }
        ];
        public bool IsRegistered(string name) => name is "get_network_health_summary" or "search_endpoints" or "get_endpoint_metrics_summary";
        public Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            if (!IsRegistered(call.Name)) return Task.FromResult(new AiToolExecutionResult { Succeeded = false, ErrorMessage = "Unknown tool requested.", ContentJson = "{\"error\":\"unknown_tool\"}" });
            if (call.ArgumentsJson == "not-json") return Task.FromResult(new AiToolExecutionResult { Succeeded = false, ErrorMessage = "Arguments must be valid JSON.", ContentJson = "{\"error\":\"invalid_arguments\"}" });
            return call.Name switch
            {
                "search_endpoints" => Task.FromResult(new AiToolExecutionResult { Succeeded = true, ContentJson = "{\"matches\":[{\"endpointId\":\"endpoint-1\",\"name\":\"WFP WAN\",\"target\":\"51.155.102.156\",\"enabled\":true,\"currentState\":\"UP\"}],\"ambiguous\":false}" }),
                "get_endpoint_metrics_summary" => Task.FromResult(new AiToolExecutionResult { Succeeded = true, ContentJson = "{\"dataSource\":\"endpoint_metrics_summary\",\"currentState\":{\"state\":\"UP\"},\"window\":{\"appliedWindow\":\"24h\"},\"checks\":{\"packetLossPercentOfReceived\":0},\"limitations\":[\"Full unrestricted raw CheckResults export is not available.\"]}" }),
                _ => Task.FromResult(new AiToolExecutionResult { Succeeded = true, ContentJson = "{\"visibleEndpointCount\":1,\"stateCounts\":{\"DOWN\":0}}" })
            };
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
