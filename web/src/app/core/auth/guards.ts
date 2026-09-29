import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { SessionStore } from './session.store';
import { UserRole } from './jwt';

/** Requires an authenticated principal; otherwise routes to `/login` with a return url. */
export const authGuard: CanActivateFn = (_route, state): boolean | UrlTree => {
  // Merely injecting AuthService (unused otherwise here) matters: it's what constructs
  // StubAuthService, whose constructor auto-signs-in. Without this, the very first navigation
  // would see an empty SessionStore, redirect to /login before Stub ever got a chance to run,
  // and — since the redirect changes the URL — lose any `?stubRole=` override in the process.
  inject(AuthService);
  const session = inject(SessionStore);
  const router = inject(Router);
  if (session.isAuthenticated()) return true;
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Requires a specific role; otherwise routes to `/forbidden` (no redirect loop). */
export function roleGuard(role: UserRole): CanMatchFn {
  return (): boolean | UrlTree => {
    inject(AuthService); // see authGuard's comment above
    const session = inject(SessionStore);
    const router = inject(Router);
    if (!session.isAuthenticated()) {
      return router.createUrlTree(['/login']);
    }
    return session.hasRole(role) ? true : router.createUrlTree(['/forbidden']);
  };
}
