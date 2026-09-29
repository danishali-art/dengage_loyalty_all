import { Injectable } from '@angular/core';

const KEY = 'loyalty.portal.token';

/** Where the access token lives. Swap the binding to change the persistence strategy. */
@Injectable({ providedIn: 'root' })
export abstract class TokenStorage {
  abstract get(): string | null;
  abstract set(token: string | null): void;
}

/** Survives reload. Fine for an internal admin tool; revisit if XSS surface grows. */
@Injectable()
export class LocalStorageTokenStorage extends TokenStorage {
  get(): string | null {
    try {
      return localStorage.getItem(KEY);
    } catch {
      return null;
    }
  }
  set(token: string | null): void {
    try {
      if (token) localStorage.setItem(KEY, token);
      else localStorage.removeItem(KEY);
    } catch {
      // storage disabled — fall back to a memory-only session
    }
  }
}

/** Lost on reload — used by tests and the stub auth path. */
@Injectable()
export class InMemoryTokenStorage extends TokenStorage {
  private token: string | null = null;
  get(): string | null {
    return this.token;
  }
  set(token: string | null): void {
    this.token = token;
  }
}
