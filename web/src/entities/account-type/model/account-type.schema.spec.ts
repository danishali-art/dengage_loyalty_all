// 1.3.CL items 1–4: the form must never submit what AccountTypeConfigValidators.cs rejects.
import { describe, expect, it } from 'vitest';
import {
  accountTypeFormSchema,
  accountTypeFormValues,
  toCreateAccountTypeRequest,
  toUpdateAccountTypeRequest,
  warningDaysError,
} from './account-type.schema';
import type { AccountType } from './types';

const points = (overrides: Record<string, unknown> = {}) => ({
  ...accountTypeFormValues(),
  name: 'Points',
  ...overrides,
});

describe('account-type form', () => {
  it.each([
    { expiration: '30', warning: '30', error: 'accountTypes.warningDays.beforeExpiry' },
    { expiration: '30', warning: '45', error: 'accountTypes.warningDays.beforeExpiry' },
    { expiration: '', warning: '7', error: 'accountTypes.warningDays.needsExpiry' },
    { expiration: '30', warning: '0', error: 'accountTypes.warningDays.positive' },
    { expiration: '30', warning: '7', error: null },
    { expiration: '30', warning: '', error: null },
  ])('validates warning days $warning against expiration $expiration', ({ expiration, warning, error }) => {
    expect(warningDaysError(expiration, warning)).toBe(error);
  });

  it('does not accept an invalid warning', () => {
    expect(
      accountTypeFormSchema().safeParse(points({ expirationDays: '30', warningDays: '30' })).success,
    ).toBe(false);
  });

  it('sends warning_days and the tier flag for POINTS', () => {
    const values = accountTypeFormSchema().parse(
      points({ decimals: '2', expirationDays: '30', warningDays: '7', isTierQualifying: true }),
    );
    expect(toCreateAccountTypeRequest(values)).toEqual({
      name: 'Points',
      type: 'POINTS',
      config: { decimals: 2, expiration_days: 30, warning_days: 7 },
      isTierQualifying: true,
    });
  });

  it('sends CASH with the default SAR currency, no expiry and no tier flag', () => {
    const values = accountTypeFormSchema().parse(
      points({ name: 'Cash', type: 'CASH', decimals: '2', expirationDays: '90', isTierQualifying: true }),
    );
    expect(toCreateAccountTypeRequest(values)).toEqual({
      name: 'Cash',
      type: 'CASH',
      config: { currency: 'SAR', decimals: 2 },
      isTierQualifying: undefined,
    });
  });

  it('keeps the existing CASH currency on edit', () => {
    const existing = {
      id: 'existing',
      type: 'CASH',
      name: 'Cash',
      config: { currency: 'AED', decimals: 2 },
      createdAt: '2026-09-01T00:00:00Z',
      isTierQualifying: false,
    } as AccountType;
    const values = accountTypeFormSchema().parse(accountTypeFormValues(existing));
    expect(toUpdateAccountTypeRequest(values, existing)).toEqual({
      name: 'Cash',
      config: { currency: 'AED', decimals: 2 },
      isTierQualifying: undefined,
    });
  });

  it('sends the transfer daily limit for POINTS when transfer is allowed', () => {
    const values = accountTypeFormSchema().parse(
      points({ transferEnabled: true, transferDailyLimit: '5000' }),
    );
    expect(toCreateAccountTypeRequest(values).config).toEqual({
      decimals: 0,
      transfer: { daily_limit: 5000 },
    });
  });

  it('does not let a hidden, invalid limit block a save when transfer is off', () => {
    const parsed = accountTypeFormSchema().safeParse(points({ transferDailyLimit: '0' }));
    expect(parsed.success).toBe(true);
    expect(toCreateAccountTypeRequest(parsed.data!).config).toEqual({ decimals: 0 });
  });

  it('requires a redemption target wallet when redemption is on', () => {
    expect(accountTypeFormSchema().safeParse(points({ redemptionEnabled: true })).success).toBe(false);
  });

  // Regression: the form rebuilt the config from its own fields only, so editing a wallet
  // silently dropped `transfer` (and any other key it doesn't show).
  it('keeps the transfer limit and unknown settings when editing a POINTS account type', () => {
    const existing = {
      id: 'existing',
      type: 'POINTS',
      name: 'FinPuan',
      config: { decimals: 0, transfer: { daily_limit: 5000 }, custom_flag: true },
      createdAt: '2026-09-01T00:00:00Z',
      isTierQualifying: false,
    } as AccountType;
    const initial = accountTypeFormValues(existing);
    expect(initial.transferEnabled).toBe(true);
    expect(initial.transferDailyLimit).toBe('5000');
    const values = accountTypeFormSchema().parse({ ...initial, name: 'FinPuan renamed' });
    expect(toUpdateAccountTypeRequest(values, existing).config).toEqual({
      custom_flag: true,
      decimals: 0,
      transfer: { daily_limit: 5000 },
    });
  });

  it('removes the transfer setting when transfer is switched off', () => {
    const existing = {
      id: 'existing',
      type: 'POINTS',
      name: 'FinPuan',
      config: { decimals: 0, transfer: { daily_limit: 5000 } },
      createdAt: '2026-09-01T00:00:00Z',
      isTierQualifying: false,
    } as AccountType;
    const values = accountTypeFormSchema().parse({
      ...accountTypeFormValues(existing),
      transferEnabled: false,
    });
    expect(toUpdateAccountTypeRequest(values, existing).config).toEqual({ decimals: 0 });
  });
});
