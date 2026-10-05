import {
  DEFAULT_RANGE_DAYS,
  defaultActivityFilter,
  defaultEventFilter,
  isDebit,
  isZeroAmount,
  programOptions,
  toEventQuery,
  toLedgerQuery,
  toRuleFireQuery,
  walletOptions,
} from './customer-filters';
import { AccountBalance } from './customer.model';
import { DecimalString } from '../../shared/money/decimal-string';

// CR 2026-10-02 (Customer 360): the filter bars open on the last 30 days (D9) and send the
// admin's local times to the API as UTC.
describe('customer filters', () => {
  const now = new Date(2026, 9, 2, 15, 45); // 2 Oct 2026, 15:45 local

  it('defaults both filter bars to the start of the local day 30 days ago, no end', () => {
    expect(DEFAULT_RANGE_DAYS).toBe(30);
    expect(defaultActivityFilter(now)).toEqual({
      programId: '',
      accountTypeId: '',
      reasonGroup: '',
      from: '2026-09-02T00:00',
      to: '',
      eventId: '',
    });
    expect(defaultEventFilter(now)).toEqual({
      eventType: '',
      status: '',
      from: '2026-09-02T00:00',
      to: '',
    });
  });

  it('sends local times as UTC and leaves empty fields out', () => {
    const query = toLedgerQuery({
      programId: 'p1',
      accountTypeId: '',
      reasonGroup: 'transfer',
      from: '2026-09-02T00:00',
      to: '',
      eventId: '  evt-1 ',
    });

    expect(query).toEqual({
      programId: 'p1',
      accountTypeId: undefined,
      reasonGroup: 'transfer',
      from: new Date('2026-09-02T00:00').toISOString(),
      to: undefined,
      eventId: 'evt-1',
    });
    expect(query.from).toMatch(/Z$/);
  });

  it('builds the events query the same way', () => {
    expect(
      toEventQuery({ eventType: '', status: 'failed', from: '', to: '2026-10-01T12:30' }),
    ).toEqual({
      eventType: undefined,
      status: 'failed',
      from: undefined,
      to: new Date('2026-10-01T12:30').toISOString(),
    });
  });

  const balance = (
    accountTypeId: string,
    programId: string,
    programName: string,
  ): AccountBalance => ({
    accountTypeId,
    accountTypeName: accountTypeId,
    accountTypeType: 'POINTS',
    balance: '0' as DecimalString,
    expirationDays: null,
    programId,
    programName,
  });
  const balances = [
    balance('pts', 'p1', 'Shop'),
    balance('cash', 'p1', 'Shop'),
    balance('miles', 'p2', 'Travel'),
  ];

  it('lists each of the customer’s programs once, from their balances', () => {
    expect(programOptions(balances)).toEqual([
      { id: 'p1', name: 'Shop' },
      { id: 'p2', name: 'Travel' },
    ]);
  });

  it('narrows the wallet options to the chosen program', () => {
    expect(walletOptions(balances, 'p2').map((b) => b.accountTypeId)).toEqual(['miles']);
    expect(walletOptions(balances, '')).toHaveLength(3);
  });

  it('reads the sign of a decimal string without parsing it', () => {
    expect(isDebit('-50.0000')).toBe(true);
    expect(isDebit('0.0001')).toBe(false);
    expect(isDebit('100')).toBe(false);
  });
});

// CR 2026-10-02 P2.
describe('customer filters (P2)', () => {
  it('reads zero amounts from the string', () => {
    expect(isZeroAmount('0')).toBe(true);
    expect(isZeroAmount('0.0000')).toBe(true);
    expect(isZeroAmount('0.0001')).toBe(false);
    expect(isZeroAmount('25')).toBe(false);
  });

  it('builds the rule-fire query with an optional rule and UTC times', () => {
    expect(toRuleFireQuery({ from: '2026-09-02T00:00', to: '' }, 'r1')).toEqual({
      ruleId: 'r1',
      from: new Date('2026-09-02T00:00').toISOString(),
      to: undefined,
    });
    expect(toRuleFireQuery({ from: '', to: '' }).ruleId).toBeUndefined();
  });
});
