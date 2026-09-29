import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Icon-only button. `ariaLabel` is required (lint-enforced project-wide). */
@Component({
  selector: 'app-icon-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [type]="type()"
      [disabled]="disabled()"
      [attr.aria-label]="ariaLabel()"
      [attr.title]="ariaLabel()"
      class="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-lg text-gray-500 transition-all duration-150 hover:bg-gray-100 hover:text-gray-700 active:scale-[0.94] disabled:cursor-not-allowed disabled:opacity-50 disabled:active:scale-100"
    >
      <ng-content />
    </button>
  `,
})
export class IconButton {
  readonly ariaLabel = input.required<string>();
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input(false);
}
