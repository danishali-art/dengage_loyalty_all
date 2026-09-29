export interface ConfigVersionSummary {
  id: string;
  entityType: string;
  entityId: string;
  versionNumber: number;
  /** 'published' rows are 1.3.CL item 9 publish snapshots (entityType 'ProgramPublication'). */
  changeType: 'created' | 'updated' | 'deleted' | 'published';
  changeSummary: string | null;
  changedBy: string;
  changedAt: string;
}

export interface ConfigVersionDetail extends ConfigVersionSummary {
  snapshot: string;
}
