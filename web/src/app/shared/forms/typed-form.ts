import { AbstractControl, FormArray, FormControl, FormGroup } from '@angular/forms';

/**
 * Maps a plain shape `T` to the matching `FormGroup` controls shape, so a form can be
 * declared as `FormGroup<ControlsOf<MyModel>>`.
 */
export type ControlsOf<T> = {
  [K in keyof T]: T[K] extends readonly (infer U)[]
    ? U extends Record<string, unknown>
      ? FormArray<FormGroup<ControlsOf<U>>>
      : FormArray<FormControl<U>>
    : T[K] extends Record<string, unknown> | undefined
      ? FormGroup<ControlsOf<NonNullable<T[K]>>>
      : FormControl<T[K]>;
};

/** Narrow an `AbstractControl` to a typed `FormGroup` at a known path (throws if wrong). */
export function requireGroup<T extends Record<string, AbstractControl>>(
  control: AbstractControl | null,
): FormGroup<T> {
  if (!(control instanceof FormGroup)) throw new Error('Expected a FormGroup at this path');
  return control as FormGroup<T>;
}

export function requireArray<T extends AbstractControl>(control: AbstractControl | null): FormArray<T> {
  if (!(control instanceof FormArray)) throw new Error('Expected a FormArray at this path');
  return control as FormArray<T>;
}
