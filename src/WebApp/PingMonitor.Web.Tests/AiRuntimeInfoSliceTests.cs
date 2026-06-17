using PingMonitor.Web.Services.AiChat;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiRuntimeInfoSliceTests
{
    [Fact]
    public void RuntimeInfoTool_IsRegisteredOnSharedAiToolPath()
    {
        var programSource = ReadWebSource("Program.cs");

        Assert.Contains("IAiRuntimeInfoService, AiRuntimeInfoService", programSource);
        Assert.Contains("IAiTool, AiRuntimeInfoTool", programSource);
    }

    [Fact]
    public void RuntimeInfoTool_DefinitionUsesExpectedNameDescriptionAndSchema()
    {
        var source = ReadWebSource("Services", "AiTools", "AiRuntimeInfoTool.cs");

        Assert.Contains("get_application_runtime_info", source);
        Assert.Contains("read-only Ping Monitor runtime and build information", source);
        Assert.Contains("includeDatabase", source);
        Assert.Contains("includeEnvironment", source);
        Assert.Contains("includeBuild", source);
        Assert.Contains("additionalProperties", source);
        Assert.Contains("false", source);
    }

    [Fact]
    public void RuntimeInfoService_RedactsNonAdminOperationalDetails()
    {
        var source = ReadWebSource("Services", "AiTools", "AiRuntimeInfoService.cs");

        Assert.Contains("if (!request.IsAdmin)", source);
        Assert.Contains("DetailLevel = \"Minimal\"", source);
        Assert.Contains("detailed runtime", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admin-only", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connection strings", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API keys", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeInfoService_AdminResultIncludesSafeRuntimeStartupAndDatabaseFields()
    {
        var source = ReadWebSource("Services", "AiTools", "AiRuntimeInfoService.cs");

        Assert.Contains("RuntimeInformation.FrameworkDescription", source);
        Assert.Contains("RuntimeInformation.OSDescription", source);
        Assert.Contains("ProcessArchitecture", source);
        Assert.Contains("RequiredSchemaVersion", source);
        Assert.Contains("CurrentSchemaVersion", source);
        Assert.Contains("SchemaCompatible", source);
        Assert.Contains("Provider = snapshot.ProviderName", source);
        Assert.Contains("DatabaseName = snapshot.DatabaseName", source);
        Assert.Contains("Take(10)", source);
        Assert.Contains("Database size information is not available", source);
    }

    [Fact]
    public void RuntimeInfoService_DoesNotAccessDatabaseWhileStartupGateIsActive()
    {
        var source = ReadWebSource("Services", "AiTools", "AiRuntimeInfoService.cs");

        Assert.Contains("!_startupGateRuntimeState.IsOperationalMode", source);
        Assert.Contains("Startup Gate is active", source);
    }

    [Fact]
    public void BuiltInPromptMentionsRuntimeInfoToolAndSecretRestrictions()
    {
        Assert.Contains("get_application_runtime_info", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("Do not guess version, schema, database size, or build status", AiChatService.BuiltInSystemPrompt);
        Assert.Contains("connection strings", AiChatService.BuiltInSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API keys", AiChatService.BuiltInSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admin-only", AiChatService.BuiltInSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolArgumentDefaultsAreTrueAndRejectUnsupportedArguments()
    {
        var source = ReadWebSource("Services", "AiTools", "AiRuntimeInfoTool.cs");

        Assert.Contains("includeDatabase = true", source);
        Assert.Contains("includeEnvironment = true", source);
        Assert.Contains("includeBuild = true", source);
        Assert.Contains("Unsupported argument", source);
        Assert.Contains("must be a boolean", source);
    }

    private static string ReadWebSource(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web" }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }
}
