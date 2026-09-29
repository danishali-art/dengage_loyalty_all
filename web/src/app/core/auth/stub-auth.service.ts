import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { AuthService, LoginCredentials, LoginResult } from './auth.service';
import { SessionStore } from './session.store';
import { UserRole } from './jwt';

function base64url(json: unknown): string {
  const b64 = btoa(JSON.stringify(json));
  return b64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Builds a syntactically valid (unsigned) JWT so the real decode path is exercised. */
export function makeFakeJwt(claims: Record<string, unknown>): string {
  const header = base64url({ alg: 'none', typ: 'JWT' });
  const payload = base64url({
    iat: Math.floor(Date.now() / 1000),
    exp: Math.floor(Date.now() / 1000) + 8 * 60 * 60,
    ...claims,
  });
  return `${header}.${payload}.stub-signature`;
}

/**
 * No backend needed. Auto-signs-in a fake principal at construction so the whole app is
 * usable before the API exists. Override the role with `?stubRole=tenant_admin` in the URL.
 */
@Injectable()
export class StubAuthService extends AuthService {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  readonly interactive = false;

  constructor() {
    super();
    if (!this.session.isAuthenticated()) {
      this.session.setToken(makeFakeJwt(this.stubClaims()));
    }
  }

  login(_credentials: LoginCredentials): Observable<LoginResult> {
    const token = makeFakeJwt(this.stubClaims());
    this.session.setToken(token);
    return of({ token });
  }

  logout(): void {
    this.session.clear();
    void this.router.navigateByUrl('/login');
  }

  onUnauthorized(): void {
    // In stub mode a 401 only comes from a real backend being wired mid-session — reset.
    this.session.clear();
    void this.router.navigateByUrl('/login');
  }

  private stubClaims(): Record<string, unknown> {
    const role = this.readRoleOverride();
    return {
      sub: role === 'platform_admin' ? 'stub-platform-admin' : 'stub-tenant-admin',
      role,
      tenant_id: role === 'platform_admin' ? null : 'novapay',
    };
  }

  private readRoleOverride(): UserRole {
    try {
      const q = new URLSearchParams(location.search).get('stubRole');
      if (q === 'tenant_admin' || q === 'platform_admin') return q;
    } catch {
      /* ignore */
    }
    return 'platform_admin';
  }
}
