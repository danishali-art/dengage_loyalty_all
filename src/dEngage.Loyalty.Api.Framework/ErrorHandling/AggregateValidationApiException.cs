using System.Net;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

public sealed class AggregateValidationApiException(IReadOnlyDictionary<string, string[]> errors)
    : ApiException(HttpStatusCode.BadRequest, "invalid_request", "One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
