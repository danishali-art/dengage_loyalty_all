import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CardBucket, CreateCardBucketRequest, UpdateCardBucketRequest } from '../model/types';

/** `/tenants/{tenantId}/programs/{programId}/card-buckets` (CardBucketsController.cs). */
export function listCardBuckets(
  tenantId: string,
  programId: string,
  page: number,
  pageSize: number,
): Promise<Page<CardBucket>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets', {
      params: { path: { tenantId, programId }, query: { page, pageSize } },
    }),
  );
}

export function getCardBucket(tenantId: string, programId: string, bucketId: string): Promise<CardBucket> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}', {
      params: { path: { tenantId, programId, bucketId } },
    }),
  );
}

export function createCardBucket(
  tenantId: string,
  programId: string,
  body: CreateCardBucketRequest,
): Promise<CardBucket> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

/** PATCH semantics: a null field keeps the bucket's current value (CardBucketsAppService.UpdateAsync). */
export function updateCardBucket(
  tenantId: string,
  programId: string,
  bucketId: string,
  body: UpdateCardBucketRequest,
): Promise<CardBucket> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, bucketId } },
      body,
    }),
  );
}

export function setCardBucketStatus(
  tenantId: string,
  programId: string,
  bucketId: string,
  status: 'active' | 'disabled',
): Promise<CardBucket> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}/status', {
      params: { path: { tenantId, programId, bucketId } },
      body: { status },
    }),
  );
}

/** A soft delete (status `deleted`), like rules. */
export function deleteCardBucket(tenantId: string, programId: string, bucketId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}', {
      params: { path: { tenantId, programId, bucketId } },
    }),
  );
}
