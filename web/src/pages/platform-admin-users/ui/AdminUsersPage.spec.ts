/** A tenant's admins: list, empty state and create (always as tenant_admin). */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useSessionStore } from '@/shared/model';
import * as tenantApi from '@/entities/tenant/api/tenant.api';
import * as adminUserApi from '@/entities/admin-user/api/admin-user.api';
import type { AdminUser } from '@/entities/admin-user';
import AdminUsersPage from './AdminUsersPage.vue';

vi.mock('@/entities/tenant/api/tenant.api');
vi.mock('@/entities/admin-user/api/admin-user.api');

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600, ...claims })}.sig`;
}

const ADMIN: AdminUser = {
  id: 'u1',
  email: 'ops@novapay.test',
  role: 'tenant_admin',
  tenantId: 'novapay',
  status: 'active',
  createdAt: '2026-01-01T00:00:00Z',
};

function setInput(selector: string, value: string): void {
  const input = document.querySelector<HTMLInputElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function clickButton(text: string): void {
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!.click();
}

describe('AdminUsersPage', () => {
  beforeEach(() => {
    vi.mocked(tenantApi.getTenant).mockResolvedValue({
      id: 'novapay',
      name: 'NovaPay',
      status: 'active',
      createdAt: '2026-01-01T00:00:00Z',
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage(admins: AdminUser[] = [ADMIN]) {
    vi.mocked(adminUserApi.listAdminUsers).mockResolvedValue({
      data: admins,
      page: 1,
      pageSize: 15,
      total: admins.length,
    });
    const pinia = createPinia();
    const mounted = await mountWithProviders(AdminUsersPage, {
      pinia,
      initialPath: '/platform/tenants/novapay/admin-users',
      props: { tenantId: 'novapay' },
    });
    useSessionStore(pinia).setToken(jwt({ sub: 'u', role: 'platform_admin', tenant_id: null }));
    await flushPromises();
    return mounted;
  }

  it('lists the admins 15 to a page', async () => {
    const { wrapper } = await mountPage();
    expect(adminUserApi.listAdminUsers).toHaveBeenCalledWith('novapay', 1, 15);
    expect(wrapper.find('tbody').text()).toContain('ops@novapay.test');
    expect(wrapper.find('tbody').text()).toContain('active');
  });

  it('shows the empty state when the tenant has no admins', async () => {
    const { wrapper } = await mountPage([]);
    expect(wrapper.text()).toContain('No tenant admins yet');
    expect(wrapper.text()).toContain('Create one so someone can sign in and manage this tenant.');
    expect(wrapper.find('table').exists()).toBe(false);
  });

  it('enforces the 12-character password minimum', async () => {
    await mountPage();
    clickButton('New admin');
    await flushPromises();
    expect(document.body.textContent).toContain('Grants sign-in access to novapay.');
    setInput('#admin-email', 'new@novapay.test');
    setInput('#admin-password', 'short');
    await flushPromises();
    clickButton('Create admin');
    await vi.waitFor(() => expect(document.body.textContent).toContain('At least 12 characters.'));
    expect(adminUserApi.createAdminUser).not.toHaveBeenCalled();
  });

  it('creates a tenant admin and toasts', async () => {
    vi.mocked(adminUserApi.createAdminUser).mockResolvedValue({
      ...ADMIN,
      id: 'u2',
      email: 'new@novapay.test',
    });
    await mountPage();
    clickButton('New admin');
    await flushPromises();
    setInput('#admin-email', 'new@novapay.test');
    setInput('#admin-password', 'a-long-temporary-pass');
    await flushPromises();
    clickButton('Create admin');
    await vi.waitFor(() =>
      expect(adminUserApi.createAdminUser).toHaveBeenCalledWith('novapay', {
        email: 'new@novapay.test',
        password: 'a-long-temporary-pass',
        role: 'tenant_admin',
      }),
    );
    await vi.waitFor(() => expect(document.body.textContent).toContain('Admin "new@novapay.test" created'));
  });
});
