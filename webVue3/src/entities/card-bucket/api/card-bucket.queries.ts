import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type { CardBucket, CreateCardBucketRequest, UpdateCardBucketRequest } from '../model/types';
import {
  createCardBucket,
  deleteCardBucket,
  getCardBucket,
  listCardBuckets,
  setCardBucketStatus,
  updateCardBucket,
} from './card-bucket.api';

export const cardBucketKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'card-buckets'] as const,
  page: (tenantId: string | null, programId: string | null, page: number, pageSize: number) =>
    [...cardBucketKeys.all(tenantId, programId), 'page', { page, pageSize }] as const,
  detail: (tenantId: string | null, programId: string | null, bucketId: string | null) =>
    [...cardBucketKeys.all(tenantId, programId), 'detail', bucketId] as const,
};

export function useCardBucketsPageQuery(
  programId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      cardBucketKeys.page(tenant.activeTenantId, toValue(programId), toValue(page), pageSize),
    ),
    queryFn: () => listCardBuckets(tenant.require(), toValue(programId), toValue(page), pageSize),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

/** One bucket; disabled while `bucketId` is null (a new bucket). */
export function useCardBucketQuery(
  programId: MaybeRefOrGetter<string>,
  bucketId: MaybeRefOrGetter<string | null>,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      cardBucketKeys.detail(tenant.activeTenantId, toValue(programId), toValue(bucketId)),
    ),
    queryFn: () => getCardBucket(tenant.require(), toValue(programId), toValue(bucketId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(bucketId)),
  });
}

/** After any write: the server's copy wins for the detail, and every list refetches. */
function useCacheUpdate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return (bucket?: CardBucket) => {
    if (bucket)
      queryClient.setQueryData(
        cardBucketKeys.detail(tenant.activeTenantId, toValue(programId), bucket.id),
        bucket,
      );
    void queryClient.invalidateQueries({
      queryKey: [...cardBucketKeys.all(tenant.activeTenantId, toValue(programId)), 'page'],
    });
  };
}

export function useCreateCardBucketMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (body: CreateCardBucketRequest) =>
      createCardBucket(tenant.require(), toValue(programId), body),
    onSuccess: update,
  });
}

export function useUpdateCardBucketMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateCardBucketRequest }) =>
      updateCardBucket(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: update,
  });
}

export function useSetCardBucketStatusMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; status: 'active' | 'disabled' }) =>
      setCardBucketStatus(tenant.require(), toValue(programId), args.id, args.status),
    onSuccess: update,
  });
}

export function useDeleteCardBucketMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (bucketId: string) => deleteCardBucket(tenant.require(), toValue(programId), bucketId),
    onSuccess: () => update(),
  });
}
