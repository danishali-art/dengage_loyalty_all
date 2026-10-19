import { z } from 'zod';
import { translate } from '@/shared/i18n';
import {
  PROGRAM_NAME_MAX,
  PROGRAM_SLUG_MAX,
  PROGRAM_SLUG_MIN,
  PROGRAM_SLUG_PATTERN,
  ROUNDING_DIRECTIONS,
  type CreateProgramRequest,
  type RoundingDirection,
  type UpdateProgramRequest,
} from './types';

/**
 * Form schemas validate what the admin types (blank strings, not nulls); the mappers below turn
 * the values into the wire request, typed against the generated request types — so a backend
 * DTO change breaks the build here instead of failing at runtime.
 */
function nameField() {
  return z
    .string()
    .trim()
    .min(1, translate('programs.form.errors.nameRequired'))
    .max(PROGRAM_NAME_MAX, translate('programs.form.errors.nameTooLong', { max: PROGRAM_NAME_MAX }));
}

function slugField() {
  return z
    .string()
    .trim()
    .min(
      PROGRAM_SLUG_MIN,
      translate('programs.form.errors.slugLength', { min: PROGRAM_SLUG_MIN, max: PROGRAM_SLUG_MAX }),
    )
    .max(
      PROGRAM_SLUG_MAX,
      translate('programs.form.errors.slugLength', { min: PROGRAM_SLUG_MIN, max: PROGRAM_SLUG_MAX }),
    )
    .regex(PROGRAM_SLUG_PATTERN, translate('programs.form.errors.slugPattern'));
}

export function createProgramFormSchema() {
  return z.object({
    name: nameField(),
    // Optional on create: the API derives it from the name when left empty.
    slug: z.union([z.literal(''), slugField()]),
    description: z.string(),
  });
}
export type CreateProgramFormValues = z.infer<ReturnType<typeof createProgramFormSchema>>;

export function toCreateProgramRequest(values: CreateProgramFormValues): CreateProgramRequest {
  // 1.3.CL item 8: a new program is always an inactive draft — no status is sent.
  return { name: values.name, description: values.description || null, slug: values.slug || null };
}

export function programInfoFormSchema() {
  return z.object({
    name: nameField(),
    slug: slugField(),
    description: z.string(),
    defaultRounding: z.enum(ROUNDING_DIRECTIONS as [RoundingDirection, ...RoundingDirection[]]),
  });
}
export type ProgramInfoFormValues = z.infer<ReturnType<typeof programInfoFormSchema>>;

/**
 * Status isn't part of Save — the Active switch applies it on its own (1.3.CL item 7). The slug is
 * only sent when changed on a draft (the API locks it once published, A5), and the rounding only
 * when changed (on a published program it marks a publish pending).
 */
export function toUpdateProgramRequest(
  values: ProgramInfoFormValues,
  current: { publicationStatus: string; slug: string; defaultRounding: RoundingDirection },
): UpdateProgramRequest {
  const slugChanged = current.publicationStatus === 'draft' && values.slug !== current.slug;
  return {
    name: values.name,
    description: values.description || null,
    ...(slugChanged ? { slug: values.slug } : {}),
    ...(values.defaultRounding !== current.defaultRounding
      ? { defaultRounding: values.defaultRounding }
      : {}),
  };
}
