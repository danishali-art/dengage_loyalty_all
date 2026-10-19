import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { ADMIN_EMAIL_MAX, ADMIN_PASSWORD_MIN, type CreateAdminUserRequest } from './types';

/** The "New tenant admin" form (port of create-admin-user.dialog.ts). */
export function createAdminUserFormSchema() {
  return z.object({
    email: z
      .string()
      .trim()
      .email(translate('platform.admins.form.emailInvalid'))
      .max(ADMIN_EMAIL_MAX, translate('platform.admins.form.tooLong', { max: ADMIN_EMAIL_MAX })),
    password: z
      .string()
      .min(ADMIN_PASSWORD_MIN, translate('platform.admins.form.passwordMin', { min: ADMIN_PASSWORD_MIN })),
  });
}
export type CreateAdminUserFormValues = z.infer<ReturnType<typeof createAdminUserFormSchema>>;

/** Always a tenant_admin scoped to the tenant in the URL (the API enforces the scope). */
export const toCreateAdminUserRequest = (v: CreateAdminUserFormValues): CreateAdminUserRequest => ({
  email: v.email,
  password: v.password,
  role: 'tenant_admin',
});
