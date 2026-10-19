import { ref } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors } from '@/shared/lib/forms';
import {
  createTenantFormSchema,
  toCreateTenantRequest,
  useCreateTenantMutation,
  type Tenant,
} from '@/entities/tenant';

const FIELDS = ['id', 'name'] as const;

/** The "New tenant" form (port of create-tenant.dialog.ts). The id can't be changed later. */
export function useCreateTenantForm(onCreated: (tenant: Tenant) => void) {
  const formErrors = ref<string[]>([]);
  const { defineField, errors, handleSubmit, resetForm, setErrors } = useForm({
    validationSchema: toTypedSchema(createTenantFormSchema()),
    initialValues: { id: '', name: '' },
  });
  const [id] = defineField('id');
  const [name] = defineField('name');
  const mutation = useCreateTenantMutation();

  const submit = handleSubmit(async (values) => {
    formErrors.value = [];
    try {
      const tenant = await mutation.mutateAsync(toCreateTenantRequest(values));
      resetForm();
      onCreated(tenant);
    } catch (error) {
      const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
      setErrors(mapped.fields);
      formErrors.value = mapped.form;
    }
  });

  function reset(): void {
    resetForm();
    formErrors.value = [];
  }

  return { id, name, errors, formErrors, pending: mutation.isPending, submit, reset };
}
