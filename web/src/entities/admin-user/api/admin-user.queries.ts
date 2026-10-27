import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useSessionStore } from '@/shared/model';
import type { CreateAdminUserRequest } from '../model/types';
import { createAdminUser, listAdminUsers } from './admin-user.api';

export const adminUserKeys = {
  all: (tenantId: string) => ['platform', 'tenants', tenantId, 'admin-users'] as const,
  page: (tenantId: string, page: number, pageSize: number) =>
    [...adminUserKeys.all(tenantId), { page, pageSize }] as const,
};

export function useAdminUsersPageQuery(
  tenantId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const session = useSessionStore();
  return useQuery({
    queryKey: computed(() => adminUserKeys.page(toValue(tenantId), toValue(page), pageSize)),
    queryFn: () => listAdminUsers(toValue(tenantId), toValue(page), pageSize),
    enabled: computed(() => session.isPlatformAdmin),
    placeholderData: keepPreviousData,
  });
}

export function useCreateAdminUserMutation(tenantId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAdminUserRequest) => createAdminUser(toValue(tenantId), body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: adminUserKeys.all(toValue(tenantId)) }),
  });
}
