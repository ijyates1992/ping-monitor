using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Services;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentTemplateVersionProviderTests
{
    [Fact]
    public void GetBundledAgentVersion_ReadsVersionFromPublishedLayout()
    {
        var contentRoot = CreateTempDirectory();
        try
        {
            var versionFile = Path.Combine(contentRoot, "Agent", "app", "version.py");
            Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);
            File.WriteAllText(versionFile, "AGENT_VERSION = \"1.2.3\"");

            var provider = new AgentTemplateVersionProvider(
                new StubWebHostEnvironment(contentRoot),
                NullLogger<AgentTemplateVersionProvider>.Instance);

            var version = provider.GetBundledAgentVersion();

            Assert.Equal("1.2.3", version);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void GetBundledAgentVersion_ReadsVersionFromDevelopmentLayout()
    {
        var root = CreateTempDirectory();
        try
        {
            var contentRoot = Path.Combine(root, "src", "WebApp", "PingMonitor.Web");
            Directory.CreateDirectory(contentRoot);
            var versionFile = Path.Combine(root, "src", "Agent", "app", "version.py");
            Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);
            File.WriteAllText(versionFile, "AGENT_VERSION = \"2.0.0\"");

            var provider = new AgentTemplateVersionProvider(
                new StubWebHostEnvironment(contentRoot),
                NullLogger<AgentTemplateVersionProvider>.Instance);

            var version = provider.GetBundledAgentVersion();

            Assert.Equal("2.0.0", version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetBundledAgentVersion_ReturnsNullWhenVersionConstantIsMissing()
    {
        var contentRoot = CreateTempDirectory();
        try
        {
            var versionFile = Path.Combine(contentRoot, "Agent", "app", "version.py");
            Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);
            File.WriteAllText(versionFile, "VERSION = \"x\"");

            var provider = new AgentTemplateVersionProvider(
                new StubWebHostEnvironment(contentRoot),
                NullLogger<AgentTemplateVersionProvider>.Instance);

            var version = provider.GetBundledAgentVersion();

            Assert.Null(version);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ping-monitor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "PingMonitor.Web.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
