import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { readStored, writeStored } from './storage';
import { useSessionStore } from './session.store';

const TENANT_KEY = 'loyalty.portal.tenant';

/**
 * Client state: which tenant the admin is acting as. Every tenant-scoped
 * query key includes this id, so switching never shows another tenant's cached data
 * (guardrails §2, Tenancy). The tenant *list* is server data — `entities/tenant` queries.
 */
export const useTenantContextStore = defineStore('tenantContext', () => {
  const activeTenantId = ref<string | null>(readStored(TENANT_KEY));
  const hasTenant = computed(() => activeTenantId.value !== null);

  function set(tenantId: string | null): void {
    activeTenantId.value = tenantId;
    writeStored(TENANT_KEY, tenantId);
  }

  /** A tenant_admin's tenant is fixed by the JWT `tenant_id` claim. */
  function adoptSessionTenant(): void {
    const claimTenant = useSessionStore().principal?.tenantId ?? null;
    if (claimTenant && activeTenantId.value !== claimTenant) set(claimTenant);
  }

  /** Throws if read while unset — callers are always behind the tenant guard. */
  function require(): string {
    if (!activeTenantId.value)
      throw new Error('No active tenant. A tenant-scoped API call was made too early.');
    return activeTenantId.value;
  }

  return { activeTenantId, hasTenant, set, adoptSessionTenant, require };
});
