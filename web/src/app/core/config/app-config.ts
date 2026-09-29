import { EnvironmentProviders, InjectionToken, Provider, inject, provideAppInitializer } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** Deploy-time configuration, served from `public/app-config.json`. One build, many environments. */
export interface AppConfig {
  /** Base path for every API call, e.g. `/api/v1`. */
  readonly apiBaseUrl: string;
  /** Selects the `AuthService` implementation at startup. */
  readonly authMode: 'stub' | 'http';
  readonly defaultLocale: string;
  readonly platformTenantPageSize: number;
  readonly adminListPageSize: number;
  readonly features: Readonly<Record<FeatureFlag, boolean>>;
}

export type FeatureFlag =
  | 'reports'
  | 'settings'
  | 'eventSimulator'
  | 'eventInbox'
  | 'outboxBrowser'
  | 'streakInspector'
  | 'jobStatus'
  | 'darkMode';

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

/**
 * Resolves with the final config once `app-config.json` has been fetched (or has definitively
 * failed, in which case it resolves with the fallback). Any other app initializer that needs
 * config-derived state (i18n locale, auth mode, ...) MUST `inject(APP_CONFIG_READY)`
 * synchronously and `await` the result directly — Angular caches a factory provider's return
 * value on its first injection, so injecting `APP_CONFIG` itself while the fetch below is
 * still in flight would permanently pin every future injection to the pre-fetch fallback, not
 * just the first one. `inject()` also cannot be called again after an `await`, so consumers
 * get the config from the resolved promise value, not a follow-up `inject(APP_CONFIG)` call.
 * This token is a plain `useValue` (the promise object itself, created synchronously before
 * any injector exists), so — unlike `APP_CONFIG` — its own value can never be cached
 * prematurely.
 */
export const APP_CONFIG_READY = new InjectionToken<Promise<AppConfig>>('APP_CONFIG_READY');

const FALLBACK: AppConfig = {
  apiBaseUrl: '/api/v1',
  authMode: 'stub',
  defaultLocale: 'en',
  platformTenantPageSize: 50,
  adminListPageSize: 25,
  features: {
    reports: false,
    settings: false,
    eventSimulator: false,
    eventInbox: false,
    outboxBrowser: false,
    streakInspector: false,
    jobStatus: false,
    darkMode: false,
  },
};

/**
 * Loads `app-config.json` before the app bootstraps and exposes it as `APP_CONFIG`.
 * A mutable holder is filled by the initializer, then read by the token factory.
 */
export function provideAppConfig(): (Provider | EnvironmentProviders)[] {
  const holder: { value: AppConfig } = { value: FALLBACK };
  let markReady!: (config: AppConfig) => void;
  const ready = new Promise<AppConfig>((resolve) => {
    markReady = resolve;
  });

  return [
    provideAppInitializer(async () => {
      const http = inject(HttpClient);
      try {
        const loaded = await firstValueFrom(
          http.get<Partial<AppConfig>>('app-config.json', { headers: { 'Cache-Control': 'no-cache' } }),
        );
        holder.value = { ...FALLBACK, ...loaded, features: { ...FALLBACK.features, ...loaded.features } };
      } catch {
        // Keep the fallback — the app still boots, and a missing file is loud in the network tab.
      } finally {
        markReady(holder.value);
      }
    }),
    { provide: APP_CONFIG, useFactory: () => holder.value },
    { provide: APP_CONFIG_READY, useValue: ready },
  ];
}
