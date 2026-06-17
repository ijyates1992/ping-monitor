using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiEventTasksSliceTests
{
    [Fact]
    public void EventTasksPage_FiltersVisibleEndpointIdsAfterMySqlQueryMaterialization()
    {
        var source = ReadWebFile("Controllers", "AiEventTasksController.cs");

        Assert.Contains("var visibleEndpointIds=visible.ToHashSet(StringComparer.Ordinal);", source);
        Assert.Contains("var endpointRows=await _db.Endpoints.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new{x.Name,x.EndpointId}).ToListAsync(ct);", source);
        Assert.Contains("endpointRows.Where(x=>visibleEndpointIds.Contains(x.EndpointId))", source);
        Assert.DoesNotContain("AsNoTracking().Where(x=>visibleEndpointIds.Contains(x.EndpointId))", source);
    }

    private static string ReadWebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", .. parts]));
}
