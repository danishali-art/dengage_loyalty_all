import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

type Predicate = (root: AbstractControl) => boolean;

function rootOf(control: AbstractControl): AbstractControl {
  let node = control;
  while (node.parent) node = node.parent;
  return node;
}

function isEmpty(value: unknown): boolean {
  return value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
}

/**
 * `Validators.required`, but only when `predicate(root)` is true. Pair with an
 * `effect`/`valueChanges` that calls `updateValueAndValidity()` when the predicate inputs
 * change — validators are not reactive on their own.
 */
export function requiredWhen(predicate: Predicate): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!predicate(rootOf(control))) return null;
    return isEmpty(control.value) ? { required: true } : null;
  };
}

/**
 * All-or-nothing on a group: when active, every listed child must be filled;
 * when inactive, every listed child must be empty. Applied to the parent group.
 */
export function requiredGroupWhen(predicate: Predicate, fields: readonly string[]): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const active = predicate(rootOf(group));
    const values: unknown[] = fields.map((f) => group.get(f)?.value as unknown);
    if (active) {
      return values.every((v) => !isEmpty(v)) ? null : { groupIncomplete: true };
    }
    return values.every((v) => isEmpty(v)) ? null : { groupShouldBeEmpty: true };
  };
}

/** At least one of the named sibling controls must be non-empty. Applied to the parent group. */
export function atLeastOne(fields: readonly string[]): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const some = fields.some((f) => !isEmpty(group.get(f)?.value as unknown));
    return some ? null : { atLeastOne: true };
  };
}

/** `group.get(a) < group.get(b)` when both are present. Applied to the parent group. */
export function crossFieldLt(aPath: string, bPath: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const a = group.get(aPath)?.value as unknown;
    const b = group.get(bPath)?.value as unknown;
    if (isEmpty(a) || isEmpty(b)) return null;
    const na = Number(a);
    const nb = Number(b);
    const ordered = Number.isFinite(na) && Number.isFinite(nb) ? na < nb : String(a) < String(b);
    return ordered ? null : { range: { aPath, bPath } };
  };
}
