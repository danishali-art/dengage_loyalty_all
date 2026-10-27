import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useSessionStore } from '@/shared/model';
import { createApiKey, listApiKeys, revokeApiKey } from './api-key.api';

export const apiKeyKeys = {
  all: (tenantId: string) => ['platform', 'tenants', tenantId, 'api-keys'] as const,
  page: (tenantId: string, page: number, pageSize: number) =>
    [...apiKeyKeys.all(tenantId), { page, pageSize }] as const,
};

export function useApiKeysPageQuery(
  tenantId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const session = useSessionStore();
  return useQuery({
    queryKey: computed(() => apiKeyKeys.page(toValue(tenantId), toValue(page), pageSize)),
    queryFn: () => listApiKeys(toValue(tenantId), toValue(page), pageSize),
    enabled: computed(() => session.isPlatformAdmin),
    placeholderData: keepPreviousData,
  });
}

function useInvalidate(tenantId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: apiKeyKeys.all(toValue(tenantId)) });
}

export function useCreateApiKeyMutation(tenantId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidate(tenantId);
  return useMutation({ mutationFn: () => createApiKey(toValue(tenantId)), onSuccess: invalidate });
}

export function useRevokeApiKeyMutation(tenantId: MaybeRefOrGetter<string>) {
  const invalidate = useInvalidate(tenantId);
  return useMutation({
    mutationFn: (keyId: string) => revokeApiKey(toValue(tenantId), keyId),
    onSuccess: invalidate,
  });
}
