import { describe, expect, it } from 'vitest';
import { MONEY_NUMBER_OPS, STRING_OPS, UNKNOWN_OPS, type ConditionLeaf } from '@/shared/lib/dsl';
import { fieldKinds, isDeclared, opsFor, withBetweenBound, withOperator } from './fields';

const kinds = fieldKinds([
  { path: 'amount', kind: 'money' },
  { path: 'channel', kind: 'string' },
]);

describe('condition fields (condition-tree-editor.ts behaviour)', () => {
  it('offers operators by the declared kind, and the full set for an undeclared field (A5)', () => {
    expect(opsFor(kinds, 'amount')).toEqual([...MONEY_NUMBER_OPS, 'exists', 'is_null']);
    expect(opsFor(kinds, 'channel')).toEqual([...STRING_OPS, 'exists', 'is_null']);
    expect(opsFor(kinds, 'custom.flag')).toBe(UNKNOWN_OPS);
  });

  it('flags an undeclared field only when there is a field list to check against', () => {
    expect(isDeclared(kinds, 'amount')).toBe(true);
    expect(isDeclared(kinds, 'amout')).toBe(false);
    expect(isDeclared(kinds, '')).toBe(true);
    expect(isDeclared(fieldKinds([]), 'merchant.city')).toBe(true);
  });

  it('resets the value to the new operator shape, and edits a between bound as a number', () => {
    const leaf: ConditionLeaf = { field: 'amount', operator: 'eq', value: { type: 'string', data: 'x' } };
    expect(withOperator(leaf, 'in').value).toEqual({ type: 'string[]', data: [] });
    const between = withOperator(leaf, 'between');
    expect(withBetweenBound(between, 1, '250').value).toEqual({ type: 'number', data: [0, 250] });
  });
});
