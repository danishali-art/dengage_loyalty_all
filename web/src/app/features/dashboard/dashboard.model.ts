export interface DashboardSummary {
  programCount: number;
  tierCount: number;
  customerAccountCount: number;
  /** numeric(20,4) on the wire as a string, like every other money/points field — see
   *  DecimalStringJsonConverter. Parse only for display; never for further math/storage. */
  totalBalance: string;
  redemptionCount: number;
  streakActiveCount: number;
  streakCompletedInRange: number;
}

export interface DashboardFilter {
  programId?: string | null;
  fromDate?: string | null;
  toDate?: string | null;
}
