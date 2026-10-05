import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { CustomersService } from './customers.service';
import { CustomerCardBucketsTab } from './customer-card-buckets.tab';
import { CustomerMessagesTab } from './customer-messages.tab';
import { CustomerRulesTab } from './customer-rules.tab';
import { CustomerCardBucket, RuleCapUsage, SentMessage } from './customer.model';
import { toMessageQuery } from './customer-filters';
import { DecimalString } from '../../shared/money/decimal-string';

// CR 2026-10-02 (Customer 360) P3: card buckets (and their removal from Rules & caps) and
// messages sent (D5: no payload).
describe('Customer 360 P3 tabs', () => {
  const d = (v: string) => v as DecimalString;

  function service(overrides: Record<string, unknown> = {}) {
    return {
      getLedger: vi.fn().mockResolvedValue({ data: [], nextCursor: null }),
      getCardBuckets: vi.fn().mockResolvedValue([]),
      getMessages: vi.fn().mockResolvedValue({ data: [], nextCursor: null }),
      getCapUsage: vi.fn().mockResolvedValue([]),
      getRuleFires: vi.fn().mockResolvedValue({ data: [], nextCursor: null }),
      ...overrides,
    };
  }

  async function render<T>(component: Type<T>, svc: unknown) {
    TestBed.configureTestingModule({
      imports: [component],
      providers: [provideTranslateService(), { provide: CustomersService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(component);
    fixture.componentRef.setInput('contactKey', 'c1');
    fixture.detectChanges();
    // Let the tab's load promise (and its .finally) settle before reading the DOM.
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

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

  it('a card bucket shows its caps and loads its postings by rule', async () => {
    const svc = service({ getCardBuckets: vi.fn().mockResolvedValue([bucket]) });
    const { el, fixture } = await render(CustomerCardBucketsTab, svc);

    expect(el.querySelectorAll('[role="progressbar"]')).toHaveLength(2);
    el.querySelector<HTMLButtonElement>('app-button button')!.click();
    await fixture.whenStable();

    expect(svc.getLedger).toHaveBeenCalledWith('c1', { ruleId: 'b1' }, null, 10);
  });

  it('Rules & caps leaves card buckets to their own tab', async () => {
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
    const svc = service({
      getCapUsage: vi.fn().mockResolvedValue([cap('plain', false), cap('bucket', true)]),
    });
    const { el } = await render(CustomerRulesTab, svc);

    expect(el.textContent).toContain('plain');
    expect(el.querySelectorAll('[role="listitem"]')).toHaveLength(1);
    expect(el.querySelector('[role="listitem"]')?.textContent).not.toContain('bucket');
  });

  it('messages list type and status, never a payload', async () => {
    const message: SentMessage = {
      eventId: 'm1',
      eventType: 'loyalty.points.earned',
      status: 'failed',
      attempts: 8,
      dedupKey: 'points_earned:e1',
      createdAt: '2026-10-02T10:00:00Z',
      publishedAt: null,
    };
    const { el } = await render(
      CustomerMessagesTab,
      service({ getMessages: vi.fn().mockResolvedValue({ data: [message], nextCursor: null }) }),
    );

    expect(el.textContent).toContain('loyalty.points.earned');
    expect(el.textContent).toContain('customers.messages.status.failed');
    expect(el.textContent).toContain('points_earned:e1');
  });

  it('builds the messages query with UTC times and no empty fields', () => {
    expect(
      toMessageQuery({
        eventType: ' loyalty.tier.changed ',
        status: '',
        from: '2026-09-02T00:00',
        to: '',
      }),
    ).toEqual({
      eventType: 'loyalty.tier.changed',
      status: undefined,
      from: new Date('2026-09-02T00:00').toISOString(),
      to: undefined,
    });
  });
});
