import { ref, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors } from '@/shared/lib/forms';
import {
  createAdminUserFormSchema,
  toCreateAdminUserRequest,
  useCreateAdminUserMutation,
  type AdminUser,
} from '@/entities/admin-user';

const FIELDS = ['email', 'password'] as const;

/** The "New tenant admin" form (port of create-admin-user.dialog.ts). */
export function useCreateAdminUserForm(
  tenantId: MaybeRefOrGetter<string>,
  onCreated: (user: AdminUser) => void,
) {
  const formErrors = ref<string[]>([]);
  const { defineField, errors, handleSubmit, resetForm, setErrors } = useForm({
    validationSchema: toTypedSchema(createAdminUserFormSchema()),
    initialValues: { email: '', password: '' },
  });
  const [email] = defineField('email');
  const [password] = defineField('password');
  const mutation = useCreateAdminUserMutation(tenantId);

  const submit = handleSubmit(async (values) => {
    formErrors.value = [];
    try {
      const user = await mutation.mutateAsync(toCreateAdminUserRequest(values));
      resetForm();
      onCreated(user);
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

  return { email, password, errors, formErrors, pending: mutation.isPending, submit, reset };
}
