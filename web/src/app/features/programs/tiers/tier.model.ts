export type QualifyingModel = 'lifetime' | 'periodic';

export interface Tier {
  id: string;
  name: string;
  displayName: string;
  minPoints: string;
  qualifyingModel: QualifyingModel;
  qualifyingPeriodDays: number | null;
  graceDays: number;
  sortOrder: number;
  createdAt: string;
  /** True once any customer account is assigned to this tier — locks the qualifying model
   *  server-side (changing it would invalidate those customers' accumulated qualifying math)
   *  and blocks deletion. */
  hasAssignedAccounts: boolean;
}

export interface CreateTierRequest {
  name: string;
  displayName: string;
  minPoints: string;
  qualifyingModel: QualifyingModel;
  qualifyingPeriodDays?: number | null;
  graceDays: number;
  sortOrder: number;
}

export interface UpdateTierRequest {
  name?: string;
  displayName?: string;
  minPoints?: string;
  qualifyingModel?: QualifyingModel;
  qualifyingPeriodDays?: number | null;
  graceDays?: number;
}
