namespace PingMonitor.Web.Services;

public interface IStateEvaluationService
{
    Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken);
    Task EvaluateAssignmentsAsync(IEnumerable<string> assignmentIds, CancellationToken cancellationToken);
}
