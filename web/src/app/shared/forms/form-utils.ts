import { AbstractControl, FormArray, FormGroup } from '@angular/forms';

function childControls(control: AbstractControl): Record<string, AbstractControl> | AbstractControl[] {
  return (control as FormGroup | FormArray).controls;
}

export function markAllDirtyAndTouched(control: AbstractControl): void {
  control.markAsDirty();
  control.markAsTouched();
  if (control instanceof FormGroup || control instanceof FormArray) {
    Object.values(childControls(control)).forEach(markAllDirtyAndTouched);
  }
}

/** Dotted path of the first invalid leaf control, for scroll-to / focus. */
export function firstInvalidControlPath(group: FormGroup): string | null {
  const walk = (control: AbstractControl, path: string): string | null => {
    if (control instanceof FormGroup || control instanceof FormArray) {
      for (const [key, child] of Object.entries(childControls(control))) {
        const found = walk(child, path ? `${path}.${key}` : key);
        if (found) return found;
      }
      return null;
    }
    return control.invalid ? path : null;
  };
  return walk(group, '');
}

/** Scrolls the first invalid control / error summary into view and focuses it. */
export function scrollToFirstError(host: HTMLElement): void {
  const el = host.querySelector<HTMLElement>(
    '[aria-invalid="true"], .ng-invalid[formcontrolname], [data-form-error-summary]',
  );
  if (!el) return;
  el.scrollIntoView({ behavior: 'smooth', block: 'center' });
  el.focus({ preventScroll: true });
}
