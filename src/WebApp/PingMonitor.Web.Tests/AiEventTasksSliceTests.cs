using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiEventTasksSliceTests
{
    [Fact]
    public void EventTasksPage_MaterializesVisibleEndpointIdsBeforeEndpointFilter()
    {
        var source = ReadWebFile("Controllers", "AiEventTasksController.cs");

        Assert.Contains("var visibleEndpointIds=visible.ToArray();", source);
        Assert.Contains("visibleEndpointIds.Contains(x.EndpointId)", source);
        Assert.DoesNotContain("visible.Contains(x.EndpointId)", source);
    }

    private static string ReadWebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", .. parts]));
}
