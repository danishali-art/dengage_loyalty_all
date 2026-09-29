import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import {
  CreateStreakCampaignRequest,
  StreakCampaign,
  StreakCampaignListFilter,
  UpdateStreakCampaignRequest,
} from './streak-campaign.model';

@Injectable({ providedIn: 'root' })
export class StreakCampaignsService {
  private readonly api = inject(ApiClient);

  list(programId: string, page = 1, pageSize = 20, filter: StreakCampaignListFilter = {}): Promise<Page<StreakCampaign>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<StreakCampaign>(`programs/${programId}/streak-campaigns`, {
        page,
        pageSize,
        ...(filter as Record<string, unknown>),
      }),
    );
  }

  get(programId: string, campaignId: string): Promise<StreakCampaign> {
    return firstValueFrom(this.api.tenantScope.get<StreakCampaign>(`programs/${programId}/streak-campaigns/${campaignId}`));
  }

  create(programId: string, request: CreateStreakCampaignRequest): Promise<StreakCampaign> {
    return firstValueFrom(
      this.api.tenantScope.post<StreakCampaign>(`programs/${programId}/streak-campaigns`, request, { skipErrorToast: true }),
    );
  }

  update(programId: string, campaignId: string, request: UpdateStreakCampaignRequest): Promise<StreakCampaign> {
    return firstValueFrom(
      this.api.tenantScope.patch<StreakCampaign>(`programs/${programId}/streak-campaigns/${campaignId}`, request, {
        skipErrorToast: true,
      }),
    );
  }

  setStatus(programId: string, campaignId: string, status: 'active' | 'disabled'): Promise<StreakCampaign> {
    return firstValueFrom(
      this.api.tenantScope.patch<StreakCampaign>(`programs/${programId}/streak-campaigns/${campaignId}/status`, { status }),
    );
  }

  delete(programId: string, campaignId: string): Promise<void> {
    return firstValueFrom(this.api.tenantScope.delete<void>(`programs/${programId}/streak-campaigns/${campaignId}`));
  }
}
