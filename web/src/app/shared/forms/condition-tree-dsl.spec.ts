import {
  ConditionLeaf,
  ConditionTree,
  conditionsForSave,
  emptyLeaf,
  emptyTree,
  validateConditionTree,
} from './condition-tree-dsl';

// CR 2026-10-05 item 4: the Conditions section is optional for every trigger.
describe('conditionsForSave', () => {
  const filled: ConditionLeaf = {
    field: 'amount',
    operator: 'gte',
    value: { type: 'number', data: '100' },
  };
  const tree = (...groups: ConditionLeaf[][]): ConditionTree => ({
    op: 'AND',
    groups: groups.map((conditions) => ({ op: 'AND', conditions })),
  });

  it('saves the untouched form (one blank condition) as no conditions', () => {
    const saved = conditionsForSave(emptyTree());
    expect(saved).toBeNull();
    expect(validateConditionTree(saved)).toEqual([]);
  });

  it('treats several blank conditions and groups as no conditions', () => {
    const blanks = tree([emptyLeaf(), emptyLeaf()], [{ ...emptyLeaf(), field: '   ' }]);
    expect(conditionsForSave(blanks)).toBeNull();
  });

  it('keeps a filled-in tree, so it is still validated', () => {
    const t = tree([filled]);
    expect(conditionsForSave(t)).toBe(t);
  });

  it('keeps a half-filled tree, so the blank condition still blocks saving', () => {
    const t = tree([filled, emptyLeaf()]);
    const saved = conditionsForSave(t);
    expect(saved).toBe(t);
    expect(validateConditionTree(saved).length).toBeGreaterThan(0);
  });
});
