using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Backups;
using PingMonitor.Web.Services.NetworkDiagrams;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ConfigurationBackupNetworkDiagramTests
{
    [Fact]
    public void ConfigurationBackupSections_IncludesNetworkDiagramsSection()
    {
        Assert.Contains(ConfigurationBackupSections.NetworkDiagrams, ConfigurationBackupSections.All);
    }

    [Fact]
    public async Task RestorePreview_ReportsNetworkDiagramCounts()
    {
        var document = BuildDocument(BuildNetworkDiagramSection());
        var service = new ConfigurationRestorePreviewService(
            new FakeDocumentLoader(document),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationRestorePreviewService>.Instance);

        var preview = await service.GetPreviewAsync("backup.json", CancellationToken.None);

        Assert.Contains(ConfigurationBackupSections.NetworkDiagrams, preview.IncludedSections);
        Assert.Equal(1, preview.Counts.NetworkDiagrams);
        Assert.Equal(1, preview.Counts.NetworkDiagramAreas);
        Assert.Equal(2, preview.Counts.NetworkDiagramNodes);
        Assert.Equal(1, preview.Counts.NetworkDiagramLinks);
        Assert.Equal(1, preview.Counts.NetworkDiagramLinkVlans);
    }

    [Fact]
    public async Task RestorePreview_OlderBackupWithoutNetworkDiagramsSectionStillSucceeds()
    {
        var document = new ConfigurationBackupDocument
        {
            FormatVersion = ConfigurationBackupMetadata.CurrentFormatVersion,
            AppVersion = "test",
            BackupName = "old-backup",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            MachineName = "test-machine",
            Sections = new ConfigurationBackupSectionData
            {
                Endpoints =
                [
                    new BackupEndpointRecord
                    {
                        EndpointId = "endpoint-1",
                        Name = "Gateway",
                        Target = "192.0.2.1",
                        IconKey = "router",
                        Enabled = true,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }
        };
        var service = new ConfigurationRestorePreviewService(
            new FakeDocumentLoader(document),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationRestorePreviewService>.Instance);

        var preview = await service.GetPreviewAsync("old-backup.json", CancellationToken.None);

        Assert.DoesNotContain(ConfigurationBackupSections.NetworkDiagrams, preview.IncludedSections);
        Assert.Equal(0, preview.Counts.NetworkDiagrams);
        Assert.Equal(1, preview.Counts.Endpoints);
    }

    [Fact]
    public void Validator_AcceptsNetworkDiagramBackupData()
    {
        var validator = new ConfigurationBackupDocumentValidator();
        var document = BuildDocument(BuildNetworkDiagramSection());

        validator.Validate(document, "backup.json");
    }

    [Fact]
    public void Validator_RejectsInvalidNetworkDiagramLinkVlan()
    {
        var validator = new ConfigurationBackupDocumentValidator();
        var section = BuildNetworkDiagramSection(vlanId: 4095);
        var document = BuildDocument(section);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(document, "backup.json"));
        Assert.Contains("invalid VLAN metadata", exception.Message);
    }

    [Fact]
    public void Validator_RejectsInvalidNetworkDiagramAreaDimensions()
    {
        var validator = new ConfigurationBackupDocumentValidator();
        var section = BuildNetworkDiagramSection();
        var diagram = section.Diagrams[0];
        section = new BackupNetworkDiagramSection
        {
            Diagrams =
            [
                new BackupNetworkDiagramRecord
                {
                    DiagramId = diagram.DiagramId,
                    Name = diagram.Name,
                    CanvasWidth = diagram.CanvasWidth,
                    CanvasHeight = diagram.CanvasHeight,
                    ViewportZoom = diagram.ViewportZoom,
                    Areas =
                    [
                        new BackupNetworkDiagramAreaRecord
                        {
                            AreaId = diagram.Areas[0].AreaId,
                            Label = diagram.Areas[0].Label,
                            Notes = diagram.Areas[0].Notes,
                            X = diagram.Areas[0].X,
                            Y = diagram.Areas[0].Y,
                            Width = double.NaN,
                            Height = diagram.Areas[0].Height,
                            StyleKey = diagram.Areas[0].StyleKey,
                            SortOrder = diagram.Areas[0].SortOrder,
                            CreatedAtUtc = diagram.Areas[0].CreatedAtUtc,
                            UpdatedAtUtc = diagram.Areas[0].UpdatedAtUtc
                        }
                    ],
                    Nodes = diagram.Nodes,
                    Links = diagram.Links
                }
            ]
        };
        var document = BuildDocument(section);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(document, "backup.json"));
        Assert.Contains("area width", exception.Message);
    }

    private static ConfigurationBackupDocument BuildDocument(BackupNetworkDiagramSection? networkDiagrams)
    {
        return new ConfigurationBackupDocument
        {
            FormatVersion = ConfigurationBackupMetadata.CurrentFormatVersion,
            AppVersion = "test",
            BackupName = "test-backup",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            MachineName = "test-machine",
            Sections = new ConfigurationBackupSectionData
            {
                NetworkDiagrams = networkDiagrams
            }
        };
    }

    private static BackupNetworkDiagramSection BuildNetworkDiagramSection(int vlanId = 10)
    {
        return new BackupNetworkDiagramSection
        {
            Diagrams =
            [
                new BackupNetworkDiagramRecord
                {
                    DiagramId = "diagram-1",
                    Name = "Core network",
                    Description = "Documentation diagram",
                    CanvasWidth = 5000,
                    CanvasHeight = 3000,
                    ViewportPanX = -120,
                    ViewportPanY = 80,
                    ViewportZoom = 1.25,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Areas =
                    [
                        new BackupNetworkDiagramAreaRecord
                        {
                            AreaId = "area-house",
                            Label = "House",
                            Notes = "Main house network",
                            X = 50,
                            Y = 75,
                            Width = 900,
                            Height = 500,
                            StyleKey = "blue",
                            SortOrder = 0,
                            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        }
                    ],
                    Nodes =
                    [
                        new BackupNetworkDiagramNodeRecord
                        {
                            NodeId = "node-endpoint",
                            NodeType = nameof(NetworkDiagramNodeType.MonitoredEndpoint),
                            EndpointId = "endpoint-1",
                            DisplayLabel = "Gateway",
                            IconKey = "router",
                            X = 100,
                            Y = 200,
                            Width = 178,
                            Height = 78,
                            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        },
                        new BackupNetworkDiagramNodeRecord
                        {
                            NodeId = "node-custom",
                            NodeType = nameof(NetworkDiagramNodeType.CustomDevice),
                            DisplayLabel = "Switch",
                            IconKey = "switch",
                            X = 450,
                            Y = 225,
                            Width = 178,
                            Height = 78,
                            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        }
                    ],
                    Links =
                    [
                        new BackupNetworkDiagramLinkRecord
                        {
                            LinkId = "link-1",
                            SourceNodeId = "node-endpoint",
                            TargetNodeId = "node-custom",
                            Label = "uplink",
                            SourcePortLabel = "eth0",
                            TargetPortLabel = "Gi1/0/1",
                            MediaType = NetworkDiagramLinkMediaTypes.Fibre,
                            MediaSubtype = "OS2",
                            FibreSubtype = "OS2",
                            LinkType = NetworkDiagramLinkTypes.Trunk,
                            LinkSpeedValue = 10,
                            LinkSpeedUnit = NetworkDiagramLinkSpeedUnits.Gbps,
                            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                            UpdatedAtUtc = DateTimeOffset.UtcNow,
                            Vlans =
                            [
                                new BackupNetworkDiagramLinkVlanRecord
                                {
                                    LinkVlanId = "vlan-1",
                                    VlanId = vlanId,
                                    Name = "Users",
                                    Mode = NetworkDiagramVlanModes.Tagged,
                                    Notes = "documentation only",
                                    SortOrder = 0,
                                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                                    UpdatedAtUtc = DateTimeOffset.UtcNow
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private sealed class FakeDocumentLoader : IConfigurationBackupDocumentLoader
    {
        private readonly ConfigurationBackupDocument _document;

        public FakeDocumentLoader(ConfigurationBackupDocument document)
        {
            _document = document;
        }

        public Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<BackupFileListItem> result = [];
            return Task.FromResult(result);
        }

        public string ResolveBackupPath(string fileId)
        {
            return fileId;
        }

        public Task<ConfigurationBackupDocument> LoadValidatedDocumentAsync(string fileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_document);
        }
    }
}
