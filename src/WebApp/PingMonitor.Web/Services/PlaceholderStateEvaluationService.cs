namespace PingMonitor.Web.Services;

internal sealed class PlaceholderStateEvaluationService : IStateEvaluationService
{
    public Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
