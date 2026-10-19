import { type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify } from '@/shared/ui';
import { useUpdateTenantMutation, type Tenant } from '@/entities/tenant';

/** Suspend / reactivate a tenant (port of tenant-overview.page.ts). Failures are toasted by the API client. */
export function useTenantStatus(tenantId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const mutation = useUpdateTenantMutation(tenantId);

  async function toggle(tenant: Tenant): Promise<void> {
    if (mutation.isPending.value) return;
    try {
      const updated = await mutation.mutateAsync({
        status: tenant.status === 'active' ? 'suspended' : 'active',
      });
      notify.success(
        t(updated.status === 'active' ? 'platform.overview.reactivated' : 'platform.overview.suspended'),
      );
    } catch {
      // Already toasted by the API client.
    }
  }

  return { toggle, pending: mutation.isPending };
}
