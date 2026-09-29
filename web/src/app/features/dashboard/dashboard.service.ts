import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { DashboardFilter, DashboardSummary } from './dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly api = inject(ApiClient);

  getSummary(filter: DashboardFilter): Promise<DashboardSummary> {
    const params: Record<string, unknown> = {};
    if (filter.programId) params['programId'] = filter.programId;
    if (filter.fromDate) params['fromDate'] = filter.fromDate;
    if (filter.toDate) params['toDate'] = filter.toDate;

    return firstValueFrom(this.api.tenantScope.get<DashboardSummary>('dashboard/summary', { params }));
  }
}
