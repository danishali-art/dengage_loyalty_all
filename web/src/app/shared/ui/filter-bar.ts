import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Button } from './button';

/**
 * One row of list filters with Search / Reset actions. Presentational only: the page owns the
 * (typed reactive) form and projects its fields in; this just lays them out and reports the
 * button presses. Submitting the form (Enter in a field) counts as Search.
 *
 * Fields and buttons sit on a single row from the 2xl breakpoint (1536px) and wrap below it. Projected
 * fields size themselves (flex-1 plus a min width); compact inputs (`field-input !py-2`) match
 * the standard button height.
 */
@Component({
  selector: 'app-filter-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button],
  template: `
    <form
      class="flex flex-wrap items-end gap-3 2xl:flex-nowrap"
      role="search"
      [attr.aria-label]="label()"
      (submit)="$event.preventDefault(); searched.emit()"
    >
      <ng-content />
      <div class="flex shrink-0 gap-2">
        <app-button type="submit" [pending]="busy()">
          {{ searchLabel() }}
        </app-button>
        <app-button type="button" variant="secondary" (click)="cleared.emit()">
          {{ resetLabel() }}
        </app-button>
      </div>
    </form>
  `,
})
export class FilterBar {
  readonly label = input.required<string>();
  readonly searchLabel = input.required<string>();
  readonly resetLabel = input.required<string>();
  readonly busy = input(false);
  readonly searched = output<void>();
  readonly cleared = output<void>();
}
