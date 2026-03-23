using Microsoft.AspNetCore.Http;

namespace PingMonitor.Web.Services.StartupGate;

public interface ILocalRequestEvaluator
{
    bool IsLocal(HttpContext httpContext);
}
