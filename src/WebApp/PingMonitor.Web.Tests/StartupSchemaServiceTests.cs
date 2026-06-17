using System.Data.Common;
using System.Reflection;
using System.Text.Json.Nodes;
using PingMonitor.Web.Services.StartupGate;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class StartupSchemaServiceTests
{
    [Fact]
    public void GetMissingColumns_UsesProviderNeutralConnection()
    {
        var method = typeof(StartupSchemaService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == "GetMissingColumnsAsync");

        var connectionParameter = method.GetParameters().First();

        Assert.Equal(typeof(DbConnection), connectionParameter.ParameterType);
    }

    [Fact]
    public void NetworkDiagramMetadataMigration_DoesNotCastEfConnectionToMySqlConnectorConnection()
    {
        var repoRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repoRoot,
            "src",
            "WebApp",
            "PingMonitor.Web",
            "Services",
            "StartupGate",
            "StartupSchemaService.cs");

        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("GetMissingColumnsAsync((MySqlConnection)connection", source);
    }


    [Fact]
    public void NetworkDiagramAreaAndVlanSchema_IsDeclaredInStartupGateMetadata()
    {
        var repoRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repoRoot,
            "src",
            "WebApp",
            "PingMonitor.Web",
            "Services",
            "StartupGate",
            "StartupSchemaService.cs");
        var appSettingsPath = Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "appsettings.json");
        var developmentSettingsPath = Path.Combine(repoRoot, "src", "WebApp", "PingMonitor.Web", "appsettings.Development.json");

        var source = File.ReadAllText(sourcePath);
        var appRequiredSchemaVersion = ReadRequiredSchemaVersion(appSettingsPath);
        var developmentRequiredSchemaVersion = ReadRequiredSchemaVersion(developmentSettingsPath);

        Assert.Contains("\"NetworkDiagramAreas\"", source);
        Assert.Contains("\"NetworkDiagramLinkVlans\"", source);
        Assert.Equal(24, appRequiredSchemaVersion);
        Assert.Contains("AiAssistantSettings", source);
        Assert.Contains("RequiredAiAssistantSettingsColumns", source);
        Assert.Equal(appRequiredSchemaVersion, developmentRequiredSchemaVersion);
    }

    private static int ReadRequiredSchemaVersion(string path)
    {
        var json = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"Could not parse {path}.");
        return json["StartupGate"]?["RequiredSchemaVersion"]?.GetValue<int>()
            ?? throw new InvalidOperationException($"StartupGate.RequiredSchemaVersion missing from {path}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
