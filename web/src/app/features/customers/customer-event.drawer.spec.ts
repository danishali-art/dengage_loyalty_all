import { TestBed } from '@angular/core/testing';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { ApiError } from '../../core/http/api-error';
import { CustomerEventDrawer, CustomerEventDrawerData } from './customer-event.drawer';
import { CustomersService } from './customers.service';
import { CustomerEventDetail, EventPosting } from './customer.model';
import { DecimalString } from '../../shared/money/decimal-string';

// CR 2026-10-02 (Customer 360, §3.4): the drawer shows the masked payload the API returns, the
// counterparty of a transfer, and a "scheduled job" note when there is no inbound event.
describe('CustomerEventDrawer', () => {
  const posting = (id: string, contactKey: string, delta: string): EventPosting => ({
    id,
    contactKey,
    reason: delta.startsWith('-') ? 'transfer_out' : 'transfer_in',
    delta: delta as DecimalString,
    metadata: null,
    createdAt: '2026-10-02T10:00:00Z',
    ruleId: null,
    ruleName: null,
    ruleVersion: null,
    campaignId: null,
    campaignName: null,
    accountTypeId: 'pts',
    accountTypeName: 'Shop points',
    accountTypeType: 'POINTS',
    programId: 'p1',
    programName: 'Shop',
  });

  const detail = (overrides: Partial<CustomerEventDetail> = {}): CustomerEventDetail => ({
    eventId: 'evt-1',
    event: {
      eventId: 'evt-1',
      eventType: 'points.transfer',
      occurredAt: '2026-10-02T10:00:00Z',
      receivedAt: '2026-10-02T10:00:01Z',
      processedAt: '2026-10-02T10:00:02Z',
      status: 'processed',
      error: null,
      data: { contact_key: 'cust_1', phone_number: '***', points_amount: '50' },
    },
    postings: [posting('a', 'cust_1', '-50'), posting('b', 'cust_2', '50')],
    heldPostings: [],
    ruleFires: [],
    streaksApplied: [],
    streakCompletions: [],
    rewards: [],
    tierChanges: [],
    messages: [],
    ...overrides,
  });

  async function render(
    getEvent: () => Promise<CustomerEventDetail>,
    data: Partial<CustomerEventDrawerData> = {},
  ) {
    TestBed.configureTestingModule({
      imports: [CustomerEventDrawer],
      providers: [
        provideTranslateService(),
        { provide: DialogRef, useValue: { close: vi.fn() } },
        { provide: DIALOG_DATA, useValue: { contactKey: 'cust_1', eventId: 'evt-1', ...data } },
        { provide: CustomersService, useValue: { getEvent: vi.fn(getEvent) } },
      ],
    });
    const fixture = TestBed.createComponent(CustomerEventDrawer);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the payload as the API masked it', async () => {
    const el = await render(() => Promise.resolve(detail()));

    const payload = el.querySelector('pre')?.textContent ?? '';
    expect(payload).toContain('"phone_number": "***"');
    expect(payload).toContain('"points_amount": "50"');
  });

  it('shows a transfer’s counterparty posting', async () => {
    const el = await render(() => Promise.resolve(detail()));

    expect(el.textContent).toContain('customers.drawer.counterparty');
    expect(el.textContent).toContain('-50');
    expect(el.textContent).toContain('+50');
  });

  it('highlights the posting the drawer was opened from', async () => {
    const el = await render(() => Promise.resolve(detail()), { postingId: 'b' });

    const items = [...el.querySelectorAll('li.border-brand')];
    expect(items).toHaveLength(1);
    expect(items[0]?.textContent).toContain('+50');
  });

  it('says there is no inbound event for a scheduled job’s postings', async () => {
    const el = await render(() => Promise.resolve(detail({ event: null })));

    expect(el.textContent).toContain('customers.drawer.noInbound');
    expect(el.querySelector('pre')).toBeNull();
  });

  it('shows "not linked" when the API answers 404', async () => {
    const el = await render(() =>
      Promise.reject(new ApiError({ kind: 'not-found', status: 404, title: 'not found' })),
    );

    expect(el.textContent).toContain('customers.drawer.notFound');
  });
});
