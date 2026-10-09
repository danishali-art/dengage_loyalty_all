import { eventSampleJson } from './event-samples';

const keysOf = (eventType: string | null, isGeneric = false): string[] =>
  Object.keys(JSON.parse(eventSampleJson(eventType, isGeneric)) as object);

describe('eventSampleJson', () => {
  // The fields each Consumer handler / EvaluationEvent reads (see event-samples.ts).
  it.each([
    ['order.created', ['contact_key', 'amount', 'channel']],
    ['order.refunded', ['original_event_id', 'refund_ratio']],
    ['cash.added', ['contact_key', 'amount', 'account_type_id']],
    ['cash.spent', ['contact_key', 'amount', 'account_type_id']],
    ['points.redeem', ['contact_key', 'points_amount', 'source_account_type_id']],
    [
      'points.transfer',
      ['contact_key', 'target_contact_key', 'points_amount', 'source_account_type_id'],
    ],
    ['reward.purchase', ['contact_key', 'reward_name', 'channel']],
    ['points.adjusted', ['contact_key', 'amount', 'reason_code']],
    ['signup', ['contact_key', 'segment']],
    ['kyc.completed', ['contact_key', 'segment']],
    ['card.transaction', ['contact_key', 'amount', 'mcc', 'country', 'tx_status', 'channel']],
    ['remittance', ['contact_key', 'amount', 'channel']],
  ])('%s carries the fields its handler reads', (eventType, fields) => {
    expect(keysOf(eventType)).toEqual(fields);
  });

  it('gives a tenant-defined event the three fields the generic validator requires', () => {
    expect(keysOf('tenant.custom_event', true)).toEqual(['contact_key', 'channel', 'amount']);
  });

  it('gives an unlisted built-in event only contact_key', () => {
    expect(keysOf('some.future_builtin', false)).toEqual(['contact_key']);
  });

  it('never sends an amount as a JSON number', () => {
    for (const type of [
      'order.created',
      'cash.added',
      'points.redeem',
      'points.transfer',
      'card.transaction',
    ]) {
      const data = JSON.parse(eventSampleJson(type, false)) as Record<string, unknown>;
      for (const value of Object.values(data)) expect(typeof value).toBe('string');
    }
  });
});
