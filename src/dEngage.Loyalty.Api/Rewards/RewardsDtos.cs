using System.Text.Json;

namespace dEngage.Loyalty.Api.Rewards;

public sealed record CreateRewardRequest(
    string Name, string DisplayName, string Acquisition, string RewardType,
    decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement? TypeConfig, bool IsActive);

public sealed record UpdateRewardRequest(
    string? Name, string? DisplayName,
    decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement? TypeConfig);

// Status / CreatedBy / ApprovedBy: CR 2026-09-30 (A4) cashback approval — additive.
// StampAccountTypeId was removed from requests and responses by CR 2026-10-05 (D3, D7).
public sealed record RewardResponse(
    Guid Id, string Name, string DisplayName, string Acquisition, string RewardType,
    decimal? PointsPrice, Guid? PointsAccountTypeId,
    JsonElement TypeConfig, bool IsActive, DateTime CreatedAt,
    string Status, string? CreatedBy, string? ApprovedBy);
