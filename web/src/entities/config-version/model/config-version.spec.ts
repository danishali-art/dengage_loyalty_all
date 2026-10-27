import { describe, expect, it } from 'vitest';
import { prettySnapshot, snapshotSections } from './types';

describe('config version snapshots', () => {
  it('splits a publish snapshot into its sections, in order, with counts', () => {
    const snapshot = JSON.stringify({ Tiers: [{}, {}], Program: { Name: 'P' }, Rules: [] });
    const sections = snapshotSections({ changeType: 'published', snapshot });
    expect(sections?.map((s) => [s.key, s.count])).toEqual([
      ['Program', null],
      ['Tiers', 2],
      ['Rules', 0],
    ]);
  });

  it('shows per-edit rows (and unparseable snapshots) as raw JSON', () => {
    expect(snapshotSections({ changeType: 'updated', snapshot: '{}' })).toBeNull();
    expect(snapshotSections({ changeType: 'published', snapshot: 'not json' })).toBeNull();
    expect(prettySnapshot('{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(prettySnapshot('not json')).toBe('not json');
  });
});
