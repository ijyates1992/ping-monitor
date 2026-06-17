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

    [Fact]
    public void EventTasksPage_IncludesStandaloneApplicationChromeAndMobileStyles()
    {
        var source = ReadWebFile("Views", "AiEventTasks", "Index.cshtml");

        Assert.Contains("<!DOCTYPE html>", source);
        Assert.Contains("@await Html.PartialAsync(\"_ThemeHead\")", source);
        Assert.Contains("@await Html.PartialAsync(\"_AuthenticatedNav\")", source);
        Assert.Contains("<main class=\"wrap\">", source);
        Assert.Contains("@@media(max-width:560px)", source);
        Assert.Contains("@Html.AntiForgeryToken()", source);
    }

    private static string ReadWebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "PingMonitor.Web", .. parts]));
}
