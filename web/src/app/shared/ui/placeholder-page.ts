import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { PageHeader } from './page-header';
import { EmptyState } from './empty-state';

/** Temporary stand-in for a screen that has not been built yet. */
@Component({
  selector: 'app-placeholder-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header [heading]="heading()" />
    <div class="mx-auto max-w-5xl px-8 py-6">
      <app-empty-state
        [heading]="heading() + ' — coming soon'"
        description="This screen is part of a later delivery phase."
        icon="🚧"
      />
    </div>
  `,
})
export class PlaceholderPage {
  readonly heading = input.required<string>();
}
