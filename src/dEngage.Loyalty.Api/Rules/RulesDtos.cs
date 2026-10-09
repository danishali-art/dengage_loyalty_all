using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.Api.Rules;

// Deliberately reuses RuleEngine.Models types (RuleCalculation, ConditionClause, RuleLimits) as
// the request/response DTO shapes rather than parallel API-only classes — those types already
// carry the exact [JsonPropertyName] wire contract the engine (RuleCacheService, ConditionDsl)
// reads back out of the DB, so reusing them guarantees fidelity instead of risking two DSL
// shapes drifting apart. Streak campaigns have their own DTOs — see Api/StreakCampaigns.
// TargetAccountTypeId is nullable only because ReversalRule (CR-02) inherits its target from
// the original posting at fire time rather than being configured — RulesValidators requires it
// for every other rule type.
// Conditions is the CR-05 grouped AND/OR tree (ConditionTree), not the flat ConditionClause
// list Streak Campaigns still use — see RuleEngine.Models.ConditionTree remarks.
// ExclusivityGroup/StackMode (CR-06 A6) are retired by 1.3.CL item 5: requests must leave
// ExclusivityGroup null and StackMode null/'Additive' (RulesValidators), and responses always
// return null/'Additive'. Kept on the records so the wire shape doesn't change. Exclusive rules
// (Stackable=false) now compete per target account type; stackable rules always add.
// Configuration (CR-08 A8): rounding/posting/holdDays/expiryOverrideDays/reversible/testMode/
// notifyOnAward. Null means every setting is at its A8 default.
public sealed record CreateRuleRequest(
    string Name, string Trigger, Guid? TargetAccountTypeId, string Type,
    RuleCalculation? Calculation, ConditionTree? Conditions, RuleLimits? Limits,
    int Priority, bool Stackable, string? ExclusivityGroup, string? StackMode,
    RuleSettings? Configuration, DateTime? ActiveFrom, DateTime? ActiveTo);

public sealed record UpdateRuleRequest(
    string? Name, string? Trigger, Guid? TargetAccountTypeId,
    RuleCalculation? Calculation, ConditionTree? Conditions, RuleLimits? Limits,
    int? Priority, bool? Stackable, string? ExclusivityGroup, string? StackMode,
    RuleSettings? Configuration, DateTime? ActiveFrom, DateTime? ActiveTo);

// Version (CR-09 A10 guarantee #7): the rule's CurrentVersion — bumped on every edit.
public sealed record RuleResponse(
    Guid Id, string Name, string Type, string Trigger, Guid? TargetAccountTypeId,
    RuleCalculation? Calculation, ConditionTree? Conditions, RuleLimits? Limits,
    int Priority, bool Stackable, string? ExclusivityGroup, string StackMode,
    RuleSettings? Configuration, int Version, DateTime? ActiveFrom, DateTime? ActiveTo,
    string Status, string? CreatedBy, string? ApprovedBy, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SetRuleStatusRequest(string Status);

// CR-03: single compatibility source of truth (RuleEngine.Metadata.RuleTypeCatalog /
// Shared.Events.EventTypes) served to the Angular rule builder (CR-11) instead of it
// hardcoding RULE_TYPES/EVENT_TRIGGERS arrays. Built-in events only — tenant-approved generic
// event types have no category/field metadata yet (see EventTypes.Describe) and are always
// compatible with every rule type.
public sealed record EventFieldMetadata(string Path, string Kind);

// ApplicableFields (CR 2026-10-06 Phase 2, additive): per compatible rule type, the Configuration
// and Limits fields a new rule may set — RuleEngine.Metadata.RuleFieldCatalog.
public sealed record EventMetadataResponse(
    string EventType, string Category, string Source, string Cardinality, string? Period,
    IReadOnlyList<EventFieldMetadata> Fields, IReadOnlyList<string> CompatibleRuleTypes,
    IReadOnlyList<ApplicableFieldsResponse>? ApplicableFields = null);

public sealed record ApplicableFieldsResponse(string RuleType, IReadOnlyList<string> Configuration, IReadOnlyList<string> Limits);

public sealed record RuleTypeMetadataResponse(
    string RuleType, string Category, IReadOnlyList<string> RequiredKinds,
    IReadOnlyList<string> ValidTargetAccountKinds, string Note);

public sealed record RulesMetadataResponse(
    IReadOnlyList<EventMetadataResponse> Events,
    IReadOnlyList<RuleTypeMetadataResponse> RuleTypes);
