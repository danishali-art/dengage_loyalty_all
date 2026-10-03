import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { CursorPage, Page } from '../../core/http/pagination';
import {
  CustomerCardBucket,
  CustomerEvent,
  CustomerReward,
  CustomerRuleFire,
  CustomerStreak,
  RuleCapUsage,
  RuleFireQuery,
  SentMessage,
  CustomerEventDetail,
  CustomerProfile,
  CustomerSummary,
  EventQuery,
  LedgerEntry,
  LedgerQuery,
  MessageQuery,
  TierHistoryEntry,
} from './customer.model';

@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly api = inject(ApiClient);

  list(page: number, pageSize: number, search?: string): Promise<Page<CustomerSummary>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<CustomerSummary>('customers', {
        page,
        pageSize,
        search: search || undefined,
      }),
    );
  }

  getProfile(contactKey: string): Promise<CustomerProfile> {
    return firstValueFrom(
      this.api.tenantScope.get<CustomerProfile>(`customers/${encodeURIComponent(contactKey)}`),
    );
  }

  // The backend's cursor param is named `limit` (not `pageSize`, the app-wide CursorQuery
  // convention) — pass params explicitly rather than through the generic getCursor() helper.
  getLedger(
    contactKey: string,
    query: LedgerQuery,
    cursor: string | null,
    limit = 25,
  ): Promise<CursorPage<LedgerEntry>> {
    return firstValueFrom(
      this.api.tenantScope.get<CursorPage<LedgerEntry>>(
        `customers/${encodeURIComponent(contactKey)}/ledger`,
        {
          params: { ...query, cursor: cursor ?? undefined, limit },
        },
      ),
    );
  }

  getTierHistory(contactKey: string): Promise<TierHistoryEntry[]> {
    return firstValueFrom(
      this.api.tenantScope.get<TierHistoryEntry[]>(
        `customers/${encodeURIComponent(contactKey)}/tier-history`,
      ),
    );
  }

  /** CR 2026-10-02: events received after the CR that carry this customer's contact_key. */
  getEvents(
    contactKey: string,
    query: EventQuery,
    cursor: string | null,
    limit = 25,
  ): Promise<CursorPage<CustomerEvent>> {
    return firstValueFrom(
      this.api.tenantScope.get<CursorPage<CustomerEvent>>(
        `customers/${encodeURIComponent(contactKey)}/events`,
        {
          params: { ...query, cursor: cursor ?? undefined, limit },
        },
      ),
    );
  }

  /** CR 2026-10-02: the event drawer. 404 when the event isn't linked to this customer. */
  getEvent(contactKey: string, eventId: string): Promise<CustomerEventDetail> {
    return firstValueFrom(
      this.api.tenantScope.get<CustomerEventDetail>(
        `customers/${encodeURIComponent(contactKey)}/events/${encodeURIComponent(eventId)}`,
        { skipErrorToast: true },
      ),
    );
  }

  // ── CR 2026-10-02 (Customer 360) P2 ──

  getRuleFires(
    contactKey: string,
    query: RuleFireQuery,
    cursor: string | null,
    limit = 25,
  ): Promise<CursorPage<CustomerRuleFire>> {
    return firstValueFrom(
      this.api.tenantScope.get<CursorPage<CustomerRuleFire>>(
        `customers/${encodeURIComponent(contactKey)}/rule-fires`,
        { params: { ...query, cursor: cursor ?? undefined, limit } },
      ),
    );
  }

  getCapUsage(contactKey: string): Promise<RuleCapUsage[]> {
    return firstValueFrom(
      this.api.tenantScope.get<RuleCapUsage[]>(
        `customers/${encodeURIComponent(contactKey)}/cap-usage`,
      ),
    );
  }

  getStreaks(contactKey: string): Promise<CustomerStreak[]> {
    return firstValueFrom(
      this.api.tenantScope.get<CustomerStreak[]>(
        `customers/${encodeURIComponent(contactKey)}/streaks`,
      ),
    );
  }

  getRewards(contactKey: string): Promise<CustomerReward[]> {
    return firstValueFrom(
      this.api.tenantScope.get<CustomerReward[]>(
        `customers/${encodeURIComponent(contactKey)}/rewards`,
      ),
    );
  }

  // ── CR 2026-10-02 (Customer 360) P3 ──

  getCardBuckets(contactKey: string): Promise<CustomerCardBucket[]> {
    return firstValueFrom(
      this.api.tenantScope.get<CustomerCardBucket[]>(
        `customers/${encodeURIComponent(contactKey)}/card-buckets`,
      ),
    );
  }

  /** D5: no payload. Published messages are purged after 30 days. */
  getMessages(
    contactKey: string,
    query: MessageQuery,
    cursor: string | null,
    limit = 25,
  ): Promise<CursorPage<SentMessage>> {
    return firstValueFrom(
      this.api.tenantScope.get<CursorPage<SentMessage>>(
        `customers/${encodeURIComponent(contactKey)}/messages`,
        { params: { ...query, cursor: cursor ?? undefined, limit } },
      ),
    );
  }
}
