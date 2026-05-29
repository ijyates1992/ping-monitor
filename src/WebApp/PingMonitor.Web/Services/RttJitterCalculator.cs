namespace PingMonitor.Web.Services;

internal readonly record struct RttJitterSample(DateTimeOffset TimestampUtc, double RttMs);

internal readonly record struct RttJitterDelta(DateTimeOffset TimestampUtc, double JitterMs);

internal static class RttJitterCalculator
{
    public static IReadOnlyList<RttJitterDelta> CalculateAbsoluteDeltas(IReadOnlyList<RttJitterSample> orderedSamples)
    {
        if (orderedSamples.Count < 2)
        {
            return [];
        }

        var deltas = new List<RttJitterDelta>(orderedSamples.Count - 1);
        for (var index = 1; index < orderedSamples.Count; index++)
        {
            var current = orderedSamples[index];
            var previous = orderedSamples[index - 1];
            deltas.Add(new RttJitterDelta(
                current.TimestampUtc,
                Math.Abs(current.RttMs - previous.RttMs)));
        }

        return deltas;
    }

    public static double? CalculateAverageAbsoluteDeltaMs(IReadOnlyList<RttJitterSample> orderedSamples)
    {
        if (orderedSamples.Count < 2)
        {
            return null;
        }

        var deltaSum = 0d;
        for (var index = 1; index < orderedSamples.Count; index++)
        {
            deltaSum += Math.Abs(orderedSamples[index].RttMs - orderedSamples[index - 1].RttMs);
        }

        return deltaSum / (orderedSamples.Count - 1);
    }
}
