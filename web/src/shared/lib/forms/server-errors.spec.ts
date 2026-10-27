import { describe, expect, it } from 'vitest';
import { apiErrorFrom } from '../../api/api-error';
import { mapServerErrors } from './server-errors';

describe('mapServerErrors', () => {
  it('attaches messages to known fields, case-insensitively, and keeps the rest for the form', () => {
    const error = apiErrorFrom(400, {
      title: 'Invalid',
      errors: { Slug: ['Slug already in use.'], 'conditions[2].value': ['must be numeric'] },
    });
    const mapped = mapServerErrors(error, ['name', 'slug'], 'fallback');
    expect(mapped.fields).toEqual({ slug: 'Slug already in use.' });
    expect(mapped.form).toEqual(['must be numeric']);
  });

  it('summarises an error without field errors with its title', () => {
    const mapped = mapServerErrors(
      apiErrorFrom(409, { title: 'Slug locked', code: 'slug_locked' }),
      ['slug'],
      'fallback',
    );
    expect(mapped).toEqual({ fields: {}, form: ['Slug locked'] });
  });

  it('uses the fallback for anything that is not an ApiError', () => {
    expect(mapServerErrors(new Error('boom'), ['name'], 'Something went wrong')).toEqual({
      fields: {},
      form: ['Something went wrong'],
    });
  });
});
