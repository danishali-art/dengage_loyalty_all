import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { cloneJson } from '@/shared/lib/forms';
import type { DecimalString } from '@/shared/lib/money';
import { localInputToUtcIso, utcIsoToLocalInput } from '@/shared/lib/date';
import { emptyTree, type ConditionTree } from '@/shared/lib/dsl';
import { LIMIT_GROUPS, usesOnBreach, usesPeriod, validateLimits, type LimitValues } from './rule-limits';
import {
  RULE_NAME_MAX,
  type CreateRuleRequest,
  type Rule,
  type RuleCalculation,
  type RuleConfiguration,
  type RuleLimits,
  type RuleType,
} from './types';

/**
 * The rule form, checked against RulesValidators: name 1–255,
 * priority ≥ 0, a target for every type but ReversalRule, a positive rate / amount / cash per
 * point / daily limit for the types that need one, hold days when posting is Delayed, and the
 * Limits rules of rule-limits.ts. Money, points and limits stay decimal strings.
 */
const POSITIVE = /^\d{1,16}(\.\d{1,4})?$/;
const SIGNED = /^-?\d{1,16}(\.\d{1,4})?$/;
const WHOLE = /^\d{1,9}$/;
const TRIGGER_MAX = 100;

const blank = (v: string): boolean => v.trim() === '';
const positive = (v: string): boolean => POSITIVE.test(v.trim()) && Number(v) > 0;

export function ruleFormSchema() {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, translate('rules.form.errors.nameRequired'))
        .max(RULE_NAME_MAX, translate('rules.form.errors.tooLong', { max: RULE_NAME_MAX })),
      priority: z.string(),
      trigger: z
        .string()
        .trim()
        .min(1, translate('rules.form.errors.required'))
        .max(TRIGGER_MAX, translate('rules.form.errors.tooLong', { max: TRIGGER_MAX })),
      type: z.custom<RuleType>((v) => typeof v === 'string'),
      targetAccountTypeId: z.string().nullable(),
      activeFrom: z.string(),
      activeTo: z.string(),
      stackable: z.boolean(),

      calcRate: z.string(),
      calcAmount: z.string(),
      // CR 2026-10-05: a redeem rule's cash per point (saved as `rate`) and Redeem into wallet,
      // and a transfer rule's daily limit.
      calcCashPerPoint: z.string(),
      calcCashAccountTypeId: z.string().nullable(),
      calcMinRedeem: z.string(),
      calcMaxPerDay: z.string(),
      calcMode: z.enum(['proportional', 'full']),
      calcAllowNegative: z.enum(['allow negative', 'clamp to zero']),
      calcReason: z.enum(['goodwill', 'correction', 'dispute', 'migration']),

      limitPerCustomerTotal: z.string(),
      limitPerCustomerPerDay: z.string(),
      limitPerCustomerPerPeriod: z.string(),
      limitMaxPerEvent: z.string(),
      limitMinEventAmount: z.string(),
      limitCooldownHours: z.string(),
      limitRuleBudgetTotal: z.string(),
      limitRuleBudgetPerPeriod: z.string(),
      limitMaxCustomers: z.string(),
      limitPeriod: z.enum(['', 'Day', 'Week', 'Month', 'Year']),
      limitResetWindow: z.enum(['', 'Calendar', 'Rolling']),
      limitOnBreach: z.enum(['Clamp', 'Skip']),

      cfgRounding: z.enum(['', 'down', 'nearest', 'up']),
      cfgPosting: z.enum(['Immediate', 'Delayed']),
      cfgHoldDays: z.string(),
      cfgExpiryOverrideDays: z.string(),
      cfgReversible: z.boolean(),
      cfgNotifyOnAward: z.boolean(),

      conditions: z.custom<ConditionTree>(),
    })
    .superRefine((v, ctx) => {
      const issue = (path: string, key: string, params?: Record<string, unknown>): void =>
        ctx.addIssue({ code: 'custom', path: [path], message: translate(key, params) });

      if (!WHOLE.test(v.priority.trim())) issue('priority', 'rules.form.errors.priority');
      if (v.type !== 'ReversalRule' && !v.targetAccountTypeId)
        issue('targetAccountTypeId', 'rules.form.errors.required');

      switch (v.type) {
        case 'SpendRule':
          if (!positive(v.calcRate)) issue('calcRate', 'rules.form.errors.positive');
          break;
        case 'FixedBonusRule':
          if (!positive(v.calcAmount)) issue('calcAmount', 'rules.form.errors.positive');
          break;
        case 'RedemptionRule':
          if (!positive(v.calcCashPerPoint))
            issue('calcCashPerPoint', 'rules.calc.redeem.cashPerPointRequired');
          if (!v.calcCashAccountTypeId)
            issue('calcCashAccountTypeId', 'rules.calc.redeem.redeemIntoRequired');
          if (!blank(v.calcMinRedeem) && !POSITIVE.test(v.calcMinRedeem.trim()))
            issue('calcMinRedeem', 'rules.form.errors.decimal');
          break;
        case 'TransferRule':
          if (!positive(v.calcMaxPerDay)) issue('calcMaxPerDay', 'rules.calc.transfer.dailyLimitRequired');
          break;
        case 'ManualAdjustmentRule':
          if (!blank(v.calcAmount) && !SIGNED.test(v.calcAmount.trim()))
            issue('calcAmount', 'rules.form.errors.decimal');
          break;
      }

      for (const [control, key] of Object.entries(validateLimits(v as LimitValues))) issue(control, key);

      if (v.cfgPosting === 'Delayed') {
        if (blank(v.cfgHoldDays)) issue('cfgHoldDays', 'rules.config.holdDays.required');
        else if (!WHOLE.test(v.cfgHoldDays.trim()) || Number(v.cfgHoldDays) < 1)
          issue('cfgHoldDays', 'rules.config.holdDays.min');
      }
      if (
        !blank(v.cfgExpiryOverrideDays) &&
        (!WHOLE.test(v.cfgExpiryOverrideDays.trim()) || Number(v.cfgExpiryOverrideDays) < 1)
      )
        issue('cfgExpiryOverrideDays', 'rules.config.expiryOverride.min');
    });
}
export type RuleFormValues = z.infer<ReturnType<typeof ruleFormSchema>>;

