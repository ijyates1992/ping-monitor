using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.ViewModels.Admin;
using PingMonitor.Web.ViewModels.NetworkDiagrams;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class NetworkDiagramsFeatureTests
{
    [Fact]
    public void ApplicationSettings_DefaultsNetworkDiagramsDisabled()
    {
        var settings = new ApplicationSettings();

        Assert.False(settings.NetworkDiagramsEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminFeatureSettings_CanEnableAndDisableNetworkDiagrams(bool enabled)
    {
        var service = new FakeApplicationSettingsService(new ApplicationSettingsDto
        {
            SiteUrl = "https://ping.example",
            DefaultPingIntervalSeconds = 60,
            DefaultRetryIntervalSeconds = 5,
            DefaultTimeoutMs = 1000,
            DefaultFailureThreshold = 3,
            DefaultRecoveryThreshold = 2,
            DegradedEvaluationEnabled = true,
            DegradedBaselineLookbackMinutes = 1440,
            DegradedCurrentWindowMinutes = 60,
            DegradedPacketLossIncreasePercentagePoints = 20d,
            DegradedRttIncreasePercent = 20d,
            DegradedJitterIncreasePercent = 20d,
            DegradedMinimumSamples = 10,
            NetworkDiagramsEnabled = !enabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        var controller = new AdminController(service);

        var result = await controller.SaveApplicationFeatures(
            new ApplicationFeatureSettingsPageViewModel { NetworkDiagramsEnabled = enabled },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplicationFeatureSettingsPageViewModel>(view.Model);
        Assert.True(model.Saved);
        Assert.Equal(enabled, model.NetworkDiagramsEnabled);
        Assert.Equal(enabled, service.Current.NetworkDiagramsEnabled);
    }

    [Fact]
    public async Task NetworkDiagramsIndex_ReturnsNotFound_WhenFeatureDisabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = false }),
            new FakeEndpointManagementQueryService());

        var result = await controller.Index(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Network diagrams are not enabled.", notFound.Value);
    }

    [Fact]
    public async Task NetworkDiagramsIndex_ReturnsEditorShellView_WhenFeatureEnabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = true }),
            new FakeEndpointManagementQueryService(new ManageEndpointRowViewModel
            {
                AssignmentId = "assignment-1",
                EndpointId = "endpoint-1",
                EndpointName = "Core router",
                Target = "192.0.2.1",
                IconKey = "router",
                CurrentState = EndpointStateKind.Up
            }));

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<NetworkDiagramsEditorPageViewModel>(view.Model);
        Assert.Equal("Network diagrams", model.PageTitle);
        Assert.Equal(NetworkDiagramsEditorPageViewModel.DocumentationOnlyNotice, model.Notice);
        Assert.False(model.LayoutIsSaved);
        var endpoint = Assert.Single(model.MonitoredEndpoints);
        Assert.Equal("endpoint-1", endpoint.EndpointId);
        Assert.Equal("Core router", endpoint.Name);
        Assert.Equal("192.0.2.1", endpoint.Target);
        Assert.Equal("router", endpoint.IconKey);
        Assert.Equal(EndpointStateKind.Up, endpoint.SummaryState);
    }

    [Fact]
    public void NetworkDiagramsIndex_ViewIncludesEditorShellElements()
    {
        var repoRoot = FindRepositoryRoot();
        var viewPath = Path.Combine(
            repoRoot,
            "src",
            "WebApp",
            "PingMonitor.Web",
            "Views",
            "NetworkDiagrams",
            "Index.cshtml");

        var viewMarkup = File.ReadAllText(viewPath);

        Assert.Contains("data-network-diagram-editor", viewMarkup);
        Assert.Contains("data-diagram-canvas-host", viewMarkup);
        Assert.Contains("Network diagram toolbox", viewMarkup);
        Assert.Contains("Monitored endpoints", viewMarkup);
        Assert.Contains("data-add-endpoint-node", viewMarkup);
        Assert.Contains("Draw link", viewMarkup);
        Assert.Contains("Zoom in", viewMarkup);
        Assert.Contains("Zoom out", viewMarkup);
        Assert.Contains("Reset view", viewMarkup);
        Assert.Contains("Fit content", viewMarkup);
        Assert.Contains("Drag empty space to pan. Use mouse wheel to zoom.", viewMarkup);
        Assert.Contains("data-diagram-world", viewMarkup);
        Assert.Contains("data-zoom-label", viewMarkup);
        Assert.Contains("Diagram links are visual documentation only and do not create monitoring dependencies.", viewMarkup);
        Assert.Contains("data-link-properties", viewMarkup);
        Assert.Contains("Layout is not saved yet", viewMarkup);
        Assert.Contains("/js/network-diagrams-editor.js", viewMarkup);
        Assert.Contains("/css/network-diagrams.css", viewMarkup);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "WebApp")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeEndpointManagementQueryService : IEndpointManagementQueryService
    {
        private readonly IReadOnlyList<ManageEndpointRowViewModel> _rows;

        public FakeEndpointManagementQueryService(params ManageEndpointRowViewModel[] rows)
        {
            _rows = rows;
        }

        public Task<ManageEndpointsPageViewModel> GetManagePageAsync(string? groupId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ManageEndpointsPageViewModel { Rows = _rows });
        }

        public Task<EditEndpointOptionsViewModel> GetEditOptionsAsync(string assignmentId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new EditEndpointOptionsViewModel());
        }

        public Task<RemoveEndpointDetails?> GetRemoveDetailsAsync(string assignmentId, CancellationToken cancellationToken)
        {
            return Task.FromResult<RemoveEndpointDetails?>(null);
        }
    }

    private sealed class FakeApplicationSettingsService : IApplicationSettingsService
    {
        public FakeApplicationSettingsService(ApplicationSettingsDto current)
        {
            Current = current;
        }

        public ApplicationSettingsDto Current { get; private set; }

        public Task<ApplicationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Current);
        }

        public Task<ApplicationSettingsDto> UpdateAsync(UpdateApplicationSettingsCommand command, CancellationToken cancellationToken)
        {
            Current = new ApplicationSettingsDto
            {
                SiteUrl = command.SiteUrl,
                DefaultPingIntervalSeconds = command.DefaultPingIntervalSeconds,
                DefaultRetryIntervalSeconds = command.DefaultRetryIntervalSeconds,
                DefaultTimeoutMs = command.DefaultTimeoutMs,
                DefaultFailureThreshold = command.DefaultFailureThreshold,
                DefaultRecoveryThreshold = command.DefaultRecoveryThreshold,
                DegradedEvaluationEnabled = command.DegradedEvaluationEnabled,
                DegradedBaselineLookbackMinutes = command.DegradedBaselineLookbackMinutes,
                DegradedCurrentWindowMinutes = command.DegradedCurrentWindowMinutes,
                DegradedPacketLossIncreasePercentagePoints = command.DegradedPacketLossIncreasePercentagePoints,
                DegradedRttIncreasePercent = command.DegradedRttIncreasePercent,
                DegradedJitterIncreasePercent = command.DegradedJitterIncreasePercent,
                DegradedMinimumSamples = command.DegradedMinimumSamples,
                NetworkDiagramsEnabled = command.NetworkDiagramsEnabled,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return Task.FromResult(Current);
        }
    }
}
