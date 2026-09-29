import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/** Offset paginator. Two-way `page` via `pageChange`; `pageSize` is display-only here. */
@Component({
  selector: 'app-paginator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex items-center justify-between gap-4 border-t border-gray-100 py-3 text-sm text-gray-500">
      <span aria-live="polite">{{ rangeLabel() }}</span>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-1.5 font-medium text-gray-600 transition-colors hover:border-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:border-gray-200 disabled:hover:bg-transparent"
          [disabled]="page() <= 1"
          (click)="go(page() - 1)"
        >
          ‹ Prev
        </button>
        <span class="px-2 font-medium text-gray-700">Page {{ page() }} of {{ lastPage() }}</span>
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-1.5 font-medium text-gray-600 transition-colors hover:border-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:border-gray-200 disabled:hover:bg-transparent"
          [disabled]="page() >= lastPage()"
          (click)="go(page() + 1)"
        >
          Next ›
        </button>
      </div>
    </div>
  `,
})
export class Paginator {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly lastPage = computed(() =>
    Math.max(1, Math.ceil(this.total() / Math.max(1, this.pageSize()))),
  );

  protected readonly rangeLabel = computed(() => {
    if (this.total() === 0) return '0 results';
    const from = (this.page() - 1) * this.pageSize() + 1;
    const to = Math.min(this.total(), this.page() * this.pageSize());
    return `${from}–${to} of ${this.total()}`;
  });

  protected go(next: number): void {
    const clamped = Math.min(this.lastPage(), Math.max(1, next));
    if (clamped !== this.page()) this.pageChange.emit(clamped);
  }
}
