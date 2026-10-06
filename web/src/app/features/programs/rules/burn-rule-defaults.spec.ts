import { AccountType } from '../account-types/account-type.model';
import { burnRuleDefaults } from './burn-rule-defaults';

// CR 2026-10-05 (S4): the wallet's settings pre-fill a new redeem / transfer rule, once.
describe('burnRuleDefaults', () => {
  const wallet = (
    config: AccountType['config'],
    type: AccountType['type'] = 'POINTS',
  ): AccountType => ({
    id: 'w1',
    type,
    name: 'Points',
    config,
    createdAt: '2026-10-05T00:00:00Z',
    isTierQualifying: false,
  });
  const configured = wallet({
    decimals: 0,
    redemption: { rate: 0.05, min_points: 100, target_account_type_id: 'cash-1' },
    transfer: { daily_limit: 5000 },
  });

  it('copies the redemption settings into a redeem rule', () => {
    expect(burnRuleDefaults('RedemptionRule', configured)).toEqual({
      calcCashPerPoint: 0.05,
      calcMinRedeem: 100,
      calcCashAccountTypeId: 'cash-1',
    });
  });

  it('copies the daily limit into a transfer rule', () => {
    expect(burnRuleDefaults('TransferRule', configured)).toEqual({ calcMaxPerDay: 5000 });
  });

  it('gives nothing for a wallet without settings, a cash wallet, no wallet or another rule type', () => {
    expect(burnRuleDefaults('RedemptionRule', wallet({ decimals: 0 }))).toEqual({});
    expect(burnRuleDefaults('TransferRule', wallet({ decimals: 0, transfer: null }))).toEqual({});
    expect(
      burnRuleDefaults('RedemptionRule', wallet({ currency: 'SAR', decimals: 2 }, 'CASH')),
    ).toEqual({});
    expect(burnRuleDefaults('RedemptionRule', undefined)).toEqual({});
    expect(burnRuleDefaults('SpendRule', configured)).toEqual({});
  });
});
