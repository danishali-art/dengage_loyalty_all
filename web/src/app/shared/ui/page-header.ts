import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Breadcrumbs, Crumb } from './breadcrumbs';

/**
 * The white bar under the top bar: breadcrumb + H1 + optional subtitle on the left,
 * projected actions on the right (`<app-page-header actions>…</app-page-header>` slot).
 * Optional `[tabs]` slot: page tabs drawn along the bottom edge of the bar; pages without it
 * render exactly as before.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Breadcrumbs],
  template: `
    <header class="flex-shrink-0 border-b border-gray-200 bg-white px-8">
      <div class="flex items-start justify-between gap-4 py-4">
        <div class="min-w-0">
          @if (crumbs().length) {
            <app-breadcrumbs [crumbs]="crumbs()" class="mb-1 block" />
          }
          <h1 class="text-lg font-bold text-gray-900">{{ heading() }}</h1>
          @if (subtitle()) {
            <p class="mt-0.5 text-sm text-gray-500">{{ subtitle() }}</p>
          }
        </div>
        <div class="flex flex-shrink-0 gap-2">
          <ng-content select="[actions]" />
        </div>
      </div>
      <ng-content select="[tabs]" />
    </header>
  `,
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly subtitle = input<string>('');
  readonly crumbs = input<readonly Crumb[]>([]);
}
