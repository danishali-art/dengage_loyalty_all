import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import { CreateRewardRequest, Reward, UpdateRewardRequest } from './reward.model';

const ALL_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class RewardsService {
  private readonly api = inject(ApiClient);

  list(programId: string, page = 1, pageSize = 20): Promise<Page<Reward>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<Reward>(`programs/${programId}/rewards`, { page, pageSize }),
    );
  }

  /** Every reward in the program — used to populate the streak "reward definition" select. */
  async listAll(programId: string): Promise<readonly Reward[]> {
    return (await this.list(programId, 1, ALL_PAGE_SIZE)).data;
  }

  create(programId: string, request: CreateRewardRequest): Promise<Reward> {
    return firstValueFrom(
      this.api.tenantScope.post<Reward>(`programs/${programId}/rewards`, request, {
        skipErrorToast: true,
      }),
    );
  }

  update(programId: string, rewardId: string, request: UpdateRewardRequest): Promise<Reward> {
    return firstValueFrom(
      this.api.tenantScope.patch<Reward>(`programs/${programId}/rewards/${rewardId}`, request, {
        skipErrorToast: true,
      }),
    );
  }

  setActive(programId: string, rewardId: string, isActive: boolean): Promise<Reward> {
    return firstValueFrom(
      this.api.tenantScope.patch<Reward>(`programs/${programId}/rewards/${rewardId}/active`, {
        isActive,
      }),
    );
  }

  /** CR 2026-09-30 (A4): a different admin than the creator — the API rejects self-approval. */
  approve(programId: string, rewardId: string): Promise<Reward> {
    return firstValueFrom(
      this.api.tenantScope.patch<Reward>(
        `programs/${programId}/rewards/${rewardId}/approve`,
        {},
        { skipErrorToast: true },
      ),
    );
  }

  delete(programId: string, rewardId: string): Promise<void> {
    return firstValueFrom(
      this.api.tenantScope.delete<void>(`programs/${programId}/rewards/${rewardId}`),
    );
  }
}
