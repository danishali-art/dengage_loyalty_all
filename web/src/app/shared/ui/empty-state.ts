import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="flex flex-col items-center justify-center rounded-xl border border-dashed border-gray-300 bg-white px-6 py-14 text-center">
      @if (icon()) {
        <div class="mb-3 text-3xl" aria-hidden="true">{{ icon() }}</div>
      }
      <h2 class="text-sm font-semibold text-gray-800">{{ heading() }}</h2>
      @if (description()) {
        <p class="mt-1 max-w-sm text-sm text-gray-500">{{ description() }}</p>
      }
      <div class="mt-4 empty:hidden">
        <ng-content />
      </div>
    </section>
  `,
})
export class EmptyState {
  readonly heading = input.required<string>();
  readonly description = input<string>('');
  readonly icon = input<string>('');
}
