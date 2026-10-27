import type { ApiSchema } from '@/shared/api';

export type Tier = ApiSchema<'TierResponse'>;
export type QualifyingModel = Tier['qualifyingModel'];
export type CreateTierRequest = ApiSchema<'CreateTierRequest'>;
export type UpdateTierRequest = ApiSchema<'UpdateTierRequest'>;

/** TiersValidators: name ≤ 100, display name ≤ 255. */
export const TIER_NAME_MAX = 100;
export const TIER_DISPLAY_NAME_MAX = 255;

export function sortTiers(tiers: readonly Tier[]): Tier[] {
  return [...tiers].sort((a, b) => a.sortOrder - b.sortOrder);
}

/** Swaps a tier with its neighbour; returns the new order of ids, or null when it can't move. */
export function moveTier(tiers: readonly Tier[], index: number, direction: -1 | 1): string[] | null {
  const target = index + direction;
  if (index < 0 || target < 0 || target >= tiers.length) return null;
  const ids = tiers.map((t) => t.id);
  [ids[index], ids[target]] = [ids[target] as string, ids[index] as string];
  return ids;
}

/**
 * Client-side pre-emption of the guards TiersAppService.DeleteAsync enforces — the server stays
 * authoritative; this only avoids a round trip to learn "no". Returns an i18n key, or null.
 */
export function tierDeleteBlockedReason(tier: Tier, programIsActive: boolean): string | null {
  if (tier.hasAssignedAccounts) return 'tiers.delete.hasAccounts';
  if (programIsActive) return 'tiers.delete.programActive';
  return null;
}
