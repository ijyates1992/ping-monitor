using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal sealed class AiEndpointLookupService : IAiEndpointLookupService
{
    private const int MaxCandidates = 5;
    private readonly PingMonitorDbContext _dbContext;
    private readonly IUserAccessScopeService _access;
    private readonly IDbActivityScope _dbActivityScope;

    public AiEndpointLookupService(PingMonitorDbContext dbContext, IUserAccessScopeService access, IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _access = access;
        _dbActivityScope = dbActivityScope;
    }

    public async Task<AiEndpointLookupResult> SearchEndpointsAsync(ClaimsPrincipal user, string userMessage, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("Ai.EndpointLookup");
        var terms = ExtractTerms(userMessage);
        if (terms.Count == 0) return new AiEndpointLookupResult { Message = "No endpoint reference was found in the question." };

        var query = _dbContext.Endpoints.AsNoTracking();
        if (!await _access.IsAdminAsync(user))
        {
            var visible = (await _access.GetVisibleEndpointIdsAsync(user, cancellationToken)).ToArray();
            query = query.Where(x => visible.Contains(x.EndpointId));
        }

        var endpoints = await query.Select(x => new AiEndpointLookupItem { EndpointId = x.EndpointId, Name = x.Name, Target = x.Target, Enabled = x.Enabled }).ToListAsync(cancellationToken);
        var scored = endpoints.Select(e => (Endpoint: e, Score: Score(e, terms))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Endpoint.Name).Take(MaxCandidates).ToList();
        if (scored.Count == 0) return new AiEndpointLookupResult { Message = "No matching visible endpoint was found." };
        if (scored.Count == 1 || scored[0].Score >= 100 || scored[0].Score >= scored[1].Score + 30)
        {
            return new AiEndpointLookupResult { Succeeded = true, Matches = [scored[0].Endpoint] };
        }

        return new AiEndpointLookupResult { Ambiguous = true, Message = "Multiple visible endpoints matched. Ask the user to choose one.", Matches = scored.Select(x => x.Endpoint).ToArray() };
    }

    private static int Score(AiEndpointLookupItem endpoint, IReadOnlyList<string> terms)
    {
        var name = endpoint.Name.ToLowerInvariant();
        var target = endpoint.Target.ToLowerInvariant();
        var best = 0;
        foreach (var term in terms)
        {
            if (name == term || target == term) best = Math.Max(best, 120);
            else if (name.Contains(term, StringComparison.Ordinal) || target.Contains(term, StringComparison.Ordinal)) best = Math.Max(best, Math.Min(90, 35 + term.Length * 3));
            else
            {
                var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var matched = words.Count(w => name.Contains(w, StringComparison.Ordinal) || target.Contains(w, StringComparison.Ordinal));
                if (matched > 0) best = Math.Max(best, matched * 15);
            }
        }
        return best;
    }

    private static IReadOnlyList<string> ExtractTerms(string message)
    {
        var normalized = message.ToLowerInvariant();
        var terms = new List<string>();
        foreach (var marker in new[] { "with ", "for ", "is ", "has " })
        {
            var idx = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0) terms.Add(Clean(normalized[(idx + marker.Length)..]));
        }
        terms.Add(Clean(normalized));
        return terms.Where(x => x.Length >= 2).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Clean(string value)
    {
        foreach (var phrase in new[] { "what is going on", "what's going on", "uptime", "down today", "been down", "flapping", "recent check pattern", "recent checks", "packet loss", "latency", "rtt", "over the last", "last 24 hours", "today", "?" })
            value = value.Replace(phrase, " ", StringComparison.OrdinalIgnoreCase);
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}

internal sealed class AiEndpointDiagnosticsService : IAiEndpointDiagnosticsService
{
    private const int MaxSeriesPoints = 120;
    private readonly PingMonitorDbContext _dbContext;
    private readonly IUserAccessScopeService _access;
    private readonly IDbActivityScope _dbActivityScope;

    public AiEndpointDiagnosticsService(PingMonitorDbContext dbContext, IUserAccessScopeService access, IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _access = access;
        _dbActivityScope = dbActivityScope;
    }

    public async Task<AiEndpointDiagnosticsResult> GetDiagnosticsPackAsync(ClaimsPrincipal user, string endpointId, string requestedWindow, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("Ai.EndpointDiagnostics");
        if (!await _access.IsAdminAsync(user) && !(await _access.GetVisibleEndpointIdsAsync(user, cancellationToken)).Contains(endpointId))
            return new AiEndpointDiagnosticsResult { ErrorMessage = "No matching visible endpoint was found." };

        var now = DateTimeOffset.UtcNow;
        var window = ResolveWindow(requestedWindow, now);
        var row = await (from endpoint in _dbContext.Endpoints.AsNoTracking().Where(x => x.EndpointId == endpointId)
                         join assignment in _dbContext.MonitorAssignments.AsNoTracking() on endpoint.EndpointId equals assignment.EndpointId
                         join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
                         join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into sj
                         from state in sj.DefaultIfEmpty()
                         orderby assignment.Enabled descending, assignment.CreatedAtUtc
                         select new { endpoint, assignment, agent, state }).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return new AiEndpointDiagnosticsResult { ErrorMessage = "No monitor assignment exists for the visible endpoint." };

        var checks = await _dbContext.CheckResults.AsNoTracking().Where(x => x.AssignmentId == row.assignment.AssignmentId && x.CheckedAtUtc >= window.FromUtc && x.CheckedAtUtc <= window.ToUtc).OrderBy(x => x.CheckedAtUtc).ToListAsync(cancellationToken);
        var transitions = await _dbContext.StateTransitions.AsNoTracking().Where(x => x.AssignmentId == row.assignment.AssignmentId && x.TransitionAtUtc >= window.FromUtc && x.TransitionAtUtc <= window.ToUtc).OrderBy(x => x.TransitionAtUtc).ToListAsync(cancellationToken);
        var intervals = await _dbContext.AssignmentStateIntervals.AsNoTracking().Where(x => x.AssignmentId == row.assignment.AssignmentId && x.StartedAtUtc < window.ToUtc && (x.EndedAtUtc == null || x.EndedAtUtc > window.FromUtc)).ToListAsync(cancellationToken);

        var samples = checks.Select(x => (double?)x.RoundTripMs).Where(x => x.HasValue).Select(x => x!.Value).Order().ToArray();
        var received = checks.Count; var ok = checks.Count(x => x.Success); var failed = received - ok;
        var expected = row.assignment.PingIntervalSeconds > 0 ? (int)Math.Ceiling((window.ToUtc - window.FromUtc).TotalSeconds / row.assignment.PingIntervalSeconds) : (int?)null;
        var uptime = BuildUptime(intervals, transitions, window);

        return new AiEndpointDiagnosticsResult { Succeeded = true, Pack = new AiEndpointDiagnosticsPack
        {
            GeneratedAtUtc = now,
            Endpoint = new AiEndpointLookupItem { EndpointId = row.endpoint.EndpointId, Name = row.endpoint.Name, Target = row.endpoint.Target, Enabled = row.endpoint.Enabled },
            Assignment = new AiEndpointAssignmentInfo { AssignmentId = row.assignment.AssignmentId, AgentId = row.agent.AgentId, AgentName = row.agent.Name ?? row.agent.InstanceId, AgentOnlineState = row.agent.Status },
            CurrentState = new AiEndpointCurrentStateInfo { State = row.state?.CurrentState ?? EndpointStateKind.Unknown, LastChangedUtc = row.state?.LastStateChangeUtc, LastCheckUtc = row.state?.LastCheckUtc, UnknownReason = row.agent.Status != AgentHealthStatus.Online && (row.state?.CurrentState ?? EndpointStateKind.Unknown) == EndpointStateKind.Unknown ? "Agent is offline or stale; UNKNOWN is not endpoint downtime." : null },
            Window = window,
            Uptime = uptime,
            Checks = new AiEndpointCheckSummary { ReceivedSamples = received, SuccessfulSamples = ok, FailedSamples = failed, ExpectedSamples = expected, MissingSamplesEstimate = expected.HasValue ? Math.Max(0, expected.Value - received) : null, PacketLossPercent = received > 0 ? Math.Round(failed * 100d / received, 2) : null },
            Rtt = samples.Length == 0 ? new AiEndpointRttSummary { Available = false, Reason = "No RTT samples in the selected bounded window." } : new AiEndpointRttSummary { Available = true, MinMs = samples.First(), MaxMs = samples.Last(), AvgMs = Math.Round(samples.Average(), 2), MedianMs = Percentile(samples, .5), P95Ms = Percentile(samples, .95) },
            RecentTransitions = transitions.OrderByDescending(x => x.TransitionAtUtc).Take(20).Select(x => new AiEndpointTransitionItem { PreviousState = x.PreviousState, NewState = x.NewState, TransitionAtUtc = x.TransitionAtUtc, ReasonCode = x.ReasonCode }).ToArray(),
            RecentSampleTail = checks.OrderByDescending(x => x.CheckedAtUtc).Take(MaxSeriesPoints).OrderBy(x => x.CheckedAtUtc).Select(x => new AiEndpointCheckSeriesPoint { CheckedAtUtc = x.CheckedAtUtc, Success = x.Success, RttMs = (double?)x.RoundTripMs, ErrorCode = x.ErrorCode, ErrorMessage = x.ErrorMessage == null ? null : x.ErrorMessage[..Math.Min(160, x.ErrorMessage.Length)] }).ToArray()
        }};
    }

    internal static AiTimeWindowInfo ResolveWindow(string requested, DateTimeOffset now)
    {
        var r = requested?.Trim().ToLowerInvariant() ?? "24h";
        var (label, span, clamped) = r switch { "1h" => ("1h", TimeSpan.FromHours(1), false), "6h" => ("6h", TimeSpan.FromHours(6), false), "7d" => ("7d", TimeSpan.FromDays(7), false), "24h" or "today" or "" => ("24h", TimeSpan.FromHours(24), r == "today"), _ => ("7d", TimeSpan.FromDays(7), true) };
        return new AiTimeWindowInfo { RequestedWindow = string.IsNullOrWhiteSpace(requested) ? "24h" : requested, AppliedWindow = label, Clamped = clamped, FromUtc = now - span, ToUtc = now };
    }

    private static AiEndpointUptimeSummary BuildUptime(List<AssignmentStateInterval> intervals, List<StateTransition> transitions, AiTimeWindowInfo window)
    {
        long up = 0, down = 0, unknown = 0, suppressed = 0;
        foreach (var i in intervals)
        {
            var start = i.StartedAtUtc > window.FromUtc ? i.StartedAtUtc : window.FromUtc;
            var end = (i.EndedAtUtc ?? window.ToUtc) < window.ToUtc ? (i.EndedAtUtc ?? window.ToUtc) : window.ToUtc;
            var seconds = Math.Max(0, (long)(end - start).TotalSeconds);
            if (i.State == EndpointStateKind.Up || i.State == EndpointStateKind.Degraded) up += seconds;
            else if (i.State == EndpointStateKind.Down) down += seconds;
            else if (i.State == EndpointStateKind.Suppressed) suppressed += seconds;
            else unknown += seconds;
        }
        var known = up + down;
        return new AiEndpointUptimeSummary { UptimeSeconds = up, DowntimeSeconds = down, UnknownSeconds = unknown, SuppressedSeconds = suppressed, UptimePercent = known > 0 ? Math.Round(up * 100d / known, 2) : null, DownTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Down), RecoveryTransitions = transitions.Count(x => x.PreviousState == EndpointStateKind.Down && x.NewState != EndpointStateKind.Down) };
    }

    private static double Percentile(double[] sorted, double p)
    {
        var index = (int)Math.Ceiling(sorted.Length * p) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Length - 1)], 2);
    }
}
