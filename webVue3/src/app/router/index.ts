import { createRouter, createWebHistory } from 'vue-router';
import { translate } from '@/shared/i18n';
import { routes } from './routes';

declare module 'vue-router' {
  interface RouteMeta {
    /** i18n key of the browser tab title. */
    titleKey?: string;
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
});

router.afterEach((to) => {
  const title = to.meta.titleKey ? translate(to.meta.titleKey) : null;
  document.title = title ? `${title} · ${translate('app.title')}` : translate('app.title');
});
