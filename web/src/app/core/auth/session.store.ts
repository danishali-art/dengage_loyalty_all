import { Injectable, computed, inject, signal } from '@angular/core';
import { JwtClaims, UserRole, decodeJwt, isJwtExpired } from './jwt';
import { TokenStorage } from './token-storage';

export interface Principal {
  readonly userId: string;
  readonly role: UserRole;
  /** The tenant this user is bound to; `null` for a platform_admin. */
  readonly tenantId: string | null;
}

/**
 * Single source of truth for "who is signed in". Derived from the JWT only —
 * guards, interceptors and the `*appHasRole` directive read this, never a concrete AuthService.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly storage = inject(TokenStorage);

  private readonly _token = signal<string | null>(this.storage.get());
  private readonly _claims = computed<JwtClaims | null>(() => {
    const t = this._token();
    return t ? decodeJwt(t) : null;
  });

  readonly token = this._token.asReadonly();

  readonly principal = computed<Principal | null>(() => {
    const c = this._claims();
    if (!c || isJwtExpired(c)) return null;
    return { userId: c.sub, role: c.role, tenantId: c.tenant_id ?? null };
  });

  readonly role = computed<UserRole | null>(() => this.principal()?.role ?? null);
  readonly isAuthenticated = computed(() => this.principal() !== null);
  readonly isPlatformAdmin = computed(() => this.role() === 'platform_admin');

  setToken(token: string): void {
    this.storage.set(token);
    this._token.set(token);
  }

  clear(): void {
    this.storage.set(null);
    this._token.set(null);
  }

  hasRole(role: UserRole): boolean {
    return this.role() === role;
  }
}
