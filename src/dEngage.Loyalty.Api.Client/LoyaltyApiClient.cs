using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using dEngage.Loyalty.Contract;

namespace dEngage.Loyalty.Api.Client;

// dEngage.Loyalty.Public.Api: event ingestion and event status. Every publish returns 202 with the
// event id when the event is accepted for processing; the business outcome comes later, through
// GetEventStatusAsync (or the outbound events). `idempotencyKey`, when given, becomes the event id,
// so sending the same event again with the same key is de-duplicated by loyalty.
public interface ILoyaltyApiClient
{
    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishOrderCreatedAsync(
        string tenantId, OrderCreatedRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishOrderRefundedAsync(
        string tenantId, OrderRefundedRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishCashAddedAsync(
        string tenantId, CashAddedRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishCashSpentAsync(
        string tenantId, CashSpentRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishPointsRedeemAsync(
        string tenantId, PointsRedeemRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishPointsTransferAsync(
        string tenantId, PointsTransferRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishRewardPurchaseAsync(
        string tenantId, RewardPurchaseRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    // A generic (deployment-configured) event type. `request.Data` is passed to the rule engine
    // as is, so its field names must be the snake_case names the rules use.
    Task<LoyaltyApiResult<EventAcceptedResponse>> PublishGenericAsync(
        string tenantId, GenericEventRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    Task<LoyaltyApiResult<EventStatusResponse>> GetEventStatusAsync(
        string tenantId, string eventId, CancellationToken ct = default);
}

public sealed class LoyaltyApiClient(HttpClient http, ILoyaltyApiKeyProvider apiKeys) : ILoyaltyApiClient
{
    // The API's wire format (the server's JsonConventions.Options): camelCase, nulls omitted,
    // decimals as JSON strings.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DecimalStringJsonConverter() }
    };

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishOrderCreatedAsync(
        string tenantId, OrderCreatedRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "order-created", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishOrderRefundedAsync(
        string tenantId, OrderRefundedRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "order-refunded", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishCashAddedAsync(
        string tenantId, CashAddedRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "cash-added", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishCashSpentAsync(
        string tenantId, CashSpentRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "cash-spent", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishPointsRedeemAsync(
        string tenantId, PointsRedeemRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "points-redeem", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishPointsTransferAsync(
        string tenantId, PointsTransferRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "points-transfer", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishRewardPurchaseAsync(
        string tenantId, RewardPurchaseRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "reward-purchase", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventAcceptedResponse>> PublishGenericAsync(
        string tenantId, GenericEventRequest request, string? idempotencyKey = null, CancellationToken ct = default) =>
        PublishAsync(tenantId, "generic", request, idempotencyKey, ct);

    public Task<LoyaltyApiResult<EventStatusResponse>> GetEventStatusAsync(
        string tenantId, string eventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("An event id is required.", nameof(eventId));
        return SendAsync<object, EventStatusResponse>(HttpMethod.Get, tenantId, Uri.EscapeDataString(eventId), null, null, ct);
    }

    private Task<LoyaltyApiResult<EventAcceptedResponse>> PublishAsync<TRequest>(
        string tenantId, string route, TRequest request, string? idempotencyKey, CancellationToken ct)
        where TRequest : class
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        return SendAsync<TRequest, EventAcceptedResponse>(HttpMethod.Post, tenantId, route, request, idempotencyKey, ct);
    }

    private async Task<LoyaltyApiResult<TResponse>> SendAsync<TRequest, TResponse>(
        HttpMethod method, string tenantId, string route, TRequest? body, string? idempotencyKey, CancellationToken ct)
        where TRequest : class
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A tenant id is required.", nameof(tenantId));

        // Relative to the client's BaseAddress (which ends with "/", see LoyaltyApiClientModule), so a
        // deployment can serve Public.Api under a path prefix.
        using var message = new HttpRequestMessage(method, $"api/v1/tenants/{Uri.EscapeDataString(tenantId)}/events/{route}");
        message.Headers.Add("X-Api-Key", await apiKeys.GetApiKeyAsync(tenantId, ct).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
        {
            message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions));
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        var payload = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
            return LoyaltyApiResult<TResponse>.Success(status, JsonSerializer.Deserialize<TResponse>(payload, JsonOptions)!);

        return LoyaltyApiResult<TResponse>.Failure(status, ReadProblem(payload, status, response.ReasonPhrase));
    }

    private static LoyaltyApiProblem ReadProblem(byte[] payload, int status, string? reason)
    {
        if (payload.Length > 0)
        {
            try
            {
                var problem = JsonSerializer.Deserialize<LoyaltyApiProblem>(payload, JsonOptions);
                if (problem is { Code.Length: > 0 })
                    return problem;
            }
            catch (JsonException)
            {
                // Not the API's error body; described below.
            }
        }

        var code = $"http_{status}";
        return new LoyaltyApiProblem($"https://loyalty.local/errors/{code}", reason ?? code, status, code, null);
    }
}
