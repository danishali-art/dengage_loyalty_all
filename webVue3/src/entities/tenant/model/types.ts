import type { ApiSchema } from '@/shared/api';

export type Tenant = ApiSchema<'TenantResponse'>;
export type TenantStatus = Tenant['status'];
export type CreateTenantRequest = ApiSchema<'CreateTenantRequest'>;
export type UpdateTenantRequest = ApiSchema<'UpdateTenantRequest'>;

/** PlatformValidators: the id is 1–50 lowercase letters, digits or underscores; the name ≤ 255. */
export const TENANT_ID_PATTERN = /^[a-z0-9_]{1,50}$/;
export const TENANT_NAME_MAX = 255;
