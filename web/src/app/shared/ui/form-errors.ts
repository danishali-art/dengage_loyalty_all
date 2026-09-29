import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input } from '@angular/core';

/**
 * Focus-managed summary of form-level errors (unmatched server messages, cross-field
 * failures). Render it just above the form's submit row; it grabs focus when it appears.
 */
@Component({
  selector: 'app-form-errors',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (messages().length) {
      <div
        data-form-error-summary
        role="alert"
        tabindex="-1"
        class="rounded-lg border border-danger-border bg-danger-bg p-3 text-sm text-danger-fg"
      >
        <p class="font-medium">{{ title() }}</p>
        <ul class="mt-1 list-disc pl-5">
          @for (message of messages(); track $index) {
            <li>{{ message }}</li>
          }
        </ul>
      </div>
    }
  `,
})
export class FormErrors {
  readonly messages = input.required<readonly string[]>();
  readonly title = input('Please fix the following');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    effect(() => {
      if (this.messages().length) {
        queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>('[role="alert"]')?.focus());
      }
    });
  }
}
