namespace dEngage.Loyalty.Api.Client;

// The outcome of one call: the response body on success, otherwise the API's error body. Like the
// platform's ApiClientHttpResponse, a call doesn't throw for an HTTP error status; it throws only
// when there is no response at all (network failure, timeout, cancellation) or the call can't be
// made (no API key for the tenant).
public sealed class LoyaltyApiResult<T>
{
    private LoyaltyApiResult(int statusCode, T? value, LoyaltyApiProblem? problem)
    {
        StatusCode = statusCode;
        Value = value;
        Problem = problem;
    }

    public int StatusCode { get; }

    // Set when IsSuccess.
    public T? Value { get; }

    // Set when not IsSuccess.
    public LoyaltyApiProblem? Problem { get; }

    public bool IsSuccess => Problem is null;

    internal static LoyaltyApiResult<T> Success(int statusCode, T value) => new(statusCode, value, null);

    internal static LoyaltyApiResult<T> Failure(int statusCode, LoyaltyApiProblem problem) => new(statusCode, default, problem);
}

// The API's RFC 7807 error body. `Code` is the stable part to switch on (for example
// "invalid_request", "unauthorized", "forbidden", "rate_limited", "not_found"); `Errors` holds
// per-field messages for "invalid_request". A response that isn't such a body (for example from a
// proxy in front of the API) gets the code "http_<status>".
public sealed record LoyaltyApiProblem(
    string Type,
    string Title,
    int Status,
    string Code,
    string? TraceId,
    IReadOnlyDictionary<string, string[]>? Errors = null);
