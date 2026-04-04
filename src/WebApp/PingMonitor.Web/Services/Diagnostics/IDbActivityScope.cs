namespace PingMonitor.Web.Services.Diagnostics;

public interface IDbActivityScope
{
    string CurrentSubsystem { get; }
    IDisposable BeginScope(string subsystem);
}
