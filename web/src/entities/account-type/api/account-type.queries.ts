import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type { CreateAccountTypeRequest, UpdateAccountTypeRequest } from '../model/types';
import {
  createAccountType,
  listAccountTypes,
  listAllAccountTypes,
  updateAccountType,
} from './account-type.api';

export const accountTypeKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'account-types'] as const,
  page: (tenantId: string | null, programId: string | null, page: number, pageSize: number) =>
    [...accountTypeKeys.all(tenantId, programId), 'page', { page, pageSize }] as const,
  everything: (tenantId: string | null, programId: string | null) =>
    [...accountTypeKeys.all(tenantId, programId), 'all'] as const,
};

/** Every account type of a program (selects, the overview's tier-qualifying wallet). */
export function useAccountTypesQuery(programId: MaybeRefOrGetter<string | null>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => accountTypeKeys.everything(tenant.activeTenantId, toValue(programId))),
    queryFn: async () => (await listAllAccountTypes(tenant.require(), toValue(programId) as string)).data,
    enabled: computed(() => tenant.hasTenant && !!toValue(programId)),
  });
}

/** One page of account types (the list screen). */
export function useAccountTypesPageQuery(
  programId: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  pageSize: number,
) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() =>
      accountTypeKeys.page(tenant.activeTenantId, toValue(programId), toValue(page), pageSize),
    ),
    queryFn: () => listAccountTypes(tenant.require(), toValue(programId), toValue(page), pageSize),
    enabled: computed(() => tenant.hasTenant),
    placeholderData: keepPreviousData,
  });
}

/** Any write refreshes every account-type view of the program (lists, selects, the overview). */
function useInvalidate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({
      queryKey: accountTypeKeys.all(tenant.activeTenantId, toValue(programId)),
    });
}

export function useCreateAccountTypeMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (body: CreateAccountTypeRequest) =>
      createAccountType(tenant.require(), toValue(programId), body),
    onSuccess: invalidate,
  });
}

export function useUpdateAccountTypeMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const invalidate = useInvalidate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateAccountTypeRequest }) =>
      updateAccountType(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: invalidate,
  });
}
