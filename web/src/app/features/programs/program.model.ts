/** 1.3.CL item 8 — mirrors ProgramPublicationStatus.cs. */
export type ProgramPublicationStatus = 'draft' | 'published';

export interface Program {
  id: string;
  name: string;
  description: string | null;
  status: 'active' | 'inactive';
  /** Deprecated (1.3.CL item 1): derived from the account type flagged `isTierQualifying`. Read-only. */
  qualifyingAccountTypeId: string | null;
  /** Deprecated (1.3.CL item 1): always null — warning days live on the POINTS account type config. */
  warningDays: number | null;
  createdAt: string;
  accountTypeCount: number;
  ruleCount: number;
  /** 1.3.CL item 8: a draft can't be activated; the engine only runs published + active programs. */
  publicationStatus: ProgramPublicationStatus;
  /** Something changed since the last publish (never true for a draft). */
  hasUnpublishedChanges: boolean;
  /** The ProgramPublication history version of the last publish; null while a draft. */
  publishedVersion: number | null;
  publishedAt: string | null;
  publishedBy: string | null;
}

/**
 * A new program always starts as an inactive draft (1.3.CL item 8), so there is no `status` here.
 * qualifyingAccountTypeId / warningDays are no longer accepted either (the API returns 400).
 */
export interface CreateProgramRequest {
  name: string;
  description?: string | null;
}

export interface UpdateProgramRequest {
  name?: string;
  description?: string | null;
  /** Only 'active' once published — the API returns 409 program_not_published for a draft. */
  status?: 'active' | 'inactive';
}
