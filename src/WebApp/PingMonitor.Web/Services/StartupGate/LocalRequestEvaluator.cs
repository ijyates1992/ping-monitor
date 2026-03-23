using System.Net;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class LocalRequestEvaluator : ILocalRequestEvaluator
{
    public bool IsLocal(HttpContext httpContext)
    {
        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIpAddress))
        {
            return true;
        }

        var localIpAddress = httpContext.Connection.LocalIpAddress;
        return localIpAddress is not null && remoteIpAddress.Equals(localIpAddress);
    }
}
