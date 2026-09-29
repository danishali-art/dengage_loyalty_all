import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { CursorPage, Page } from '../../core/http/pagination';
import { CustomerProfile, CustomerSummary, LedgerEntry, TierHistoryEntry } from './customer.model';

@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly api = inject(ApiClient);

  list(page: number, pageSize: number, search?: string): Promise<Page<CustomerSummary>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<CustomerSummary>('customers', { page, pageSize, search: search || undefined }),
    );
  }

  getProfile(contactKey: string): Promise<CustomerProfile> {
    return firstValueFrom(this.api.tenantScope.get<CustomerProfile>(`customers/${encodeURIComponent(contactKey)}`));
  }

  // The backend's cursor param is named `limit` (not `pageSize`, the app-wide CursorQuery
  // convention) — pass params explicitly rather than through the generic getCursor() helper.
  getLedger(contactKey: string, cursor: string | null, limit = 25): Promise<CursorPage<LedgerEntry>> {
    return firstValueFrom(
      this.api.tenantScope.get<CursorPage<LedgerEntry>>(`customers/${encodeURIComponent(contactKey)}/ledger`, {
        params: { cursor: cursor ?? undefined, limit },
      }),
    );
  }

  getTierHistory(contactKey: string): Promise<TierHistoryEntry[]> {
    return firstValueFrom(
      this.api.tenantScope.get<TierHistoryEntry[]>(`customers/${encodeURIComponent(contactKey)}/tier-history`),
    );
  }
}
