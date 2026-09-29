import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="flex min-h-screen flex-col items-center justify-center bg-page px-6 text-center">
      <div class="text-5xl font-bold text-gray-500">404</div>
      <h1 class="mt-3 text-lg font-bold text-gray-900">{{ 'notFound.title' | translate }}</h1>
      <p class="mt-1 max-w-sm text-sm text-gray-500">{{ 'notFound.body' | translate }}</p>
      <a
        routerLink="/programs"
        class="mt-5 rounded-lg bg-brand px-4 py-2 text-sm font-medium text-white hover:bg-brand-dark"
      >
        {{ 'nav.programs' | translate }}
      </a>
    </div>
  `,
})
export class NotFoundPage {}
