import { Rule, RuleType } from './rule.model';

/**
 * Rule types that never enter winner selection — the engine posts them through their own
 * processors (TransferRuleProcessor dual-entry, ReversalRuleProcessor inherited target), see
 * WinnerSelector.cs. They don't compete with or stack on anything.
 */
const OUTSIDE_WINNER_SELECTION: ReadonlySet<RuleType> = new Set<RuleType>([
  'TransferRule',
  'ReversalRule',
]);

/** Every rule that can meet the same event on the same wallet — the unit the engine resolves. */
export interface RuleApplicationGroup {
  key: string;
  trigger: string;
  targetAccountTypeId: string;
  /** Compete with each other: the highest-priority match wins, the rest don't fire. */
  exclusive: Rule[];
  /** Always add their award on top of the exclusive winner. */
  stackable: Rule[];
}

export interface GroupedRules {
  groups: RuleApplicationGroup[];
  /** Transfer / Reversal rules (and anything without a target), listed by themselves. */
  outsideWinnerSelection: Rule[];
}

const byPriorityThenName = (a: Rule, b: Rule): number =>
  b.priority - a.priority || a.name.localeCompare(b.name);

/**
 * Groups rules the way WinnerSelector applies them (1.3.CL): per trigger event, per target
 * account; inside that, exclusive rules compete on priority and stackable rules add on top.
 * `accountName` only orders groups for display — resolution itself never depends on it.
 */
export function groupRulesByApplication(
  rules: readonly Rule[],
  accountName: (accountTypeId: string) => string = (id) => id,
): GroupedRules {
  const groups = new Map<string, RuleApplicationGroup>();
  const outsideWinnerSelection: Rule[] = [];

  for (const rule of rules) {
    if (OUTSIDE_WINNER_SELECTION.has(rule.type) || !rule.targetAccountTypeId) {
      outsideWinnerSelection.push(rule);
      continue;
    }
    const key = `${rule.trigger}|${rule.targetAccountTypeId}`;
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        trigger: rule.trigger,
        targetAccountTypeId: rule.targetAccountTypeId,
        exclusive: [],
        stackable: [],
      };
      groups.set(key, group);
    }
    (rule.stackable ? group.stackable : group.exclusive).push(rule);
  }

  for (const group of groups.values()) {
    group.exclusive.sort(byPriorityThenName);
    group.stackable.sort(byPriorityThenName);
  }

  return {
    groups: [...groups.values()].sort(
      (a, b) =>
        a.trigger.localeCompare(b.trigger) ||
        accountName(a.targetAccountTypeId).localeCompare(accountName(b.targetAccountTypeId)),
    ),
    outsideWinnerSelection: outsideWinnerSelection.sort(
      (a, b) => a.trigger.localeCompare(b.trigger) || byPriorityThenName(a, b),
    ),
  };
}

/**
 * How an exclusive rule stands in its trigger/account group. WinnerSelector tries active rules
 * highest-priority first and posts the FIRST one that matches (conditions, limits, non-zero
 * amount) — so the top one wins whenever it matches and the rest are fallbacks for events it
 * doesn't match. Equal top priorities are broken by rule id in the engine ('tie'). Disabled or
 * pending rules don't compete at all ('inactive').
 */
export type ExclusiveRank = 'top' | 'tie' | 'fallback' | 'inactive';

export function rankExclusiveRules(exclusive: readonly Rule[]): Map<string, ExclusiveRank> {
  const ranks = new Map<string, ExclusiveRank>();
  const active = exclusive.filter((r) => r.status === 'active');
  const topPriority = active.length > 0 ? Math.max(...active.map((r) => r.priority)) : null;
  const topCount = active.filter((r) => r.priority === topPriority).length;

  for (const rule of exclusive) {
    if (rule.status !== 'active') ranks.set(rule.id, 'inactive');
    else if (rule.priority === topPriority) ranks.set(rule.id, topCount > 1 ? 'tie' : 'top');
    else ranks.set(rule.id, 'fallback');
  }
  return ranks;
}
