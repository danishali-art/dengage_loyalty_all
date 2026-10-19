import type { ApiSchema } from '@/shared/api';
import type { StatusTone } from '@/shared/ui';

export type ConfigVersionSummary = ApiSchema<'ConfigVersionSummary'>;
export type ConfigVersionDetail = ApiSchema<'ConfigVersionDetail'>;
export type ChangeType = ConfigVersionSummary['changeType'];

/** Mirrors ProgramsAppService.PublicationEntityType (1.3.CL item 9): one row per Publish. */
export const PUBLICATION_ENTITY_TYPE = 'ProgramPublication';
/** Per-edit rows for a program (IConfigVersionService). */
export const PROGRAM_ENTITY_TYPE = 'Program';

export const CHANGE_TONE: Record<ChangeType, StatusTone> = {
  created: 'success',
  updated: 'info',
  deleted: 'danger',
  published: 'success',
};

/**
 * Top-level keys of ProgramPublicationSnapshot (ProgramsDtos.cs), in display order. The snapshot
 * is stored with default System.Text.Json options (ConfigVersionService), so keys are PascalCase.
 */
const PUBLICATION_SECTIONS = [
  'Program',
  'AccountTypes',
  'Tiers',
  'Rewards',
  'Rules',
  'StreakCampaigns',
] as const;

export interface SnapshotSection {
  key: string;
  labelKey: string;
  count: number | null;
  json: string;
}

/** The snapshot pretty-printed, or as stored when it isn't JSON. */
export function prettySnapshot(snapshot: string): string {
  try {
    return JSON.stringify(JSON.parse(snapshot), null, 2);
  } catch {
    return snapshot;
  }
}

/** One collapsible section per part of a publish snapshot; null for per-edit rows (raw JSON view). */
export function snapshotSections(
  version: Pick<ConfigVersionDetail, 'changeType' | 'snapshot'>,
): SnapshotSection[] | null {
  if (version.changeType !== 'published') return null;
  try {
    const snapshot = JSON.parse(version.snapshot) as Record<string, unknown>;
    return PUBLICATION_SECTIONS.filter((key) => key in snapshot).map((key) => {
      const value = snapshot[key];
      return {
        key,
        labelKey: `history.snapshot.${key}`,
        count: Array.isArray(value) ? value.length : null,
        json: JSON.stringify(value, null, 2),
      };
    });
  } catch {
    return null;
  }
}
