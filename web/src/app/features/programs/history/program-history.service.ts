import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { ConfigVersionDetail, ConfigVersionSummary } from './program-history.model';

const ALL_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class ProgramHistoryService {
  private readonly api = inject(ApiClient);

  async list(entityType: string, entityId: string): Promise<ConfigVersionSummary[]> {
    const page = await firstValueFrom(
      this.api.tenantScope.getPage<ConfigVersionSummary>('config-versions', {
        page: 1,
        pageSize: ALL_PAGE_SIZE,
        entityType,
        entityId,
      }),
    );
    return [...page.data];
  }

  get(versionId: string): Promise<ConfigVersionDetail> {
    return firstValueFrom(this.api.tenantScope.get<ConfigVersionDetail>(`config-versions/${versionId}`));
  }
}
