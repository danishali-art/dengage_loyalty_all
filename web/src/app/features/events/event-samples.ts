/**
 * Event Simulator: a starting `data` payload per event type, built only from the fields the
 * Consumer handlers and the rule engine read (Consumer/Handlers/*, EvaluationEvent.FromEnvelope,
 * EventTypes.Catalog). Amounts are JSON strings — the handlers parse them with decimal.Parse on a
 * string, and money never travels as a JSON number. Ids the portal can't know (wallets, the
 * refunded order) are left as REPLACE_WITH_… placeholders for the admin to fill in.
 */
const CONTACT = 'cust_test_1';

const SAMPLES: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  'order.created': { contact_key: CONTACT, amount: '1250', channel: 'web' },
  // refund_ratio is used when present; otherwise amount / original_amount.
  'order.refunded': { original_event_id: 'REPLACE_WITH_ORDER_EVENT_ID', refund_ratio: '1' },
  'cash.added': {
    contact_key: CONTACT,
    amount: '100',
    account_type_id: 'REPLACE_WITH_CASH_WALLET_ID',
  },
  'cash.spent': {
    contact_key: CONTACT,
    amount: '40',
    account_type_id: 'REPLACE_WITH_CASH_WALLET_ID',
  },
  'points.redeem': {
    contact_key: CONTACT,
    points_amount: '100',
    source_account_type_id: 'REPLACE_WITH_POINTS_WALLET_ID',
  },
  'points.transfer': {
    contact_key: CONTACT,
    target_contact_key: 'cust_test_2',
    points_amount: '50',
    source_account_type_id: 'REPLACE_WITH_POINTS_WALLET_ID',
  },
  'reward.purchase': {
    contact_key: CONTACT,
    reward_name: 'REPLACE_WITH_REWARD_NAME',
    channel: 'web',
  },
  // amount is the adjustment itself (negative debits); without it the rule's fixed value applies.
  'points.adjusted': { contact_key: CONTACT, amount: '100', reason_code: 'goodwill' },
  // Once per customer (CR 2026-10-06 D12): no amount; segment is the condition field.
  signup: { contact_key: CONTACT, segment: 'standard' },
  'kyc.completed': { contact_key: CONTACT, segment: 'standard' },
  'card.transaction': {
    contact_key: CONTACT,
    amount: '250',
    mcc: '5411',
    country: 'SA',
    tx_status: 'approved',
    channel: 'pos',
  },
  remittance: { contact_key: CONTACT, amount: '500', channel: 'app' },
};

// GenericEventValidator rejects a tenant-defined event without all three.
const GENERIC_SAMPLE = { contact_key: CONTACT, channel: 'web', amount: '1250' };
const BUILT_IN_DEFAULT_SAMPLE = { contact_key: CONTACT };

/** The sample `data` for an event type, as the pretty-printed JSON the simulator's textarea shows. */
export function eventSampleJson(eventType: string | null, isGeneric: boolean): string {
  const sample =
    (eventType && SAMPLES[eventType]) ||
    (isGeneric || !eventType ? GENERIC_SAMPLE : BUILT_IN_DEFAULT_SAMPLE);
  return JSON.stringify(sample, null, 2);
}
