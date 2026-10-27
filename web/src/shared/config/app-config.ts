/**
 * Deploy-time configuration, served from `public/app-config.json` and loaded once before the app
 * mounts. One build, many environments.
 */
export type FeatureFlag =
  | 'reports'
  | 'settings'
  | 'eventSimulator'
  | 'eventInbox'
  | 'outboxBrowser'
  | 'streakInspector'
  | 'jobStatus'
  | 'darkMode';

export interface AppConfig {
  /** Base path for every API call, e.g. `/api/v1`. */
  readonly apiBaseUrl: string;
  /**
   * `http` signs in through `POST /auth/login`. `stub` (fake auto sign-in) isn't implemented: the
   * portal always uses `http`.
   */
  readonly authMode: 'stub' | 'http';
  readonly defaultLocale: string;
  readonly platformTenantPageSize: number;
  readonly adminListPageSize: number;
  readonly features: Readonly<Record<FeatureFlag, boolean>>;
}

const FALLBACK: AppConfig = {
  apiBaseUrl: '/api/v1',
  authMode: 'http',
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

let current: AppConfig = FALLBACK;

/** Fetches `app-config.json`; a missing or broken file keeps the fallback (loud in the network tab). */
export async function loadAppConfig(fetchImpl: typeof fetch = fetch): Promise<AppConfig> {
  try {
    const res = await fetchImpl('app-config.json', { headers: { 'Cache-Control': 'no-cache' } });
    if (res.ok) {
      const loaded = (await res.json()) as Partial<AppConfig>;
      current = { ...FALLBACK, ...loaded, features: { ...FALLBACK.features, ...loaded.features } };
    }
  } catch {
    // keep the fallback — the app still boots
  }
  return current;
}

export function appConfig(): AppConfig {
  return current;
}

export function isFeatureEnabled(flag: FeatureFlag): boolean {
  return current.features[flag] === true;
}
