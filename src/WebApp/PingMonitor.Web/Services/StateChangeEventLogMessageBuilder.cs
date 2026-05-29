using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal static class StateChangeEventLogMessageBuilder
{
    public static string Build(
        string endpointName,
        EndpointStateKind previousState,
        EndpointStateKind nextState,
        DateTimeOffset transitionAtUtc,
        DateTimeOffset previousStateChangedAtUtc,
        DegradedEndpointEvaluationResult degradedEvaluation)
    {
        if (nextState == EndpointStateKind.Down)
        {
            return $"Endpoint \"{endpointName}\" went down.";
        }

        if (previousState == EndpointStateKind.Down && nextState == EndpointStateKind.Up)
        {
            var downtime = transitionAtUtc - previousStateChangedAtUtc;
            return $"Endpoint \"{endpointName}\" recovered after {FormatDuration(downtime)} downtime.";
        }

        if (nextState == EndpointStateKind.Degraded && degradedEvaluation.IsDegraded && !string.IsNullOrWhiteSpace(degradedEvaluation.ReasonSummary))
        {
            return $"Endpoint \"{endpointName}\" is degraded: {degradedEvaluation.ReasonSummary}.";
        }

        if (previousState == EndpointStateKind.Degraded && nextState == EndpointStateKind.Up)
        {
            return $"Endpoint \"{endpointName}\" recovered from degraded performance; current RTT, packet loss, and jitter no longer exceed configured thresholds or there is insufficient comparison data.";
        }

        return $"Endpoint \"{endpointName}\" state changed from {previousState} to {nextState}.";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var hours = (int)safeDuration.TotalHours;
        return $"{hours:D2}:{safeDuration.Minutes:D2}:{safeDuration.Seconds:D2}";
    }
}
