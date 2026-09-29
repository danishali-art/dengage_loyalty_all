import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Visual chrome for a CDK-dialog-hosted component: titled header with a close button,
 * scrollable body slot, and a footer slot for actions. CDK provides the a11y (focus
 * trap/restore, `role="dialog"`, `aria-modal`, Esc).
 */
@Component({
  selector: 'app-dialog-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="flex max-h-[85vh] w-[32rem] max-w-[92vw] flex-col overflow-hidden rounded-xl bg-white shadow-card"
    >
      <header class="flex items-start justify-between border-b border-gray-200 bg-brand-light px-5 py-4">
        <h2 class="text-sm font-bold text-heading">{{ heading() }}</h2>
        <button
          type="button"
          class="cursor-pointer rounded-lg p-1 text-gray-500 transition-colors hover:bg-white/60"
          aria-label="Close dialog"
          (click)="closed.emit()"
        >
          ✕
        </button>
      </header>
      <div class="flex-1 overflow-y-auto px-5 py-4">
        <ng-content />
      </div>
      <footer class="flex justify-end gap-2 border-t border-gray-200 px-5 py-3">
        <ng-content select="[footer]" />
      </footer>
    </div>
  `,
})
export class DialogShell {
  readonly heading = input.required<string>();
  readonly closed = output<void>();
}
