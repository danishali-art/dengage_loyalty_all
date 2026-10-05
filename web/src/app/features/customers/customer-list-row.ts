import { CustomerProfile } from './customer.model';

/** What the Customers list shows for one customer, beyond the list API's own columns. */
export interface CustomerListRow {
  /** The names of the programs the customer has a wallet in. */
  programs: string[];
  firstSeenAt: string | null;
}

/**
 * Builds a list row from the data the profile endpoint already returns (Customers list,
 * Layout A UI-only, 2026-10-03).
 */
export function customerListRow(profile: CustomerProfile): CustomerListRow {
  return {
    programs: (profile.programs ?? []).map((p) => p.programName),
    firstSeenAt: profile.summary?.firstSeenAt ?? null,
  };
}
