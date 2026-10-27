import type { ApiSchema } from '@/shared/api';

export type AdminUser = ApiSchema<'AdminUserResponse'>;
export type CreateAdminUserRequest = ApiSchema<'CreateAdminUserRequest'>;

/** PlatformValidators: a valid email ≤ 255, a password of at least 12 characters. */
export const ADMIN_EMAIL_MAX = 255;
export const ADMIN_PASSWORD_MIN = 12;
