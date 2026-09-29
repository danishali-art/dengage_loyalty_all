export interface CustomerSummary {
  contactKey: string;
  accountCount: number;
  lastActivityAt: string;
}

export interface AccountBalance {
  accountTypeId: string;
  accountTypeName: string;
  accountTypeType: string;
  balance: string;
  expirationDays: number | null;
}

export interface TierProgress {
  currentTierName: string | null;
  currentTierDisplayName: string | null;
  nextTierName: string | null;
  nextTierDisplayName: string | null;
  nextTierMinPoints: string | null;
  qualifyingPoints: string;
  periodStart: string | null;
  expiresAt: string | null;
}

export interface CustomerProfile {
  contactKey: string;
  balances: AccountBalance[];
  tierProgress: TierProgress | null;
}

export interface LedgerEntry {
  id: string;
  reason: string;
  delta: string;
  accountTypeId: string;
  metadata: string | null;
  createdAt: string;
}

export interface TierHistoryEntry {
  id: string;
  fromTierName: string | null;
  toTierName: string;
  qualifyingPoints: string;
  createdAt: string;
}
