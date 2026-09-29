import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="animate-pulse rounded-md bg-gray-200"
      [style.width]="width()"
      [style.height]="height()"
      aria-hidden="true"
    ></div>
  `,
})
export class Skeleton {
  readonly width = input('100%');
  readonly height = input('1rem');
}

/** A stack of card-shaped skeletons for list placeholders. */
@Component({
  selector: 'app-skeleton-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Skeleton],
  template: `
    <div class="space-y-3" aria-busy="true" [attr.aria-label]="label()">
      @for (row of rows; track $index) {
        <div class="card">
          <app-skeleton width="40%" height="1.1rem" />
          <div class="mt-3">
            <app-skeleton width="70%" height="0.85rem" />
          </div>
        </div>
      }
    </div>
  `,
})
export class SkeletonList {
  readonly count = input(4);
  readonly label = input('Loading');
  protected get rows(): number[] {
    return Array.from({ length: this.count() });
  }
}
