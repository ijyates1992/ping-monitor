namespace PingMonitor.Web.Contracts.Hello;

public sealed record AgentHelloRequest(
    string AgentVersion,
    string MachineName,
    string Platform,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset StartedAtUtc);
