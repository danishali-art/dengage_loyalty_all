/**
 * Ported verbatim from `src/dEngage.Loyalty.RuleEngine/Models/{RuleCalculation,RuleLimits,
 * RuleSettings}.cs`. The outer `Rule`/`CreateRuleRequest`/`UpdateRuleRequest` fields with no
 * explicit `[JsonPropertyName]` are camelCase (System.Text.Json's policy); `RuleCalculation`/
 * `RuleLimits` carry explicit attributes that win over the policy (a mix of camelCase and
 * snake_case per field — matched exactly below, not "fixed" to one convention). Do not guess;
 * check the C# attribute before adding a field.
 */
import { ConditionTree } from '../../../shared/forms/condition-tree-dsl';

/** The rule types a rule can be created with — mirrors `RuleTypes.All` on the server. */
export const RULE_TYPES = [
  'SpendRule',
  'FixedBonusRule',
  'RedemptionRule',
  'TransferRule',
  'ReversalRule',
  'ManualAdjustmentRule',
] as const;
/**
 * Retired by CR 2026-10-05: never offered, but existing (disabled) rules still come back with
 * them, so they stay part of `RuleType` for display.
 */
export const RETIRED_RULE_TYPES = ['StampRule', 'ExpiryRule'] as const;
export type RuleType = (typeof RULE_TYPES)[number] | (typeof RETIRED_RULE_TYPES)[number];

export interface RuleCalculation {
  rate?: string | null; // SpendRule
  amount?: string | null; // FixedBonusRule; ManualAdjustmentRule fallback
  ratio?: string | null; // RedemptionRule, TransferRule
  minRedeem?: string | null; // RedemptionRule
  fee?: string | null; // TransferRule
  maxPerDay?: string | null; // TransferRule
  mode?: 'proportional' | 'full' | null; // ReversalRule
  allowNegative?: 'allow negative' | 'clamp to zero' | null; // ReversalRule
  reason?: 'goodwill' | 'correction' | 'dispute' | 'migration' | null; // ManualAdjustmentRule
}

export type RulePeriod = 'Day' | 'Week' | 'Month' | 'Year';
export type ResetWindow = 'Calendar' | 'Rolling';
export type OnBreach = 'Clamp' | 'Skip';

export interface RuleLimits {
  per_customer_total?: string | null;
  per_customer_per_day?: string | null;
  max_per_event?: string | null;
  min_event_amount?: string | null;
  cooldown_hours?: string | null;
  max_customers?: number | null;
  rule_budget_total?: string | null;
  rule_budget_per_period?: string | null;
  per_customer_per_period?: string | null;
  period?: RulePeriod | null;
  reset_window?: ResetWindow | null;
  on_breach?: OnBreach;
}

export type PostingMode = 'Immediate' | 'Delayed'; // 'Pending' not accepted yet — see RulesValidators
export type Rounding = 'down' | 'nearest' | 'up';

export interface RuleConfiguration {
  rounding?: Rounding | null;
  posting?: PostingMode;
  holdDays?: number | null;
  expiryOverrideDays?: number | null;
  reversible?: boolean;
  testMode?: boolean;
  notifyOnAward?: boolean;
}

/** 1.3.CL item 5: multiplier stacking is retired — the API only ever returns 'Additive'. */
export type StackMode = 'Additive';

export type RuleStatus = 'active' | 'disabled' | 'deleted' | 'pending_approval';

export interface Rule {
  id: string;
  name: string;
  type: RuleType;
  trigger: string;
  targetAccountTypeId: string | null; // null only for ReversalRule — target is inherited
  calculation: RuleCalculation | null;
  conditions: ConditionTree | null;
  limits: RuleLimits | null;
  priority: number;
  stackable: boolean;
  /** Retired by 1.3.CL item 5 — always null. Exclusive rules compete per target account type. */
  exclusivityGroup: string | null;
  stackMode: StackMode;
  configuration: RuleConfiguration | null;
  version: number;
  activeFrom: string | null;
  activeTo: string | null;
  status: RuleStatus;
  createdBy: string | null;
  approvedBy: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRuleRequest {
  name: string;
  trigger: string;
  targetAccountTypeId: string | null;
  type: RuleType;
  calculation?: RuleCalculation | null;
  conditions?: ConditionTree | null;
  limits?: RuleLimits | null;
  priority: number;
  stackable: boolean;
  configuration?: RuleConfiguration | null;
  activeFrom?: string | null;
  activeTo?: string | null;
}

export interface UpdateRuleRequest {
  name?: string;
  trigger?: string;
  targetAccountTypeId?: string | null;
  calculation?: RuleCalculation | null;
  conditions?: ConditionTree | null;
  limits?: RuleLimits | null;
  priority?: number;
  stackable?: boolean;
  configuration?: RuleConfiguration | null;
  activeFrom?: string | null;
  activeTo?: string | null;
}

export interface RuleListFilter {
  event?: string;
  type?: RuleType;
  targetAccountTypeId?: string;
  status?: RuleStatus;
  stackable?: boolean;
}

// CR-03: single compatibility source of truth — GET .../rules/metadata — replacing hardcoded
// RULE_TYPES/EVENT_TRIGGERS option lists with the server's own RuleTypeCatalog/EventTypes.
export interface EventFieldMetadata {
  path: string;
  kind: 'Money' | 'Number' | 'String';
}

export interface EventMetadata {
  eventType: string;
  category: 'Earn' | 'Burn' | 'Reverse' | 'Adjust';
  source: 'Behavioural' | 'Scheduled' | 'Operator';
  cardinality: 'Unlimited' | 'OncePerCustomer' | 'OncePerPeriod';
  period: string | null;
  fields: EventFieldMetadata[];
  compatibleRuleTypes: RuleType[];
}

export interface RuleTypeMetadata {
  ruleType: RuleType;
  category: 'Earn' | 'Burn' | 'Reverse' | 'Adjust';
  requiredKinds: ('Money' | 'Number' | 'String')[];
  validTargetAccountKinds: string[];
  note: string;
}

export interface RulesMetadata {
  events: EventMetadata[];
  ruleTypes: RuleTypeMetadata[];
}
