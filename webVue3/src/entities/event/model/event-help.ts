// Ported verbatim from EVENT_HELP in web/src/app/features/events/event-simulator.page.ts.
/** Field guidance per event type, checked against the Consumer handlers that read each payload. */
export interface EventHelp {
  required: readonly string[];
  optional?: readonly string[];
  /** i18n key for the type-specific note. */
  note: string;
}

const EVENT_HELP: Readonly<Record<string, EventHelp>> = {
  'order.created': {
    required: ['contact_key', 'amount'],
    optional: ['channel', 'items'],
    note: 'events.simulator.help.orderCreated',
  },
  'order.refunded': {
    required: ['original_event_id'],
    optional: ['refund_ratio', 'amount', 'original_amount'],
    note: 'events.simulator.help.orderRefunded',
  },
  'cash.added': {
    required: ['contact_key', 'amount', 'account_type_id'],
    note: 'events.simulator.help.cashAdded',
  },
  'cash.spent': {
    required: ['contact_key', 'amount', 'account_type_id'],
    note: 'events.simulator.help.cashSpent',
  },
  'points.redeem': {
    required: ['contact_key', 'points_amount', 'source_account_type_id'],
    note: 'events.simulator.help.pointsRedeem',
  },
  'points.transfer': {
    required: ['contact_key', 'target_contact_key', 'points_amount', 'source_account_type_id'],
    note: 'events.simulator.help.pointsTransfer',
  },
  'reward.purchase': {
    // reward_id identifies the reward exactly (CR 2026-09-30); reward_name is the alternative.
    required: ['contact_key', 'reward_name'],
    optional: ['reward_id', 'channel'],
    note: 'events.simulator.help.rewardPurchase',
  },
  'points.adjusted': {
    required: ['contact_key', 'reason_code'],
    note: 'events.simulator.help.pointsAdjusted',
  },
};

const BUILT_IN_DEFAULT_HELP: EventHelp = {
  required: ['contact_key'],
  note: 'events.simulator.help.builtInDefault',
};
// GenericEventValidator rejects a non-built-in event without all three.
const GENERIC_HELP: EventHelp = {
  required: ['contact_key', 'channel', 'amount'],
  note: 'events.simulator.help.generic',
};

/** The guidance for an event type; null before a type is picked. */
export function eventHelp(eventType: string | null, isGeneric: boolean): EventHelp | null {
  if (!eventType) return null;
  return EVENT_HELP[eventType] ?? (isGeneric ? GENERIC_HELP : BUILT_IN_DEFAULT_HELP);
}

/** The status pill tone for an inbox status. */
export function eventStatusTone(status: string): 'success' | 'danger' | 'neutral' {
  if (status === 'processed') return 'success';
  if (status === 'failed') return 'danger';
  return 'neutral';
}
