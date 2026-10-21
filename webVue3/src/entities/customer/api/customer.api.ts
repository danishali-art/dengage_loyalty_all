import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type {
  CursorPage,
  CustomerCardBucket,
  CustomerEvent,
  CustomerEventDetail,
  CustomerProfile,
  CustomerReward,
  CustomerRuleFire,
  CustomerStreak,
  CustomerSummary,
  EventQuery,
  LedgerEntry,
  LedgerQuery,
  MessageQuery,
  RuleCapUsage,
  RuleFireQuery,
  SentMessage,
  TierHistoryEntry,
} from '../model/types';

/** `/tenants/{tenantId}/customers` (CustomersController.cs). The contact key is a path segment. */
export function listCustomers(
  tenantId: string,
  page: number,
  pageSize: number,
  search: string,
): Promise<Page<CustomerSummary>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers', {
      params: { path: { tenantId }, query: { page, pageSize, search: search || undefined } },
    }),
  );
}

export function getCustomerProfile(tenantId: string, contactKey: string): Promise<CustomerProfile> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

// The cursor endpoints name their page size `limit` (not `pageSize`).

export function getCustomerLedger(
  tenantId: string,
  contactKey: string,
  query: LedgerQuery,
  cursor: string | null,
  limit: number,
): Promise<CursorPage<LedgerEntry>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/ledger', {
      params: { path: { tenantId, contactKey }, query: { ...query, cursor: cursor ?? undefined, limit } },
    }),
  );
}

export function getCustomerTierHistory(tenantId: string, contactKey: string): Promise<TierHistoryEntry[]> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/tier-history', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

/** Events received after CR 2026-10-02 that carry this customer's contact_key. */
export function getCustomerEvents(
  tenantId: string,
  contactKey: string,
  query: EventQuery,
  cursor: string | null,
  limit: number,
): Promise<CursorPage<CustomerEvent>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/events', {
      params: { path: { tenantId, contactKey }, query: { ...query, cursor: cursor ?? undefined, limit } },
    }),
  );
}

/** The event drawer. 404 when the event isn't linked to this customer — the drawer says so. */
export function getCustomerEvent(
  tenantId: string,
  contactKey: string,
  eventId: string,
): Promise<CustomerEventDetail> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/events/{eventId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, contactKey, eventId } },
    }),
  );
}

export function getCustomerRuleFires(
  tenantId: string,
  contactKey: string,
  query: RuleFireQuery,
  cursor: string | null,
  limit: number,
): Promise<CursorPage<CustomerRuleFire>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/rule-fires', {
      params: { path: { tenantId, contactKey }, query: { ...query, cursor: cursor ?? undefined, limit } },
    }),
  );
}

/** D7: per-customer cap usage, computed from the ledger the way the engine counts it. */
export function getCustomerCapUsage(tenantId: string, contactKey: string): Promise<RuleCapUsage[]> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/cap-usage', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

export function getCustomerStreaks(tenantId: string, contactKey: string): Promise<CustomerStreak[]> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/streaks', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

export function getCustomerRewards(tenantId: string, contactKey: string): Promise<CustomerReward[]> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/rewards', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

export function getCustomerCardBuckets(tenantId: string, contactKey: string): Promise<CustomerCardBucket[]> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/card-buckets', {
      params: { path: { tenantId, contactKey } },
    }),
  );
}

/** D5: never the payload. Published messages are purged after 30 days. */
export function getCustomerMessages(
  tenantId: string,
  contactKey: string,
  query: MessageQuery,
  cursor: string | null,
  limit: number,
): Promise<CursorPage<SentMessage>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/customers/{contactKey}/messages', {
      params: { path: { tenantId, contactKey }, query: { ...query, cursor: cursor ?? undefined, limit } },
    }),
  );
}
