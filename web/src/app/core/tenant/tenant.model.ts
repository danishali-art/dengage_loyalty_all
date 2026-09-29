export type TenantStatus = 'active' | 'suspended';

/** A tenant as returned by `GET /api/v1/platform/tenants`. */
export interface Tenant {
  id: string;
  name: string;
  status: TenantStatus;
  createdAt: string;
}
