/** Account types page with only the entity api functions mocked. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as programApi from '@/entities/program/api/program.api';
import type { AccountType } from '@/entities/account-type';
import type { Program } from '@/entities/program';
import AccountTypesPage from './AccountTypesPage.vue';

vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/program/api/program.api');

const points = {
  id: 'pts',
  type: 'POINTS',
  name: 'NovaPoints',
  config: { decimals: 0, transfer: { daily_limit: 5000 }, custom_flag: true },
  createdAt: '2026-01-01T00:00:00Z',
  isTierQualifying: true,
} as AccountType;

describe('AccountTypesPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({ id: 'p1', name: 'NovaPay' } as Program);
    const page = { data: [points], page: 1, pageSize: 12, total: 1 };
    vi.mocked(accountTypeApi.listAccountTypes).mockResolvedValue(page);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({ ...page, pageSize: 100 });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(AccountTypesPage, { pinia, props: { programId: 'p1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('lists wallets with their type and tier-qualifying badge', async () => {
    const { wrapper } = await mountPage();
    expect(wrapper.text()).toContain('NovaPoints');
    expect(wrapper.text()).toContain('POINTS');
    expect(wrapper.text()).toContain('Tier qualifying');
  });

  it('keeps settings the form does not show when a wallet is renamed (regression)', async () => {
    vi.mocked(accountTypeApi.updateAccountType).mockResolvedValue(points);
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Edit')!
      .trigger('click');
    await flushPromises();
    const name = document.querySelector<HTMLInputElement>('#accountTypeName')!;
    name.value = 'NovaPoints Plus';
    name.dispatchEvent(new Event('input'));
    await flushPromises();
    [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Save')!.click();
    await vi.waitFor(() =>
      expect(accountTypeApi.updateAccountType).toHaveBeenCalledWith('novapay', 'p1', 'pts', {
        name: 'NovaPoints Plus',
        config: { custom_flag: true, decimals: 0, transfer: { daily_limit: 5000 } },
        isTierQualifying: true,
      }),
    );
  });
});
