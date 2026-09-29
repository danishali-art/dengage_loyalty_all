import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SessionStore } from '../../core/auth/session.store';
import { ApiError } from '../../core/http/api-error';
import { Button } from '../../shared/ui/button';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Button],
  template: `
    <div class="mx-auto flex min-h-screen max-w-sm flex-col justify-center px-6">
      <div class="mb-6 text-center">
        <div
          class="mx-auto mb-3 flex h-11 w-11 items-center justify-center rounded-xl bg-brand text-lg font-bold text-white"
        >
          L·
        </div>
        <h1 class="text-lg font-bold text-gray-900">Sign in to Loyalty</h1>
      </div>

      @if (!auth.interactive) {
        <p class="text-center text-sm text-gray-500">Signing you in…</p>
      } @else {
        <form class="card space-y-4" [formGroup]="form" (ngSubmit)="submit()">
          <div>
            <label for="email" class="field-label">Email</label>
            <input id="email" type="email" formControlName="email" class="field-input" autocomplete="username" />
          </div>
          <div>
            <label for="password" class="field-label">Password</label>
            <input
              id="password"
              type="password"
              formControlName="password"
              class="field-input"
              autocomplete="current-password"
            />
          </div>
          @if (error()) {
            <p class="text-sm text-danger-fg" role="alert">{{ error() }}</p>
          }
          <app-button type="submit" [block]="true" [pending]="pending()">Sign in</app-button>
        </form>
      }
    </div>
  `,
})
export class LoginPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.fb.group({
    email: this.fb.control('', [Validators.required, Validators.email]),
    password: this.fb.control('', Validators.required),
  });

  ngOnInit(): void {
    if (this.session.isAuthenticated() || !this.auth.interactive) {
      // Stub mode auto-authenticates in its constructor; just move on.
      this.goHome();
    }
  }

  protected submit(): void {
    if (this.form.invalid || this.pending()) return;
    this.pending.set(true);
    this.error.set(null);
    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => {
        this.pending.set(false);
        this.goHome();
      },
      error: (err: unknown) => {
        this.pending.set(false);
        this.error.set(err instanceof ApiError ? err.message : 'Sign in failed. Check your credentials.');
      },
    });
  }

  private goHome(): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/programs';
    void this.router.navigateByUrl(returnUrl);
  }
}
