import { DecimalString } from '../../shared/money/decimal-string';

export interface CustomerSummary {
  contactKey: string;
  accountCount: number;
  lastActivityAt: string;
}

export interface AccountBalance {
  accountTypeId: string;
  accountTypeName: string;
  accountTypeType: string;
  balance: DecimalString;
  expirationDays: number | null;
  /** CR 2026-10-02: feeds the program/wallet filters without reading the programs feature. */
  programId: string;
  programName: string;
}

export interface TierProgress {
  currentTierName: string | null;
  currentTierDisplayName: string | null;
  nextTierName: string | null;
  nextTierDisplayName: string | null;
  nextTierMinPoints: DecimalString | null;
  qualifyingPoints: DecimalString;
  periodStart: string | null;
  expiresAt: string | null;
}

export interface CustomerProfile {
  contactKey: string;
  balances: AccountBalance[];
  tierProgress: TierProgress | null;
  /** CR 2026-10-02 P2: header facts. */
  summary?: CustomerSummaryInfo;
  /** CR 2026-10-02 P2: one entry per program the customer has a wallet in. */
  programs?: ProgramOverview[];
}

export interface CustomerSummaryInfo {
  /** The customer's earliest posting. */
  firstSeenAt?: string | null;
  lastActivityAt: string;
  /** Counts events received after the Customer 360 release only. */
  failedEventsLast7Days: number;
  activeStreakCount: number;
}

export interface ProgramOverview {
  programId: string;
  programName: string;
  wallets: WalletOverview[];
  tier?: ProgramTierStatus | null;
  streaks: StreakSummary[];
}

export interface WalletOverview {
  accountTypeId: string;
  name: string;
  type: string;
  balance: DecimalString;
  currency?: string | null;
  /** Delayed postings not yet released. */
  pendingAmount: DecimalString;
  /** Same FIFO figure as the "points expiring" warning; absent when nothing is about to expire. */
  expiringAmount?: DecimalString | null;
  expiresOn?: string | null;
}

export interface ProgramTierStatus {
  tierName?: string | null;
  tierDisplayName?: string | null;
  nextTierName?: string | null;
  nextTierDisplayName?: string | null;
  nextTierMinPoints?: DecimalString | null;
  qualifyingPoints: DecimalString;
  periodStart?: string | null;
  /** The date the nightly downgrade job reviews the tier. */
  graceEndsAt?: string | null;
  /** Set by a tier-upgrade reward with a duration. */
  lockedUntil?: string | null;
}

export interface StreakSummary {
  campaignId: string;
  campaignName: string;
  streakCount: number;
  targetPeriods: number;
  completions: number;
  status: string;
}

/** Mirrors LedgerReason.cs. */
export type LedgerReason =
  | 'earn'
  | 'stamp_earn'
  | 'stamp_reset'
  | 'cash_load'
  | 'cash_spend'
  | 'points_redeemed'
  | 'points_redeemed_cash'
  | 'refund'
  | 'points_expired'
  | 'reward_purchase'
  | 'transfer_out'
  | 'transfer_in'
  | 'points_adjusted'
  | 'rule_reversal'
  | 'reward_cashback';

/** Mirrors LedgerReasonGroups.cs — the Activity tab's reason-group filter. */
export type ReasonGroup = 'earn' | 'burn' | 'transfer' | 'expiry' | 'reward' | 'adjustment';
export const REASON_GROUPS: readonly ReasonGroup[] = [
  'earn',
  'burn',
  'transfer',
  'expiry',
  'reward',
  'adjustment',
];

/** Mirrors InboxStatus.cs. */
export type InboxStatus = 'pending' | 'processed' | 'failed';
export const INBOX_STATUSES: readonly InboxStatus[] = ['pending', 'processed', 'failed'];

