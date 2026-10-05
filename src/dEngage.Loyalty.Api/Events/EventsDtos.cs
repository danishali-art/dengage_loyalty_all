using System.Text.Json;

namespace dEngage.Loyalty.Api.Events;

public sealed record OrderItemDto(string Sku, string Category, decimal Qty, decimal Total);
public sealed record OrderCreatedRequest(string ContactKey, decimal Amount, string? Channel, string? PaymentMethod, List<OrderItemDto>? Items);
public sealed record OrderRefundedRequest(string? ContactKey, string OriginalEventId, decimal? RefundRatio, decimal? Amount, decimal? OriginalAmount);
public sealed record CashAddedRequest(string ContactKey, decimal Amount, Guid AccountTypeId);
public sealed record CashSpentRequest(string ContactKey, decimal Amount, Guid AccountTypeId);
public sealed record PointsRedeemRequest(string ContactKey, decimal PointsAmount, Guid SourceAccountTypeId);
public sealed record PointsTransferRequest(string ContactKey, string TargetContactKey, decimal Amount, Guid AccountTypeId)
{
    // The request's names are public API and stay as they are, but the event must carry the names
    // PointsTransferHandler reads ("points_amount", "source_account_type_id"). Publishing the
    // request as-is sent "amount" / "account_type_id", so every transfer through this route was
    // dead-lettered.
    public PointsTransferEventData ToEventData() => new(ContactKey, TargetContactKey, Amount, AccountTypeId);
}

public sealed record PointsTransferEventData(string ContactKey, string TargetContactKey, decimal PointsAmount, Guid SourceAccountTypeId);
// RewardId: CR 2026-09-30 (§3.7) — optional, additive. Identifies the reward exactly; reward_name
// keeps working (an active name is unique per tenant since the same CR).
public sealed record RewardPurchaseRequest(string ContactKey, string? RewardName, string? Channel, Guid? RewardId = null);
public sealed record GenericEventRequest(string EventType, JsonElement Data);

public sealed record EventAcceptedResponse(string EventId, string Status);
public sealed record EventStatusResponse(string EventId, string EventType, string Status, DateTime ReceivedAt, DateTime? ProcessedAt, string? Error);
// Publishable (CR 2026-09-30, O9): the subset a caller may actually send — built-ins minus the
// Scheduled-source ones, plus the generic types. Additive: BuiltIn stays complete because the rule
// and streak builders still need scheduled types (birthdaybonus) as triggers. points.expired was
// retired as a trigger by CR 2026-10-05 and is no longer a built-in.
public sealed record EventTypesResponse(IReadOnlyList<string> BuiltIn, IReadOnlyList<string> Generic, IReadOnlyList<string> Publishable);
