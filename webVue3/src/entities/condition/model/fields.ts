import {
  defaultValueForOperator,
  MONEY_NUMBER_OPS,
  STRING_OPS,
  UNKNOWN_OPS,
  type ConditionLeaf,
  type ConditionLeafOp,
  type ConditionValue,
} from '@/shared/lib/dsl';

/** A trigger event's declared payload field (`GET rules/metadata` → `fields`, kind lower-cased). */
export interface KnownField {
  path: string;
  kind: 'money' | 'number' | 'string';
}

export type FieldKinds = ReadonlyMap<string, KnownField['kind']>;

export function fieldKinds(knownFields: readonly KnownField[]): FieldKinds {
  return new Map(knownFields.map((f) => [f.path, f.kind]));
}

/**
 * A5: an undeclared field path is flagged, never blocked. A blank one isn't flagged yet, and
 * nothing is flagged without a field list to check against (card buckets, a tenant-defined
 * trigger) — the Angular editor flagged every field there.
 */
export function isDeclared(kinds: FieldKinds, field: string): boolean {
  return !field || kinds.size === 0 || kinds.has(field);
}

export function kindFor(kinds: FieldKinds, field: string): KnownField['kind'] {
  return kinds.get(field) ?? 'string';
}

/** The operators offered for a field: by its declared kind, or the full set when undeclared (A5). */
export function opsFor(kinds: FieldKinds, field: string): readonly ConditionLeafOp[] {
  const kind = kinds.get(field);
  if (kind === 'money' || kind === 'number') return MONEY_NUMBER_OPS.concat('exists', 'is_null');
  if (kind === 'string') return STRING_OPS.concat('exists', 'is_null');
  return UNKNOWN_OPS;
}

export const asCsv = (value: unknown): string => (Array.isArray(value) ? value.join(', ') : '');

export const fromCsv = (raw: string): string[] =>
  raw
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s);

export function betweenBound(value: unknown, index: 0 | 1): number {
  return Array.isArray(value) && typeof value[index] === 'number' ? value[index] : 0;
}

/** Changing the operator resets the value to that operator's shape (condition-tree-editor.ts). */
export function withOperator(leaf: ConditionLeaf, operator: ConditionLeafOp): ConditionLeaf {
  return { ...leaf, operator, value: defaultValueForOperator(operator) };
}

export function withBetweenBound(leaf: ConditionLeaf, index: 0 | 1, raw: string): ConditionLeaf {
  const data = leaf.value.data;
  const current: number[] = Array.isArray(data) ? (data as number[]).slice() : [0, 0];
  current[index] = Number(raw);
  return { ...leaf, value: { ...leaf.value, type: 'number', data: current } };
}

export function withValue(leaf: ConditionLeaf, value: ConditionValue): ConditionLeaf {
  return { ...leaf, value };
}
