import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { CustomersService } from './customers.service';
import { CustomerOverviewTab } from './customer-overview.tab';
import { CustomerRulesTab } from './customer-rules.tab';
import { CustomerRewardsTab } from './customer-rewards.tab';
import { CustomerTiersTab } from './customer-tiers.tab';
import { CustomerProfile, CustomerReward, CustomerRuleFire } from './customer.model';
import { DecimalString } from '../../shared/money/decimal-string';

// CR 2026-10-02 (Customer 360) P2 tabs: what the overview shows per wallet, rule filtering in
// Rules & caps, and reward payouts.
describe('Customer 360 P2 tabs', () => {
  const d = (v: string) => v as DecimalString;

  function service(overrides: Partial<Record<keyof CustomersService, unknown>> = {}) {
    return {
      getLedger: vi.fn().mockResolvedValue({ data: [], nextCursor: null }),
      getCapUsage: vi.fn().mockResolvedValue([]),
      getRuleFires: vi.fn().mockResolvedValue({ data: [], nextCursor: null }),
      getRewards: vi.fn().mockResolvedValue([]),
      ...overrides,
    };
  }

  async function render<T>(component: Type<T>, svc: unknown, inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [component],
      providers: [provideTranslateService(), { provide: CustomersService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(component);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

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
        tier: null,
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
    ],
  };

  it('overview shows pending and expiring amounts only where there are any', async () => {
    const { el } = await render(CustomerOverviewTab, service(), { profile });

    expect(el.textContent).toContain('customers.overview.pending');
    expect(el.textContent?.match(/customers\.overview\.pending/g)).toHaveLength(1);
    expect(el.textContent).toContain('customers.overview.expiring');
    expect(el.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('50');
  });

  // Overview redesign (2026-10-03): a profile panel; postings stay on the Activity tab.
  it('overview shows the profile panel and no recent activity', async () => {
    const svc = service();
    const { el } = await render(CustomerOverviewTab, svc, { profile });

    const panel = el.querySelector('app-customer-profile-panel')!;
    expect(panel.textContent).toContain('c1');
    expect(panel.textContent).toContain('Shop');
    expect(panel.textContent).toContain('customers.panel.failedEvents');
    expect(svc.getLedger).not.toHaveBeenCalled();
    expect(el.textContent).not.toContain('customers.overview.recent');
  });

  it('clicking a rule narrows the decisions to that rule', async () => {
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
    const svc = service({
      getRuleFires: vi.fn().mockResolvedValue({ data: [fire], nextCursor: null }),
    });
    const { el, fixture } = await render(CustomerRulesTab, svc, { contactKey: 'c1' });

    el.querySelector<HTMLButtonElement>('tbody button')!.click();
    await fixture.whenStable();

    expect(svc.getRuleFires).toHaveBeenLastCalledWith(
      'c1',
      expect.objectContaining({ ruleId: 'r1' }),
      null,
      10,
    );
  });

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
    const { el } = await render(
      CustomerRewardsTab,
      service({ getRewards: vi.fn().mockResolvedValue([reward]) }),
      {
        contactKey: 'c1',
      },
    );

    expect(el.textContent).toContain('customers.rewards.outcome.cash_credited');
    expect(el.textContent).toContain('+5 Wallet');
  });

  // Tiers tab (2026-10-03): one status tile per program, in a sideways-scrolling row.
  it('tiers tab shows a tile only for programs that have tiers', async () => {
    const tiered: CustomerProfile = {
      ...profile,
      programs: [
        {
          ...profile.programs![0]!,
          tier: {
            tierDisplayName: 'Gold',
            nextTierDisplayName: 'Platinum',
            nextTierMinPoints: d('25000'),
            qualifyingPoints: d('6000'),
            periodStart: '2026-10-01',
          },
        },
        { programId: 'p3', programName: 'third one', wallets: [], tier: null, streaks: [] },
      ],
    };
    const svc = service({ getTierHistory: vi.fn().mockResolvedValue([]) });
    const { el } = await render(CustomerTiersTab, svc, { contactKey: 'c1', profile: tiered });

    const tiles = el.querySelectorAll('[role="listitem"]');
    expect(tiles).toHaveLength(1);
    expect(tiles[0]?.textContent).toContain('Gold');
    expect(tiles[0]?.textContent).toContain('6000');
    expect(tiles[0]?.textContent).toContain('25000');
    expect(tiles[0]?.textContent).toContain('24%');
    expect(el.textContent).not.toContain('third one');
  });
});
