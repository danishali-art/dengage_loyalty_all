using System.Net;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

// Thrown deliberately by API-layer code (modules/app services) for known outcomes — kept distinct
// from the pre-existing domain layer's InvalidOperationException/prefix convention (see
// DomainErrorTranslator), which is translated rather than rethrown as one of these.
public class ApiException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class NotFoundApiException(string resource)
    : ApiException(HttpStatusCode.NotFound, "not_found", $"{resource} not found.");

public sealed class ConflictApiException(string code, string message)
    : ApiException(HttpStatusCode.Conflict, code, message);

public sealed class ValidationApiException(string message)
    : ApiException(HttpStatusCode.BadRequest, "invalid_request", message);

public sealed class ForbiddenApiException(string message = "Not permitted for this tenant.")
    : ApiException(HttpStatusCode.Forbidden, "forbidden", message);

public sealed class UnauthorizedApiException(string message = "Authentication required.")
    : ApiException(HttpStatusCode.Unauthorized, "unauthorized", message);

public sealed class TooManyRequestsApiException(string message = "Rate limit exceeded.")
    : ApiException(HttpStatusCode.TooManyRequests, "rate_limited", message);
