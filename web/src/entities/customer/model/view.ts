// Pure view helpers for the customer screens (list row, rules tab, event drawer, rewards and
// messages tabs, icons), kept together.
import { meterPercent, type IconName, type StatusTone } from '@/shared/ui';
import type { CustomerProfile, RewardOutcome, RuleCapUsage } from './types';

/** What the Customers list shows for one customer, beyond the list API's own columns. */
export interface CustomerListRow {
  /** The names of the programs the customer has a wallet in. */
  programs: string[];
  firstSeenAt: string | null;
}

/** A list row from the data the profile endpoint already returns (Customers list, Layout A). */
export function customerListRow(profile: CustomerProfile): CustomerListRow {
  return {
    programs: (profile.programs ?? []).map((p) => p.programName),
    firstSeenAt: profile.summary?.firstSeenAt ?? null,
  };
}

export interface CapWindow {
  key: 'today' | 'period' | 'total';
  labelKey: string;
  used: string;
  cap: string | null | undefined;
}

export interface CapTile {
  cap: RuleCapUsage;
  windows: CapWindow[];
  /** The tightest cap's use, 0–100. */
  percent: number;
  reached: boolean;
}

/** One tile per capped rule: its three windows, and how close the tightest cap is. */
export function capTile(cap: RuleCapUsage): CapTile {
  const windows: CapWindow[] = [
    { key: 'total', labelKey: 'customers.rules.windowTotal', used: cap.usedTotal, cap: cap.perCustomerTotal },
    {
      key: 'today',
      labelKey: 'customers.rules.windowToday',
      used: cap.usedToday,
      cap: cap.perCustomerPerDay,
    },
    {
      key: 'period',
      labelKey: 'customers.rules.windowPeriod',
      used: cap.usedThisPeriod ?? '0',
      cap: cap.perCustomerPerPeriod,
    },
  ];
  const percents = windows.filter((w) => w.cap).map((w) => meterPercent(w.used, w.cap));
  const percent = Math.round(Math.max(0, ...percents));
  return { cap, windows, percent, reached: percent >= 100 };
}

export function inboxStatusTone(status: string): StatusTone {
  return status === 'processed' ? 'success' : status === 'failed' ? 'danger' : 'warning';
}

export function outboxStatusTone(status: string): StatusTone {
  return status === 'published' ? 'success' : status === 'failed' ? 'danger' : 'warning';
}

export function rewardOutcomeTone(outcome: RewardOutcome): StatusTone {
  return outcome === 'none' ? 'neutral' : 'success';
}

/** The icon for a wallet type. */
export function walletIcon(type: string): IconName {
  return type === 'CASH' ? 'cash' : type === 'STAMP' ? 'stamp' : 'star';
}

/** A snapshot column (rule fire) pretty-printed when it is JSON, as it is otherwise. */
export function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
