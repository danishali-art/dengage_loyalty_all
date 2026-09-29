import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Text shown directly under a form field: an error (danger token) takes priority over a plain
 * hint (e.g. "Type cannot be changed after creation."). Pair with `[attr.aria-invalid]` on the
 * field itself — this component only renders the text, it doesn't drive the red border.
 */
@Component({
  selector: 'app-field-hint',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error(); as e) {
      <p [id]="id()" class="mt-1 text-xs text-danger-fg">{{ e }}</p>
    } @else if (hint(); as h) {
      <p [id]="id()" class="mt-1 text-xs text-gray-500">{{ h }}</p>
    }
  `,
})
export class FieldHint {
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
  /** Pass the field's `aria-describedby` target so the message is announced on focus. */
  readonly id = input<string | null>(null);
}
