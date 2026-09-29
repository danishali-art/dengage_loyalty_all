import { FormGroup } from '@angular/forms';
import { ApiError } from '../../core/http/api-error';

/**
 * Maps an `ApiError`'s `fieldErrors` (`{ "conditions[2].value": ["…"] }`) back onto the
 * matching form controls as `{ server: string[] }`, clearing on the next edit. Field paths
 * that don't resolve are collected and returned for a form-level summary.
 *
 * @returns messages that could not be attached to a control.
 */
export function applyServerErrors(form: FormGroup, apiError: ApiError): string[] {
  const unmatched: string[] = [];

  for (const [rawPath, messages] of Object.entries(apiError.fieldErrors)) {
    const path = rawPath.replace(/\[(\d+)\]/g, '.$1'); // conditions[2].value -> conditions.2.value
    const control = form.get(path);
    if (control) {
      control.setErrors({ ...(control.errors ?? {}), server: messages });
      control.markAsTouched();
      const sub = control.valueChanges.subscribe(() => {
        const rest = { ...(control.errors ?? {}) };
        delete rest['server'];
        control.setErrors(Object.keys(rest).length ? rest : null);
        sub.unsubscribe();
      });
    } else {
      unmatched.push(...messages);
    }
  }

  // A validation error with no field map at all still deserves a summary line.
  if (unmatched.length === 0 && Object.keys(apiError.fieldErrors).length === 0 && apiError.isValidation) {
    unmatched.push(apiError.message);
  }
  return unmatched;
}
