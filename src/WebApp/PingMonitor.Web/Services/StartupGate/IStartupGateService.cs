using Microsoft.AspNetCore.Http;

namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupGateService
{
    Task<StartupGateStatus> EvaluateAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
