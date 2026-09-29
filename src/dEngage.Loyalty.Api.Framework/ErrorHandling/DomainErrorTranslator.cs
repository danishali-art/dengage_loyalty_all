using System.Net;

namespace dEngage.Loyalty.Api.Framework.ErrorHandling;

// The pre-existing domain layer (Ledger/RuleEngine) signals failures via InvalidOperationException
// with a "snake_case_prefix: detail" message convention (e.g. "insufficient_balance: ..."),
// not typed exceptions. This translates that convention into an ApiException without touching
// the domain layer itself, so app services can let those exceptions bubble up and still get a
// correct HTTP status.
public static class DomainErrorTranslator
{
    private static readonly string[] NotFoundPrefixes = ["not_found", "unknown_reward", "unknown_event_type"];
    private static readonly string[] ConflictPrefixes =
    [
        "insufficient_balance", "insufficient_points", "daily_limit_exceeded", "already_",
        "self_transfer_not_allowed", "below_minimum"
    ];
    private static readonly string[] BadRequestPrefixes =
    [
        "invalid_", "redemption_not_configured", "transfer_not_configured", "original_entries_not_found"
    ];

    public static ApiException Translate(InvalidOperationException ex)
    {
        var message = ex.Message;
        var prefix = message.Contains(':') ? message[..message.IndexOf(':')].Trim() : message.Trim();

        if (NotFoundPrefixes.Any(p => prefix.StartsWith(p, StringComparison.Ordinal)))
            return new ApiException(HttpStatusCode.NotFound, prefix, message);

        if (ConflictPrefixes.Any(p => prefix.StartsWith(p, StringComparison.Ordinal)))
            return new ApiException(HttpStatusCode.Conflict, prefix, message);

        if (BadRequestPrefixes.Any(p => prefix.StartsWith(p, StringComparison.Ordinal)))
            return new ApiException(HttpStatusCode.BadRequest, prefix, message);

        return new ApiException(HttpStatusCode.BadRequest, prefix, message);
    }
}
