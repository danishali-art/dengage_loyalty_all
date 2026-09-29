import { RuleLimits } from './rule.model';

/**
 * Rule limits, grouped by what they cap, with one validation per value kind. Mirrors
 * RuleLimitsValidator in src/dEngage.Loyalty.Api/Rules/RulesValidators.cs — keep them in step:
 * the portal must never accept what the API rejects.
 *
 * Values stay strings end-to-end (DecimalString contract) — they are parsed only to compare,
 * exactly, as integers scaled by 10^4 (the ledger's numeric(20,4)), never into a JS number.
 */

/** points / amounts: > 0, up to 4 decimals · count: whole number ≥ 1 · hours: > 0, up to 2 decimals */
export type LimitKind = 'points' | 'amount' | 'hours' | 'count';

export type LimitControl =
  | 'limitPerCustomerTotal'
  | 'limitPerCustomerPerDay'
  | 'limitPerCustomerPerPeriod'
  | 'limitMaxPerEvent'
  | 'limitMinEventAmount'
  | 'limitCooldownHours'
  | 'limitRuleBudgetTotal'
  | 'limitRuleBudgetPerPeriod'
  | 'limitMaxCustomers';

export interface LimitField {
  control: LimitControl;
  apiKey: keyof RuleLimits;
  kind: LimitKind;
  /** i18n key prefix: `<prefix>.label`, `<prefix>.hint`. */
  i18n: string;
}

export interface LimitGroup {
  id: 'customer' | 'event' | 'rule';
  i18n: string;
  fields: readonly LimitField[];
}

export const LIMIT_GROUPS: readonly LimitGroup[] = [
  {
    id: 'customer',
    i18n: 'rules.limits.groups.customer',
    fields: [
      {
        control: 'limitPerCustomerTotal',
        apiKey: 'per_customer_total',
        kind: 'points',
        i18n: 'rules.limits.perCustomerTotal',
      },
      {
        control: 'limitPerCustomerPerDay',
        apiKey: 'per_customer_per_day',
        kind: 'points',
        i18n: 'rules.limits.perCustomerPerDay',
      },
      {
        control: 'limitPerCustomerPerPeriod',
        apiKey: 'per_customer_per_period',
        kind: 'points',
        i18n: 'rules.limits.perCustomerPerPeriod',
      },
    ],
  },
  {
    id: 'event',
    i18n: 'rules.limits.groups.event',
    fields: [
      {
        control: 'limitMaxPerEvent',
        apiKey: 'max_per_event',
        kind: 'points',
        i18n: 'rules.limits.maxPerEvent',
      },
      {
        control: 'limitMinEventAmount',
        apiKey: 'min_event_amount',
        kind: 'amount',
        i18n: 'rules.limits.minEventAmount',
      },
      {
        control: 'limitCooldownHours',
        apiKey: 'cooldown_hours',
        kind: 'hours',
        i18n: 'rules.limits.cooldownHours',
      },
    ],
  },
  {
    id: 'rule',
    i18n: 'rules.limits.groups.rule',
    fields: [
      {
        control: 'limitRuleBudgetTotal',
        apiKey: 'rule_budget_total',
        kind: 'points',
        i18n: 'rules.limits.ruleBudgetTotal',
      },
      {
        control: 'limitRuleBudgetPerPeriod',
        apiKey: 'rule_budget_per_period',
        kind: 'points',
        i18n: 'rules.limits.ruleBudgetPerPeriod',
      },
      {
        control: 'limitMaxCustomers',
        apiKey: 'max_customers',
        kind: 'count',
        i18n: 'rules.limits.maxCustomers',
      },
    ],
  },
];

export const ALL_LIMIT_FIELDS: readonly LimitField[] = LIMIT_GROUPS.flatMap((g) => g.fields);

/** Caps measured over the chosen Period — they need one (the engine would silently use Day). */
export const PER_PERIOD_CONTROLS: readonly LimitControl[] = [
  'limitPerCustomerPerPeriod',
  'limitRuleBudgetPerPeriod',
];

/**
 * Caps that can be partly paid out when reached — On breach (Clamp/Skip) applies only to these.
 * Cooldown, minimum amount and max customers are yes/no gates that always skip (RuleLimits.cs).
 */
export const CLAMPABLE_CONTROLS: readonly LimitControl[] = [
  'limitRuleBudgetTotal',
  'limitRuleBudgetPerPeriod',
  'limitPerCustomerPerPeriod',
];

export type LimitValues = Record<LimitControl, string> & {
  limitPeriod: '' | 'Day' | 'Week' | 'Month' | 'Year';
  limitResetWindow: '' | 'Calendar' | 'Rolling';
};

const PATTERNS: Record<LimitKind, RegExp> = {
  points: /^\d{1,16}(\.\d{1,4})?$/,
  amount: /^\d{1,16}(\.\d{1,4})?$/,
  hours: /^\d{1,6}(\.\d{1,2})?$/,
  count: /^\d{1,9}$/,
};

/** i18n key for a single value's format error, or null when blank or valid. */
export function limitFormatError(kind: LimitKind, raw: string): string | null {
  const value = raw.trim();
  if (value === '') return null;
  if (!PATTERNS[kind].test(value)) return `rules.limits.errors.format.${kind}`;
  if (scaled(value) <= 0n)
    return kind === 'count' ? 'rules.limits.errors.atLeastOne' : 'rules.limits.errors.positive';
  return null;
}

/** Exact decimal as an integer of 1/10000ths — no floating point involved. */
function scaled(value: string): bigint {
  const [whole = '0', fraction = ''] = value.trim().split('.');
  return BigInt(whole) * 10000n + BigInt((fraction + '0000').slice(0, 4));
}

const filled = (raw: string): boolean => raw.trim() !== '';

/**
 * Every error on the Limits section, keyed by control, as i18n keys. Format first; the
 * cross-field checks only run between values that are themselves valid.
 */
export function validateLimits(
  v: LimitValues,
): Partial<Record<LimitControl | 'limitPeriod', string>> {
  const errors: Partial<Record<LimitControl | 'limitPeriod', string>> = {};
  for (const field of ALL_LIMIT_FIELDS) {
    const error = limitFormatError(field.kind, v[field.control]);
    if (error) errors[field.control] = error;
  }

  const ok = (c: LimitControl): boolean => filled(v[c]) && !errors[c];
  const notAbove = (child: LimitControl, parent: LimitControl, key: string): void => {
    if (ok(child) && ok(parent) && scaled(v[child]) > scaled(v[parent])) errors[child] = key;
  };
  // A smaller window can never allow more than the lifetime cap above it.
  notAbove(
    'limitPerCustomerPerDay',
    'limitPerCustomerTotal',
    'rules.limits.errors.aboveCustomerTotal',
  );
  notAbove(
    'limitPerCustomerPerPeriod',
    'limitPerCustomerTotal',
    'rules.limits.errors.aboveCustomerTotal',
  );
  notAbove(
    'limitRuleBudgetPerPeriod',
    'limitRuleBudgetTotal',
    'rules.limits.errors.aboveBudgetTotal',
  );

  if (PER_PERIOD_CONTROLS.some((c) => filled(v[c])) && !v.limitPeriod) {
    errors.limitPeriod = 'rules.limits.errors.periodRequired';
  }
  return errors;
}

export const usesPeriod = (v: LimitValues): boolean =>
  PER_PERIOD_CONTROLS.some((c) => filled(v[c]));
export const usesOnBreach = (v: LimitValues): boolean =>
  CLAMPABLE_CONTROLS.some((c) => filled(v[c]));
