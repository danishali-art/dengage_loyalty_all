namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

// Hand-rolled, RFC 7807-shaped — Nancy has no built-in ProblemDetails type.
public sealed class ProblemDetailsBody
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Code { get; init; }
    public required string TraceId { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
