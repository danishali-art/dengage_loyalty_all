import type { App } from 'vue';
import { createPinia } from 'pinia';
import { VueQueryPlugin } from '@tanstack/vue-query';
import { configureApi, createAppQueryClient } from '@/shared/api';
import { appConfig } from '@/shared/config/app-config';
import { i18n } from '@/shared/i18n';
import { useSessionStore, useTenantContextStore } from '@/shared/model';
import { notify } from '@/shared/ui';
import { router } from '../router';

/**
 * Wires every app-wide plugin and the API client's hooks. The API client (shared) can't import
 * the session or toasts directly, so they're handed in here.
 */
export function installProviders(app: App): void {
  const pinia = createPinia();
  const queryClient = createAppQueryClient();

  app.use(pinia);
  app.use(i18n);
  app.use(router);
  app.use(VueQueryPlugin, { queryClient });

  const session = useSessionStore(pinia);
  const tenant = useTenantContextStore(pinia);
  configureApi({
    apiBaseUrl: appConfig().apiBaseUrl,
    hooks: {
      getToken: () => session.token,
      getTenantId: () => tenant.activeTenantId,
      onUnauthorized: () => {
        // No refresh endpoint exists, so a 401 ends the session.
        session.clear();
        queryClient.clear();
        void router.push({ path: '/login', query: { returnUrl: router.currentRoute.value.fullPath } });
      },
      onError: (error) =>
        notify.error(error.message, { detail: error.code ?? undefined, traceId: error.traceId ?? undefined }),
    },
  });

  // A tenant_admin's tenant is fixed by the JWT — adopt it now so the guards pass on first navigation.
  if (session.role === 'tenant_admin') tenant.adoptSessionTenant();
}
