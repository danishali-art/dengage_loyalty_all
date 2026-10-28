import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { isAppLocale } from '@/shared/i18n';
import { useSessionStore, useTenantContextStore } from '@/shared/model';
import type { PopupMenuItem } from '@/shared/ui';
import { useTenantQuery } from '@/entities/tenant';
import { useLogout } from '@/features/auth-logout';
import { useLocaleSwitch } from '@/features/locale-switch';
import { useTenantPicker } from '@/features/tenant-switch';

/** Topbar state: the tenant switcher (platform_admin) and the user menu. */
export function useTopbar() {
  const { t } = useI18n();
  const session = useSessionStore();
  const tenantContext = useTenantContextStore();
  const { logout } = useLogout();
  const language = useLocaleSwitch();

  const pickerOpen = ref(false);
  // The tenant list is only fetched while the switcher dialog is open.
  const picker = useTenantPicker({ onSelected: () => (pickerOpen.value = false), active: pickerOpen });
  // A tenant_admin can't read /platform/tenants; their label is the id from the JWT.
  const tenant = useTenantQuery(() => tenantContext.activeTenantId);

  const tenantLabel = computed(
    () => tenant.data.value?.name ?? tenantContext.activeTenantId ?? t('tenant.none'),
  );
  const userId = computed(() => session.principal?.userId ?? '');
  const userInitial = computed(() => (userId.value[0] ?? '?').toUpperCase());
  const userMenu = computed<PopupMenuItem[]>(() => [
    { label: session.role ?? '', info: true },
    {
      label: t('locale.label'),
      choices: {
        options: language.options,
        current: language.locale.value,
        select: (value) => isAppLocale(value) && language.select(value),
      },
    },
    { label: t('auth.signOut'), action: () => void logout() },
  ]);

  return {
    canSwitchTenant: computed(() => session.isPlatformAdmin),
    tenantLabel,
    pickerOpen,
    picker,
    userId,
    userInitial,
    userMenu,
  };
}
