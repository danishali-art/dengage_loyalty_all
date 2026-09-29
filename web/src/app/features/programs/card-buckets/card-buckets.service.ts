import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import { CardBucket, CardBucketListFilter, CreateCardBucketRequest, UpdateCardBucketRequest } from './card-bucket.model';

@Injectable({ providedIn: 'root' })
export class CardBucketsService {
  private readonly api = inject(ApiClient);

  list(programId: string, page = 1, pageSize = 20, filter: CardBucketListFilter = {}): Promise<Page<CardBucket>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<CardBucket>(`programs/${programId}/card-buckets`, {
        page,
        pageSize,
        ...(filter as Record<string, unknown>),
      }),
    );
  }

  get(programId: string, bucketId: string): Promise<CardBucket> {
    return firstValueFrom(this.api.tenantScope.get<CardBucket>(`programs/${programId}/card-buckets/${bucketId}`));
  }

  create(programId: string, request: CreateCardBucketRequest): Promise<CardBucket> {
    return firstValueFrom(
      this.api.tenantScope.post<CardBucket>(`programs/${programId}/card-buckets`, request, { skipErrorToast: true }),
    );
  }

  update(programId: string, bucketId: string, request: UpdateCardBucketRequest): Promise<CardBucket> {
    return firstValueFrom(
      this.api.tenantScope.patch<CardBucket>(`programs/${programId}/card-buckets/${bucketId}`, request, { skipErrorToast: true }),
    );
  }

  setStatus(programId: string, bucketId: string, status: 'active' | 'disabled'): Promise<CardBucket> {
    return firstValueFrom(
      this.api.tenantScope.patch<CardBucket>(`programs/${programId}/card-buckets/${bucketId}/status`, { status }),
    );
  }

  delete(programId: string, bucketId: string): Promise<void> {
    return firstValueFrom(this.api.tenantScope.delete<void>(`programs/${programId}/card-buckets/${bucketId}`));
  }
}
