/** One tenant's overview: status strip, links and suspend / reactivate. */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useSessionStore } from '@/shared/model';
import * as tenantApi from '@/entities/tenant/api/tenant.api';
import type { Tenant } from '@/entities/tenant';
import TenantOverviewPage from './TenantOverviewPage.vue';

vi.mock('@/entities/tenant/api/tenant.api');

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

const NOVAPAY: Tenant = {
  id: 'novapay',
  name: 'NovaPay',
  status: 'active',
  createdAt: '2026-01-01T00:00:00Z',
};

function clickButton(text: string): void {
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!.click();
}

describe('TenantOverviewPage', () => {
  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage(tenant: Tenant = NOVAPAY) {
    vi.mocked(tenantApi.getTenant).mockResolvedValue(tenant);
    const pinia = createPinia();
    const mounted = await mountWithProviders(TenantOverviewPage, {
      pinia,
      initialPath: `/platform/tenants/${tenant.id}`,
      props: { tenantId: tenant.id },
    });
    useSessionStore(pinia).setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    await flushPromises();
    return mounted;
  }

  it('shows the tenant and links to its API keys and admins', async () => {
    const { wrapper } = await mountPage();
    expect(tenantApi.getTenant).toHaveBeenCalledWith('novapay');
    expect(wrapper.find('h1').text()).toBe('NovaPay');
    const links = wrapper.findAll('a').map((a) => a.attributes('href'));
    expect(links).toContain('/platform/tenants/novapay/api-keys');
    expect(links).toContain('/platform/tenants/novapay/admin-users');
    expect(links).toContain('/platform/tenants');
  });

  it('suspends an active tenant', async () => {
    vi.mocked(tenantApi.updateTenant).mockResolvedValue({ ...NOVAPAY, status: 'suspended' });
    const { wrapper } = await mountPage();
    clickButton('Suspend');
    await vi.waitFor(() =>
      expect(tenantApi.updateTenant).toHaveBeenCalledWith('novapay', { status: 'suspended' }),
    );
    await vi.waitFor(() => expect(document.body.textContent).toContain('Tenant suspended'));
    await vi.waitFor(() => expect(wrapper.text()).toContain('Reactivate'));
  });

  it('reactivates a suspended tenant', async () => {
    vi.mocked(tenantApi.updateTenant).mockResolvedValue(NOVAPAY);
    await mountPage({ ...NOVAPAY, status: 'suspended' });
    clickButton('Reactivate');
    await vi.waitFor(() =>
      expect(tenantApi.updateTenant).toHaveBeenCalledWith('novapay', { status: 'active' }),
    );
    await vi.waitFor(() => expect(document.body.textContent).toContain('Tenant reactivated'));
  });
});
