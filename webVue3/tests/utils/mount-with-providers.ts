/**
 * Mounts a component with the app's real plugins (Pinia, vue-i18n, Vue Query, a memory router)
 * plus the toast and confirm hosts, for component tests. Lives outside src/ so it isn't part of any FSD layer.
 */
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query';
import { defineComponent, h, type Component } from 'vue';
import { i18n } from '@/shared/i18n';
import { AppConfirmHost, AppToastHost } from '@/shared/ui';

export interface Mounted {
  wrapper: VueWrapper;
  router: Router;
  pinia: Pinia;
}

export async function mountWithProviders(
  component: Component,
  options: {
    routes?: RouteRecordRaw[];
    initialPath?: string;
    props?: Record<string, unknown>;
    pinia?: Pinia;
  } = {},
): Promise<Mounted> {
  const pinia = options.pinia ?? createPinia();
  setActivePinia(pinia);
  const routes = options.routes ?? [{ path: '/:pathMatch(.*)*', component: { render: () => null } }];
  const router = createRouter({ history: createMemoryHistory(), routes });
  await router.push(options.initialPath ?? '/');
  await router.isReady();

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const Host = defineComponent({
    render: () => [h(component, options.props ?? {}), h(AppToastHost), h(AppConfirmHost)],
  });
  const wrapper = mount(Host, {
    attachTo: document.body,
    global: {
      plugins: [pinia, i18n, router, [VueQueryPlugin, { queryClient }]],
    },
  });
  await flushPromises();
  return { wrapper, router, pinia };
}
