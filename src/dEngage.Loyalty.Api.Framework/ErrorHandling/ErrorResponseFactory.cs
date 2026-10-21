using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

public static class ErrorResponseFactory
{
    // The one mapping from an exception thrown anywhere in an /api request to its error body.
    public static SerializedResult FromException(Exception exception, string traceId, bool includeDetailInBody) =>
        exception switch
        {
            AggregateValidationApiException vex => FromValidationFailure(traceId, vex.Errors),
            ApiException apiEx => FromApiException(apiEx, traceId, includeDetailInBody),
            InvalidOperationException ioe => FromApiException(DomainErrorTranslator.Translate(ioe), traceId, includeDetailInBody),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } =>
                FromApiException(
                    new ConflictApiException("unique_violation", "A record with the same unique value already exists."),
                    traceId, includeDetailInBody),
            _ => FromUnexpectedException(traceId, includeDetailInBody, exception)
        };

    public static SerializedResult FromApiException(ApiException ex, string traceId, bool includeDetailInBody)
    {
        var body = new ProblemDetailsBody
        {
            Type = $"https://loyalty.local/errors/{ex.Code}",
            Title = includeDetailInBody ? ex.Message : GenericTitleFor(ex.StatusCode),
            Status = (int)ex.StatusCode,
            Code = ex.Code,
            TraceId = traceId
        };
        return Write(body, ex.StatusCode);
    }

    public static SerializedResult FromValidationFailure(string traceId, IReadOnlyDictionary<string, string[]> errors)
    {
        var body = new ProblemDetailsBody
        {
            Type = "https://loyalty.local/errors/invalid_request",
            Title = "One or more validation errors occurred.",
            Status = (int)HttpStatusCode.BadRequest,
            Code = "invalid_request",
            TraceId = traceId,
            Errors = errors
        };
        return Write(body, HttpStatusCode.BadRequest);
    }

    public static SerializedResult FromUnexpectedException(string traceId, bool includeDetailInBody, Exception ex)
    {
        var body = new ProblemDetailsBody
        {
            Type = "https://loyalty.local/errors/internal",
            Title = includeDetailInBody ? ex.ToString() : "An unexpected error occurred.",
            Status = (int)HttpStatusCode.InternalServerError,
            Code = "internal_error",
            TraceId = traceId
        };
        return Write(body, HttpStatusCode.InternalServerError);
    }

    public static SerializedResult FromStatus(HttpStatusCode statusCode, string code, string title, string traceId)
    {
        var body = new ProblemDetailsBody
        {
            Type = $"https://loyalty.local/errors/{code}",
            Title = title,
            Status = (int)statusCode,
            Code = code,
            TraceId = traceId
        };
        return Write(body, statusCode);
    }

    private static string GenericTitleFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => "The requested resource was not found.",
        HttpStatusCode.Conflict => "The request conflicts with the current state of the resource.",
        HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
        HttpStatusCode.Unauthorized => "Authentication is required.",
        HttpStatusCode.TooManyRequests => "Too many requests.",
        _ => "The request was invalid."
    };

    private static SerializedResult Write(ProblemDetailsBody body, HttpStatusCode statusCode) =>
        new((int)statusCode, "application/problem+json", JsonSerializer.SerializeToUtf8Bytes(body, JsonConventions.Options));
}
