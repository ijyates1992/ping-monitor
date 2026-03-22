namespace PingMonitor.Web.Services;

public interface IStateEvaluationService
{
    Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken);
}
