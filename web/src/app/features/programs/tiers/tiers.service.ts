import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import { CreateTierRequest, Tier, UpdateTierRequest } from './tier.model';

/**
 * Realistically bounded (a handful of tiers per program) — the reorder feature needs the
 * *complete* set in one page anyway (you can't meaningfully drag-reorder across page
 * boundaries), so the tiers grid always fetches "all of them" via this page size rather than
 * true multi-page paging. The endpoint still supports real pagination like every other list.
 */
const ALL_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class TiersService {
  private readonly api = inject(ApiClient);

  list(programId: string, page = 1, pageSize = ALL_PAGE_SIZE): Promise<Page<Tier>> {
    return firstValueFrom(this.api.tenantScope.getPage<Tier>(`programs/${programId}/tiers`, { page, pageSize }));
  }

  create(programId: string, request: CreateTierRequest): Promise<Tier> {
    return firstValueFrom(
      this.api.tenantScope.post<Tier>(`programs/${programId}/tiers`, request, { skipErrorToast: true }),
    );
  }

  update(programId: string, tierId: string, request: UpdateTierRequest): Promise<Tier> {
    return firstValueFrom(
      this.api.tenantScope.patch<Tier>(`programs/${programId}/tiers/${tierId}`, request, { skipErrorToast: true }),
    );
  }

  reorder(programId: string, tierIds: string[]): Promise<void> {
    return firstValueFrom(this.api.tenantScope.post<void>(`programs/${programId}/tiers/reorder`, { tierIds }));
  }

  delete(programId: string, tierId: string): Promise<void> {
    return firstValueFrom(
      this.api.tenantScope.delete<void>(`programs/${programId}/tiers/${tierId}`, { skipErrorToast: true }),
    );
  }
}
