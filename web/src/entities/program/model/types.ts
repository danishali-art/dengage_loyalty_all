import type { ApiSchema } from '@/shared/api';

/** Wire types come from the generated OpenAPI types — never hand-written copies of the DTOs. */
export type Program = ApiSchema<'ProgramResponse'>;
export type CreateProgramRequest = ApiSchema<'CreateProgramRequest'>;
export type UpdateProgramRequest = ApiSchema<'UpdateProgramRequest'>;
export type RoundingDirection = Program['defaultRounding'];

/** CR 2026-10-06 Phase 4: RoundingDirection.All (Shared/Constants/RoundingDirection.cs). */
export const ROUNDING_DIRECTIONS: readonly RoundingDirection[] = ['down', 'nearest', 'up'];

/**
 * CR 2026-09-30 (A5): mirrors ProgramSlugRules.Pattern (ProgramsValidators.cs) — lowercase
 * letters, digits and single hyphens, never `_` (rewards are named `{slug}_{suffix}`).
 */
export const PROGRAM_SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
export const PROGRAM_SLUG_MIN = 2;
export const PROGRAM_SLUG_MAX = 40;
export const PROGRAM_NAME_MAX = 255;

/** 1.3.CL item 8: a draft can always be published; a published program only when something changed. */
export function canPublish(program: Program): boolean {
  return program.publicationStatus === 'draft' || program.hasUnpublishedChanges;
}

export function isRunning(program: Program): boolean {
  return program.status === 'active';
}
