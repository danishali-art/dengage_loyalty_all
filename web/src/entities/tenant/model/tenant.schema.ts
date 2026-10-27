import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { TENANT_ID_PATTERN, TENANT_NAME_MAX, type CreateTenantRequest } from './types';

/** The "New tenant" form, checked against PlatformValidators. */
export function createTenantFormSchema() {
  return z.object({
    id: z.string().trim().regex(TENANT_ID_PATTERN, translate('platform.tenants.form.idPattern')),
    name: z
      .string()
      .trim()
      .min(1, translate('platform.tenants.form.nameRequired'))
      .max(TENANT_NAME_MAX, translate('platform.tenants.form.tooLong', { max: TENANT_NAME_MAX })),
  });
}
export type CreateTenantFormValues = z.infer<ReturnType<typeof createTenantFormSchema>>;

export const toCreateTenantRequest = (v: CreateTenantFormValues): CreateTenantRequest => ({
  id: v.id,
  name: v.name,
});
