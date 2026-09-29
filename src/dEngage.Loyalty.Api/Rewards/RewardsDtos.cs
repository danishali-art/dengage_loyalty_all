using System.Text.Json;

namespace dEngage.Loyalty.Api.Rewards;

public sealed record CreateRewardRequest(
    string Name, string DisplayName, string Acquisition, string RewardType,
    Guid? StampAccountTypeId, decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement? TypeConfig, bool IsActive);

public sealed record UpdateRewardRequest(
    string? Name, string? DisplayName,
    Guid? StampAccountTypeId, decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement? TypeConfig);

public sealed record RewardResponse(
    Guid Id, string Name, string DisplayName, string Acquisition, string RewardType,
    Guid? StampAccountTypeId, decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement TypeConfig, bool IsActive, DateTime CreatedAt);
