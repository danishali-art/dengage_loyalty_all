import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthService, LoginCredentials, LoginResult } from './auth.service';
import { SessionStore } from './session.store';
import { ApiClient } from '../http/api-client';

/**
 * Real auth against `POST /api/v1/auth/login`. Swapped in via `APP_CONFIG.authMode === 'http'`.
 * No refresh endpoint exists yet, so a 401 ends the session.
 */
@Injectable()
export class HttpAuthService extends AuthService {
  private readonly api = inject(ApiClient);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  readonly interactive = true;

  login(credentials: LoginCredentials): Observable<LoginResult> {
    return this.api.auth
      .post<LoginResult>('login', credentials, { skipErrorToast: true })
      .pipe(tap((res) => this.session.setToken(res.token)));
  }

  logout(): void {
    this.session.clear();
    void this.router.navigateByUrl('/login');
  }

  onUnauthorized(): void {
    this.session.clear();
    void this.router.navigateByUrl('/login', {
      state: { reason: 'session-expired' },
    });
  }
}
