import {
  EnvironmentInjector,
  EnvironmentProviders,
  ErrorHandler,
  Provider,
  inject,
  provideAppInitializer,
  runInInjectionContext,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { provideAppConfig, APP_CONFIG_READY } from './config/app-config';
import { provideAuth } from './auth/auth.providers';
import { httpInterceptors } from './http/interceptors';
import { GlobalErrorHandler } from './errors/global-error-handler';
import { SessionStore } from './auth/session.store';
import { TenantStore } from './tenant/tenant.store';

/**
 * Everything singleton the app needs, wired once at the root. Order:
 * config first (initializer), then HTTP + auth + i18n, then a session/tenant priming step.
 */
export function provideCore(): (Provider | EnvironmentProviders)[] {
  return [
    ...provideAppConfig(),
    provideHttpClient(withInterceptors(httpInterceptors)),
    ...provideAuth(),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },

    ...provideTranslateService({
      fallbackLang: 'en',
      lang: 'en',
      loader: provideTranslateHttpLoader({ prefix: 'i18n/', suffix: '.json' }),
    }),

    provideAppInitializer(async () => {
      // Every inject() call must happen synchronously, before the await below — inject() can't
      // be called once execution resumes in a later microtask (hence capturing `injector` to
      // reopen an injection context afterwards). Getting the config from the resolved
      // APP_CONFIG_READY value (not a follow-up inject(APP_CONFIG)) — and deferring
      // inject(TenantStore) until after the await, since TenantStore's own constructor reads
      // APP_CONFIG too — is what lets this safely run concurrently with the fetch in
      // provideAppConfig()'s own initializer; see APP_CONFIG_READY's doc comment for why
      // injecting APP_CONFIG (even transitively) early would permanently pin it to the
      // pre-fetch fallback value.
      const injector = inject(EnvironmentInjector);
      const translate = inject(TranslateService);
      const session = inject(SessionStore);
      const configReady = inject(APP_CONFIG_READY);

      const config = await configReady;

      translate.use(config.defaultLocale || 'en');

      // A tenant_admin's tenant is fixed by the JWT — adopt it now so guards pass on first nav.
      if (session.role() === 'tenant_admin') {
        runInInjectionContext(injector, () => inject(TenantStore).initFromSession());
      }
    }),
  ];
}
