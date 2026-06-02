using System.Data.Common;
using System.Reflection;
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
