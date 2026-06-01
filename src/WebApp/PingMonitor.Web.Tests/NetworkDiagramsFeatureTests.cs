using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.NetworkDiagrams;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Identity;
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
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(),
            new FakeNetworkDiagramPdfExportService());

        var result = await controller.Index(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Network diagrams are not enabled.", notFound.Value);
    }

    [Fact]
    public async Task NetworkDiagramsIndex_ReturnsListView_WhenFeatureEnabled()
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
            }),
            new FakeNetworkDiagramService(new NetworkDiagram { DiagramId = "diagram-1", Name = "Core", UpdatedAtUtc = DateTimeOffset.UtcNow }),
            new FakeNetworkDiagramPdfExportService());

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<NetworkDiagramListPageViewModel>(view.Model);
        var diagram = Assert.Single(model.Diagrams);
        Assert.Equal("diagram-1", diagram.DiagramId);
        Assert.Equal("Core", diagram.Name);
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
            "Edit.cshtml");

        var viewMarkup = File.ReadAllText(viewPath);

        Assert.Contains("data-network-diagram-editor", viewMarkup);
        Assert.Contains("data-diagram-canvas-host", viewMarkup);
        Assert.Contains("Network diagram toolbox", viewMarkup);
        Assert.Contains("Monitored endpoints", viewMarkup);
        Assert.Contains("data-add-endpoint-node", viewMarkup);
        Assert.Contains("Draw link", viewMarkup);
        Assert.Contains("Select all nodes", viewMarkup);
        Assert.Contains("Clear selection", viewMarkup);
        Assert.Contains("Delete selected", viewMarkup);
        Assert.Contains("data-select-all", viewMarkup);
        Assert.Contains("data-clear-selection", viewMarkup);
        Assert.Contains("data-delete-selection", viewMarkup);
        Assert.Contains("data-node-properties", viewMarkup);
        Assert.Contains(@"data-node-field=""label""", viewMarkup);
        Assert.Contains(@"data-node-field=""notes""", viewMarkup);
        Assert.Contains("data-multi-node-properties", viewMarkup);
        Assert.Contains("This visual link does not create or modify monitoring dependencies.", viewMarkup);
        Assert.Contains("Zoom in", viewMarkup);
        Assert.Contains("Zoom out", viewMarkup);
        Assert.Contains("Reset view", viewMarkup);
        Assert.Contains("Fit content", viewMarkup);
        Assert.Contains("Drag empty space to pan. Use mouse wheel to zoom.", viewMarkup);
        Assert.Contains("data-diagram-world", viewMarkup);
        Assert.Contains("data-zoom-label", viewMarkup);
        Assert.Contains("Diagram links are visual documentation only and do not create monitoring dependencies.", viewMarkup);
        Assert.Contains("data-link-properties", viewMarkup);
        Assert.Contains("data-save-status", viewMarkup);
        Assert.Contains("/js/network-diagrams-editor.js", viewMarkup);
        Assert.Contains("/css/network-diagrams.css", viewMarkup);
    }


    [Fact]
    public void NetworkDiagram_DefaultCanvasUsesASeriesLandscapeRatio()
    {
        var diagram = new NetworkDiagram();

        Assert.Equal(4000, diagram.CanvasWidth);
        Assert.Equal(2828, diagram.CanvasHeight);
        Assert.True(NetworkDiagramPaper.IsApproximatelyASeriesLandscape(diagram.CanvasWidth, diagram.CanvasHeight));
    }

    [Fact]
    public void NetworkDiagramEditor_IncludesCanvasPresetAndPdfExportControls()
    {
        var repoRoot = FindRepositoryRoot();
        var viewMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Views", "NetworkDiagrams", "Edit.cshtml"));
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "js", "network-diagrams-editor.js"));

        Assert.Contains("data-export-pdf-url", viewMarkup);
        Assert.Contains("data-canvas-size-preset", viewMarkup);
        Assert.Contains("Small (4000 × 2828)", viewMarkup);
        Assert.Contains("data-export-pdf", viewMarkup);
        Assert.Contains("A4 landscape", viewMarkup);
        Assert.Contains("A3 landscape", viewMarkup);
        Assert.Contains("Canvas cannot shrink", script);
        Assert.Contains("1.41421356237", script);
        Assert.Contains("Export uses the last saved diagram", script);
    }


    [Fact]
    public void NetworkDiagramPdfExportService_RendersSavedNodesAndLinksToPdf()
    {
        var service = new NetworkDiagramPdfExportService();
        var diagram = new NetworkDiagramDto
        {
            DiagramId = "diagram-1",
            Name = "Core",
            CanvasWidth = 4000,
            CanvasHeight = 2828,
            Nodes =
            [
                new NetworkDiagramNodeDto { NodeId = "node-1", NodeType = "CustomDevice", DisplayLabel = "Router", X = 100, Y = 100, Width = 178, Height = 78 },
                new NetworkDiagramNodeDto { NodeId = "node-2", NodeType = "MonitoredEndpoint", DisplayLabel = "Web server", X = 600, Y = 300, Width = 178, Height = 78 }
            ],
            Links =
            [
                new NetworkDiagramLinkDto { LinkId = "link-1", SourceNodeId = "node-1", TargetNodeId = "node-2", Label = "uplink", SourcePortLabel = "Gi1/0/1", TargetPortLabel = "eth0" }
            ]
        };

        var export = service.Export(diagram, new NetworkDiagramPdfExportOptions("A4", new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero)));
        var pdfText = System.Text.Encoding.ASCII.GetString(export.Content);

        Assert.Equal("application/pdf", export.ContentType);
        Assert.StartsWith("%PDF-1.4", pdfText);
        Assert.Contains("Core", pdfText);
        Assert.Contains("Router", pdfText);
        Assert.Contains("Web server", pdfText);
        Assert.Contains("uplink", pdfText);
        Assert.Contains("Diagram links are visual documentation only", pdfText);
    }

    [Fact]
    public void NetworkDiagramsController_RequiresAdminAuthorizationForExport()
    {
        var authorize = typeof(NetworkDiagramsController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();

        Assert.Equal(ApplicationRoles.Admin, authorize.Roles);
    }

    [Fact]
    public async Task NetworkDiagramsExportPdf_ReturnsNotFound_WhenFeatureDisabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = false }),
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(),
            new FakeNetworkDiagramPdfExportService());

        var result = await controller.ExportPdf("missing", "A4", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Network diagrams are not enabled.", notFound.Value);
    }

    [Fact]
    public async Task NetworkDiagramsExportPdf_ReturnsNotFound_WhenDiagramMissing()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = true }),
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(),
            new FakeNetworkDiagramPdfExportService());

        var result = await controller.ExportPdf("missing", "A4", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Network diagram was not found.", notFound.Value);
    }

    [Fact]
    public async Task NetworkDiagramsExportPdf_ReturnsPdfForExistingDiagram()
    {
        var service = new FakeNetworkDiagramService(new NetworkDiagram { DiagramId = "diagram-1", Name = "Core", UpdatedAtUtc = DateTimeOffset.UtcNow });
        service.LoadResult = new NetworkDiagramDto
        {
            DiagramId = "diagram-1",
            Name = "Core",
            CanvasWidth = 4000,
            CanvasHeight = 2828
        };
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = true }),
            new FakeEndpointManagementQueryService(),
            service,
            new FakeNetworkDiagramPdfExportService());

        var result = await controller.ExportPdf("diagram-1", "A4", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("PingMonitor-NetworkDiagram-Core", file.FileDownloadName);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents));
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


    private sealed class FakeNetworkDiagramService : INetworkDiagramService
    {
        private readonly List<NetworkDiagram> _diagrams;

        public FakeNetworkDiagramService(params NetworkDiagram[] diagrams)
        {
            _diagrams = diagrams.ToList();
        }

        public Task<IReadOnlyList<NetworkDiagramListSummary>> ListAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<NetworkDiagramListSummary> result = _diagrams.Select(x => new NetworkDiagramListSummary
            {
                DiagramId = x.DiagramId,
                Name = x.Name,
                Description = x.Description,
                UpdatedAtUtc = x.UpdatedAtUtc
            }).ToArray();
            return Task.FromResult(result);
        }

        public Task<NetworkDiagram?> GetDiagramAsync(string diagramId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_diagrams.FirstOrDefault(x => x.DiagramId == diagramId));
        }

        public Task<NetworkDiagram> CreateAsync(string name, string? description, string? userId, CancellationToken cancellationToken)
        {
            var diagram = new NetworkDiagram { DiagramId = "diagram-created", Name = name, Description = description, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _diagrams.Add(diagram);
            return Task.FromResult(diagram);
        }

        public NetworkDiagramDto? LoadResult { get; set; }

        public Task<NetworkDiagramDto?> LoadAsync(string diagramId, CancellationToken cancellationToken)
        {
            return Task.FromResult(LoadResult);
        }

        public Task<NetworkDiagramDto> SaveAsync(string diagramId, NetworkDiagramSaveRequest request, string? userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new NetworkDiagramDto { DiagramId = diagramId, Name = request.Name });
        }

        public Task<bool> DeleteAsync(string diagramId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_diagrams.RemoveAll(x => x.DiagramId == diagramId) > 0);
        }
    }

    private sealed class FakeNetworkDiagramPdfExportService : INetworkDiagramPdfExportService
    {
        public NetworkDiagramPdfExportResult Export(NetworkDiagramDto diagram, NetworkDiagramPdfExportOptions options)
        {
            return new NetworkDiagramPdfExportResult(System.Text.Encoding.ASCII.GetBytes("%PDF test"), "application/pdf", $"PingMonitor-NetworkDiagram-{diagram.Name}-{options.ExportedAtUtc:yyyyMMdd-HHmm}.pdf");
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
