import type { ApiSchema } from '@/shared/api';

/**
 * Rules (RulesDtos.cs and the engine models they carry). The outer rule is camelCase; the
 * engine models carry explicit `[JsonPropertyName]` names that win over that policy —
 * `RuleLimits` is snake_case (`per_customer_total`), `RuleCalculation` / `RuleSettings` are
 * camelCase. The generated types already have the right names: never hand-write these shapes.
 */
export type Rule = ApiSchema<'RuleResponse'>;
export type CreateRuleRequest = ApiSchema<'CreateRuleRequest'>;
export type UpdateRuleRequest = ApiSchema<'UpdateRuleRequest'>;
export type RuleCalculation = ApiSchema<'RuleCalculation'>;
export type RuleLimits = ApiSchema<'RuleLimits'>;
export type RuleConfiguration = ApiSchema<'RuleSettings'>;
/** Every type a rule can carry, the retired StampRule / ExpiryRule included (view-only). */
export type RuleType = Rule['type'];
/** The types a rule can be created with — `RuleTypes.All`. */
export type CreatableRuleType = CreateRuleRequest['type'];
export type RuleStatus = Rule['status'];
export type RulePeriod = NonNullable<RuleLimits['period']>;
export type ResetWindow = NonNullable<RuleLimits['reset_window']>;
export type OnBreach = RuleLimits['on_breach'];
export type PostingMode = RuleConfiguration['posting'];

export type RulesMetadata = ApiSchema<'RulesMetadataResponse'>;
export type EventMetadata = ApiSchema<'EventMetadataResponse'>;
export type RuleTypeMetadata = ApiSchema<'RuleTypeMetadataResponse'>;

/** `RuleTypes.All`, in the order the form offers them. */
export const RULE_TYPES: readonly CreatableRuleType[] = [
  'SpendRule',
  'FixedBonusRule',
  'RedemptionRule',
  'TransferRule',
  'ReversalRule',
  'ManualAdjustmentRule',
];

/** RulesValidators: name ≤ 255. */
export const RULE_NAME_MAX = 255;
