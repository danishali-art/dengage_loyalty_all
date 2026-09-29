/**
 * Ported verbatim from `LoyaltySaaS.RuleEngine.Models.{ConditionClause,ConditionDsl}` — mirrors
 * the server's acceptance rules so a form can reject an invalid clause before submit. The server
 * remains the source of truth; this is a UX convenience, not a replacement for it.
 *
 * Shared between `features/programs/rules` and `features/programs/streak-campaigns` — both use
 * the same condition DSL, and the lint config forbids `features/* -> features/*` imports, so this
 * lives here rather than in either feature.
 */

export const CONDITION_OPS = ['eq', 'ne', 'in', 'gte', 'lte', 'gt', 'lt', 'exists', 'occurred_within'] as const;
export type ConditionOp = (typeof CONDITION_OPS)[number];

export interface OccurredWithinValue {
  after_event: string;
  hours: number;
}

export interface ConditionClause {
  field: string;
  op: ConditionOp;
  value: string | number | boolean | string[] | OccurredWithinValue;
}

export const MAX_WINDOW_HOURS = 8760; // event_log retention is 12 months

function isNumeric(value: unknown): boolean {
  if (typeof value === 'number') return Number.isFinite(value);
  if (typeof value === 'string') return value.trim() !== '' && !Number.isNaN(Number(value));
  return false;
}

function isOccurredWithinValue(value: unknown): value is OccurredWithinValue {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v['after_event'] === 'string' && v['after_event'].trim() !== '' && isNumeric(v['hours']);
}

/** @returns an error message, or null if the clause is valid. */
export function validateConditionClause(clause: ConditionClause): string | null {
  if (!clause.field?.trim()) return 'field is required';
  const ops: readonly string[] = CONDITION_OPS;
  if (!ops.includes(clause.op)) return `unknown op '${clause.op}'`;

  switch (clause.op) {
    case 'in':
      if (!Array.isArray(clause.value) || clause.value.length === 0) {
        return "'in' requires a non-empty list";
      }
      return null;
    case 'gte':
    case 'lte':
    case 'gt':
    case 'lt':
      return isNumeric(clause.value) ? null : `'${clause.op}' requires a numeric value`;
    case 'eq':
    case 'ne':
      return typeof clause.value === 'string' ||
        typeof clause.value === 'number' ||
        typeof clause.value === 'boolean'
        ? null
        : `'${clause.op}' requires a single value`;
    case 'exists':
      return typeof clause.value === 'boolean' ? null : "'exists' requires true/false";
    case 'occurred_within': {
      if (!isOccurredWithinValue(clause.value)) {
        return "'occurred_within' requires an event name and a number of hours";
      }
      const hours = Number(clause.value.hours);
      if (hours <= 0 || hours > MAX_WINDOW_HOURS) {
        return `occurred_within.hours must be between 0 and ${MAX_WINDOW_HOURS}`;
      }
      return null;
    }
    default:
      return null;
  }
}

export function validateConditions(conditions: readonly ConditionClause[] | null | undefined): string[] {
  if (!conditions) return [];
  return conditions.map(validateConditionClause).filter((e): e is string => e !== null);
}

/** Default value shape for a freshly-added clause of a given op, so the form never renders `undefined`. */
export function defaultValueForOp(op: ConditionOp): ConditionClause['value'] {
  switch (op) {
    case 'in':
      return [];
    case 'exists':
      return true;
    case 'occurred_within':
      return { after_event: '', hours: 24 };
    case 'gte':
    case 'lte':
    case 'gt':
    case 'lt':
      return 0;
    default:
      return '';
  }
}
