import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SessionStore } from '../../auth/session.store';
import { AuthService } from '../../auth/auth.service';

/**
 * Attaches the bearer token; on 401 hands off to `AuthService.onUnauthorized()`.
 *
 * Bootstrap requests (app-config.json, the i18n loader) must skip `inject(AuthService)`
 * entirely: `AuthService`'s factory reads `APP_CONFIG.authMode`, and `APP_CONFIG` is only
 * populated once the app-config.json fetch itself resolves. Injecting `AuthService` while
 * that very fetch is in flight would force it to construct against the pre-fetch fallback
 * config (`authMode: 'stub'`) and — being a singleton — permanently lock in the wrong
 * implementation regardless of what the real deployed config says.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const isBootstrapRequest = req.url.includes('app-config.json') || req.url.includes('i18n/');
  if (isBootstrapRequest) return next(req);

  const session = inject(SessionStore);
  const auth = inject(AuthService);

  const isLogin = req.url.includes('/auth/login');
  const token = session.token();
  const authed = token && !isLogin ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authed).pipe(
    catchError((err: unknown) => {
      if (typeof err === 'object' && err !== null && 'status' in err && err.status === 401 && !isLogin) {
        auth.onUnauthorized();
      }
      return throwError(() => err);
    }),
  );
};
