/** New card bucket page with only the entity api functions mocked: the request it sends, and its checks. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as accountTypeApi from '@/entities/account-type/api/account-type.api';
import * as programApi from '@/entities/program/api/program.api';
import * as cardBucketApi from '@/entities/card-bucket/api/card-bucket.api';
import type { AccountType } from '@/entities/account-type';
import type { CardBucket } from '@/entities/card-bucket';
import type { Program } from '@/entities/program';
import CardBucketEditPage from './CardBucketEditPage.vue';

vi.mock('@/entities/account-type/api/account-type.api');
vi.mock('@/entities/program/api/program.api');
vi.mock('@/entities/card-bucket/api/card-bucket.api');

const points = { id: 'pts', type: 'POINTS', name: 'NovaPoints', config: { decimals: 0 } } as AccountType;

function fill(id: string, value: string): void {
  const input = document.querySelector<HTMLInputElement>(id)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
const save = (): void =>
  [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Save')!.click();

describe('CardBucketEditPage (new)', () => {
  beforeEach(() => {
    vi.mocked(programApi.getProgram).mockResolvedValue({ id: 'p1', name: 'NovaPay' } as Program);
    vi.mocked(accountTypeApi.listAllAccountTypes).mockResolvedValue({
      data: [points],
      page: 1,
      pageSize: 100,
      total: 1,
    });
    vi.mocked(cardBucketApi.createCardBucket).mockResolvedValue({ id: 'b1' } as CardBucket);
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(CardBucketEditPage, { pinia, props: { programId: 'p1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it('creates a bucket with amounts as decimal strings and CSV fields as lists', async () => {
    await mountPage();
    fill('#name', 'Groceries');
    fill('#rewardAmount', '2.5');
    fill('#mccCodes', '5411, 5412');
    fill('#amountMin', '10');
    fill('#countries', 'SA');
    await flushPromises();
    save();
    await vi.waitFor(() =>
      expect(cardBucketApi.createCardBucket).toHaveBeenCalledWith('novapay', 'p1', {
        name: 'Groceries',
        mccCodes: ['5411', '5412'],
        amountMin: '10',
        amountMax: null,
        countryMode: 'in',
        countries: ['SA'],
        requireCaptured: true,
        hourFrom: null,
        hourTo: null,
        daysOfWeek: null,
        perCustomerPerDay: null,
        perCustomerTotal: null,
        targetAccountTypeId: 'pts',
        rewardAmount: '2.5',
        priority: 100,
        activeFrom: null,
        activeTo: null,
        additionalConditions: null,
      }),
    );
  });

  it('blocks an amount max below the min, and an hour past 23 (CardBucketsValidators)', async () => {
    await mountPage();
    fill('#name', 'Fuel');
    fill('#amountMin', '100');
    fill('#amountMax', '50');
    fill('#hourFrom', '24');
    await flushPromises();
    save();
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain('Amount max must be greater than or equal to amount min.'),
    );
    expect(document.body.textContent).toContain('Enter a whole hour from 0 to 23.');
    expect(cardBucketApi.createCardBucket).not.toHaveBeenCalled();
  });
});
