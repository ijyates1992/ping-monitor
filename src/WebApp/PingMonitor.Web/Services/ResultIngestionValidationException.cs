using PingMonitor.Web.Support;

namespace PingMonitor.Web.Services;

internal sealed class ResultIngestionValidationException : Exception
{
    public ResultIngestionValidationException(IReadOnlyList<ApiErrorDetail> errors)
        : base("One or more fields are invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyList<ApiErrorDetail> Errors { get; }
}
