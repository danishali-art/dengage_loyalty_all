/** The platform tenants list, with the tenant entity's request functions as the only seam. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useSessionStore } from '@/shared/model';
import * as tenantApi from '@/entities/tenant/api/tenant.api';
import type { Tenant } from '@/entities/tenant';
import TenantsPage from './TenantsPage.vue';

vi.mock('@/entities/tenant/api/tenant.api');

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

function tenant(overrides: Partial<Tenant> = {}): Tenant {
  return {
    id: 'novapay',
    name: 'NovaPay',
    status: 'active',
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function setInput(selector: string, value: string): void {
  const input = document.querySelector<HTMLInputElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function clickButton(text: string): void {
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!.click();
}

describe('TenantsPage', () => {
  beforeEach(() => {
    vi.mocked(tenantApi.searchTenants).mockResolvedValue({
      data: [tenant(), tenant({ id: 'acme', name: 'Acme', status: 'suspended' })],
      page: 1,
      pageSize: 50,
      total: 2,
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(TenantsPage, { pinia, initialPath: '/platform/tenants' });
    useSessionStore(pinia).setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    await flushPromises();
    return mounted;
  }

  it('lists every tenant, 50 to a page, with its status', async () => {
    const { wrapper } = await mountPage();
    expect(tenantApi.searchTenants).toHaveBeenCalledWith(1, 50, '');
    const text = wrapper.text();
    expect(text).toContain('NovaPay');
    expect(text).toContain('acme');
    expect(text).toContain('suspended');
    expect(text).toContain('1–2 of 2');
  });

  it('opens a tenant when its row is clicked', async () => {
    const { wrapper, router } = await mountPage();
    await wrapper.findAll('tbody tr')[1]!.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/platform/tenants/acme'));
  });

  it('rejects an id that is not lowercase letters, digits or underscores', async () => {
    await mountPage();
    clickButton('New tenant');
    await flushPromises();
    setInput('#tenant-id', 'Nova-Pay');
    setInput('#tenant-name', 'NovaPay');
    await flushPromises();
    clickButton('Create tenant');
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain('1-50 lowercase letters, digits, or underscores.'),
    );
    expect(tenantApi.createTenant).not.toHaveBeenCalled();
  });

  it('creates a tenant, toasts and opens it', async () => {
    vi.mocked(tenantApi.createTenant).mockResolvedValue(tenant({ id: 'fresh', name: 'Fresh' }));
    const { router } = await mountPage();
    clickButton('New tenant');
    await flushPromises();
    setInput('#tenant-id', 'fresh');
    setInput('#tenant-name', 'Fresh');
    await flushPromises();
    clickButton('Create tenant');
    await vi.waitFor(() =>
      expect(tenantApi.createTenant).toHaveBeenCalledWith({ id: 'fresh', name: 'Fresh' }),
    );
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/platform/tenants/fresh'));
    await vi.waitFor(() => expect(document.body.textContent).toContain('Tenant "Fresh" created'));
  });

  it('shows a server conflict on the id field', async () => {
    const { apiErrorFrom } = await import('@/shared/api');
    vi.mocked(tenantApi.createTenant).mockRejectedValue(
      apiErrorFrom(409, {
        title: 'Conflict',
        code: 'tenant_exists',
        errors: { id: ['Tenant id is taken.'] },
      }),
    );
    await mountPage();
    clickButton('New tenant');
    await flushPromises();
    setInput('#tenant-id', 'novapay');
    setInput('#tenant-name', 'Dup');
    await flushPromises();
    clickButton('Create tenant');
    await vi.waitFor(() => expect(document.body.textContent).toContain('Tenant id is taken.'));
  });
});
