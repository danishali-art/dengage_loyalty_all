/** A tenant's API keys: list, create (raw key shown once) and revoke with confirmation. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useSessionStore } from '@/shared/model';
import * as tenantApi from '@/entities/tenant/api/tenant.api';
import * as apiKeyApi from '@/entities/api-key/api/api-key.api';
import ApiKeysPage from './ApiKeysPage.vue';

vi.mock('@/entities/tenant/api/tenant.api');
vi.mock('@/entities/api-key/api/api-key.api');

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

function buttons(scope: ParentNode = document): HTMLButtonElement[] {
  return [...scope.querySelectorAll<HTMLButtonElement>('button')];
}

describe('ApiKeysPage', () => {
  beforeEach(() => {
    vi.mocked(tenantApi.getTenant).mockResolvedValue({
      id: 'novapay',
      name: 'NovaPay',
      status: 'active',
      createdAt: '2026-01-01T00:00:00Z',
    });
    vi.mocked(apiKeyApi.listApiKeys).mockResolvedValue({
      data: [
        { id: 'k1', prefix: 'lk_live1', createdAt: '2026-02-01T00:00:00Z' },
        {
          id: 'k2',
          prefix: 'lk_old22',
          createdAt: '2026-01-01T00:00:00Z',
          lastUsedAt: '2026-01-15T00:00:00Z',
          revokedAt: '2026-01-20T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 15,
      total: 2,
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(ApiKeysPage, {
      pinia,
      initialPath: '/platform/tenants/novapay/api-keys',
      props: { tenantId: 'novapay' },
    });
    useSessionStore(pinia).setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    await flushPromises();
    return mounted;
  }

  it('lists keys 15 to a page; a revoked key has no revoke button', async () => {
    const { wrapper } = await mountPage();
    expect(apiKeyApi.listApiKeys).toHaveBeenCalledWith('novapay', 1, 15);
    const rows = wrapper.findAll('tbody tr');
    expect(rows[0]!.text()).toContain('lk_live1…');
    expect(rows[0]!.text()).toContain('Never used');
    expect(rows[0]!.text()).toContain('Revoke key');
    expect(rows[1]!.text()).toContain('revoked');
    expect(rows[1]!.text()).not.toContain('Revoke key');
    // Crumbs: Tenants › NovaPay › API Keys.
    expect(wrapper.text()).toContain('NovaPay');
  });

  it('shows a new key once, and forgets it when the dialog closes', async () => {
    vi.mocked(apiKeyApi.createApiKey).mockResolvedValue({
      id: 'k3',
      prefix: 'lk_new33',
      rawKey: 'lk_new33_secretvalue',
      createdAt: '2026-03-01T00:00:00Z',
    });
    await mountPage();
    buttons()
      .find((b) => b.textContent?.trim() === 'New key')!
      .click();
    await vi.waitFor(() =>
      expect(document.querySelector('[data-raw-key]')?.textContent).toBe('lk_new33_secretvalue'),
    );
    expect(document.body.textContent).toContain('Copy this key now — it will not be shown again.');
    expect(document.body.textContent).toContain('Prefix lk_new33');

    buttons(document.querySelector('[role="dialog"]')!)
      .find((b) => b.textContent?.trim() === 'Done')!
      .click();
    await vi.waitFor(() => expect(document.querySelector('[data-raw-key]')).toBeNull());
    expect(apiKeyApi.listApiKeys).toHaveBeenCalledTimes(2);
  });

  it('revokes a key only after confirmation', async () => {
    vi.mocked(apiKeyApi.revokeApiKey).mockResolvedValue(undefined);
    const { wrapper } = await mountPage();
    await wrapper.findAll('tbody tr')[0]!.find('button').trigger('click');
    await vi.waitFor(() =>
      expect(document.querySelector('[role="alertdialog"]')?.textContent).toContain(
        'Revoke key lk_live1…? Any client still using it will start getting 401s immediately.',
      ),
    );
    expect(apiKeyApi.revokeApiKey).not.toHaveBeenCalled();
    buttons(document.querySelector('[role="alertdialog"]')!)
      .find((b) => b.textContent?.trim() === 'Revoke')!
      .click();
    await vi.waitFor(() => expect(apiKeyApi.revokeApiKey).toHaveBeenCalledWith('novapay', 'k1'));
    await vi.waitFor(() => expect(document.body.textContent).toContain('API key revoked'));
  });
});
