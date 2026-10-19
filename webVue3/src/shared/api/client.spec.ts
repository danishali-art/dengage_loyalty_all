import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from './api-error';
import { apiClient, configureApi, unwrap, withFlags, type ApiHooks } from './client';
import { isLoading } from '../lib/loading/loading';

/** The middleware that replaces the Angular interceptors (auth, tenant, correlation, errors, loading). */
describe('API client middleware', () => {
  let onUnauthorized: ReturnType<typeof vi.fn<() => void>>;
  let onError: ReturnType<typeof vi.fn<(error: ApiError) => void>>;
  let hooks: ApiHooks;
  let fetchMock: ReturnType<typeof vi.fn>;

  function respond(status: number, body: unknown): void {
    fetchMock.mockResolvedValueOnce(
      new Response(body === null ? null : JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
  }

  function sentRequest(): Request {
    return fetchMock.mock.calls[0]![0] as Request;
  }

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    onUnauthorized = vi.fn<() => void>();
    onError = vi.fn<(error: ApiError) => void>();
    hooks = { getToken: () => 'tok', getTenantId: () => 'novapay', onUnauthorized, onError };
    // Node's Request needs an absolute URL; the browser resolves '/api/v1' against the page.
    configureApi({ apiBaseUrl: 'http://localhost/api/v1', hooks });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('adds the bearer token, tenant header and a correlation id', async () => {
    respond(200, { data: [], page: 1, pageSize: 25, total: 0 });
    await unwrap(
      apiClient().GET('/api/v1/tenants/{tenantId}/programs', { params: { path: { tenantId: 'novapay' } } }),
    );
    const req = sentRequest();
    expect(new URL(req.url).pathname).toBe('/api/v1/tenants/novapay/programs');
    expect(req.headers.get('Authorization')).toBe('Bearer tok');
    expect(req.headers.get('X-Tenant-Id')).toBe('novapay');
    expect(req.headers.get('X-Correlation-Id')).toBeTruthy();
  });

  it('never sends the token with the login call', async () => {
    respond(200, { token: 't', tokenType: 'Bearer', expiresInSeconds: 1 });
    await unwrap(apiClient().POST('/api/v1/auth/login', { body: { email: 'a@b.c', password: 'x' } }));
    expect(sentRequest().headers.get('Authorization')).toBeNull();
  });

  it('throws an ApiError and toasts it, unless the caller opted out', async () => {
    respond(409, { title: 'Slug locked', code: 'slug_locked' });
    const call = unwrap(
      apiClient().GET('/api/v1/tenants/{tenantId}/programs', { params: { path: { tenantId: 'x' } } }),
    );
    await expect(call).rejects.toBeInstanceOf(ApiError);
    expect(onError).toHaveBeenCalledTimes(1);

    respond(400, { title: 'Invalid', errors: { name: ['Required'] } });
    const quiet = unwrap(
      apiClient().POST('/api/v1/tenants/{tenantId}/programs', {
        ...withFlags({ skipErrorToast: true }),
        params: { path: { tenantId: 'x' } },
        body: { name: '' },
      }),
    );
    await expect(quiet).rejects.toMatchObject({ kind: 'validation', fieldErrors: { name: ['Required'] } });
    expect(onError).toHaveBeenCalledTimes(1);
    // The internal flags header never leaves the browser.
    expect((fetchMock.mock.calls[1]![0] as Request).headers.get('X-Client-Flags')).toBeNull();
  });

  it('ends the session on a 401 without a toast', async () => {
    respond(401, { title: 'Authentication is required.' });
    await expect(
      unwrap(apiClient().GET('/api/v1/tenants/{tenantId}/programs', { params: { path: { tenantId: 'x' } } })),
    ).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(onError).not.toHaveBeenCalled();
  });

  it('turns a network failure into a network ApiError and always clears the loading count', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await expect(
      unwrap(apiClient().GET('/api/v1/tenants/{tenantId}/programs', { params: { path: { tenantId: 'x' } } })),
    ).rejects.toMatchObject({ kind: 'network' });
    expect(isLoading.value).toBe(false);
  });
});
