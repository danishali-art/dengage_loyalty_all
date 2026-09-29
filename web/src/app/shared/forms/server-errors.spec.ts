import { FormArray, FormControl, FormGroup } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { applyServerErrors } from './server-errors';
import { apiErrorFromHttp } from '../../core/http/api-error';

function validationError(errors: Record<string, string[]>) {
  return apiErrorFromHttp(
    new HttpErrorResponse({ status: 400, error: { title: 'Invalid', code: 'invalid_rule', errors } }),
  );
}

describe('applyServerErrors', () => {
  it('attaches messages to matching controls and normalises bracket paths', () => {
    const form = new FormGroup({
      name: new FormControl(''),
      conditions: new FormArray([new FormGroup({ value: new FormControl('') })]),
    });

    const unmatched = applyServerErrors(form, validationError({ 'conditions[0].value': ['must be numeric'] }));

    expect(unmatched).toEqual([]);
    expect(form.get('conditions.0.value')?.errors?.['server']).toEqual(['must be numeric']);
  });

  it('clears the server error on the next edit', () => {
    const form = new FormGroup({ name: new FormControl('') });
    applyServerErrors(form, validationError({ name: ['taken'] }));
    expect(form.get('name')?.errors?.['server']).toEqual(['taken']);

    form.get('name')?.setValue('new value');
    expect(form.get('name')?.errors).toBeNull();
  });

  it('returns messages for unresolved field paths', () => {
    const form = new FormGroup({ name: new FormControl('') });
    const unmatched = applyServerErrors(form, validationError({ 'nope.deep': ['orphan'] }));
    expect(unmatched).toEqual(['orphan']);
  });

  it('surfaces a bare validation error with no field map', () => {
    const form = new FormGroup({ name: new FormControl('') });
    const unmatched = applyServerErrors(form, validationError({}));
    expect(unmatched).toEqual(['Invalid']);
  });
});
