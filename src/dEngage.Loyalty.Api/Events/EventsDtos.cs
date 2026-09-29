using System.Text.Json;

namespace dEngage.Loyalty.Api.Events;

public sealed record OrderItemDto(string Sku, string Category, decimal Qty, decimal Total);
public sealed record OrderCreatedRequest(string ContactKey, decimal Amount, string? Channel, string? PaymentMethod, List<OrderItemDto>? Items);
public sealed record OrderRefundedRequest(string? ContactKey, string OriginalEventId, decimal? RefundRatio, decimal? Amount, decimal? OriginalAmount);
public sealed record CashAddedRequest(string ContactKey, decimal Amount, Guid AccountTypeId);
public sealed record CashSpentRequest(string ContactKey, decimal Amount, Guid AccountTypeId);
public sealed record PointsRedeemRequest(string ContactKey, decimal PointsAmount, Guid SourceAccountTypeId);
public sealed record PointsTransferRequest(string ContactKey, string TargetContactKey, decimal Amount, Guid AccountTypeId);
public sealed record RewardPurchaseRequest(string ContactKey, string RewardName, string? Channel);
public sealed record GenericEventRequest(string EventType, JsonElement Data);

public sealed record EventAcceptedResponse(string EventId, string Status);
public sealed record EventStatusResponse(string EventId, string EventType, string Status, DateTime ReceivedAt, DateTime? ProcessedAt, string? Error);
public sealed record EventTypesResponse(IReadOnlyList<string> BuiltIn, IReadOnlyList<string> Generic);
