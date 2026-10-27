/**
 * The built-in triggers, shown until the catalog loads (or if it can't): the streak form seeds
 * its picker with these.
 */
export const BUILT_IN_TRIGGERS = [
  'order.created',
  'order.refunded',
  'cash.added',
  'cash.spent',
  'points.redeem',
  'points.transfer',
  'reward.purchase',
] as const;

export const DEFAULT_TRIGGER = 'order.created';
