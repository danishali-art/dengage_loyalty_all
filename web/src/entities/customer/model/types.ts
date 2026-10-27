import type { ApiSchema } from '@/shared/api';

/**
 * Customer 360 (CustomersDtos.cs). Amounts are decimal strings, read without parsing (see
 * `isDebit` / `isZeroAmount`). Event data inside the event drawer keeps its snake_case keys and
 * arrives with phone and card / national-id fields already masked by the API.
 */
export type CustomerSummary = ApiSchema<'CustomerSummaryResponse'>;
export type CustomerProfile = ApiSchema<'CustomerProfileResponse'>;
export type AccountBalance = ApiSchema<'AccountBalanceResponse'>;
export type ProgramOverview = ApiSchema<'ProgramOverviewResponse'>;
export type LedgerEntry = ApiSchema<'LedgerEntryResponse'>;
export type TierHistoryEntry = ApiSchema<'TierHistoryEntryResponse'>;
export type CustomerEvent = ApiSchema<'CustomerEventResponse'>;
export type CustomerEventDetail = ApiSchema<'CustomerEventDetailResponse'>;
export type RuleFire = ApiSchema<'RuleFireResponse'>;
export type CustomerRuleFire = ApiSchema<'CustomerRuleFireResponse'>;
export type RuleCapUsage = ApiSchema<'RuleCapUsageResponse'>;
export type CustomerStreak = ApiSchema<'CustomerStreakResponse'>;
export type StreakCompletion = ApiSchema<'StreakCompletionResponse'>;
export type CustomerReward = ApiSchema<'CustomerRewardResponse'>;
export type RewardOutcome = CustomerReward['outcome'];
export type CustomerCardBucket = ApiSchema<'CustomerCardBucketResponse'>;
export type SentMessage = ApiSchema<'SentMessageResponse'>;

/** A cursor page (CursorPage<T> in Api.Framework): `nextCursor` reaches the next page. */
export interface CursorPage<T> {
  readonly data: readonly T[];
  readonly nextCursor?: string | null;
  readonly total?: number | null;
}

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
export type InboxStatus = CustomerEvent['status'];
export const INBOX_STATUSES: readonly InboxStatus[] = ['pending', 'processed', 'failed'];

/** Mirrors OutboxStatus.cs. */
export type OutboxStatus = SentMessage['status'];
export const OUTBOX_STATUSES: readonly OutboxStatus[] = ['pending', 'published', 'failed'];

/** GET customers/{contactKey}/ledger filters; times are UTC ISO strings. */
export interface LedgerQuery {
  programId?: string;
  accountTypeId?: string;
  reasonGroup?: ReasonGroup;
  from?: string;
  to?: string;
  eventId?: string;
  /** A card bucket's (or any rule's) postings. */
  ruleId?: string;
}

export interface EventQuery {
  eventType?: string;
  status?: InboxStatus;
  from?: string;
  to?: string;
}

export interface RuleFireQuery {
  ruleId?: string;
  from?: string;
  to?: string;
}

export interface MessageQuery {
  eventType?: string;
  status?: OutboxStatus;
  from?: string;
  to?: string;
}
