using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Framework.Json;

public static class JsonResponses
{
    public static IActionResult Ok<T>(T value, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new SerializedResult((int)statusCode, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(value, JsonConventions.Options));

    public static IActionResult NoContent() => new SerializedResult(StatusCodes.Status204NoContent, null, null);

    public static async Task<T> ReadJsonBodyAsync<T>(this HttpRequest request, CancellationToken ct)
    {
        var value = await JsonSerializer.DeserializeAsync<T>(request.Body, JsonConventions.Options, ct);
        if (value is null)
            throw new ErrorHandling.ValidationApiException("Request body is required.");
        return value;
    }
}

// Writes an already-serialized body with an exact Content-Type. MVC's own output formatters are
// bypassed on purpose: every body is written with JsonConventions.Options (one place JSON options
// are defined), and the Content-Type stays "application/json" / "application/problem+json"
// without a charset suffix, as the API has always sent it.
public sealed class SerializedResult(int statusCode, string? contentType, byte[]? body) : IActionResult
{
    public int StatusCode => statusCode;

    public Task ExecuteResultAsync(ActionContext context) => WriteAsync(context.HttpContext);

    public async Task WriteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        if (body is null)
            return;
        httpContext.Response.ContentType = contentType;
        await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
    }
}
