import { describe, expect, it } from 'vitest';
import {
  createProgramFormSchema,
  programInfoFormSchema,
  toCreateProgramRequest,
  toUpdateProgramRequest,
} from './program.schema';

describe('program form schemas', () => {
  it('accepts a create form with a blank slug (the API derives it)', () => {
    expect(createProgramFormSchema().safeParse({ name: 'Rewards', slug: '', description: '' }).success).toBe(
      true,
    );
  });

  it('rejects slugs that break ProgramSlugRules.Pattern', () => {
    for (const slug of ['A', 'has_underscore', 'double--hyphen', '-lead', 'UPPER']) {
      expect(createProgramFormSchema().safeParse({ name: 'x', slug, description: '' }).success).toBe(false);
    }
  });

  it('requires a name', () => {
    expect(createProgramFormSchema().safeParse({ name: '  ', slug: '', description: '' }).success).toBe(
      false,
    );
  });

  it('maps blank optional fields to null on create, and never sends a status', () => {
    expect(toCreateProgramRequest({ name: 'Rewards', slug: '', description: '' })).toEqual({
      name: 'Rewards',
      description: null,
      slug: null,
    });
  });

  it('sends the slug only when it changed on a draft, the rounding only when it changed', () => {
    const values = programInfoFormSchema().parse({
      name: 'N',
      slug: 'new-slug',
      description: '',
      defaultRounding: 'up',
    });
    expect(
      toUpdateProgramRequest(values, { publicationStatus: 'draft', slug: 'old', defaultRounding: 'down' }),
    ).toEqual({
      name: 'N',
      description: null,
      slug: 'new-slug',
      defaultRounding: 'up',
    });
    expect(
      toUpdateProgramRequest(values, { publicationStatus: 'published', slug: 'old', defaultRounding: 'up' }),
    ).toEqual({ name: 'N', description: null });
  });
});