/** A stored value (JSON string or number) as the text an input edits; blank = none. */
const toInput = (value: string | number | null | undefined): string =>
  value === null || value === undefined ? '' : String(value);

export function ruleFormValues(
  existing: Rule | undefined,
  defaults: { trigger: string; targetAccountTypeId: string | null },
): RuleFormValues {
  const calc = existing?.calculation;
  const limits = existing?.limits;
  const config = existing?.configuration;
  return {
    name: existing?.name ?? '',
    priority: String(existing?.priority ?? 0),
    trigger: existing?.trigger ?? defaults.trigger,
    type: existing?.type ?? 'SpendRule',
    targetAccountTypeId: existing ? (existing.targetAccountTypeId ?? null) : defaults.targetAccountTypeId,
    activeFrom: utcIsoToLocalInput(existing?.activeFrom),
    activeTo: utcIsoToLocalInput(existing?.activeTo),
    stackable: existing?.stackable ?? true,

    calcRate: calc?.rate ?? '1',
    calcAmount: calc?.amount ?? (existing ? '' : '10'),
    // A rule saved before CR 2026-10-05 has only the retired `ratio`: these stay empty and must be
    // filled in before it can be saved (or switched on) again.
    calcCashPerPoint: existing?.type === 'RedemptionRule' ? toInput(calc?.rate) : '',
    calcCashAccountTypeId: calc?.cashAccountTypeId ?? null,
    calcMinRedeem: toInput(calc?.minRedeem),
    calcMaxPerDay: toInput(calc?.maxPerDay),
    calcMode: calc?.mode ?? 'proportional',
    calcAllowNegative: calc?.allowNegative ?? 'clamp to zero',
    calcReason: calc?.reason ?? 'correction',

    limitPerCustomerTotal: toInput(limits?.per_customer_total),
    limitPerCustomerPerDay: toInput(limits?.per_customer_per_day),
    limitPerCustomerPerPeriod: toInput(limits?.per_customer_per_period),
    limitMaxPerEvent: toInput(limits?.max_per_event),
    limitMinEventAmount: toInput(limits?.min_event_amount),
    limitCooldownHours: toInput(limits?.cooldown_hours),
    limitRuleBudgetTotal: toInput(limits?.rule_budget_total),
    limitRuleBudgetPerPeriod: toInput(limits?.rule_budget_per_period),
    limitMaxCustomers: toInput(limits?.max_customers),
    limitPeriod: limits?.period ?? '',
    limitResetWindow: limits?.reset_window ?? 'Calendar',
    limitOnBreach: limits?.on_breach ?? 'Clamp',

    cfgRounding: config?.rounding ?? '',
    cfgPosting: config?.posting ?? 'Immediate',
    cfgHoldDays: toInput(config?.holdDays),
    cfgExpiryOverrideDays: toInput(config?.expiryOverrideDays),
    cfgReversible: config?.reversible ?? true,
    cfgNotifyOnAward: config?.notifyOnAward ?? false,

    // The API leaves nulls out of its JSON (WhenWritingNull), so `inferred` is never null here.
    conditions: existing?.conditions ? (cloneJson(existing.conditions) as ConditionTree) : emptyTree(),
  };
}

