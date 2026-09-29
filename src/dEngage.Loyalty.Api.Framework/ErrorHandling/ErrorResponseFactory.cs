using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Json;
using Nancy;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

public static class ErrorResponseFactory
{
    public static Response FromApiException(ApiException ex, string traceId, bool includeDetailInBody)
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

    public static Response FromValidationFailure(string traceId, IReadOnlyDictionary<string, string[]> errors)
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

    public static Response FromUnexpectedException(string traceId, bool includeDetailInBody, Exception ex)
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

    public static Response FromStatus(HttpStatusCode statusCode, string code, string title, string traceId)
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

    private static Response Write(ProblemDetailsBody body, HttpStatusCode statusCode)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonConventions.Options);
        return new Response
        {
            StatusCode = (Nancy.HttpStatusCode)(int)statusCode,
            ContentType = "application/problem+json",
            Contents = stream => stream.Write(bytes, 0, bytes.Length)
        };
    }
}
