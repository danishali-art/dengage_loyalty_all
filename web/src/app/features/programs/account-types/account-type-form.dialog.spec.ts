import { TestBed } from '@angular/core/testing';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AccountTypeFormDialog, AccountTypeFormData } from './account-type-form.dialog';
import { AccountTypesService } from './account-types.service';
import { AccountType } from './account-type.model';

// 1.3.CL items 1–4: the dialog must never submit what AccountTypeConfigValidators.cs rejects —
// warning_days bounds, no CASH expiration_days, whitelisted currency, POINTS-only tier flag.
describe('AccountTypeFormDialog', () => {
  let service: { create: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };

  function setup(data: Partial<AccountTypeFormData> = {}) {
    service = {
      create: vi.fn().mockResolvedValue({ id: 'new' }),
      update: vi.fn().mockResolvedValue({ id: 'existing' }),
    };
    TestBed.configureTestingModule({
      imports: [AccountTypeFormDialog],
      providers: [
        provideTranslateService(),
        { provide: DialogRef, useValue: { close: vi.fn() } },
        { provide: DIALOG_DATA, useValue: { programId: 'p1', ...data } },
        { provide: AccountTypesService, useValue: service },
      ],
    });
    const component = TestBed.createComponent(AccountTypeFormDialog).componentInstance;
    return {
      form: component['form'],
      warningDaysError: () => component['warningDaysError'](),
      submit: () => component['submit'](),
    };
  }

  it.each([
    { expiration: 30, warning: 30, error: 'accountTypes.warningDays.beforeExpiry' },
    { expiration: 30, warning: 45, error: 'accountTypes.warningDays.beforeExpiry' },
    { expiration: null, warning: 7, error: 'accountTypes.warningDays.needsExpiry' },
    { expiration: 30, warning: 0, error: 'accountTypes.warningDays.positive' },
    { expiration: 30, warning: 7, error: null },
    { expiration: 30, warning: null, error: null },
  ])(
    'validates warning days $warning against expiration $expiration',
    ({ expiration, warning, error }) => {
      const { form, warningDaysError } = setup();
      form.patchValue({
        name: 'Points',
        type: 'POINTS',
        expirationDays: expiration,
        warningDays: warning,
      });
      expect(warningDaysError()).toBe(error);
    },
  );

  it('does not submit an invalid warning', async () => {
    const { form, submit } = setup();
    form.patchValue({ name: 'Points', type: 'POINTS', expirationDays: 30, warningDays: 30 });
    await submit();
    expect(service.create).not.toHaveBeenCalled();
  });

  it('sends warning_days and the tier flag for POINTS', async () => {
    const { form, submit } = setup();
    form.patchValue({
      name: 'Points',
      type: 'POINTS',
      decimals: 2,
      expirationDays: 30,
      warningDays: 7,
      isTierQualifying: true,
    });
    await submit();
    expect(service.create).toHaveBeenCalledWith('p1', {
      name: 'Points',
      type: 'POINTS',
      config: { decimals: 2, expiration_days: 30, warning_days: 7 },
      isTierQualifying: true,
    });
  });

  it('sends CASH with the default SAR currency, no expiry and no tier flag', async () => {
    const { form, submit } = setup();
    form.patchValue({
      name: 'Cash',
      type: 'CASH',
      decimals: 2,
      expirationDays: 90,
      isTierQualifying: true,
    });
    await submit();
    expect(service.create).toHaveBeenCalledWith('p1', {
      name: 'Cash',
      type: 'CASH',
      config: { currency: 'SAR', decimals: 2 },
      isTierQualifying: undefined,
    });
  });

  it('locks the currency when editing an existing CASH account type', async () => {
    const existing: AccountType = {
      id: 'existing',
      type: 'CASH',
      name: 'Cash',
      config: { currency: 'AED', decimals: 2 },
      createdAt: '2026-09-01T00:00:00Z',
      isTierQualifying: false,
    };
    const { form, submit } = setup({ existing });
    expect(form.controls.currency.disabled).toBe(true);
    await submit();
    expect(service.update).toHaveBeenCalledWith('p1', 'existing', {
      name: 'Cash',
      config: { currency: 'AED', decimals: 2 },
      isTierQualifying: undefined,
    });
  });
});
