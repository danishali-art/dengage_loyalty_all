namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

// Hand-rolled, RFC 7807-shaped, with the loyalty error `code` the portals switch on. Kept instead of
// ASP.NET Core's ProblemDetails so the body stays exactly what clients already parse.
public sealed class ProblemDetailsBody
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Code { get; init; }
    public required string TraceId { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
