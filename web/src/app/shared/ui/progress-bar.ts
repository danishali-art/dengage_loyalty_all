import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Thin indeterminate bar pinned to the top of the shell while requests are in flight. */
@Component({
  selector: 'app-progress-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (active()) {
      <div
        class="pointer-events-none fixed inset-x-0 top-0 z-50 h-0.5 overflow-hidden bg-brand-light"
        role="progressbar"
        aria-label="Loading"
      >
        <div class="h-full w-1/3 animate-[indeterminate_1.1s_ease-in-out_infinite] bg-brand"></div>
      </div>
    }
  `,
  styles: `
    @keyframes indeterminate {
      0% {
        transform: translateX(-100%);
      }
      100% {
        transform: translateX(400%);
      }
    }
  `,
})
export class ProgressBar {
  readonly active = input(false);
}