/** Which Configuration / Limits fields apply (rule-field-visibility.ts) — only those are sent. */
export interface RuleFieldFilter {
  showLimit(key: string): boolean;
  showConfig(key: string): boolean;
}

const dec = (v: string): DecimalString => v.trim() as DecimalString;

function buildCalculation(v: RuleFormValues): RuleCalculation | null {
  switch (v.type) {
    case 'SpendRule':
      return { rate: dec(v.calcRate) };
    case 'FixedBonusRule':
      return { amount: dec(v.calcAmount) };
    case 'RedemptionRule': {
      const calc: RuleCalculation = {
        rate: dec(v.calcCashPerPoint),
        cashAccountTypeId: v.calcCashAccountTypeId,
      };
      if (!blank(v.calcMinRedeem)) calc.minRedeem = dec(v.calcMinRedeem);
      return calc;
    }
    case 'TransferRule':
      return { maxPerDay: dec(v.calcMaxPerDay) };
    case 'ReversalRule':
      return { mode: v.calcMode, allowNegative: v.calcAllowNegative };
    case 'ManualAdjustmentRule': {
      const calc: RuleCalculation = { reason: v.calcReason };
      if (!blank(v.calcAmount)) calc.amount = dec(v.calcAmount);
      return calc;
    }
    // Retired by CR 2026-10-05 — never offered; the API refuses to save them.
    case 'StampRule':
    case 'ExpiryRule':
      return null;
  }
}

/** Only called on values that passed the schema, so every filled limit is well-formed. */
function buildLimits(v: RuleFormValues, fields: RuleFieldFilter): RuleLimits | null {
  const values = v as LimitValues;
  // on_breach always has a value on the wire; 'Clamp' is its default, which every rule may send.
  const limits: RuleLimits = { on_breach: 'Clamp' };
  let any = false;
  for (const group of LIMIT_GROUPS) {
    for (const field of group.fields) {
      if (!fields.showLimit(field.apiKey)) continue; // Phase 2: not sent when it doesn't apply
      const raw = values[field.control].trim();
      if (raw === '') continue;
      any = true;
      (limits as Record<string, unknown>)[field.apiKey] = field.kind === 'count' ? Number(raw) : raw;
    }
  }
  if (!any) return null;
  // Period / reset window only mean something with a per-period cap; On breach only with a cap
  // that can be partly paid out (rule-limits.ts). Don't send settings that do nothing.
  if (usesPeriod(values) && fields.showLimit('period')) {
    limits.period = values.limitPeriod || null;
    limits.reset_window = values.limitResetWindow || 'Calendar';
  }
  if (usesOnBreach(values) && fields.showLimit('on_breach')) limits.on_breach = v.limitOnBreach;
  return limits;
}

function buildConfiguration(v: RuleFormValues, fields: RuleFieldFilter): RuleConfiguration {
  // Phase 2: a field that doesn't apply is sent at its default (the API accepts defaults).
  const posting = fields.showConfig('posting') ? v.cfgPosting : 'Immediate';
  const config: RuleConfiguration = {
    posting,
    reversible: fields.showConfig('reversible') ? v.cfgReversible : true,
    // CR 2026-10-06 D20: test mode was removed; false is the only value the API accepts.
    testMode: false,
    notifyOnAward: fields.showConfig('notifyOnAward') ? v.cfgNotifyOnAward : false,
  };
  if (v.cfgRounding && fields.showConfig('rounding')) config.rounding = v.cfgRounding;
  if (!blank(v.cfgHoldDays) && posting === 'Delayed') config.holdDays = Number(v.cfgHoldDays);
  if (!blank(v.cfgExpiryOverrideDays) && fields.showConfig('expiryOverrideDays'))
    config.expiryOverrideDays = Number(v.cfgExpiryOverrideDays);
  return config;
}

/** The whole rule, for create and for edit. */
export function toRuleRequest(
  v: RuleFormValues,
  conditions: ConditionTree | null,
  fields: RuleFieldFilter,
): CreateRuleRequest {
  return {
    name: v.name,
    trigger: v.trigger,
    targetAccountTypeId: v.type === 'ReversalRule' ? null : v.targetAccountTypeId || null,
    type: v.type as CreateRuleRequest['type'],
    calculation: buildCalculation(v),
    conditions,
    limits: buildLimits(v, fields),
    priority: Number(v.priority),
    stackable: v.stackable,
    configuration: buildConfiguration(v, fields),
    activeFrom: localInputToUtcIso(v.activeFrom),
    activeTo: localInputToUtcIso(v.activeTo),
  };
}
