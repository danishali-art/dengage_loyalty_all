using Nancy;
using Nancy.ErrorHandling;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

// Covers routes Nancy itself rejects before any module runs (unmatched route / wrong verb),
// so even those get the same ProblemDetails shape as everything else.
public sealed class ApiStatusCodeHandler : IStatusCodeHandler
{
    private static readonly HashSet<Nancy.HttpStatusCode> Handled =
    [
        Nancy.HttpStatusCode.NotFound,
        Nancy.HttpStatusCode.MethodNotAllowed
    ];

    public bool HandlesStatusCode(Nancy.HttpStatusCode statusCode, NancyContext context) =>
        Handled.Contains(statusCode) && context.Response?.ContentType != "application/problem+json";

    public void Handle(Nancy.HttpStatusCode statusCode, NancyContext context)
    {
        var traceId = context.Items.TryGetValue("CorrelationId", out var id) ? (string)id : Guid.NewGuid().ToString("N");
        var code = statusCode == Nancy.HttpStatusCode.NotFound ? "not_found" : "method_not_allowed";
        var title = statusCode == Nancy.HttpStatusCode.NotFound
            ? "The requested route was not found."
            : "This HTTP method is not allowed for this route.";

        context.Response = ErrorResponseFactory.FromStatus((System.Net.HttpStatusCode)(int)statusCode, code, title, traceId);
    }
}
