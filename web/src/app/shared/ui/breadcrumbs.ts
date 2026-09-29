import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface Crumb {
  label: string;
  /** Router link commands; omit for the current (non-link) page. */
  link?: unknown[];
}

@Component({
  selector: 'app-breadcrumbs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <nav aria-label="Breadcrumb">
      <ol class="flex flex-wrap items-center gap-2 text-sm text-gray-500">
        @for (crumb of crumbs(); track $index; let last = $last) {
          <li class="flex items-center gap-2">
            @if (crumb.link && !last) {
              <a [routerLink]="crumb.link" class="text-brand hover:text-brand-dark">{{ crumb.label }}</a>
            } @else {
              <span class="font-medium text-gray-700" [attr.aria-current]="last ? 'page' : null">
                {{ crumb.label }}
              </span>
            }
            @if (!last) {
              <span aria-hidden="true">›</span>
            }
          </li>
        }
      </ol>
    </nav>
  `,
})
export class Breadcrumbs {
  readonly crumbs = input.required<readonly Crumb[]>();
}
