import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SessionStore } from '../../core/auth/session.store';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-user-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <div class="relative">
      <button
        type="button"
        class="flex h-8 w-8 items-center justify-center rounded-full bg-navy text-xs font-bold text-white"
        [attr.aria-expanded]="open()"
        aria-haspopup="menu"
        [attr.aria-label]="userId()"
        (click)="open.set(!open())"
      >
        {{ initial() }}
      </button>
      @if (open()) {
        <div
          role="menu"
          class="absolute right-0 z-50 mt-1 w-56 rounded-lg border border-gray-200 bg-white py-1 shadow-card"
        >
          <div class="px-3 py-2 text-xs text-gray-500">
            <div>{{ role() }}</div>
          </div>
          <button
            type="button"
            role="menuitem"
            class="w-full px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50"
            (click)="signOut()"
          >
            {{ 'auth.signOut' | translate }}
          </button>
        </div>
      }
    </div>
  `,
})
export class UserMenu {
  private readonly session = inject(SessionStore);
  private readonly auth = inject(AuthService);
  protected readonly open = signal(false);

  protected userId(): string {
    return this.session.principal()?.userId ?? 'unknown';
  }
  protected role(): string {
    return this.session.role() ?? '';
  }
  protected initial(): string {
    return (this.userId()[0] ?? '?').toUpperCase();
  }

  protected signOut(): void {
    this.open.set(false);
    this.auth.logout();
  }
}