export interface LedgerEntry {
  id: string;
  reason: string;
  delta: DecimalString;
  accountTypeId: string;
  metadata: string | null;
  createdAt: string;
  // CR 2026-10-02 (Customer 360): where the posting came from and which wallet it hit.
  sourceEventId: string;
  /** Null when there is no inbound event (scheduled jobs such as points expiry). */
  eventType: string | null;
  ruleId: string | null;
  ruleName: string | null;
  /** The version the posting was made under, not the rule's current one. */
  ruleVersion: number | null;
  campaignId: string | null;
  campaignName: string | null;
  accountTypeName: string;
  accountTypeType: string;
  programId: string;
  programName: string;
}

/** GET customers/{contactKey}/ledger filters; times are UTC ISO strings. */
export interface LedgerQuery {
  programId?: string;
  accountTypeId?: string;
  reasonGroup?: ReasonGroup;
  from?: string;
  to?: string;
  eventId?: string;
  /** CR 2026-10-02 P3: a card bucket's (or any rule's) postings. */
  ruleId?: string;
}

/** CR 2026-10-02 P2: why a tier changed. */
export type TierChangeCause = 'points' | 'reward' | 'downgrade';

export interface TierHistoryEntry {
  id: string;
  fromTierName: string | null;
  toTierName: string;
  qualifyingPoints: DecimalString;
  createdAt: string;
  programId?: string;
  programName?: string;
  cause?: TierChangeCause;
  sourceEventId?: string;
}

// ── CR 2026-10-02 (Customer 360): events and the event drawer ──

/** GET customers/{contactKey}/events filters; times are UTC ISO strings. */
export interface EventQuery {
  eventType?: string;
  status?: InboxStatus;
  from?: string;
  to?: string;
}

export interface EventWalletOutcome {
  accountTypeId: string;
  accountTypeName: string;
  accountTypeType: string;
  postings: number;
  netDelta: DecimalString;
}

export interface CustomerEvent {
  eventId: string;
  eventType: string;
  occurredAt: string | null;
  receivedAt: string;
  processedAt: string | null;
  status: InboxStatus;
  error: string | null;
  outcome: EventWalletOutcome[];
}

export interface InboundEvent {
  eventId: string;
  eventType: string;
  occurredAt: string | null;
  receivedAt: string;
  processedAt: string | null;
  status: InboxStatus;
  error: string | null;
  /** The event's data, snake_case keys, with phone and card / national-id fields masked. */
  data: unknown;
}

export interface EventPosting {
  id: string;
  /** Differs from the customer's own only for a transfer's counterparty. */
  contactKey: string;
  reason: string;
  delta: DecimalString;
  metadata: string | null;
  createdAt: string;
  ruleId: string | null;
  ruleName: string | null;
  ruleVersion: number | null;
  campaignId: string | null;
  campaignName: string | null;
  accountTypeId: string;
  accountTypeName: string;
  accountTypeType: string;
  programId: string;
  programName: string;
}

export interface HeldPosting {
  id: string;
  ruleId: string;
  ruleName: string | null;
  accountTypeId: string;
  accountTypeName: string;
  reason: string;
  delta: DecimalString;
  holdUntil: string;
  postedAt: string | null;
}

export interface RuleFire {
  id: string;
  ruleId: string;
  ruleName: string | null;
  ruleVersion: number;
  resultingDelta: DecimalString;
  ledgerEntryId: string | null;
  conditionsSnapshot: string | null;
  calculationSnapshot: string;
  resolutionSnapshot: string | null;
  createdAt: string;
}

export interface StreakApplied {
  campaignId: string;
  campaignName: string | null;
  appliedAt: string;
}

export interface StreakCompletion {
  campaignId: string;
  campaignName: string | null;
  completionNo: number;
  completedPeriod: string;
  periods: number;
  rewardKind: string;
  rewardRef: string | null;
  createdAt: string;
}

export interface RewardLogEntry {
  id: string;
  rewardName: string;
  rewardDefinitionId: string | null;
  status: string;
  completionCount: number;
  createdAt: string;
  deliveredAt: string | null;
}

export interface TierChange {
  id: string;
  fromTierName: string | null;
  toTierName: string;
  qualifyingPoints: DecimalString;
  createdAt: string;
}

