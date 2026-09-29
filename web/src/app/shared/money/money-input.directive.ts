import { Directive, HostListener, ElementRef, inject } from '@angular/core';
import { NgControl } from '@angular/forms';
import { isDecimalString } from './decimal-string';

/**
 * Keeps a text input's control value a decimal **string**: blocks obviously invalid
 * keystrokes, normalises on blur, and flags a bad shape as `{ decimal: true }`.
 * Use on `<input type="text" appMoneyInput formControlName="…">`.
 */
@Directive({ selector: 'input[appMoneyInput]' })
export class MoneyInputDirective {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  private readonly ngControl = inject(NgControl, { optional: true });

  @HostListener('input')
  onInput(): void {
    const raw = this.el.nativeElement.value;
    // Allow digits, one dot, optional leading minus, while typing.
    const cleaned = raw.replace(/[^\d.-]/g, '');
    if (cleaned !== raw) {
      this.el.nativeElement.value = cleaned;
      this.ngControl?.control?.setValue(cleaned, { emitEvent: false });
    }
  }

  @HostListener('blur')
  onBlur(): void {
    const ctrl = this.ngControl?.control;
    if (!ctrl) return;
    const value = String(ctrl.value ?? '').trim();
    if (value === '') return;
    if (!isDecimalString(value)) {
      ctrl.setErrors({ ...(ctrl.errors ?? {}), decimal: true });
      return;
    }
    // Normalise "5." -> "5", ".5" -> "0.5"
    const normalised = String(Number(value));
    if (normalised !== value && isDecimalString(normalised)) {
      ctrl.setValue(normalised);
    }
  }
}
