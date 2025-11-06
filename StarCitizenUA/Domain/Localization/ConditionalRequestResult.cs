using System.Net.Http;

namespace StarCitizenUA.Domain.Localization;

internal sealed class ConditionalRequestResult
{
    private ConditionalRequestResult(ConditionalRequestStatus status, HttpResponseMessage? response, TimeSpan? retryAfter, string? error)
    {
        Status = status;
        Response = response;
        RetryAfter = retryAfter;
        ErrorMessage = error;
    }

    public ConditionalRequestStatus Status { get; }

    public HttpResponseMessage? Response { get; }

    public TimeSpan? RetryAfter { get; }

    public string? ErrorMessage { get; }

    public static ConditionalRequestResult Success(HttpResponseMessage response)
        => new(ConditionalRequestStatus.Success, response, null, null);

    public static ConditionalRequestResult NotModified()
        => new(ConditionalRequestStatus.NotModified, null, null, null);

    public static ConditionalRequestResult PreconditionFailed()
        => new(ConditionalRequestStatus.PreconditionFailed, null, null, null);

    public static ConditionalRequestResult RateLimited(TimeSpan? retryAfter)
        => new(ConditionalRequestStatus.RateLimited, null, retryAfter, null);

    public static ConditionalRequestResult Forbidden(string? error)
        => new(ConditionalRequestStatus.Forbidden, null, null, error);
}
