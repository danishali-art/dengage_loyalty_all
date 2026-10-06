/**
 * Ported verbatim from `dEngage.Loyalty.RuleEngine.Models.ConditionTree` and
 * `dEngage.Loyalty.RuleEngine.GroupedConditionDsl` — the CR-05 grouped AND/OR condition tree
 * used by Rules (and Card Buckets, which share the same `rules` table/column under the hood).
 *
 * Deliberately a SEPARATE file from `condition-dsl.ts` — Streak Campaigns still use that flat
 * DSL (their own table, out of CR-05's scope), so this only touches `features/programs/rules`.
 */

export const GROUP_JOIN_OPS = ['AND', 'OR'] as const;
export type GroupJoinOp = (typeof GROUP_JOIN_OPS)[number];

export const CONDITION_LEAF_OPS = [
  'gte',
  'lte',
  'between',
  'eq',
  'neq',
  'in',
  'not_in',
  'starts_with',
  'exists',
  'is_null',
] as const;
export type ConditionLeafOp = (typeof CONDITION_LEAF_OPS)[number];

/** money/number kind — full operator set minus exists/is_null (A5's "undeclared" extras). */
export const MONEY_NUMBER_OPS: readonly ConditionLeafOp[] = ['gte', 'lte', 'between', 'eq', 'neq'];
/** string kind. */
export const STRING_OPS: readonly ConditionLeafOp[] = ['eq', 'neq', 'in', 'not_in', 'starts_with'];
/** undeclared/unknown kind: the full set. */
export const UNKNOWN_OPS: readonly ConditionLeafOp[] = CONDITION_LEAF_OPS;

export type ConditionValueType = 'money' | 'number' | 'string' | 'string[]' | 'none';

export interface ConditionValue {
  type: ConditionValueType;
  data: unknown;
  currency?: string | null;
  inferred?: boolean;
}

export interface ConditionLeaf {
  field: string;
  operator: ConditionLeafOp;
  value: ConditionValue;
}

export interface ConditionGroup {
  op: GroupJoinOp;
  conditions: ConditionLeaf[];
}

export interface ConditionTree {
  op: GroupJoinOp;
  groups: ConditionGroup[];
}

/** Default value shape for a freshly-added leaf of a given operator, so the form never renders `undefined`. */
export function defaultValueForOperator(op: ConditionLeafOp): ConditionValue {
  switch (op) {
    case 'in':
    case 'not_in':
      return { type: 'string[]', data: [] };
    case 'between':
      return { type: 'number', data: [0, 0] };
    case 'gte':
    case 'lte':
      return { type: 'number', data: 0 };
    case 'exists':
      return { type: 'none', data: true };
    case 'is_null':
      return { type: 'none', data: null };
    default:
      return { type: 'string', data: '' };
  }
}

function isEmptyValue(v: ConditionValue): boolean {
  if (v.type === 'none') return false; // exists/is_null carry no value to check
  if (v.type === 'string[]') return !Array.isArray(v.data) || v.data.length === 0;
  return v.data === '' || v.data === null || v.data === undefined;
}

/**
 * @returns an error message, or null if the leaf is valid. Structural only — matches
 * GroupedConditionDsl's scope (kind-correctness against the trigger event's schema is a UI
 * autocomplete concern, not enforced here or server-side).
 */
export function validateConditionLeaf(leaf: ConditionLeaf): string | null {
  if (!leaf.field?.trim()) return 'field is required';
  const ops: readonly string[] = CONDITION_LEAF_OPS;
  if (!ops.includes(leaf.operator)) return `unknown operator '${leaf.operator}'`;
  if (leaf.operator === 'exists' || leaf.operator === 'is_null') return null;
  if (isEmptyValue(leaf.value)) return `'${leaf.operator}' has no value (field '${leaf.field}')`;
  if ((leaf.operator === 'in' || leaf.operator === 'not_in') && !Array.isArray(leaf.value.data)) {
    return `'${leaf.operator}' requires a list`;
  }
  if (
    leaf.operator === 'between' &&
    (!Array.isArray(leaf.value.data) || leaf.value.data.length !== 2)
  ) {
    return `'between' requires a [min, max] pair`;
  }
  return null;
}

/** A9 blocking rule: an AND-joined group can never match when the same field's gte > lte. */
function findUnsatisfiableBound(group: ConditionGroup): string | null {
  if (group.op !== 'AND') return null;
  const bounds = new Map<string, { lo?: number; hi?: number }>();
  for (const leaf of group.conditions) {
    if (leaf.operator !== 'gte' && leaf.operator !== 'lte') continue;
    const n = Number(leaf.value.data);
    if (!Number.isFinite(n)) continue;
    const b = bounds.get(leaf.field) ?? {};
    if (leaf.operator === 'gte') b.lo = n;
    else b.hi = n;
    bounds.set(leaf.field, b);
  }
  for (const [field, b] of bounds) {
    if (b.lo !== undefined && b.hi !== undefined && b.lo > b.hi) {
      return `'${field}' must be at least ${b.lo} and at most ${b.hi} — this group can never match`;
    }
  }
  return null;
}

export function validateConditionTree(tree: ConditionTree | null | undefined): string[] {
  if (!tree) return [];
  const errors: string[] = [];
  for (const group of tree.groups) {
    for (const leaf of group.conditions) {
      const e = validateConditionLeaf(leaf);
      if (e) errors.push(e);
    }
    const boundError = findUnsatisfiableBound(group);
    if (boundError) errors.push(boundError);
  }
  return errors;
}

export function emptyLeaf(): ConditionLeaf {
  return { field: '', operator: 'eq', value: defaultValueForOperator('eq') };
}

export function emptyGroup(): ConditionGroup {
  return { op: 'AND', conditions: [emptyLeaf()] };
}

/** A9 UI invariant #4: changing the trigger resets the condition tree. */
export function emptyTree(): ConditionTree {
  return { op: 'AND', groups: [emptyGroup()] };
}

/** True once the user has actually filled in a field path — used to gate the "you'll lose your
 * conditions" warning so it doesn't fire for a still-blank, freshly-added leaf. */
export function hasConditions(tree: ConditionTree): boolean {
  return tree.groups.some((g) => g.conditions.some((c) => c.field.trim() !== ''));
}

/**
 * CR 2026-10-05 item 4: what a rule form saves. A tree with only blank leaves means "no
 * conditions" and is saved as null, the one no-conditions value GroupedConditionDsl accepts
 * (it rejects a blank field path). Anything filled in is kept, so validation still applies.
 */
export function conditionsForSave(tree: ConditionTree): ConditionTree | null {
  return hasConditions(tree) ? tree : null;
}
