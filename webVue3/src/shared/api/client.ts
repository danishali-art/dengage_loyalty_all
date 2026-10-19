/**
 * The ONLY place that talks HTTP (scope change 2026-10-12 §3.2; lint bans `fetch`/`openapi-fetch`
 * elsewhere). A typed `openapi-fetch` client over the generated `paths`, plus middleware that
 * ports the Angular portal's interceptors (web/src/app/core/http/interceptors):
 *
 * - correlation id       → `X-Correlation-Id` on every request
 * - auth                 → bearer token; a 401 (except on login) ends the session
 * - tenant header        → `X-Tenant-Id` for tracing (the URL already carries the tenant)
 * - error normalisation  → every failure is thrown as an `ApiError`, toasted unless opted out
 * - loading              → ref-counted for the progress bar unless opted out
 * - retry                → NOT here: Vue Query retries transient GET failures (shared/api/query-client)
 *
 * Session, tenant and toast live in higher slices, so they're injected through `configureApi`
 * by the app layer at start-up rather than imported.
 */
import createClient, { type Client, type Middleware } from 'openapi-fetch';
import type { paths } from './generated/schema';
import { ApiError, apiErrorFrom } from './api-error';
import { beginLoading, endLoading } from '../lib/loading/loading';

export interface ApiHooks {
  getToken(): string | null;
  getTenantId(): string | null;
  onUnauthorized(): void;
  onError(error: ApiError): void;
}

/** Per-request opt-outs (port of http-context.ts). */
export interface RequestFlags {
  /** The caller shows the error itself (form submits map field errors inline). */
  skipErrorToast?: boolean;
  /** Background request — no progress bar. */
  skipLoading?: boolean;
}

const FLAGS_HEADER = 'X-Client-Flags';

let hooks: ApiHooks = {
  getToken: () => null,
  getTenantId: () => null,
  onUnauthorized: () => undefined,
  onError: () => undefined,
};
let client: Client<paths> | null = null;
const flagsById = new Map<string, RequestFlags>();

function correlationId(): string {
  return (
    globalThis.crypto?.randomUUID?.() ??
    `cid-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
  );
}

async function readBody(response: Response): Promise<unknown> {
  try {
    return await response.clone().json();
  } catch {
    return null;
  }
}

const middleware: Middleware = {
  onRequest({ request, id }) {
    const raw = request.headers.get(FLAGS_HEADER);
    const flags: RequestFlags = raw ? (JSON.parse(raw) as RequestFlags) : {};
    flagsById.set(id, flags);
    request.headers.delete(FLAGS_HEADER);

    if (!request.headers.has('X-Correlation-Id')) request.headers.set('X-Correlation-Id', correlationId());
    const isLogin = request.url.includes('/auth/login');
    const token = hooks.getToken();
    if (token && !isLogin) request.headers.set('Authorization', `Bearer ${token}`);
    const tenantId = hooks.getTenantId();
    if (tenantId && !request.headers.has('X-Tenant-Id')) request.headers.set('X-Tenant-Id', tenantId);

    if (!flags.skipLoading) beginLoading();
    return request;
  },

  async onResponse({ request, response, id }) {
    const flags = flagsById.get(id) ?? {};
    flagsById.delete(id);
    if (!flags.skipLoading) endLoading();
    if (response.ok) return response;

    const error = apiErrorFrom(
      response.status,
      await readBody(response),
      response.headers.get('Retry-After'),
    );
    if (error.kind === 'unauthorized' && !request.url.includes('/auth/login')) {
      hooks.onUnauthorized();
    } else if (!flags.skipErrorToast) {
      // 401 is handled by the redirect; a toast on top is noise (same as the Angular portal).
      hooks.onError(error);
    }
    throw error;
  },

  onError({ error, id }) {
    const flags = flagsById.get(id) ?? {};
    flagsById.delete(id);
    if (!flags.skipLoading) endLoading();
    if (error instanceof ApiError) return error;
    const networkError = apiErrorFrom(0, null);
    if (!flags.skipErrorToast) hooks.onError(networkError);
    return networkError;
  },
};

/** Called once by the app layer before mount. */
export function configureApi(options: { apiBaseUrl: string; hooks: ApiHooks }): void {
  hooks = options.hooks;
  // The generated paths already start with `/api/v1`; `apiBaseUrl` (app-config.json) only adds an
  // origin or prefix in front of it when the API is hosted elsewhere.
  const baseUrl = options.apiBaseUrl.replace(/\/api\/v1\/?$/, '');
  client = createClient<paths>({ baseUrl });
  client.use(middleware);
}

/** The typed client. Use only from `entities/*` / `features/*` api segments. */
export function apiClient(): Client<paths> {
  if (!client) throw new Error('API client used before configureApi() — call it in app/main.ts');
  return client;
}

/** Spread into a request's options: `{ ...withFlags({ skipErrorToast: true }), body }`. */
export function withFlags(flags: RequestFlags): { headers: Record<string, string> } {
  return { headers: { [FLAGS_HEADER]: JSON.stringify(flags) } };
}

/**
 * The middleware throws on any non-2xx, so a settled call always has its data (or none, for 204).
 */
export async function unwrap<T>(call: Promise<{ data?: T; response: Response }>): Promise<T> {
  const { data } = await call;
  return data as T;
}
