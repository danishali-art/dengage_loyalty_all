/**
 * Customer 360 with only the entity api functions mocked: the event drawer and the P2 and P3
 * tabs.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import { apiErrorFrom } from '@/shared/api';
import type { DecimalString } from '@/shared/lib/money';
import * as customerApi from '@/entities/customer/api/customer.api';
import * as eventTypeApi from '@/entities/event-type/api/event-type.api';
import type {
  CustomerCardBucket,
  CustomerEventDetail,
  CustomerProfile,
  CustomerReward,
  CustomerRuleFire,
  RuleCapUsage,
  SentMessage,
} from '@/entities/customer';
import CustomerDetailPage from './CustomerDetailPage.vue';

vi.mock('@/entities/customer/api/customer.api');
vi.mock('@/entities/event-type/api/event-type.api');

const d = (v: string) => v as DecimalString;
const empty = { data: [], nextCursor: null };

const profile: CustomerProfile = {
  contactKey: 'c1',
  balances: [],
  tierProgress: null,
  summary: {
    firstSeenAt: '2025-05-06T10:00:00Z',
    lastActivityAt: '2026-10-02T10:00:00Z',
    failedEventsLast7Days: 2,
    activeStreakCount: 1,
  },
  programs: [
    {
      programId: 'p1',
      programName: 'Shop',
      wallets: [
        {
          accountTypeId: 'pts',
          name: 'Shop points',
          type: 'POINTS',
          balance: d('150'),
          pendingAmount: d('25'),
          expiringAmount: d('145'),
          expiresOn: '2026-10-22',
        },
        {
          accountTypeId: 'cash',
          name: 'Wallet',
          type: 'CASH',
          balance: d('15'),
          currency: 'SAR',
          pendingAmount: d('0'),
        },
      ],
      tier: {
        tierDisplayName: 'Gold',
        nextTierDisplayName: 'Platinum',
        nextTierMinPoints: d('25000'),
        qualifyingPoints: d('6000'),
        periodStart: '2026-10-01',
      },
      streaks: [
        {
          campaignId: 's1',
          campaignName: 'Weekly coffee',
          streakCount: 2,
          targetPeriods: 4,
          completions: 1,
          status: 'active',
        },
      ],
    },
    { programId: 'p3', programName: 'third one', wallets: [], tier: null, streaks: [] },
  ],
};

describe('CustomerDetailPage', () => {
  beforeEach(() => {
    vi.mocked(customerApi.getCustomerProfile).mockResolvedValue(profile);
    vi.mocked(customerApi.getCustomerLedger).mockResolvedValue(empty);
    vi.mocked(customerApi.getCustomerEvents).mockResolvedValue(empty);
    vi.mocked(customerApi.getCustomerRuleFires).mockResolvedValue(empty);
    vi.mocked(customerApi.getCustomerMessages).mockResolvedValue(empty);
    vi.mocked(customerApi.getCustomerCapUsage).mockResolvedValue([]);
    vi.mocked(customerApi.getCustomerRewards).mockResolvedValue([]);
    vi.mocked(customerApi.getCustomerCardBuckets).mockResolvedValue([]);
    vi.mocked(customerApi.getCustomerTierHistory).mockResolvedValue([]);
    vi.mocked(customerApi.getCustomerStreaks).mockResolvedValue([]);
    vi.mocked(eventTypeApi.getEventTypes).mockResolvedValue({ builtIn: [], generic: [], publishable: [] });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(CustomerDetailPage, { pinia, props: { contactKey: 'c1' } });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  async function openTab(label: string) {
    const tab = [...document.querySelectorAll<HTMLElement>('[role="tab"]')].find(
      (t) => t.textContent?.trim() === label,
    )!;
    tab.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await flushPromises();
  }

  const text = (): string => document.body.textContent ?? '';

  // ── Overview (P2) ──

  it('overview shows pending and expiring amounts only where there are any, and the profile panel', async () => {
    await mountPage();
    expect(text().match(/pending/gi)?.length ?? 0).toBeGreaterThan(0);
    expect(document.querySelectorAll('[role="progressbar"]').length).toBeGreaterThan(0);
    // Shop: the tier meter (6000 of 25000) then the Weekly coffee streak (2 of 4 periods).
    const meters = document.querySelectorAll('[role="progressbar"]');
    expect(meters[0]?.getAttribute('aria-valuenow')).toBe('24');
    expect(meters[1]?.getAttribute('aria-valuenow')).toBe('50');
    expect(text()).toContain('c1');
    expect(customerApi.getCustomerLedger).not.toHaveBeenCalled();
  });

  // ── Rules & caps (P2, P3) ──

  it('clicking a rule narrows the decisions to that rule, and card buckets stay off the caps', async () => {
    const fire: CustomerRuleFire = {
      id: 'f1',
      ruleId: 'r1',
      ruleName: 'Order earn',
      ruleVersion: 2,
      sourceEventId: 'e1',
      resultingDelta: d('10'),
      calculationSnapshot: '{}',
      createdAt: '2026-10-02T10:00:00Z',
    };
    const cap = (ruleId: string, isCardBucket: boolean): RuleCapUsage => ({
      ruleId,
      ruleName: ruleId,
      programId: 'p1',
      programName: 'Card',
      status: 'active',
      isCardBucket,
      perCustomerTotal: d('100'),
      usedTotal: d('10'),
      usedToday: d('0'),
    });
    vi.mocked(customerApi.getCustomerRuleFires).mockResolvedValue({ data: [fire], nextCursor: null });
    vi.mocked(customerApi.getCustomerCapUsage).mockResolvedValue([cap('plain', false), cap('bucket', true)]);
    await mountPage();
    await openTab('Rules & caps');

    const tiles = document.querySelectorAll('[role="listitem"]');
    expect(tiles).toHaveLength(1);
    expect(tiles[0]?.textContent).toContain('plain');

    [...document.querySelectorAll<HTMLButtonElement>('tbody button')]
      .find((b) => b.textContent?.includes('Order earn'))!
      .click();
    await vi.waitFor(() =>
      expect(customerApi.getCustomerRuleFires).toHaveBeenLastCalledWith(
        'novapay',
        'c1',
        expect.objectContaining({ ruleId: 'r1' }),
        null,
        10,
      ),
    );
  });

  // ── Rewards (P2) ──

  it('rewards show the cash paid out', async () => {
    const reward: CustomerReward = {
      source: 'points_purchase',
      rewardName: 'shop_coffee',
      rewardType: 'cashback',
      sourceEventId: 'e1',
      cost: d('30'),
      costWalletName: 'Shop points',
      outcome: 'cash_credited',
      cashAmount: d('5'),
      cashWalletName: 'Wallet',
      createdAt: '2026-10-02T10:00:00Z',
    };
    vi.mocked(customerApi.getCustomerRewards).mockResolvedValue([reward]);
    await mountPage();
    await openTab('Rewards');
    await vi.waitFor(() => expect(text()).toContain('+5 Wallet'));
  });

  // ── Tiers (P2) ──

  it('tiers show a tile only for programs that have tiers', async () => {
    await mountPage();
    await openTab('Tiers');
    const tiles = document.querySelectorAll('[role="listitem"]');
    expect(tiles).toHaveLength(1);
    expect(tiles[0]?.textContent).toContain('Gold');
    expect(tiles[0]?.textContent).toContain('6000');
    expect(tiles[0]?.textContent).toContain('25000');
    expect(tiles[0]?.textContent).toContain('24%');
    expect(document.querySelector('[role="list"]')?.textContent).not.toContain('third one');
  });

  // ── Card buckets (P3) ──

  it('a card bucket shows its caps and loads its postings by rule', async () => {
    const bucket: CustomerCardBucket = {
      ruleId: 'b1',
      name: 'Groceries bucket',
      programId: 'p1',
      programName: 'Card',
      status: 'active',
      rewardAmount: d('50'),
      perCustomerPerDay: d('100'),
      usedToday: d('40'),
      perCustomerTotal: d('300'),
      usedTotal: d('90'),
      postings: 2,
      lastPostedAt: '2026-10-02T00:00:00Z',
    };
    vi.mocked(customerApi.getCustomerCardBuckets).mockResolvedValue([bucket]);
    await mountPage();
    await openTab('Card buckets');
    await vi.waitFor(() => expect(text()).toContain('Groceries bucket'));
    expect(document.querySelectorAll('section [role="progressbar"]')).toHaveLength(2);
    [...document.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => /postings/i.test(b.textContent ?? ''))!
      .click();
    await vi.waitFor(() =>
      expect(customerApi.getCustomerLedger).toHaveBeenCalledWith('novapay', 'c1', { ruleId: 'b1' }, null, 10),
    );
  });

  // ── Messages sent (P3) ──

  it('messages list type, status and the failure reason, never a payload', async () => {
    const message: SentMessage = {
      eventId: 'm1',
      eventType: 'loyalty.points.earned',
      status: 'failed',
      attempts: 8,
      dedupKey: 'points_earned:e1',
      createdAt: '2026-10-02T10:00:00Z',
    };
    const failed: SentMessage = {
      eventId: 'm2',
      eventType: 'loyalty.points.transfer_failed',
      status: 'published',
      attempts: 1,
      reason: 'no_rule',
      dedupKey: 'transfer_failed:e2',
      createdAt: '2026-10-02T11:00:00Z',
      publishedAt: '2026-10-02T11:00:01Z',
    };
    vi.mocked(customerApi.getCustomerMessages).mockResolvedValue({
      data: [message, failed],
      nextCursor: null,
    });
    await mountPage();
    await openTab('Messages sent');
    await vi.waitFor(() => expect(text()).toContain('loyalty.points.earned'));
    expect(text()).toContain('points_earned:e1');
    const rows = document.querySelectorAll('tbody tr');
    expect(rows[1]?.textContent).toContain('no_rule');
    expect(rows[0]?.textContent).toContain('—');
  });

  // ── Event drawer (§3.4) ──

  const detail = (overrides: Partial<CustomerEventDetail> = {}): CustomerEventDetail => {
    const posting = (id: string, contactKey: string, delta: string) => ({
      id,
      contactKey,
      reason: delta.startsWith('-') ? 'transfer_out' : 'transfer_in',
      delta: d(delta),
      createdAt: '2026-10-02T10:00:00Z',
      accountTypeId: 'pts',
      accountTypeName: 'Shop points',
      accountTypeType: 'POINTS',
      programId: 'p1',
      programName: 'Shop',
    });
    return {
      eventId: 'evt-1',
      event: {
        eventId: 'evt-1',
        eventType: 'points.transfer',
        occurredAt: '2026-10-02T10:00:00Z',
        receivedAt: '2026-10-02T10:00:01Z',
        processedAt: '2026-10-02T10:00:02Z',
        status: 'processed',
        data: { contact_key: 'c1', phone_number: '***', points_amount: '50' },
      },
      postings: [posting('a', 'c1', '-50'), posting('b', 'c2', '50')],
      heldPostings: [],
      ruleFires: [],
      streaksApplied: [],
      streakCompletions: [],
      rewards: [],
      tierChanges: [],
      messages: [],
      ...overrides,
    } as CustomerEventDetail;
  };

  async function openDrawerFromEvents(eventDetail: () => Promise<CustomerEventDetail>) {
    vi.mocked(customerApi.getCustomerEvents).mockResolvedValue({
      data: [
        {
          eventId: 'evt-1',
          eventType: 'points.transfer',
          receivedAt: '2026-10-02T10:00:01Z',
          status: 'processed',
          outcome: [],
        },
      ],
      nextCursor: null,
    });
    vi.mocked(customerApi.getCustomerEvent).mockImplementation(eventDetail);
    await mountPage();
    await openTab('Events');
    await vi.waitFor(() => expect(document.querySelector('tbody tr')).not.toBeNull());
    document.querySelector<HTMLButtonElement>('tbody button[aria-label]')!.click();
    await flushPromises();
  }

  it('the drawer shows the payload as the API masked it, and a transfer’s counterparty', async () => {
    await openDrawerFromEvents(() => Promise.resolve(detail()));
    const drawer = document.querySelector('[role="dialog"]')!;
    await vi.waitFor(() => expect(drawer.querySelector('[data-payload]')).not.toBeNull());
    const payload = drawer.querySelector('[data-payload]')!.textContent ?? '';
    expect(payload).toContain('"phone_number": "***"');
    expect(payload).toContain('"points_amount": "50"');
    expect(drawer.textContent).toContain('c2');
    expect(drawer.textContent).toContain('-50');
    expect(drawer.textContent).toContain('+50');
    expect(customerApi.getCustomerEvent).toHaveBeenCalledWith('novapay', 'c1', 'evt-1');
  });

  it('the drawer says there is no inbound event for a scheduled job’s postings', async () => {
    await openDrawerFromEvents(() => Promise.resolve(detail({ event: null })));
    const drawer = document.querySelector('[role="dialog"]')!;
    await vi.waitFor(() => expect(drawer.textContent).toContain('scheduled'));
    expect(drawer.querySelector('[data-payload]')).toBeNull();
  });

  it('the drawer says "not linked" when the API answers 404', async () => {
    await openDrawerFromEvents(() => Promise.reject(apiErrorFrom(404, { title: 'not found' })));
    await vi.waitFor(() =>
      expect(document.querySelector('[role="dialog"]')?.textContent).toMatch(
        /not linked|isn't linked|not found/i,
      ),
    );
  });

  it('a profile that fails to load reads as "not found"', async () => {
    vi.mocked(customerApi.getCustomerProfile).mockRejectedValue(apiErrorFrom(404, { title: 'not found' }));
    await mountPage();
    await vi.waitFor(() => expect(text()).toContain('Customer not found'));
  });
});
