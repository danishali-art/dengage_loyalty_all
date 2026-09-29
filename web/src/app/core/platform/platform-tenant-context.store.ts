import { Injectable, signal } from '@angular/core';
import { Tenant } from '../tenant/tenant.model';

/**
 * Route-scoped context for "which tenant is currently open in Platform's tenant detail view" —
 * deliberately separate from `TenantContext`/`TenantStore` (which tracks the platform_admin's own
 * *active working tenant* for Programs/Customers API calls). Browsing another tenant's API keys
 * here must never change which tenant their own Programs/Customers screens are scoped to.
 */
@Injectable({ providedIn: 'root' })
export class PlatformTenantContextStore {
  private readonly _tenantId = signal<string | null>(null);
  private readonly _tenant = signal<Tenant | null>(null);

  readonly tenantId = this._tenantId.asReadonly();
  readonly tenant = this._tenant.asReadonly();

  setTenant(tenant: Tenant): void {
    this._tenantId.set(tenant.id);
    this._tenant.set(tenant);
  }
}