/** D5: no payload. */
export interface SentMessage {
  eventId: string;
  eventType: string;
  status: string;
  attempts: number;
  dedupKey: string | null;
  createdAt: string;
  publishedAt: string | null;
}

export interface CustomerEventDetail {
  eventId: string;
  /** Null when the id has no inbound event (a scheduled job's postings). */
  event: InboundEvent | null;
  postings: EventPosting[];
  heldPostings: HeldPosting[];
  ruleFires: RuleFire[];
  streaksApplied: StreakApplied[];
  streakCompletions: StreakCompletion[];
  rewards: RewardLogEntry[];
  tierChanges: TierChange[];
  messages: SentMessage[];
}

// ── CR 2026-10-02 (Customer 360) P2: rules & caps, streaks, rewards ──

/** GET customers/{contactKey}/rule-fires filters; times are UTC ISO strings. */
export interface RuleFireQuery {
  ruleId?: string;
  from?: string;
  to?: string;
}

export interface CustomerRuleFire {
  id: string;
  ruleId: string;
  ruleName?: string | null;
  ruleVersion: number;
  sourceEventId: string;
  eventType?: string | null;
  resultingDelta: DecimalString;
  ledgerEntryId?: string | null;
  conditionsSnapshot?: string | null;
  calculationSnapshot: string;
  resolutionSnapshot?: string | null;
  createdAt: string;
}

/** D7: usage computed from the ledger the way the engine counts it. */
export interface RuleCapUsage {
  ruleId: string;
  ruleName: string;
  programId: string;
  programName: string;
  status: string;
  isCardBucket: boolean;
  perCustomerTotal?: DecimalString | null;
  usedTotal: DecimalString;
  perCustomerPerDay?: DecimalString | null;
  usedToday: DecimalString;
  perCustomerPerPeriod?: DecimalString | null;
  period?: string | null;
  resetWindow?: string | null;
  periodStart?: string | null;
  usedThisPeriod?: DecimalString | null;
}

export interface CustomerStreak {
  campaignId: string;
  campaignName: string;
  programId: string;
  programName: string;
  campaignStatus: string;
  period: string;
  targetPeriods: number;
  metric: string;
  threshold: DecimalString;
  rewardKind: string;
  streakCount: number;
  lastMetPeriod?: string | null;
  completions: number;
  status: string;
  /** The most recent period recorded for the customer — not necessarily the current one. */
  latestPeriodStart?: string | null;
  latestAggSum?: DecimalString | null;
  latestAggCount?: number | null;
  latestPeriodMet?: boolean | null;
  history: StreakCompletion[];
}

export type RewardOutcome = 'cash_credited' | 'tier_upgraded' | 'none';

export interface CustomerReward {
  /** points_purchase | stamp_completion | streak_completion */
  source: string;
  rewardName: string;
  rewardDefinitionId?: string | null;
  rewardType?: string | null;
  sourceEventId: string;
  cost?: DecimalString | null;
  costWalletName?: string | null;
  outcome: RewardOutcome;
  cashAmount?: DecimalString | null;
  cashWalletName?: string | null;
  tierName?: string | null;
  status?: string | null;
  createdAt: string;
}

// ── CR 2026-10-02 (Customer 360) P3: card buckets, messages sent ──

export interface CustomerCardBucket {
  ruleId: string;
  name: string;
  programId: string;
  programName: string;
  status: string;
  rewardAmount?: DecimalString | null;
  perCustomerPerDay?: DecimalString | null;
  usedToday: DecimalString;
  perCustomerTotal?: DecimalString | null;
  usedTotal: DecimalString;
  postings: number;
  lastPostedAt?: string | null;
}

/** Mirrors OutboxStatus.cs. */
export type OutboxStatus = 'pending' | 'published' | 'failed';
export const OUTBOX_STATUSES: readonly OutboxStatus[] = ['pending', 'published', 'failed'];

/** GET customers/{contactKey}/messages filters; times are UTC ISO strings. */
export interface MessageQuery {
  eventType?: string;
  status?: OutboxStatus;
  from?: string;
  to?: string;
}
