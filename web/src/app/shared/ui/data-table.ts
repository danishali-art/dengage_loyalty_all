import { ChangeDetectionStrategy, Component, contentChild, input, TemplateRef } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

export interface Column<T> {
  key: string;
  header: string;
  /** Tailwind alignment/width classes for the cell + header. */
  cellClass?: string;
  /** Value accessor when no cell template is supplied. */
  value?: (row: T) => string | number;
}

/**
 * Semantic `<table>` with a caption, projected row template and busy/empty states.
 * Provide `<ng-template #row let-row>` for cell markup, or per-column `value` accessors.
 *
 * Busy handling picks the tool for the moment rather than one spinner for everything: the
 * *first* load has nothing on screen yet, so a skeleton (ghost rows matching the real column
 * count) sets expectations for the layout about to arrive and reads as faster than a spinner
 * would. A *refetch* (paging, filtering) already has real rows on screen — replacing them with a
 * skeleton would be a step backward, so those stay visible, just dimmed, while it's busy.
 */
@Component({
  selector: 'app-data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `
    <div class="overflow-x-auto">
      <table class="w-full text-sm" [attr.aria-busy]="busy() ? 'true' : null">
        <caption class="sr-only">{{ caption() }}</caption>
        <thead>
          <tr>
            @for (col of columns(); track col.key) {
              <th
                scope="col"
                class="section-label px-4 py-3 text-left"
                [class]="col.cellClass ?? ''"
              >
                {{ col.header }}
              </th>
            }
          </tr>
        </thead>
        <tbody [class.opacity-50]="busy() && rows().length > 0" class="transition-opacity">
          @if (busy() && !rows().length) {
            @for (skeletonRow of skeletonRows; track skeletonRow) {
              <tr class="border-t border-gray-100" aria-hidden="true">
                @for (col of columns(); track col.key) {
                  <td class="px-4 py-3.5" [class]="col.cellClass ?? ''">
                    <div class="h-4 animate-pulse rounded bg-gray-200" [style.width.%]="skeletonWidth(skeletonRow, $index)"></div>
                  </td>
                }
              </tr>
            }
          } @else if (!busy() && !rows().length) {
            <tr>
              <td [attr.colspan]="columns().length" class="px-4 py-8 text-center text-gray-500">
                {{ emptyText() }}
              </td>
            </tr>
          } @else {
            @for (row of rows(); track trackKey()(row); let i = $index) {
              <tr class="border-t border-gray-100 hover:bg-gray-50">
                @if (rowTpl()) {
                  <ng-container [ngTemplateOutlet]="rowTpl()!" [ngTemplateOutletContext]="{ $implicit: row, index: i }" />
                } @else {
                  @for (col of columns(); track col.key) {
                    <td class="px-4 py-3.5" [class]="col.cellClass ?? ''">
                      {{ col.value ? col.value(row) : '' }}
                    </td>
                  }
                }
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
})
export class DataTable<T> {
  readonly columns = input.required<readonly Column<T>[]>();
  readonly rows = input.required<readonly T[]>();
  readonly caption = input.required<string>();
  readonly busy = input(false);
  readonly emptyText = input('Nothing here yet');
  readonly trackKey = input<(row: T) => unknown>((row) => row);
  /** How many ghost rows the first-load skeleton shows — tune to roughly match page size. */
  readonly skeletonRowCount = input(6);

  protected readonly rowTpl = contentChild<TemplateRef<{ $implicit: T; index: number }>>('row');

  protected get skeletonRows(): number[] {
    return Array.from({ length: this.skeletonRowCount() }, (_, i) => i);
  }

  /** Deterministic per-cell width so the skeleton looks like varied text, not a uniform grid,
   * without reshuffling on every re-render. */
  protected skeletonWidth(row: number, col: number): number {
    const widths = [88, 55, 70, 45, 82, 60];
    return widths[(row + col * 2) % widths.length] ?? 70;
  }
}
