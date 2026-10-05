import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Visual chrome for a CDK-dialog-hosted drawer docked to the right edge: titled header with a
 * close button and a scrollable body. Like `DialogShell`, CDK provides the a11y (focus
 * trap/restore, `role="dialog"`, `aria-modal`, Esc); open it with `DialogService.openSidePanel`.
 */
@Component({
  selector: 'app-side-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="shadow-card flex h-screen w-[40rem] max-w-[100vw] flex-col overflow-hidden bg-white"
    >
      <header
        class="bg-brand-light flex items-start justify-between gap-3 border-b border-gray-200 px-5 py-4"
      >
        <div class="min-w-0">
          <h2 class="text-heading text-sm font-bold">{{ heading() }}</h2>
          @if (subheading()) {
            <p class="mt-0.5 truncate font-mono text-xs text-gray-500">{{ subheading() }}</p>
          }
        </div>
        <button
          type="button"
          class="cursor-pointer rounded-lg p-1 text-gray-500 transition-colors hover:bg-white/60"
          [attr.aria-label]="closeLabel()"
          (click)="closed.emit()"
        >
          ✕
        </button>
      </header>
      <div class="flex-1 overflow-y-auto px-5 py-4" [attr.aria-busy]="busy() ? 'true' : null">
        <ng-content />
      </div>
    </div>
  `,
})
export class SidePanel {
  readonly heading = input.required<string>();
  readonly subheading = input('');
  readonly closeLabel = input('Close');
  readonly busy = input(false);
  readonly closed = output<void>();
}
