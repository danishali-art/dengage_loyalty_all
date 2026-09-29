import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Sidebar } from '../sidebar/sidebar';
import { Topbar } from '../topbar/topbar';
import { ProgressBar } from '../../shared/ui/progress-bar';
import { ToastContainer } from '../../shared/ui/toast-container';
import { LoadingService } from '../../core/ui/loading.service';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, Sidebar, Topbar, ProgressBar, ToastContainer],
  template: `
    <app-progress-bar [active]="loading.isLoading()" />
    <a
      href="#main-content"
      class="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-[70] focus:rounded-lg focus:bg-white focus:px-3 focus:py-2 focus:text-sm focus:shadow-card"
    >
      Skip to content
    </a>

    <div class="flex h-screen overflow-hidden">
      <app-sidebar />
      <div class="flex flex-1 flex-col overflow-hidden">
        <app-topbar />
        <main id="main-content" class="flex-1 overflow-y-auto" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>

    <app-toast-container />
  `,
})
export class Shell {
  protected readonly loading = inject(LoadingService);
}
