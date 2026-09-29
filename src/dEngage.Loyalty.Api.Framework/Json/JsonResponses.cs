using System.Text.Json;
using Nancy;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace dEngage.Loyalty.Api.Framework.Json;

public static class JsonResponses
{
    public static Response Ok<T>(T value, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonConventions.Options);
        return new Response
        {
            StatusCode = (Nancy.HttpStatusCode)(int)statusCode,
            ContentType = "application/json",
            Contents = stream => stream.Write(bytes, 0, bytes.Length)
        };
    }

    public static Response NoContent()
    {
        return new Response { StatusCode = Nancy.HttpStatusCode.NoContent };
    }

    public static async Task<T> ReadJsonBodyAsync<T>(this Request request, CancellationToken ct)
    {
        var value = await JsonSerializer.DeserializeAsync<T>(request.Body, JsonConventions.Options, ct);
        if (value is null)
            throw new ErrorHandling.ValidationApiException("Request body is required.");
        return value;
    }
}
