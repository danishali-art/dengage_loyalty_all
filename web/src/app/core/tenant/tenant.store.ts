import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../http/api-client';
import { SessionStore } from '../auth/session.store';
import { APP_CONFIG } from '../config/app-config';
import { TenantContext } from './tenant-context';
import { Tenant } from './tenant.model';

const PICKER_PAGE_SIZE = 20;

/**
 * Owns tenant selection behaviour on top of the bare `TenantContext`:
 * the list (platform_admin), the current tenant object, and switching.
 */
@Injectable({ providedIn: 'root' })
export class TenantStore {
  private readonly ctx = inject(TenantContext);
  private readonly session = inject(SessionStore);
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);
  private readonly config = inject(APP_CONFIG);

  private readonly _tenants = signal<readonly Tenant[]>([]);
  private readonly _loaded = signal(false);

  readonly activeTenantId = this.ctx.activeTenantId;
  readonly availableTenants = this._tenants.asReadonly();
  readonly tenantsLoaded = this._loaded.asReadonly();
  readonly isSwitchable = computed(() => this.session.role() === 'platform_admin');

  /** Search results for the tenant picker popup — separate from `availableTenants`, which
   * stays a small unfiltered cache used only to resolve the active tenant's display name. */
  private readonly _pickerResults = signal<readonly Tenant[]>([]);
  private readonly _pickerLoading = signal(false);
  private readonly _pickerPage = signal(1);
  private readonly _pickerTotal = signal(0);
  readonly pickerResults = this._pickerResults.asReadonly();
  readonly pickerLoading = this._pickerLoading.asReadonly();
  readonly pickerPage = this._pickerPage.asReadonly();
  readonly pickerTotal = this._pickerTotal.asReadonly();
  readonly pickerPageSize = PICKER_PAGE_SIZE;

  readonly activeTenant = computed<Tenant | null>(() => {
    const id = this.ctx.activeTenantId();
    if (!id) return null;
    return this._tenants().find((t) => t.id === id) ?? { id, name: id, status: 'active', createdAt: '' };
  });

  /** For a tenant_admin the tenant is fixed by the JWT claim. */
  initFromSession(): void {
    const claimTenant = this.session.principal()?.tenantId ?? null;
    if (claimTenant && this.ctx.activeTenantId() !== claimTenant) {
      this.ctx.set(claimTenant);
    }
  }

  /**
   * Fetches the active tenant's own record if the label cache doesn't have it yet — covers a
   * platform_admin reloading the app with a tenant already persisted from a previous session,
   * where nothing else would otherwise populate its display name.
   */
  async ensureActiveTenantCached(): Promise<void> {
    const id = this.ctx.activeTenantId();
    if (!id || !this.isSwitchable() || this._tenants().some((t) => t.id === id)) return;
    try {
      const tenant = await firstValueFrom(this.api.platform.get<Tenant>(`tenants/${id}`));
      this._tenants.update((list) => (list.some((t) => t.id === id) ? list : [...list, tenant]));
    } catch {
      /* label falls back to the raw id — not fatal */
    }
  }

  async loadTenants(): Promise<void> {
    if (this._loaded() || !this.isSwitchable()) return;
    try {
      const page = await firstValueFrom(
        this.api.platform.getPage<Tenant>('tenants', {
          page: 1,
          pageSize: this.config.platformTenantPageSize,
        }),
      );
      this._tenants.set(page.data ?? []);
      this._loaded.set(true);
    } catch {
      this._loaded.set(true); // don't wedge the shell; switcher just shows nothing
    }
  }

  /**
   * Searches tenants by name/id for the picker popup — independent of `loadTenants`'s cache,
   * since the picker's whole point is reaching tenants that cache doesn't include.
   */
  async searchTenants(query: string, page = 1): Promise<void> {
    this._pickerLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.api.platform.getPage<Tenant>('tenants', {
          page,
          pageSize: PICKER_PAGE_SIZE,
          search: query || undefined,
        }),
      );
      this._pickerResults.set(result.data);
      this._pickerPage.set(result.page);
      this._pickerTotal.set(result.total);
    } finally {
      this._pickerLoading.set(false);
    }
  }

  /** Switch tenant and reset routed state (programIds are tenant-specific). */
  async setTenant(tenantId: string, knownTenant?: Tenant): Promise<void> {
    if (knownTenant) {
      this._tenants.update((list) => (list.some((t) => t.id === knownTenant.id) ? list : [...list, knownTenant]));
    }
    if (this.ctx.activeTenantId() === tenantId) return;
    this.ctx.set(tenantId);
    await this.router.navigateByUrl('/programs');
  }
}
