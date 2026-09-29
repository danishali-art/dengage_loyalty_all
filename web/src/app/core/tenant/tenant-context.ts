import { Injectable, computed, signal } from '@angular/core';

const STORAGE_KEY = 'loyalty.portal.tenant';

/**
 * The minimal "which tenant are we acting as" holder. Kept dependency-free so `ApiClient`
 * can read it without a cycle. `TenantStore` owns the richer behaviour (list, switching).
 */
@Injectable({ providedIn: 'root' })
export class TenantContext {
  private readonly _id = signal<string | null>(this.readPersisted());
  readonly activeTenantId = this._id.asReadonly();
  readonly hasTenant = computed(() => this._id() !== null);

  set(tenantId: string | null): void {
    this._id.set(tenantId);
    try {
      if (tenantId) localStorage.setItem(STORAGE_KEY, tenantId);
      else localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* storage disabled — session-only */
    }
  }

  /** Throws if read while unset — call sites are always behind `tenantResolvedGuard`. */
  require(): string {
    const id = this._id();
    if (!id) throw new Error('No active tenant. A tenant-scoped API call was made too early.');
    return id;
  }

  private readPersisted(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }
}
