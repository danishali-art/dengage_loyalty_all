import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { useTenantContextStore } from '@/shared/model';
import type { CreateRuleRequest, Rule, UpdateRuleRequest } from '../model/types';
import {
  approveRule,
  createRule,
  deleteRule,
  getRule,
  getRulesMetadata,
  listAllRules,
  setRuleStatus,
  updateRule,
} from './rule.api';

export const ruleKeys = {
  all: (tenantId: string | null, programId: string | null) =>
    ['tenants', tenantId, 'programs', programId, 'rules'] as const,
  everything: (tenantId: string | null, programId: string | null) =>
    [...ruleKeys.all(tenantId, programId), 'all'] as const,
  detail: (tenantId: string | null, programId: string | null, ruleId: string | null) =>
    [...ruleKeys.all(tenantId, programId), 'detail', ruleId] as const,
  metadata: (tenantId: string | null, programId: string | null) =>
    [...ruleKeys.all(tenantId, programId), 'metadata'] as const,
};

/** Every rule of the program (the grouped list). */
export function useAllRulesQuery(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => ruleKeys.everything(tenant.activeTenantId, toValue(programId))),
    queryFn: () => listAllRules(tenant.require(), toValue(programId)),
    enabled: computed(() => tenant.hasTenant),
  });
}

/** One rule; disabled while `ruleId` is null (a new rule). */
export function useRuleQuery(programId: MaybeRefOrGetter<string>, ruleId: MaybeRefOrGetter<string | null>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => ruleKeys.detail(tenant.activeTenantId, toValue(programId), toValue(ruleId))),
    queryFn: () => getRule(tenant.require(), toValue(programId), toValue(ruleId) as string),
    enabled: computed(() => tenant.hasTenant && !!toValue(ruleId)),
  });
}

export function useRulesMetadataQuery(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  return useQuery({
    queryKey: computed(() => ruleKeys.metadata(tenant.activeTenantId, toValue(programId))),
    queryFn: () => getRulesMetadata(tenant.require(), toValue(programId)),
    enabled: computed(() => tenant.hasTenant),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** After any write: the server's copy wins for the detail, and the list refetches. */
function useCacheUpdate(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const queryClient = useQueryClient();
  return (rule?: Rule) => {
    if (rule)
      queryClient.setQueryData(ruleKeys.detail(tenant.activeTenantId, toValue(programId), rule.id), rule);
    void queryClient.invalidateQueries({
      queryKey: ruleKeys.everything(tenant.activeTenantId, toValue(programId)),
    });
  };
}

export function useCreateRuleMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (body: CreateRuleRequest) => createRule(tenant.require(), toValue(programId), body),
    onSuccess: update,
  });
}

export function useUpdateRuleMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; body: UpdateRuleRequest }) =>
      updateRule(tenant.require(), toValue(programId), args.id, args.body),
    onSuccess: update,
  });
}

export function useSetRuleStatusMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (args: { id: string; status: 'active' | 'disabled' }) =>
      setRuleStatus(tenant.require(), toValue(programId), args.id, args.status),
    onSuccess: update,
  });
}

export function useApproveRuleMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (ruleId: string) => approveRule(tenant.require(), toValue(programId), ruleId),
    onSuccess: update,
  });
}

export function useDeleteRuleMutation(programId: MaybeRefOrGetter<string>) {
  const tenant = useTenantContextStore();
  const update = useCacheUpdate(programId);
  return useMutation({
    mutationFn: (ruleId: string) => deleteRule(tenant.require(), toValue(programId), ruleId),
    onSuccess: () => update(),
  });
}
