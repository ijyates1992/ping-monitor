using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

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
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

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
        Assert.Contains("Media type describes the physical/transport medium.", viewMarkup);
        Assert.Contains("data-draw-link-type", viewMarkup);
        Assert.Contains(@"data-link-field=""linkType""", viewMarkup);
        Assert.Contains(@"data-link-field=""mediaSubtype""", viewMarkup);
        Assert.Contains(@"data-link-field=""linkSpeedPreset""", viewMarkup);
        Assert.Contains("Media subtype", viewMarkup);
        Assert.Contains("Wireless", viewMarkup);
        Assert.Contains("Zoom in", viewMarkup);
        Assert.Contains("Zoom out", viewMarkup);
        Assert.Contains("Reset view", viewMarkup);
        Assert.Contains("Fit content", viewMarkup);
        Assert.Contains("Drag empty space to pan. Use mouse wheel to zoom.", viewMarkup);
        Assert.Contains("data-diagram-world", viewMarkup);
        Assert.Contains("data-zoom-label", viewMarkup);
        Assert.Contains("Diagram links are visual documentation only and do not create monitoring dependencies.", viewMarkup);
        Assert.Contains("data-link-properties", viewMarkup);
        Assert.Contains("data-link-vlan-section", viewMarkup);
        Assert.Contains("data-add-link-vlan", viewMarkup);
        Assert.Contains("VLANs are documentation metadata for this visual link only", viewMarkup);
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
    public void NetworkDiagramLinkTypes_NormalizesOldAndMissingValuesToCopper()
    {
        Assert.Equal(NetworkDiagramLinkMediaTypes.Copper, NetworkDiagramLinkMediaTypes.Normalize(null));
        Assert.Equal(NetworkDiagramLinkMediaTypes.Copper, NetworkDiagramLinkMediaTypes.Normalize("default"));
        Assert.Equal(NetworkDiagramLinkMediaTypes.Fibre, NetworkDiagramLinkMediaTypes.Normalize("fibre"));
        Assert.True(NetworkDiagramLinkMediaTypes.IsAllowed("Wireless"));
        Assert.Equal(NetworkDiagramLinkTypes.Standard, NetworkDiagramLinkTypes.Normalize(null));
        Assert.Equal(NetworkDiagramLinkTypes.Lacp, NetworkDiagramLinkTypes.Normalize("lacp"));
        Assert.False(NetworkDiagramLinkTypes.IsAllowed("unsupported"));
    }

    [Fact]
    public void NetworkDiagramService_ValidatesAndPersistsLinkTypeMetadata()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Services", "NetworkDiagrams", "NetworkDiagramService.cs"));

        Assert.Contains("Unsupported link type", serviceSource);
        Assert.Contains("ResolveMediaType(linkRequest.MediaType, linkRequest.LinkType)", serviceSource);
        Assert.Contains("ResolveLinkType(linkRequest.LinkType)", serviceSource);
        Assert.DoesNotContain("same two", serviceSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NetworkDiagramEditor_AllowsParallelLinksAndRendersLabelsByType()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "js", "network-diagrams-editor.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "css", "network-diagrams.css"));

        Assert.DoesNotContain("linkExists(sourceNodeId, targetNodeId)", script);
        Assert.Contains("getParallelOffsetIndexes", script);
        Assert.Contains("buildVisibleLinkLabel", script);
        Assert.Contains("diagram-link-hit", script);
        Assert.Contains("group.dataset.linkId = link.id", script);
        Assert.Contains("hit.dataset.linkId = link.id", script);
        Assert.Contains("line.dataset.linkId = link.id", script);
        Assert.Contains("const selectRenderedLink = event =>", script);
        Assert.Contains("label.addEventListener('pointerdown', selectRenderedLink)", script);
        Assert.Contains("selectLink(link.id)", script);
        Assert.Contains("event.stopPropagation();", script);
        Assert.Contains("group.append(line, hit)", script);
        Assert.Contains(@"data-media-type=""wireless""", styles);
        Assert.Contains("stroke-dasharray: 12 8", styles);
        Assert.Contains("stroke-width: 18", styles);
        Assert.Contains("vector-effect: non-scaling-stroke", styles);
        Assert.Contains("pointer-events: all", styles);
        Assert.Contains("pointer-events: stroke", styles);
        Assert.Contains(".diagram-node-layer", styles);
        Assert.Contains("pointer-events: none", styles);
        Assert.Contains("pointer-events: auto", styles);
        Assert.Contains(@"data-media-type=""fibre""", styles);
        Assert.Contains("buildVlanSummary", script);
        Assert.Contains("normalizeVlans", script);
        Assert.Contains("data-link-vlan-list", script);
        Assert.Contains("link-vlan-card", styles);
    }

    [Fact]
    public void NetworkDiagramEditor_WiresAddVlanAndPersistsVlanPayload()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "js", "network-diagrams-editor.js"));

        Assert.Contains("addLinkVlanButton.addEventListener('click', addLinkVlan)", script);
        Assert.Contains("function addLinkVlan(event)", script);
        Assert.Contains("event?.preventDefault();", script);
        Assert.Contains("state.selectedLinkId ? findLinkById(state.selectedLinkId) : null", script);
        Assert.Contains("selectedLink.vlans = normalizeVlans(selectedLink.vlans);", script);
        Assert.Contains("clientId: createVlanClientId()", script);
        Assert.Contains("mode: 'Tagged'", script);
        Assert.Contains("sortOrder: nextSortOrder", script);
        Assert.Contains("markDirty();", script);
        Assert.Contains("data-vlan-field=\"vlanId\"", script);
        Assert.Contains("data-vlan-field=\"name\"", script);
        Assert.Contains("data-vlan-field=\"mode\"", script);
        Assert.Contains("data-vlan-field=\"notes\"", script);
        Assert.Contains("validateVlanForSave", script);
        Assert.Contains("VLAN ID must be between 1 and 4094.", script);
        Assert.Contains("vlans: normalizeVlans(link.vlans).map", script);
        Assert.Contains("vlanId,", script);
        Assert.Contains("sortOrder: index", script);
    }


    [Fact]
    public void NetworkDiagramVlanModes_ValidateAllowedValues()
    {
        Assert.Equal(NetworkDiagramVlanModes.Tagged, NetworkDiagramVlanModes.Normalize("tagged"));
        Assert.True(NetworkDiagramVlanModes.IsAllowed("Untagged"));
        Assert.True(NetworkDiagramVlanModes.IsAllowed("Native"));
        Assert.True(NetworkDiagramVlanModes.IsAllowed("Management"));
        Assert.True(NetworkDiagramVlanModes.IsAllowed("Other"));
        Assert.False(NetworkDiagramVlanModes.IsAllowed("Invalid"));
    }

    [Fact]
    public void NetworkDiagramService_SourceContainsVlanValidationAndPersistenceGuards()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Services", "NetworkDiagrams", "NetworkDiagramService.cs"));
        var schemaSource = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Services", "StartupGate", "StartupSchemaService.cs"));

        Assert.Contains("VLAN ID must be between 1 and 4094.", serviceSource);
        Assert.Contains("This link already contains VLAN", serviceSource);
        Assert.Contains("Select a VLAN mode.", serviceSource);
        Assert.Contains("NetworkDiagramLinkVlan", serviceSource);
        Assert.Contains("NetworkDiagramLinkVlans", schemaSource);
        Assert.Contains("ON DELETE CASCADE", schemaSource);
    }

    [Theory]
    [InlineData(NetworkDiagramLinkMediaTypes.Copper, "Cat5e")]
    [InlineData(NetworkDiagramLinkMediaTypes.Copper, "Cat6")]
    [InlineData(NetworkDiagramLinkMediaTypes.Copper, "Cat6a")]
    [InlineData(NetworkDiagramLinkMediaTypes.Fibre, "OM1")]
    [InlineData(NetworkDiagramLinkMediaTypes.Fibre, "OM4")]
    [InlineData(NetworkDiagramLinkMediaTypes.Fibre, "OS2")]
    [InlineData(NetworkDiagramLinkMediaTypes.Wireless, "802.11ac / Wi-Fi 5")]
    [InlineData(NetworkDiagramLinkMediaTypes.Wireless, "802.11ax / Wi-Fi 6")]
    [InlineData(NetworkDiagramLinkMediaTypes.Dac, "Passive DAC")]
    [InlineData(NetworkDiagramLinkMediaTypes.Vpn, "WireGuard")]
    [InlineData(NetworkDiagramLinkMediaTypes.Virtual, "VMware vSwitch")]
    public void NetworkDiagramMediaSubtypes_ValidateAgainstMediaType(string mediaType, string subtype)
    {
        Assert.True(NetworkDiagramMediaSubtypes.IsAllowed(subtype, mediaType));
        Assert.Equal(subtype, NetworkDiagramMediaSubtypes.Normalize(subtype.ToLowerInvariant(), mediaType));
    }

    [Fact]
    public void NetworkDiagramMediaSubtypes_RejectInvalidSubtypeForMediaType()
    {
        Assert.False(NetworkDiagramMediaSubtypes.IsAllowed("OM4", NetworkDiagramLinkMediaTypes.Copper));
        Assert.False(NetworkDiagramMediaSubtypes.IsAllowed("Cat6", NetworkDiagramLinkMediaTypes.Wireless));
    }

    [Fact]
    public void NetworkDiagramPdfTextFitter_WrapsShortMultiWordLabels()
    {
        var fit = NetworkDiagramPdfTextFitter.Fit("Living Room Access Point", maxWidth: 46, maxLines: 2, fontSize: 8, minimumFontSize: 5.5, useEllipsis: true);

        Assert.True(fit.Lines.Count >= 2);
        Assert.True(fit.Lines.Count <= 2);
        Assert.All(fit.Lines, line => Assert.True(NetworkDiagramPdfTextFitter.MeasureWidth(line, fit.FontSize) <= 46.01));
    }

    [Fact]
    public void NetworkDiagramPdfTextFitter_TruncatesVeryLongLabelsWithEllipsis()
    {
        var fit = NetworkDiagramPdfTextFitter.Fit("TradingVmNodeWithAnExtremelyLongUnbrokenHostnameThatCannotFit", maxWidth: 38, maxLines: 1, fontSize: 8, minimumFontSize: 5, useEllipsis: true);

        var line = Assert.Single(fit.Lines);
        Assert.EndsWith("...", line);
        Assert.True(NetworkDiagramPdfTextFitter.MeasureWidth(line, fit.FontSize) <= 38.01);
    }

    [Fact]
    public void NetworkDiagramPdfTextFitter_NeverReturnsLinesWiderThanMaxWidth()
    {
        var fit = NetworkDiagramPdfTextFitter.Fit("Summerhouse Access Point monitored endpoint secondary text", maxWidth: 58, maxLines: 2, fontSize: 8, minimumFontSize: 5, useEllipsis: true);

        Assert.InRange(fit.Lines.Count, 1, 2);
        Assert.All(fit.Lines, line => Assert.True(NetworkDiagramPdfTextFitter.MeasureWidth(line, fit.FontSize) <= 58.01));
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
                new NetworkDiagramLinkDto { LinkId = "link-1", SourceNodeId = "node-1", TargetNodeId = "node-2", Label = "uplink", SourcePortLabel = "Gi1/0/1", TargetPortLabel = "eth0", MediaType = NetworkDiagramLinkMediaTypes.Copper, MediaSubtype = "Cat6", LinkType = NetworkDiagramLinkTypes.Standard, LinkSpeedValue = 1, LinkSpeedUnit = NetworkDiagramLinkSpeedUnits.Gbps, Vlans = [new NetworkDiagramLinkVlanDto { VlanId = 10, Name = "LAN", Mode = NetworkDiagramVlanModes.Tagged }, new NetworkDiagramLinkVlanDto { VlanId = 5, Name = "Mgmt", Mode = NetworkDiagramVlanModes.Untagged }] },
                new NetworkDiagramLinkDto { LinkId = "link-2", SourceNodeId = "node-2", TargetNodeId = "node-1", Label = "backup", Notes = "wireless failover", MediaType = NetworkDiagramLinkMediaTypes.Wireless, MediaSubtype = "802.11ac / Wi-Fi 5", LinkType = NetworkDiagramLinkTypes.PointToPoint },
                new NetworkDiagramLinkDto { LinkId = "link-3", SourceNodeId = "node-1", TargetNodeId = "node-2", Label = "storage", Notes = "10Gb fibre", MediaType = NetworkDiagramLinkMediaTypes.Fibre, MediaSubtype = "OM4", LinkType = NetworkDiagramLinkTypes.Lacp, LinkSpeedValue = 10, LinkSpeedUnit = NetworkDiagramLinkSpeedUnits.Gbps, LacpMemberCount = 2 }
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
        Assert.Contains("backup", pdfText);
        Assert.Contains("10Gb fibre", pdfText);
        Assert.Contains("Cat6", pdfText);
        Assert.Contains("T:10 LAN", pdfText);
        Assert.Contains("U:5", pdfText);
        Assert.Contains("LACP", pdfText);
        Assert.Contains("[8 5] 0 d", pdfText);
        Assert.Contains("0.49 0.23 0.93 RG", pdfText);
        Assert.Contains("Diagram links are visual documentation only", pdfText);
    }


    [Fact]
    public void NetworkDiagramPdfExportService_ExportsLongAndDenseNodeLabelsToPdf()
    {
        var service = new NetworkDiagramPdfExportService();
        var nodes = new List<NetworkDiagramNodeDto>();
        for (var i = 0; i < 12; i++)
        {
            nodes.Add(new NetworkDiagramNodeDto
            {
                NodeId = $"node-{i}",
                NodeType = "MonitoredEndpoint",
                DisplayLabel = i % 2 == 0 ? $"Trading VM {i} With Very Long Endpoint Label" : $"Summerhouse Access Point {i}",
                X = 120 + i * 90,
                Y = 400 + (i % 3) * 90,
                Width = 178,
                Height = 78,
                Notes = "operator note that should only render when there is enough room"
            });
        }

        var diagram = new NetworkDiagramDto
        {
            DiagramId = "dense",
            Name = "Dense Home",
            CanvasWidth = 4000,
            CanvasHeight = 2828,
            Nodes = nodes,
            Links = nodes.Skip(1).Select((node, index) => new NetworkDiagramLinkDto
            {
                LinkId = $"link-{index}",
                SourceNodeId = nodes[index].NodeId,
                TargetNodeId = node.NodeId,
                Label = "very long access uplink label that should not overflow"
            }).ToArray()
        };

        var export = service.Export(diagram, new NetworkDiagramPdfExportOptions("A3", new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero)));
        var pdfText = System.Text.Encoding.ASCII.GetString(export.Content);

        Assert.Equal("application/pdf", export.ContentType);
        Assert.StartsWith("%PDF-1.4", pdfText);
        Assert.Contains("Dense Home", pdfText);
        Assert.Contains("re W n", pdfText);
        Assert.Contains("Trading VM", pdfText);
    }

    [Fact]
    public void NetworkDiagramsController_AllowsAuthenticatedViewerAndRequiresAdminForEditAndExport()
    {
        var controllerAuthorize = typeof(NetworkDiagramsController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        var editAuthorize = typeof(NetworkDiagramsController).GetMethod(nameof(NetworkDiagramsController.Edit))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        var exportAuthorize = typeof(NetworkDiagramsController).GetMethod(nameof(NetworkDiagramsController.ExportPdf))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();

        Assert.Null(controllerAuthorize.Roles);
        Assert.Equal(ApplicationRoles.Admin, editAuthorize.Roles);
        Assert.Equal(ApplicationRoles.Admin, exportAuthorize.Roles);
    }

    [Fact]
    public async Task NetworkDiagramsExportPdf_ReturnsNotFound_WhenFeatureDisabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = false }),
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(),
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

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
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

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
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

        var result = await controller.ExportPdf("diagram-1", "A4", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("PingMonitor-NetworkDiagram-Core", file.FileDownloadName);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents));
    }


    [Fact]
    public void NetworkDiagramViewer_HasReadOnlyCanvasAndLiveOverlayControls()
    {
        var repoRoot = FindRepositoryRoot();
        var viewMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Views", "NetworkDiagrams", "View.cshtml"));
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "js", "network-diagrams-viewer.js"));

        Assert.Contains("data-network-diagram-viewer", viewMarkup);
        Assert.Contains("data-live-data-url", viewMarkup);
        Assert.Contains("Read-only viewer", viewMarkup);
        Assert.DoesNotContain("data-save-diagram", viewMarkup);
        Assert.DoesNotContain("data-add-endpoint-node", viewMarkup);
        Assert.DoesNotContain("data-delete-selection", viewMarkup);
        Assert.DoesNotContain("Draw link", viewMarkup);
        Assert.Contains("refreshIntervalMs = 20000", script);
        Assert.Contains("State: ${stateLabel(stateValue)}", script);
        Assert.Contains("24h:", script);
        Assert.Contains("RTT:", script);
    }

    [Fact]
    public void NetworkDiagramViewer_RendersNoSelectionSummaryAndClickToCentreHooks()
    {
        var repoRoot = FindRepositoryRoot();
        var viewMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "Views", "NetworkDiagrams", "View.cshtml"));
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "js", "network-diagrams-viewer.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "wwwroot", "css", "network-diagrams.css"));

        Assert.Contains("data-no-selection-panel", viewMarkup);
        Assert.Contains("Loading diagram summary", viewMarkup);
        Assert.Contains("Viewer overlays existing monitoring status only. Visual links remain documentation-only.", viewMarkup);
        Assert.Contains("buildDiagramSummaryHtml", script);
        Assert.Contains("Total nodes", script);
        Assert.Contains("Monitored", script);
        Assert.Contains("Diagram-only", script);
        Assert.Contains("Visual links", script);
        Assert.Contains("Live overlay refresh", script);
        Assert.Contains("Up: 0, Degraded: 0, Down: 0, Suppressed: 0, Unknown: 0", script);
        Assert.Contains("Live overlay data is pending. Saved diagram summary is available.", script);
        Assert.Contains("Live overlay data is currently unavailable. Showing saved diagram only.", script);
        Assert.Contains("No visible live overlay data is available for monitored endpoint nodes.", script);
        Assert.Contains("No affected endpoints.", script);
        Assert.Contains("Array.isArray(data?.nodes) ? data.nodes : []", script);
        Assert.Contains("assignments: Array.isArray(node.assignments) ? node.assignments : []", script);
        Assert.Contains("state.liveOverlayAttempted = true", script);
        Assert.Contains("state.liveOverlayStale = true", script);
        Assert.Contains("Down endpoints", script);
        Assert.Contains("Degraded endpoints", script);
        Assert.Contains("Suppressed endpoints", script);
        Assert.Contains("Unknown endpoints", script);
        Assert.Contains("Highest RTT", script);
        Assert.Contains("data-summary-node-id", script);
        Assert.Contains("function centreOnNode(nodeId, preferredZoom = 1)", script);
        Assert.Contains("selectNode(node.id)", script);
        Assert.Contains("clampPan();", script);
        Assert.Contains("diagram-summary-stat-grid", styles);
        Assert.Contains("diagram-summary-endpoint:focus-visible", styles);
    }

    [Fact]
    public async Task NetworkDiagramsViewer_ReturnsViewer_WhenFeatureEnabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = true }),
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(new NetworkDiagram { DiagramId = "diagram-1", Name = "Core", UpdatedAtUtc = DateTimeOffset.UtcNow }),
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService(isAdmin: true));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) } };

        var result = await controller.ViewDiagram("diagram-1", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("View", view.ViewName);
        var model = Assert.IsType<NetworkDiagramViewerPageViewModel>(view.Model);
        Assert.Equal("diagram-1", model.DiagramId);
        Assert.True(model.IsAdmin);
    }

    [Fact]
    public async Task NetworkDiagramsLiveData_ReturnsNotFound_WhenFeatureDisabled()
    {
        var controller = new NetworkDiagramsController(
            new FakeApplicationSettingsService(new ApplicationSettingsDto { NetworkDiagramsEnabled = false }),
            new FakeEndpointManagementQueryService(),
            new FakeNetworkDiagramService(new NetworkDiagram { DiagramId = "diagram-1", Name = "Core", UpdatedAtUtc = DateTimeOffset.UtcNow }),
            new FakeNetworkDiagramPdfExportService(),
            new FakeNetworkDiagramLiveOverlayService(),
            new FakeUserAccessScopeService());

        var result = await controller.LiveData("diagram-1", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("Network diagrams are not enabled", notFound.Value!.ToString());
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


    private sealed class FakeNetworkDiagramLiveOverlayService : INetworkDiagramLiveOverlayService
    {
        public Task<NetworkDiagramLiveOverlayResponse> GetOverlayAsync(string diagramId, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            return Task.FromResult(new NetworkDiagramLiveOverlayResponse
            {
                DiagramId = diagramId,
                RefreshedAtUtc = DateTimeOffset.UtcNow,
                Nodes =
                [
                    new NetworkDiagramNodeLiveOverlayDto
                    {
                        NodeId = "node-1",
                        EndpointId = "endpoint-1",
                        SummaryState = EndpointStateKind.Up,
                        SummaryStateLabel = "Up",
                        UptimePercent24h = 99.9,
                        UptimeDisplay = "99.9%",
                        LastRttMs = 4.2
                    }
                ]
            });
        }
    }

    private sealed class FakeUserAccessScopeService : IUserAccessScopeService
    {
        private readonly bool _isAdmin;

        public FakeUserAccessScopeService(bool isAdmin = true)
        {
            _isAdmin = isAdmin;
        }

        public Task<bool> IsAdminAsync(ClaimsPrincipal principal) => Task.FromResult(_isAdmin);

        public Task<IReadOnlySet<string>> GetVisibleEndpointIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            IReadOnlySet<string> result = new HashSet<string>(StringComparer.Ordinal) { "endpoint-1" };
            return Task.FromResult(result);
        }

        public Task<bool> CanAccessAssignmentAsync(ClaimsPrincipal principal, string assignmentId, CancellationToken cancellationToken) => Task.FromResult(_isAdmin);
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
