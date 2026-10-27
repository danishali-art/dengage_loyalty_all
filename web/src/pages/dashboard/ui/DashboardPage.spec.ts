/** Dashboard with only the entity api functions mocked. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as dashboardApi from '@/entities/dashboard/api/dashboard.api';
import * as programApi from '@/entities/program/api/program.api';
import type { Program } from '@/entities/program';
import DashboardPage from './DashboardPage.vue';

vi.mock('@/entities/dashboard/api/dashboard.api');
vi.mock('@/entities/program/api/program.api');

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.mocked(dashboardApi.getDashboardSummary).mockResolvedValue({
      programCount: 3,
      tierCount: 5,
      customerAccountCount: 120,
      totalBalance: '15250.5000',
      redemptionCount: 7,
      streakActiveCount: 4,
      streakCompletedInRange: 2,
    });
    vi.mocked(programApi.listPrograms).mockResolvedValue({
      data: [{ id: 'p1', name: 'NovaPay Rewards' } as Program],
      page: 1,
      pageSize: 100,
      total: 1,
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(DashboardPage, { pinia });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('shows the tenant-wide tiles, the balance only formatted for display', async () => {
    const { wrapper } = await mountPage();
    await vi.waitFor(() => expect(wrapper.text()).toContain('Customer accounts'));
    expect(wrapper.text()).toContain('120');
    expect(wrapper.text()).toMatch(/15[,.\s]?250[.,]5/);
    expect(dashboardApi.getDashboardSummary).toHaveBeenCalledWith('novapay', {
      programId: null,
      fromDate: null,
      toDate: null,
    });
  });

  it('refetches for a program and a date range', async () => {
    const { wrapper } = await mountPage();
    document.querySelector<HTMLElement>('#filter-program')!.click();
    await flushPromises();
    [...document.querySelectorAll<HTMLElement>('[role="option"]')]
      .find((o) => o.textContent?.trim() === 'NovaPay Rewards')!
      .click();
    await wrapper.find('#filter-from').setValue('2026-10-01');
    await vi.waitFor(() =>
      expect(dashboardApi.getDashboardSummary).toHaveBeenLastCalledWith('novapay', {
        programId: 'p1',
        fromDate: '2026-10-01',
        toDate: null,
      }),
    );
  });
});
